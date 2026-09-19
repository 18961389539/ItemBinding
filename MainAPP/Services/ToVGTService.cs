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

        // 2026-09-13: 自定义协议 UDP 客户端缓存（per-endpoint，避免每帧 new UdpClient——
        // 帧频高时省略套接字反复创建/释放；DisposeAsync 统一释放）
        private readonly System.Collections.Concurrent.ConcurrentDictionary<string, UdpClient> _customClients = new();

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
        // 2026-09-17: 编码器上报缓冲改为**非消费式**（EncoderBindingBuffer，容量 1000 靠上限淘汰）。
        // 原实现绑定即消费（RemoveRange），编码器 UDP 包晚于图像到达时，帧会绑到上一拍的值
        // 并从此系统性错位；现保留历史，绑定改「取 Time ≤ 拍照时刻的最近一条」，
        // 迟到的包在后续帧解析时仍能被正确纳入。线程安全由缓冲内部保证。
        private readonly EncoderBindingBuffer _bindingBuffer = new(MaxEncoderHistory);
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
                lock (_lastEncoderLock)
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
                lock (_lastEncoderLock)
                {
                    return _lastEncoderValue;
                }
            }
        }
        private uint? _lastEncoderValue;
        // 2026-09-17: _encoderValuesLock 随消费式列表一起移除；_last* 两个 UI 属性改用独立锁
        private readonly object _lastEncoderLock = new();

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
                                lock (_lastEncoderLock)
                                {
                                    if (_lastReceiveTime.HasValue)
                                    {
                                        intervalMs = (long)(now - _lastReceiveTime.Value).TotalMilliseconds;
                                    }
                                    _lastEncoderValue = encoderOfReceived;
                                    _lastReceiveTime = now;
                                    // 2026-09-17: 非消费式缓冲——历史保留（靠容量上限淘汰），
                                    // 绑定按「Time ≤ 拍照时刻」取最近一条，迟到的包仍能被后续帧正确纳入
                                    _bindingBuffer.Add(encoderOfReceived, now);
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
        /// 取「Time ≤ <paramref name="searchTime"/> 的最近一条」编码器记录（2026-09-17 起<b>不消费</b>）。
        /// <para>使用二分查找，O(log n)。调用方（收图线程）在帧入处理通道前调用一次；
        /// 历史保留在缓冲内（靠容量上限淘汰最旧），迟到的编码器包仍能被后续帧正确纳入。</para>
        /// </summary>
        /// <param name="searchTime">绑定的基准时刻（帧的拍照/到达时刻）。</param>
        /// <returns>Encoder/Time 为命中的记录（无命中时 0/MinValue）；Stale = 记录已超龄
        /// （XY 与该编码器值的对应关系错位约一个触发间隔），发送路径据此可整条拒发。</returns>
        public (uint Encoder, DateTime Time, bool Stale) MostRecentDateEncode(DateTime searchTime)
        {
            ThrowIfDisposed();
            var hit = _bindingBuffer.TryResolve(searchTime, Speed, out var encoder, out var time, out var stale);

            if (!hit)
            {
                return (0, DateTime.MinValue, true);
            }

            // 绑定新鲜度自检（2026-09-12 引入，2026-09-17 移入缓冲判定）：记录时间与请求时刻（图像到达）的差
            // = 图像-编码器配对的偏移。稳态 ≈ 上报周期（几十 ms）；
            // 超过动态阈值（2×当前上报间隔，且 ≥500ms）说明编码器包丢失/断流恢复——
            // 本帧绑到的是上一次触发的编码器，位置错位约一个触发间隔（200mm）。
            var stalenessMs = (searchTime - time).TotalMilliseconds;
            var thresholdMs = Math.Max(500, Speed * 2.0);
            if (stalenessMs > thresholdMs)
            {
                LogService.Instance.Warning(
                    $"[编码器绑定] 记录滞后 {stalenessMs:F0}ms（阈值 {thresholdMs:F0}ms）——" +
                    "编码器包可能丢失/断流，本帧 XY 与编码器对应关系错位约一个触发间隔");
            }

            return (encoder, time, stale);
        }

        #endregion

        #region SendTo

        /// <summary>发送审计日志统一前缀（2026-09-13 收敛分散的发送标识）。</summary>
        private const string AuditPrefix = "[发送审计]";

        /// <summary>发送成功审计（统一格式：接收方/帧/编码器/条目数/内容）。</summary>
        private static void AuditSend(string receiver, uint frame, uint encoder, int count, string content)
            => LogService.Instance.Info($"{AuditPrefix}[{receiver}] 帧={frame} 编码器={encoder} 条目数={count} | {content}");

        /// <summary>发送告警审计（统一前缀）。</summary>
        private static void AuditWarn(string receiver, string message)
            => LogService.Instance.Warning($"{AuditPrefix}[{receiver}] {message}");

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
                AuditWarn("VGT", "vgtClient 为 null，无法发送消息。");
                return;
            }

            byte[] data = Encoding.UTF8.GetBytes(content);
            // REVIEW-FIX: 补充捕获 SocketException。UDP 发送在目标不可达（ICMP 错误）、网络断开时
            // 抛 SocketException，原实现仅捕获 ObjectDisposedException，异常会冒泡中断检测主循环。
            // 发送失败属于非致命（VGT 侧自行超时），降级为 Warning 日志。
            try { client.Send(data, data.Length); }
            catch (ObjectDisposedException) { /* 客户端在发送期间被释放，吞掉异常 */ }
            catch (System.Net.Sockets.SocketException ex) { AuditWarn("VGT", $"消息发送失败: {ex.Message}"); }
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
                AuditWarn("LL", "vgtClient 为 null，无法发送消息。");
                return;
            }

            byte[] data = Encoding.UTF8.GetBytes(content);
            // REVIEW-FIX: 同 SendToVGT，补充捕获 SocketException，避免 UDP 发送失败冒泡中断检测主循环。
            try { client.Send(data, data.Length); }
            catch (ObjectDisposedException) { /* 客户端在发送期间被释放，吞掉异常 */ }
            catch (System.Net.Sockets.SocketException ex) { AuditWarn("LL", $"消息发送失败: {ex.Message}"); }
        }

        /// <summary>
        /// 将一组 <see cref="DbModel"/> 转换为 <see cref="MessageToVGT"/> 并发送给 VGT。
        /// </summary>
        public void SendTo(IEnumerable<DbModel> models, FrameResult scannerResult, string receiver = "LL")
        {
            ThrowIfDisposed();
            ArgumentNullException.ThrowIfNull(models);
            ArgumentNullException.ThrowIfNull(scannerResult);

            // 生效角度域：内置 VGT/LL 走全局默认（自定义协议在自己的分支里按协议项覆盖解析）
            var effectiveDomain = AngleDomainConverter.Parse(Settings.Instance.Protocol?.DefaultAngleDomain);

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
                    // 此处换算对 Signed180 域是幂等的（保留作防御：历史/导入数据越界时仍能安全落域）。
                    // 2026-09-17: 改为按"当前生效的角度域"换算——机械手腕关节只能转 ±90 时，
                    // 现场会切到 Folded90（折叠 180°，仅适用于 180° 对称的产品/夹爪）。
                    // ★ 落库值仍是 (-180,180]：折算只作用于发送，否则"产品装反"与"正常"在数据里将无法区分。
                    RZ = AngleDomainConverter.ToDomain(model.Angle, effectiveDomain),
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
                // 2026-09-13: 自定义通讯协议——receiver 非内置 VGT/LL 时按名称匹配启用协议，
                // 逐条走模板渲染（ProtocolTemplateRenderer），复用同一帧编码器，向配置端点发送。
                var proto = FindCustomProtocol(receiver);
                if (proto is not null)
                {
                    SendCustomProtocol(models, proto, scannerResult);
                    sent = true;
                }
                else
                {
                    LogService.Instance.Warning($"ToVGTService.SendTo: 未知的接收方 '{receiver}'，未发送消息。");
                }
            }

            if (sent)
            {
                // 2026-09-07: 记录到主页"发送"面板（仅真实发出的产品；空列表内部忽略，不刷屏）
                RobotSendMonitor.Instance.RecordBatch(scannerResult.FrameNumber, scannerResult.EncoderValue, messages);

                var detail = string.Join(" | ", messages.Select(m =>
                    $"条码={m.Barcode}, X={m.X:F2}, Y={m.Y:F2}, 角度={m.RZ:F2}"));
                // 2026-09-17: 审计里写明生效的角度域——折叠域下发送值与落库值不同，
                // 这条是事后核对"为什么发的是 -30 而不是 150"的第一线索
                AuditSend(receiver, scannerResult.FrameNumber, scannerResult.EncoderValue, messages.Count,
                    $"角度域={AngleDomainConverter.Describe(effectiveDomain)} | {detail}");
            }
        }

        /// <summary>
        /// 按名称查找启用的自定义协议（2026-09-13）。非内置 VGT/LL 的 receiver 即协议名。
        /// </summary>
        private static ProtocolTemplateConfig? FindCustomProtocol(string receiver)
        {
            if (string.IsNullOrWhiteSpace(receiver))
            {
                return null;
            }

            var list = MainAPP.Models.Settings.Instance.Protocol?.Protocols;
            if (list is null)
            {
                return null;
            }

            foreach (var p in list)
            {
                if (p.Enabled && string.Equals(p.Name, receiver, StringComparison.OrdinalIgnoreCase))
                {
                    return p;
                }
            }

            return null;
        }

        /// <summary>
        /// 按自定义协议模板渲染并发送本帧全部记录（2026-09-13）。
        /// 每条产品一行（行分隔按协议 LineEnding），逐条经 <see cref="ProtocolTemplateRenderer.Render"/>
        /// 决定是否拒发（角度未知 / 无码 RejectMessage）；整帧一条 UDP 报文发向配置端点。
        /// </summary>
        private void SendCustomProtocol(IEnumerable<DbModel> models, ProtocolTemplateConfig proto, FrameResult scannerResult)
        {
            if (models is null)
            {
                LogService.Instance.Warning("[自定义协议] 模型列表为 null。");
                return;
            }

            // 2026-09-17: 协议项可单独覆盖角度域（空 = 跟随全局默认）。
            // 换算在**渲染之外**算好再传入：渲染器保持纯函数，且不碰 EF 跟踪的实体（改写会污染落库值）。
            var domain = AngleDomainConverter.Parse(
                string.IsNullOrWhiteSpace(proto.AngleDomain)
                    ? Settings.Instance.Protocol?.DefaultAngleDomain
                    : proto.AngleDomain);

            var lineEnding = ProtocolTemplateRenderer.FrameLineEnding(proto);
            var parts = new List<string>();
            foreach (var m in models)
            {
                var s = ProtocolTemplateRenderer.Render(m, proto, AngleDomainConverter.ToDomain(m.Angle, domain));
                if (s is not null)
                {
                    parts.Add(s);
                }
            }

            if (parts.Count == 0)
            {
                LogService.Instance.Debug($"{AuditPrefix}[{proto.Name}] 帧={scannerResult.FrameNumber} 无可发记录（拒发策略过滤）。");
                return;
            }

            var endpoint = ParseEndPoint(proto.EndPoint);
            if (endpoint is null)
            {
                AuditWarn(proto.Name, $"端点 '{proto.EndPoint}' 无法解析，未发送。");
                return;
            }

            var content = string.Join(lineEnding, parts);
            byte[] data = Encoding.UTF8.GetBytes(content);
            try
            {
                // 2026-09-13: 复用 per-endpoint 缓存客户端（原每帧 new UdpClient）
                var client = _customClients.GetOrAdd(proto.EndPoint, static _ => new UdpClient());
                client.Send(data, data.Length, endpoint);
                AuditSend(proto.Name, scannerResult.FrameNumber, scannerResult.EncoderValue, parts.Count, content);
            }
            catch (ObjectDisposedException) { /* 发送期间被释放，吞掉异常 */ }
            catch (SocketException ex)
            {
                AuditWarn(proto.Name, $"发送失败: {ex.Message}");
            }
        }

        /// <summary>"ip:port" → IPEndPoint；解析失败返回 null。</summary>
        private static System.Net.IPEndPoint? ParseEndPoint(string ep)
        {
            if (string.IsNullOrWhiteSpace(ep))
            {
                return null;
            }

            var idx = ep.LastIndexOf(':');
            if (idx <= 0 || idx == ep.Length - 1 || !ushort.TryParse(ep[(idx + 1)..], out var port))
            {
                return null;
            }

            return System.Net.IPAddress.TryParse(ep[..idx], out var ip)
                ? new System.Net.IPEndPoint(ip, port)
                : null;
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

            // 2026-09-13: 释放自定义协议缓存客户端（per-endpoint）
            try
            {
                foreach (var c in _customClients.Values)
                {
                    try { c.Close(); } catch { }
                    c.Dispose();
                }

                _customClients.Clear();
            }
            catch (Exception ex) { LogService.Instance.Warning($"ToVGTService.DisposeAsync: 释放自定义协议客户端失败: {ex}"); }

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
