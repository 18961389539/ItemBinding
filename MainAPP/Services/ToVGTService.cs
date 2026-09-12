using HikScanner;
using MainAPP.Models;
using System;
using System.Collections.Generic;
using System.Globalization;
using System.Net.NetworkInformation;
using System.Net.Sockets;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

namespace MainAPP.Services
{
    /// <summary>
    /// 与 VGT（视觉引导系统）通信的服务实现。
    /// 从原静态类 ToVGT 搬迁逻辑，改为实例服务以支持 DI 和单元测试。
    ///
    /// 生命周期：由 DI 容器管理为单例，应用启动时调用 <see cref="StartAsync"/>，
    /// 应用退出时调用 <see cref="DisposeAsync"/> 释放资源。
    /// </summary>
    public sealed class ToVGTService : IToVGTService
    {
        #region Fields

        // 网络配置（构造函数注入，解除对 Settings.Instance 的直接依赖）
        private readonly string _connectivityCheckIP;
        private readonly string _detectionResultSendIP;
        private readonly int _encoderReceiverPort;
        private readonly int _detectionResultSendPort;

        // 后台任务：连通性监控与编码器监听
        private Task? _monitorRobotCommunicateStateTask;
        private Task? _monitorEncodeValueTask;

        // 发送到 VGT 的 UDP 客户端（StartAsync 中创建，DisposeAsync 中释放）
        private volatile UdpClient? _vgtClient;

        // 用于接收编码器数据的 UDP 客户端（StartAsync 中创建，DisposeAsync 中释放）
        private volatile UdpClient? _encoderClient;

        /// <summary>表示机器人是否可达（Ping 成功）。由连通性监控任务（后台线程）更新，UI 线程读取。</summary>
        private volatile bool _isRobotOnLive;
        public bool IsRobotOnLive => _isRobotOnLive;

        /// <summary>表示 VGT 是否可达（Ping 成功）。由连通性监控任务（后台线程）更新，UI 线程读取。</summary>
        private volatile bool _isVGTOnLive;
        public bool IsVGTOnLive => _isVGTOnLive;

        // Speed 特殊值常量
        private const long SpeedFirstReceive = -1;
        private const long SpeedUninitialized = -10;

        private long _speed = SpeedUninitialized;
        public long Speed
        {
            get => Interlocked.Read(ref _speed);
            private set => Interlocked.Exchange(ref _speed, value);
        }

        // 后台任务取消令牌
        private CancellationTokenSource? _ctsOfRobot;

        // 编码器历史记录缓冲
        private readonly List<(uint Encoder, DateTime Time)> _encoderValues = new();
        private readonly object _encoderValuesLock = new();
        private const int MaxEncoderHistory = 1000;

        // 网络与重试常量
        private const int PingTimeoutMs = 3_000;
        private const int PingIntervalMs = 60_000;
        private const int EncoderReceiveTimeoutSec = 5;
        private const int ErrorRetryDelayMs = 1_000;

        // 上一次成功接收编码器报文的时间
        private DateTime? _lastReceiveTime;

        /// <summary>
        /// 最近一次成功接收编码器报文的时间（本地时间），为 null 表示尚未收到任何报文。
        /// </summary>
        public DateTime? LastEncoderReceiveTime
        {
            get
            {
                lock (_encoderValuesLock)
                {
                    return _lastReceiveTime;
                }
            }
        }

        /// <summary>
        /// 最近一次接收到的编码器原始值（uint counts），为 null 表示尚未收到任何报文。
        /// REVIEW(2026-08-05): 供 UI 状态栏显示当前编码器值。
        /// </summary>
        public uint? LastEncoderValue
        {
            get
            {
                lock (_encoderValuesLock)
                {
                    return _lastEncoderValue;
                }
            }
        }
        private uint? _lastEncoderValue;

        // Dispose 相关
        private int _disposed;

        #endregion

        #region Constructor

        /// <summary>
        /// 创建 ToVGTService 实例。
        /// 接受 <see cref="INetworkSettings"/> 以支持依赖注入和单元测试。
        /// </summary>
        /// <param name="networkSettings">网络配置（IP 地址、端口等）。</param>
        public ToVGTService(INetworkSettings networkSettings)
        {
            _connectivityCheckIP = networkSettings.ConnectivityCheckIP;
            _detectionResultSendIP = networkSettings.DetectionResultSendIP;
            _encoderReceiverPort = networkSettings.EncoderReceiverPort;
            _detectionResultSendPort = networkSettings.DetectionResultSendPort;
        }

        #endregion

        #region StartAsync

        /// <summary>
        /// 启动服务：创建 UDP 客户端、启动连通性监控和编码器监听后台任务。
        /// 幂等：重复调用无副作用（已在运行时直接返回）。
        /// </summary>
        public Task StartAsync(CancellationToken ct = default)
        {
            // 已释放后禁止重启，避免"半启动"状态（UdpClient/任务重建但 SendTo 抛 ObjectDisposedException）
            ThrowIfDisposed();

            // 幂等保护
            if (_vgtClient is not null && _ctsOfRobot is not null && !_ctsOfRobot.IsCancellationRequested)
            {
                return Task.CompletedTask;
            }

            // 创建或重新创建 CTS
            if (_ctsOfRobot is null || _ctsOfRobot.IsCancellationRequested)
            {
                _ctsOfRobot?.Dispose();
                _ctsOfRobot = new CancellationTokenSource();
            }

            // 创建 vgtClient
            if (_vgtClient is null)
            {
                _vgtClient = new UdpClient();
                try
                {
                    _vgtClient.Connect(_detectionResultSendIP, _detectionResultSendPort);
                    LogService.Instance.Info($"vgtClient 已连接到 {_detectionResultSendIP}:{_detectionResultSendPort}");
                }
                catch (Exception exc)
                {
                    LogService.Instance.Error("初始化 vgtClient 失败: " + exc);
                    _vgtClient.Dispose();
                    _vgtClient = null;
                }
            }

            // 创建 encoderClient
            if (_encoderClient is null)
            {
                try
                {
                    _encoderClient = new UdpClient(_encoderReceiverPort);
                }
                catch (Exception exc)
                {
                    LogService.Instance.Error("初始化 encoderClient 失败: " + exc);
                }
            }

            var token = _ctsOfRobot.Token;

            // 启动连通性监控任务
            if (_monitorRobotCommunicateStateTask is null || _monitorRobotCommunicateStateTask.IsCompleted)
            {
                if (_monitorRobotCommunicateStateTask?.IsFaulted == true)
                {
                    LogService.Instance.Error($"监控任务异常: {_monitorRobotCommunicateStateTask.Exception}");
                }
                _monitorRobotCommunicateStateTask = Task.Run(async () =>
                {
                    using var robotPing = new Ping();
                    using var vgtPing = new Ping();
                    while (!token.IsCancellationRequested)
                    {
                        try
                        {
                            var robotTask = robotPing.SendPingAsync(_connectivityCheckIP, PingTimeoutMs);
                            var vgtTask = vgtPing.SendPingAsync(_detectionResultSendIP, PingTimeoutMs);
                            await Task.WhenAll(robotTask, vgtTask).WaitAsync(token).ConfigureAwait(false);

                            var robotReply = await robotTask.ConfigureAwait(false);
                            _isRobotOnLive = robotReply.Status == IPStatus.Success;
                            if (!_isRobotOnLive)
                                LogService.Instance.Error("Robot is not reachable");

                            var vgtReply = await vgtTask.ConfigureAwait(false);
                            _isVGTOnLive = vgtReply.Status == IPStatus.Success;
                            if (!_isVGTOnLive)
                                LogService.Instance.Error("VGT is not reachable");
                        }
                        catch (OperationCanceledException) { break; }
                        catch (Exception exc)
                        {
                            LogService.Instance.Error("连通性检测异常: " + exc);
                        }
                        await Task.Delay(PingIntervalMs, token).ConfigureAwait(false);
                    }
                }, token);
            }

            // 启动编码器监听任务
            if (_monitorEncodeValueTask is null || _monitorEncodeValueTask.IsCompleted)
            {
                if (_monitorEncodeValueTask?.IsFaulted == true)
                {
                    LogService.Instance.Error($"编码器监听任务异常: {_monitorEncodeValueTask.Exception}");
                }
                _monitorEncodeValueTask = Task.Run(async () =>
                {
                    var client = _encoderClient;
                    if (client is null) return;
                    while (!token.IsCancellationRequested)
                    {
                        try
                        {
                            var result = await client.ReceiveAsync()
                                .WaitAsync(TimeSpan.FromSeconds(EncoderReceiveTimeoutSec), token).ConfigureAwait(false);
                            var buffer = result.Buffer;
                            if (buffer.Length >= 4)
                            {
                                var encoderOfReceived = BitConverter.ToUInt32(buffer, 0);
                                var now = DateTime.Now;
                                long intervalMs = SpeedFirstReceive;
                                lock (_encoderValuesLock)
                                {
                                    if (_lastReceiveTime.HasValue)
                                    {
                                        intervalMs = (long)(now - _lastReceiveTime.Value).TotalMilliseconds;
                                    }
                                    _lastEncoderValue = encoderOfReceived;
                                    _lastReceiveTime = now;
                                    _encoderValues.Add((encoderOfReceived, now));

                                    if (_encoderValues.Count > MaxEncoderHistory)
                                    {
                                        _encoderValues.RemoveRange(0, _encoderValues.Count - MaxEncoderHistory);
                                    }
                                }
                                Speed = intervalMs;
                            }
                        }
                        catch (OperationCanceledException) { break; }
                        catch (TimeoutException) { LogService.Instance.Debug("编码器监听接收超时，等待下一周期"); }
                        catch (Exception exc)
                        {
                            LogService.Instance.Error("编码器监听任务异常: " + exc);
                            await Task.Delay(ErrorRetryDelayMs, token).ConfigureAwait(false);
                        }
                    }
                }, token);
            }

            return Task.CompletedTask;
        }

        #endregion

        #region MostRecentDateEncode

        /// <summary>
        /// 查找并移除最接近但不晚于指定时间点的编码器记录。
        /// 使用二分查找，O(log n)。
        /// </summary>
        public (uint Encoder, DateTime Time) MostRecentDateEncode(DateTime searchTime)
        {
            ThrowIfDisposed();
            lock (_encoderValuesLock)
            {
                if (_encoderValues.Count == 0)
                    return (0, DateTime.MinValue);

                int left = 0, right = _encoderValues.Count - 1;
                int foundIdx = -1;
                while (left <= right)
                {
                    int mid = left + (right - left) / 2;
                    var t = _encoderValues[mid].Time;
                    if (t <= searchTime)
                    {
                        foundIdx = mid;
                        left = mid + 1;
                    }
                    else
                    {
                        right = mid - 1;
                    }
                }

                if (foundIdx < 0)
                    return (0, DateTime.MinValue);

                var closest = _encoderValues[foundIdx];
                _encoderValues.RemoveRange(0, foundIdx + 1);

                // 绑定新鲜度自检（2026-09-12）：记录时间与请求时刻（图像到达）的差
                // = 图像-编码器配对的偏移。稳态 ≈ 上报周期（几十 ms）；
                // 超过动态阈值（2×当前上报间隔，且 ≥500ms）说明编码器包丢失/断流恢复——
                // 本帧绑到的是上一次触发的编码器，位置错位约一个触发间隔（200mm）。
                var stalenessMs = (searchTime - closest.Time).TotalMilliseconds;
                var thresholdMs = Math.Max(500, Speed * 2.0);
                if (stalenessMs > thresholdMs)
                {
                    LogService.Instance.Warning(
                        $"[编码器绑定] 记录滞后 {stalenessMs:F0}ms（阈值 {thresholdMs:F0}ms）——" +
                        "编码器包可能丢失/断流，本帧 XY 与编码器对应关系错位约一个触发间隔");
                }

                return (closest.Encoder, closest.Time);
            }
        }

        #endregion

        #region SendTo

        /// <summary>
        /// 将一组 <see cref="MessageToVGT"/> 按协议格式组合为待发送字符串。
        /// </summary>
        private static string CombineMessage(IEnumerable<MessageToVGT> messages, uint encoderValue)
        {
            var sb = new StringBuilder();
            if (encoderValue != 0)
            {
                sb.Append($"[E]{encoderValue}");
            }
            foreach (var msg in messages)
            {
                sb.Append(msg.ToString());
            }
            sb.Append("[END]");
            return sb.ToString();
        }

        private void SendToVGT(IEnumerable<MessageToVGT> messages, uint encoderValue = 0)
        {
            if (messages is null)
            {
                LogService.Instance.Warning("发送给VGT的消息列表为null。");
                return;
            }

            var content = CombineMessage(messages, encoderValue);
            var client = _vgtClient;
            if (client is null)
            {
                LogService.Instance.Warning("ToVGTService.SendToVGT: vgtClient 为 null，无法发送消息。");
                return;
            }

            byte[] data = Encoding.UTF8.GetBytes(content);
            // REVIEW-FIX: 补充捕获 SocketException。UDP 发送在目标不可达（ICMP 错误）、网络断开时
            // 抛 SocketException，原实现仅捕获 ObjectDisposedException，异常会冒泡中断检测主循环。
            // 发送失败属于非致命（VGT 侧自行超时），降级为 Warning 日志。
            try { client.Send(data, data.Length); }
            catch (ObjectDisposedException) { /* 客户端在发送期间被释放，吞掉异常 */ }
            catch (System.Net.Sockets.SocketException ex) { LogService.Instance.Warning($"VGT 消息发送失败: {ex.Message}"); }
        }

        private void SendToLeiLei(IEnumerable<MessageToVGT> messages, uint encoderValue = 0)
        {
            if (messages is null)
            {
                LogService.Instance.Warning("发送给LL的消息列表为null。");
                return;
            }

            var sb = new StringBuilder();
            foreach (var msg in messages)
            {
                sb.AppendLine(msg.ToStringLeiLei());
            }
            var content = sb + "#Error#";

            var client = _vgtClient;
            if (client is null)
            {
                LogService.Instance.Warning("ToVGTService.SendToLeiLei: vgtClient 为 null，无法发送消息。");
                return;
            }

            byte[] data = Encoding.UTF8.GetBytes(content);
            // REVIEW-FIX: 同 SendToVGT，补充捕获 SocketException，避免 UDP 发送失败冒泡中断检测主循环。
            try { client.Send(data, data.Length); }
            catch (ObjectDisposedException) { /* 客户端在发送期间被释放，吞掉异常 */ }
            catch (System.Net.Sockets.SocketException ex) { LogService.Instance.Warning($"LL 消息发送失败: {ex.Message}"); }
        }

        /// <summary>
        /// 将一组 <see cref="DbModel"/> 转换为 <see cref="MessageToVGT"/> 并发送给 VGT。
        /// </summary>
        public void SendTo(IEnumerable<DbModel> models, FrameResult scannerResult, string receiver = "LL")
        {
            ThrowIfDisposed();
            ArgumentNullException.ThrowIfNull(models);
            ArgumentNullException.ThrowIfNull(scannerResult);

            List<MessageToVGT> messages = [];
            foreach (var model in models)
            {
                // REVIEW-FIX(需求 2026-08-05): 角度未知（-9999）的产品整条不发送 VGT，
                // 避免下游把未知角度当作真实角度执行错误旋转。
                // 注意：角度检测功能关闭时，AngleTracker 对所有新产品也返回 -9999，
                // 此时该过滤会使 VGT 收不到任何消息——如需区分"功能关闭"与"识别失败"，另行调整。
                if (model.Angle == AngleTracker.UnknownAngle)
                {
                    continue;
                }

                var msg = new MessageToVGT
                {
                    X = model.WorldX,
                    Y = model.WorldY,
                    // 2026-09-05: DbModel.Angle 落库已统一为 (-180,180]（AngleTracker 归一化），
                    // 此处 ToRobotAngle 为幂等换算，保留作防御（历史/导入数据越界时仍能安全落域）。
                    RZ = ToVGT.ToRobotAngle(model.Angle),
                    Barcode = model.Barcode,
                };
                messages.Add(msg);
            }

            bool sent = false;
            if (string.Equals(receiver, "VGT", StringComparison.OrdinalIgnoreCase))
            {
                SendToVGT(messages, scannerResult.EncoderValue);
                sent = true;
            }
            else if (string.Equals(receiver, "LL", StringComparison.OrdinalIgnoreCase))
            {
                SendToLeiLei(messages, scannerResult.EncoderValue);
                sent = true;
            }
            else
            {
                LogService.Instance.Warning($"ToVGTService.SendTo: 未知的接收方 '{receiver}'，未发送消息。");
            }

            if (sent)
            {
                // 2026-09-07: 记录到主页"发送"面板（仅真实发出的产品；空列表内部忽略，不刷屏）
                RobotSendMonitor.Instance.RecordBatch(scannerResult.FrameNumber, scannerResult.EncoderValue, messages);

                var detail = string.Join(" | ", messages.Select(m =>
                    $"条码={m.Barcode}, X={m.X:F2}, Y={m.Y:F2}, 角度={m.RZ:F2}"));
                LogService.Instance.Info(
                    $"[UDP→{receiver}] 帧={scannerResult.FrameNumber} 编码器={scannerResult.EncoderValue} 条目数={messages.Count} | {detail}");
            }
        }

        #endregion

        #region IDisposable / IAsyncDisposable

        private void ThrowIfDisposed()
        {
            if (_disposed != 0)
                throw new ObjectDisposedException(nameof(ToVGTService));
        }

        /// <summary>
        /// 异步释放所有资源：取消后台任务、关闭 UDP 客户端。
        /// </summary>
        public async ValueTask DisposeAsync()
        {
            if (Interlocked.Exchange(ref _disposed, 1) != 0)
                return;

            // 取消后台任务
            try
            {
                if (_ctsOfRobot is not null && !_ctsOfRobot.IsCancellationRequested)
                {
                    // RTC(2026-08-06): 改用异步 CancelAsync,消除 VSTHRD103(Cancel 同步阻塞)警告
                    await _ctsOfRobot.CancelAsync().ConfigureAwait(false);
                }
            }
            catch (Exception ex) { LogService.Instance.Warning($"ToVGTService.DisposeAsync: 取消任务失败: {ex}"); }

            // 等待后台任务退出
            try { if (_monitorRobotCommunicateStateTask is not null) await _monitorRobotCommunicateStateTask.WaitAsync(TimeSpan.FromSeconds(1)).ConfigureAwait(false); }
            catch (Exception ex) { LogService.Instance.Warning($"ToVGTService.DisposeAsync: 等待监控任务结束失败: {ex}"); }
            try { if (_monitorEncodeValueTask is not null) await _monitorEncodeValueTask.WaitAsync(TimeSpan.FromSeconds(1)).ConfigureAwait(false); }
            catch (Exception ex) { LogService.Instance.Warning($"ToVGTService.DisposeAsync: 等待编码器任务结束失败: {ex}"); }

            // 释放 UDP 客户端
            try
            {
                _vgtClient?.Close();
                _vgtClient?.Dispose();
            }
            catch (Exception ex) { LogService.Instance.Warning($"ToVGTService.DisposeAsync: 释放 vgtClient 失败: {ex}"); }
            finally { _vgtClient = null; }

            try
            {
                _encoderClient?.Close();
                _encoderClient?.Dispose();
            }
            catch (Exception ex) { LogService.Instance.Warning($"ToVGTService.DisposeAsync: 释放 encoderClient 失败: {ex}"); }
            finally { _encoderClient = null; }

            // 释放 CTS
            try
            {
                _ctsOfRobot?.Dispose();
                _ctsOfRobot = null;
            }
            catch (Exception ex) { LogService.Instance.Warning($"ToVGTService.DisposeAsync: 释放 CtsOfRobot 失败: {ex}"); }
        }

        #endregion
    }
}
