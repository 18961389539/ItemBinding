using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Drawing;
using System.Drawing.Imaging;
using System.IO;
using System.Runtime.InteropServices;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
using MvCodeReaderSDKNet;

namespace HikScanner
{
    /// <summary>
    /// 海康读码器SDK封装类（核心）。连接、采集、参数读写、面单/算法、图像保存、Dispose。
    /// 其他功能见 partial 类：GigE / FileAccess / Msc / Reconnect。
    /// </summary>
    public partial class HikScanner : IDisposable
    {
        private ICodeReaderSdk _device;
        private HikDeviceInfo _currentDeviceInfo;
        private volatile bool _isConnected;
        private volatile bool _isGrabbing;
        private volatile bool _disposed;
        private readonly object _eventLock = new();
        private readonly object _grabLock = new();

        // 预分配的非托管缓冲区（用于 GetOneFrameTimeoutEx2）
        private IntPtr _pFrameInfo = IntPtr.Zero;
        private int _frameInfoSize;
        private readonly object _bufferLock = new();

        // SDK 回调委托引用（防止 GC 回收）
        private MvCodeReader.cbOutputEx2delegate _imageCallback;
        private MvCodeReader.cbExceptiondelegate _exceptionCallback;
        internal MvCodeReader.cbMSCOutputdelegate _mscCallback0;
        internal MvCodeReader.cbMSCOutputdelegate _mscCallback1;

        // 采集线程取消令牌
        private CancellationTokenSource _grabCts;
        // #7: 采集循环 Task 引用，用于异常观察
        private Task _grabLoopTask;

        // #1: 有界 Channel 用于背压控制，防止高帧率下 Task.Run 无限堆积
        private System.Threading.Channels.Channel<HikGrabResult> _imageChannel;
        private Task _channelConsumerTask;

        // 重连配置
        private bool _autoReconnect;
        private int _reconnectIntervalMs = 2000;
        private volatile CancellationTokenSource _reconnectCts;

        // ApplyOptions 注册的委托引用（用于取消订阅）
        internal EventHandler _appliedDisconnectedHandler;
        internal EventHandler _appliedReconnectedHandler;
        internal EventHandler<HikGrabResult> _appliedFrameGrabbedHandler;

        // 重连后参数恢复
        private float _savedExposure = -1f;
        private float _savedGain = -1f;
        private float _savedFrameRate = -1f;
        private HikTriggerMode _savedTriggerMode = HikTriggerMode.Continuous;
        private HikTriggerSource _savedTriggerSource = HikTriggerSource.Line0;
        private bool _savedWayBillEnable;
        private bool _hasSavedParams;

        // 采集模式跟踪（用于重连恢复）
        private HikGrabMode _activeGrabMode = HikGrabMode.Polling;
        private Action<HikGrabResult> _mscCallback;
        private int _grabLoopTimeoutMs = 1000;

        // 连接状态跟踪
        private HikConnectionState _connectionState = HikConnectionState.Disconnected;
        private readonly object _stateLock = new();
        private volatile bool _isReconnecting;
        private int _reconnectInitialDelayMs = 500;

        /// <summary>内部诊断回调（兼容旧版，Logger 优先）。默认 null</summary>
        public Action<string, Exception> DiagnosticCallback;

        #region 公共属性

        public bool IsConnected => _isConnected;
        public bool IsGrabbing => _isGrabbing;
        public HikDeviceInfo DeviceInfo => _currentDeviceInfo;

        /// <summary>是否启用自动重连。回调采集模式下即时生效。</summary>
        public bool AutoReconnect
        {
            get => _autoReconnect;
            set
            {
                if (_autoReconnect == value) return;
                _autoReconnect = value;
                // #6: 加锁保护，防止 Dispose 并发时 _device 为 null
                lock (_grabLock)
                {
                    if (_disposed || !_isConnected || !_isGrabbing || _device == null || _imageCallback == null) return;
                    if (value)
                    {
                        _exceptionCallback ??= OnExceptionCallback;
                        // #5: 检查回调注册返回值
                        int ret = _device.MV_CODEREADER_RegisterExceptionCallBack_NET(_exceptionCallback, IntPtr.Zero);
                        if (ret != MvCodeReader.MV_CODEREADER_OK)
                            Log(LogLevel.Warning, $"[AutoReconnect] 注册异常回调失败: 0x{ret:X8}");
                        GC.KeepAlive(_exceptionCallback);
                    }
                    else
                    {
                        _device.MV_CODEREADER_RegisterExceptionCallBack_NET(null, IntPtr.Zero);
                    }
                }
            }
        }

        public int ReconnectIntervalMs { get => _reconnectIntervalMs; set { if (value > 0) _reconnectIntervalMs = value; } }
        public int MaxReconnectAttempts { get; set; } = 0;
        public bool ExponentialBackoff { get; set; } = true;
        public int MaxReconnectIntervalMs { get; set; } = 30000;

        /// <summary>重连前初始等待毫秒数，默认 500</summary>
        public int ReconnectInitialDelayMs { get => _reconnectInitialDelayMs; set => _reconnectInitialDelayMs = value > 0 ? value : 500; }

        /// <summary>单帧图像最大字节数，超过则返回 BufferOverflow。默认 100MB，可根据相机分辨率调整。</summary>
        public long MaxFrameSizeBytes { get; set; } = 100 * 1024 * 1024;

        /// <summary>采集默认超时毫秒数，用于 StartGrabbingLoop 等。默认 1000。</summary>
        public int DefaultTimeoutMs { get; set; } = 1000;

        /// <summary>结构化日志记录器，默认 null。设置后替代 DiagnosticCallback 输出诊断信息。</summary>
        public ILogger Logger { get; set; }

        /// <summary>设备枚举函数，默认调用真实 SDK。测试时可注入 Mock 实现。</summary>
        public static Func<HikDeviceType, List<HikDeviceInfo>> DeviceEnumerationFunc { get; set; } = DefaultEnumerateDevices;

        #endregion

        #region 便捷属性

        /// <summary>曝光时间（微秒）。设置时自动关闭 ExposureAuto。</summary>
        public float ExposureTime
        {
            get => GetFloatParam("ExposureTime");
            set { SetEnumParam("ExposureAuto", 0u); SetFloatParam("ExposureTime", value); _savedExposure = value; _hasSavedParams = true; }
        }

        /// <summary>增益。设置时自动关闭 GainAuto。</summary>
        public float Gain
        {
            get => GetFloatParam("Gain");
            set { SetEnumParam("GainAuto", 0u); SetFloatParam("Gain", value); _savedGain = value; _hasSavedParams = true; }
        }

        /// <summary>采集帧率</summary>
        public float FrameRate { get => GetFloatParam("AcquisitionFrameRate"); set { SetFloatParam("AcquisitionFrameRate", value); _savedFrameRate = value; _hasSavedParams = true; } }

        #endregion

        #region 事件

        public event EventHandler<HikGrabResult> ImageGrabbed;
        public event EventHandler DeviceDisconnected;
        public event EventHandler DeviceReconnected;
        /// <summary>重连过程中某一步骤失败时触发，携带具体错误码及描述</summary>
        public event EventHandler<HikReconnectErrorEventArgs> ReconnectError;
        /// <summary>连接状态变化事件（统一断线/重连通知）</summary>
        public event EventHandler<HikConnectionStateChangedEventArgs> ConnectionStateChanged;

        #endregion

        #region 构造与配置

        public HikScanner() { }

        /// <summary>通过注入 ICodeReaderSdk 实例构造（用于测试或自定义 SDK 适配器）</summary>
        public HikScanner(ICodeReaderSdk device) { _device = device; }

        public HikScanner(HikScannerOptions options) { options?.ApplyTo(this); }

        /// <summary>连接前应用配置</summary>
        public void ApplyOptions(HikScannerOptions options) { options?.ApplyTo(this); }

        #endregion

        #region 日志与诊断

        /// <summary>检查对象是否已释放，已释放则抛出 ObjectDisposedException</summary>
        private void ThrowIfDisposed() { if (_disposed) throw new ObjectDisposedException(nameof(HikScanner)); }

        /// <summary>统一诊断日志：优先 Logger，其次 DiagnosticCallback，兜底 Debug.WriteLine</summary>
        internal void LogDiagnostic(string message, Exception ex = null) { Log(LogLevel.Warning, message, ex); }

        /// <summary>统一日志方法，按级别输出到 Logger / DiagnosticCallback / Debug</summary>
        internal void Log(LogLevel level, string message, Exception ex = null)
        {
            if (Logger != null)
            {
                switch (level)
                {
                    case LogLevel.Trace: Logger.LogTrace("{Message}", message); break;
                    case LogLevel.Debug: Logger.LogDebug("{Message}", message); break;
                    case LogLevel.Information: Logger.LogInformation("{Message}", message); break;
                    case LogLevel.Warning: Logger.LogWarning("{Message}", message); break;
                    case LogLevel.Error: Logger.LogError(ex, "{Message}", message); break;
                    case LogLevel.Critical: Logger.LogCritical(ex, "{Message}", message); break;
                    // #7: LogLevel.None 及未知值不输出
                    default: break;
                }
            }
            else
            {
                DiagnosticCallback?.Invoke(message, ex);
                if (DiagnosticCallback == null)
                    Debug.WriteLine($"[HikScanner:{level}] {message}{(ex != null ? " => " + ex.Message : "")}");
            }
        }

        #endregion

        #region 静态方法

        public static uint GetSDKVersion() => MvCodeReader.MV_CODEREADER_GetSDKVersion_NET();

        /// <summary>枚举指定类型的所有设备。委托至 DeviceEnumerationFunc，可在测试中替换。</summary>
        public static List<HikDeviceInfo> EnumerateDevices(HikDeviceType deviceType) => DeviceEnumerationFunc(deviceType);

        private static List<HikDeviceInfo> DefaultEnumerateDevices(HikDeviceType deviceType)
        {
            var list = new List<HikDeviceInfo>();
            var devList = new MvCodeReader.MV_CODEREADER_DEVICE_INFO_LIST();
            int nRet = MvCodeReader.MV_CODEREADER_EnumDevices_NET(ref devList, (uint)deviceType);
            if (nRet != 0) throw new HikScannerException("枚举设备失败", nRet);
            if (devList.nDeviceNum == 0) return list;
            for (int i = 0; i < devList.nDeviceNum; i++)
            {
                var stDevInfo = (MvCodeReader.MV_CODEREADER_DEVICE_INFO)Marshal.PtrToStructure(
                    devList.pDeviceInfo[i], typeof(MvCodeReader.MV_CODEREADER_DEVICE_INFO));
                list.Add(ParseDeviceInfo(stDevInfo));
            }
            return list;
        }

        public static bool IsTextUtf8(byte[] inputStream)
        {
            int remainingBytes = 0;
            bool hasNonAscii = false;
            for (int i = 0; i < inputStream.Length; i++)
            {
                byte b = inputStream[i];
                if ((b & 0x80) != 0) hasNonAscii = true;
                if (remainingBytes == 0)
                {
                    if ((b & 0x80) != 0)
                    {
                        if ((b & 0xC0) != 0xC0) return false;
                        remainingBytes = 1;
                        b <<= 2;
                        while ((b & 0x80) != 0) { b <<= 1; remainingBytes++; }
                    }
                }
                else
                {
                    if ((b & 0xC0) != 0x80) return false;
                    remainingBytes--;
                }
            }
            return remainingBytes == 0 && hasNonAscii;
        }

        public static bool IsDeviceAccessible(HikDeviceInfo deviceInfo, HikAccessMode accessMode = HikAccessMode.Exclusive)
        {
            var rawInfo = deviceInfo.RawDeviceInfo;
            return MvCodeReader.MV_CODEREADER_IsDeviceAccessible_NET(ref rawInfo, (uint)accessMode);
        }

        #endregion

        #region 连接与断开

        /// <summary>通过设备信息连接设备。失败时抛出 HikScannerException。</summary>
        public void Connect(HikDeviceInfo deviceInfo)
        {
            ThrowIfDisposed();
            if (deviceInfo == null) throw new ArgumentNullException(nameof(deviceInfo));
            lock (_grabLock)
            {
                DisconnectInternal();

                if (_device == null) _device = new CodeReaderSdkAdapter();

                var rawInfo = deviceInfo.RawDeviceInfo;
                int nRet = _device.MV_CODEREADER_CreateHandle_NET(ref rawInfo);
                if (nRet != MvCodeReader.MV_CODEREADER_OK) { _device = null; throw new HikScannerException("创建句柄失败", nRet); }

                nRet = _device.MV_CODEREADER_OpenDevice_NET();
                if (nRet != MvCodeReader.MV_CODEREADER_OK) { _device.MV_CODEREADER_DestroyHandle_NET(); _device = null; throw new HikScannerException("打开设备失败", nRet); }

                int triggerRet = _device.MV_CODEREADER_SetEnumValue_NET("TriggerMode", (uint)MvCodeReader.MV_CODEREADER_TRIGGER_MODE.MV_CODEREADER_TRIGGER_MODE_OFF);
                if (triggerRet != MvCodeReader.MV_CODEREADER_OK)
                    Log(LogLevel.Warning, $"[Connect] 关闭触发模式失败: 0x{triggerRet:X8}");
                _savedTriggerMode = HikTriggerMode.Continuous;

                _currentDeviceInfo = deviceInfo;
                _isConnected = true;
                OnConnectionStateChanged(HikConnectionState.Connected);

                ConfigureGigEPacketSize(deviceInfo.RawDeviceInfo.nTLayerType);
            }
        }

        /// <summary>通过序列号连接设备。失败时抛出 HikScannerException。</summary>
        public void ConnectBySerialNumber(string serialNumber)
        {
            ThrowIfDisposed();
            if (string.IsNullOrEmpty(serialNumber)) throw new ArgumentNullException(nameof(serialNumber));
            lock (_grabLock)
            {
                DisconnectInternal();
                if (_device == null) _device = new CodeReaderSdkAdapter();

                int nRet = _device.MV_CODEREADER_CreateHandleBySerialNumber_NET(serialNumber);
                // #4: 错误清理统一 — 失败时 _device 置 null
                if (nRet != MvCodeReader.MV_CODEREADER_OK) { _device = null; throw new HikScannerException("通过序列号创建句柄失败", nRet); }

                nRet = _device.MV_CODEREADER_OpenDevice_NET();
                if (nRet != MvCodeReader.MV_CODEREADER_OK) { _device.MV_CODEREADER_DestroyHandle_NET(); _device = null; throw new HikScannerException("打开设备失败", nRet); }

                // #10: 连接时关闭触发模式（默认 Continuous），保存到 _savedTriggerMode 供重连恢复
                // #2: 检查返回值，失败时记录日志但不中断连接流程
                int triggerRet2 = _device.MV_CODEREADER_SetEnumValue_NET("TriggerMode", (uint)MvCodeReader.MV_CODEREADER_TRIGGER_MODE.MV_CODEREADER_TRIGGER_MODE_OFF);
                if (triggerRet2 != MvCodeReader.MV_CODEREADER_OK)
                    Log(LogLevel.Warning, $"[ConnectBySerialNumber] 关闭触发模式失败: 0x{triggerRet2:X8}");
                _savedTriggerMode = HikTriggerMode.Continuous;

                var devInfo = new MvCodeReader.MV_CODEREADER_DEVICE_INFO();
                nRet = _device.MV_CODEREADER_GetDeviceInfo_NET(ref devInfo);
                // #7: 获取设备信息失败时记录日志，便于诊断
                if (nRet == MvCodeReader.MV_CODEREADER_OK) _currentDeviceInfo = ParseDeviceInfo(devInfo);
                else Log(LogLevel.Warning, $"[ConnectBySerialNumber] GetDeviceInfo 失败: 0x{nRet:X8}");

                _isConnected = true;
                OnConnectionStateChanged(HikConnectionState.Connected);

                // #7: GigE 设备设置最优包大小
                if (_currentDeviceInfo != null)
                    ConfigureGigEPacketSize(_currentDeviceInfo.RawDeviceInfo.nTLayerType);
                // #6: 移除无意义的 ConfigureGigEPacketSize(0) else 分支
            }
        }

        /// <summary>内联断开逻辑（不调 StopGrabbing/JoinMscThreads，用于 Connect 内部复用）</summary>
        private void DisconnectInternal()
        {
            if (_isGrabbing)
            {
                _isGrabbing = false;
                // REVIEW-FIX: 只 Cancel 不立即 Dispose，CTS 由采集循环任务结束后兜底释放，避免 use-after-dispose
                CancelGrabCts();
                _device?.MV_CODEREADER_StopGrabbing_NET();
            }
            // #5: 注销 SDK 回调，防止已释放的回调被触发
            if (_isConnected && _device != null)
            {
                if (_imageCallback != null)
                    _device.MV_CODEREADER_RegisterImageCallBackEx2_NET(null, IntPtr.Zero);
                if (_exceptionCallback != null)
                    _device.MV_CODEREADER_RegisterExceptionCallBack_NET(null, IntPtr.Zero);
            }
            if (_isConnected)
            {
                _device?.MV_CODEREADER_CloseDevice_NET();
                _device?.MV_CODEREADER_DestroyHandle_NET();
                _isConnected = false;
            }
            _imageCallback = null;
            _exceptionCallback = null;
            _mscCallback0 = null;
            _mscCallback1 = null;
        }

        /// <summary>配置 GigE 设备的最佳包大小</summary>
        /// <remarks>#10: 仅对 GigE 设备生效。deviceInfo 来自枚举结果或 GetDeviceInfo，
        /// Connect 路径传入的 deviceInfo 参数不会为 null；ConnectBySerialNumber 中
        /// GetDeviceInfo 失败时 _currentDeviceInfo 为 null，但 nTLayerType 默认为 0，
        /// 不会进入 GigE 分支，逻辑安全。</remarks>
        private void ConfigureGigEPacketSize(uint tLayerType)
        {
            if (tLayerType != MvCodeReader.MV_CODEREADER_GIGE_DEVICE) return;
            // #5: 防御性检查 _device null
            if (_device == null) { Log(LogLevel.Warning, "[Connect] ConfigureGigEPacketSize: _device 为 null"); return; }
            int packetSize = _device.MV_CODEREADER_GetOptimalPacketSize_NET();
            if (packetSize > 0)
            {
                int psRet = _device.MV_CODEREADER_SetIntValue_NET("GevSCPSPacketSize", packetSize);
                if (psRet != MvCodeReader.MV_CODEREADER_OK)
                    Log(LogLevel.Warning, $"[Connect] 设置 PacketSize={packetSize} 失败: 0x{psRet:X8}");
            }
            else
            {
                Log(LogLevel.Warning, "[Connect] GetOptimalPacketSize 返回无效值");
            }
        }

        /// <summary>异步通过设备信息连接设备。</summary>
        public Task ConnectAsync(HikDeviceInfo deviceInfo) => Task.Run(() => Connect(deviceInfo));

        /// <summary>异步通过设备信息连接设备，支持取消令牌。</summary>
        public Task ConnectAsync(HikDeviceInfo deviceInfo, CancellationToken cancellationToken)
            => Task.Run(() => { cancellationToken.ThrowIfCancellationRequested(); Connect(deviceInfo); }, cancellationToken);

        /// <summary>异步通过序列号连接设备。</summary>
        public Task ConnectBySerialNumberAsync(string serialNumber) => Task.Run(() => ConnectBySerialNumber(serialNumber));

        /// <summary>异步通过序列号连接设备，支持取消令牌。</summary>
        public Task ConnectBySerialNumberAsync(string serialNumber, CancellationToken cancellationToken)
            => Task.Run(() => { cancellationToken.ThrowIfCancellationRequested(); ConnectBySerialNumber(serialNumber); }, cancellationToken);

        /// <summary>断开设备连接。</summary>
        /// <param name="clearEvents">是否清空所有事件订阅，默认 false（保留用户订阅，支持重连后继续接收回调）</param>
        public void Disconnect(bool clearEvents = false)
        {
            // #4: Dispose 后不再操作
            if (_disposed) return;
            lock (_grabLock)
            {
                // #5: StopGrabbing 内部也 lock(_grabLock)，Monitor 支持同线程可重入，不会死锁
                if (_isGrabbing) StopGrabbing();
                if (_isConnected)
                {
                    // #5: 注销 SDK 回调，防止已释放的回调被触发
                    if (_imageCallback != null)
                        _device?.MV_CODEREADER_RegisterImageCallBackEx2_NET(null, IntPtr.Zero);
                    if (_exceptionCallback != null)
                        _device?.MV_CODEREADER_RegisterExceptionCallBack_NET(null, IntPtr.Zero);
                    _device?.MV_CODEREADER_CloseDevice_NET();
                    _device?.MV_CODEREADER_DestroyHandle_NET();
                    _isConnected = false;
                    // #2: 主动断开也触发 ConnectionStateChanged
                    OnConnectionStateChanged(HikConnectionState.Disconnected);
                }
                _imageCallback = null;
                _exceptionCallback = null;
                _mscCallback0 = null;
                _mscCallback1 = null;
            }

            if (clearEvents)
            {
                // #1/#9: 仅在明确请求时清空事件，并同步清理 ApplyOptions 委托引用
                lock (_eventLock)
                {
                    ImageGrabbed = null;
                    DeviceDisconnected = null;
                    DeviceReconnected = null;
                    ReconnectError = null;
                    ConnectionStateChanged = null;
                    _appliedDisconnectedHandler = null;
                    _appliedReconnectedHandler = null;
                    _appliedFrameGrabbedHandler = null;
                }
            }
        }

        #endregion

        #region 采集控制

        public void StartGrabbing()
        {
            ThrowIfDisposed();
            lock (_grabLock)
            {
                if (!_isConnected) throw new InvalidOperationException("设备未连接");
                if (_isGrabbing) throw new InvalidOperationException("设备已在采集状态，请先调用 StopGrabbing");
                int nRet = _device.MV_CODEREADER_StartGrabbing_NET();
                if (nRet != MvCodeReader.MV_CODEREADER_OK) throw new HikScannerException("开始采集失败", nRet);
                _isGrabbing = true;
                _activeGrabMode = HikGrabMode.Polling;
                StartImageChannel();
            }
        }

        /// <summary>#1: 初始化有界 Channel 并启动消费线程（容量 8，满时丢弃旧帧）</summary>
        private void StartImageChannel()
        {
            _imageChannel = System.Threading.Channels.Channel.CreateBounded<HikGrabResult>(
                new System.Threading.Channels.BoundedChannelOptions(8)
                {
                    FullMode = System.Threading.Channels.BoundedChannelFullMode.DropOldest,
                    SingleReader = true,
                    SingleWriter = false
                });
            var reader = _imageChannel.Reader;
            _channelConsumerTask = Task.Run(async () =>
            {
                try
                {
                    await foreach (var result in reader.ReadAllAsync())
                    {
                        try { OnImageGrabbed(result); }
                        catch (Exception ex) { Log(LogLevel.Error, "[ChannelConsumer] 事件处理异常", ex); }
                    }
                }
                catch (Exception ex) { Log(LogLevel.Error, "[ChannelConsumer] 消费线程异常", ex); }
            });
        }

        /// <summary>#1: 关闭 Channel 并等待消费线程退出</summary>
        private void StopImageChannel()
        {
            if (_imageChannel != null)
            {
                try { _imageChannel.Writer.TryComplete(); } catch { }
                _imageChannel = null;
            }
        }

        public void StopGrabbing()
        {
            // #9: Dispose 后不操作，防止访问已释放资源
            if (_disposed) return;
            lock (_grabLock)
            {
                if (!_isGrabbing) return;
                _isGrabbing = false;
                // REVIEW-FIX: 只 Cancel 不立即 Dispose，CTS 由采集循环任务结束后兜底释放，避免 use-after-dispose
                CancelGrabCts();
                _device?.MV_CODEREADER_StopGrabbing_NET();
            }
            // #1: 在锁外关闭 Channel，避免与回调线程死锁
            StopImageChannel();
            JoinMscThreads();
        }

        /// <summary>
        /// REVIEW-FIX: 取消并解除引用采集 CTS，但不立即 Dispose。
        /// CTS 由采集循环任务（StartGrabbingLoop / RestoreGrabModeAfterReconnect）的
        /// 完成回调在任务结束后兜底释放，避免采集循环访问已释放 token 抛 ObjectDisposedException。
        /// </summary>
        private void CancelGrabCts()
        {
            var cts = Interlocked.Exchange(ref _grabCts, null);
            if (cts == null) return;
            try { cts.Cancel(); }
            catch (ObjectDisposedException) { /* 已被其他路径释放，忽略 */ }
        }

        /// <summary>获取一帧图像和读码结果。通过 result.Status 区分超时/无数据/成功。</summary>
        /// <param name="timeoutMs">超时毫秒数，超时返回 Status=Timeout</param>
        // REVIEW-FIX: 默认超时由 6000_000ms（100 分钟，疑为笔误）改为 6000ms（6 秒）。
        // 已确认所有调用方均显式传参（Demo/Tests/Compat.GetImageAsync），不依赖默认值。
        public HikGrabResult GrabOneFrame(int timeoutMs = 6000)
        {
            // #10: 加 _isConnected 检查，断线后不访问已断开的 _device
            if (!_isGrabbing || !_isConnected || _disposed) return new HikGrabResult { Status = HikGrabStatus.Error };
            return GrabOneFrameInternal((uint)timeoutMs);
        }

        /// <summary>获取一帧图像和读码结果。</summary>
        /// <param name="timeout">超时时间</param>
        public HikGrabResult GrabOneFrame(TimeSpan timeout)
        {
            // #4: 加 _isConnected 检查，与 int 重载一致
            if (!_isGrabbing || !_isConnected || _disposed) return new HikGrabResult { Status = HikGrabStatus.Error };
            return GrabOneFrameInternal((uint)timeout.TotalMilliseconds);
        }

        /// <summary>获取一帧图像和读码结果，支持取消令牌。</summary>
        /// <param name="timeoutMs">超时毫秒数</param>
        /// <param name="cancellationToken">取消令牌，取消时返回 Status=Error</param>
        public HikGrabResult GrabOneFrame(int timeoutMs, CancellationToken cancellationToken)
        {
            // #10: 加 _isConnected 检查，与无参版本一致
            if (!_isGrabbing || !_isConnected || _disposed) return new HikGrabResult { Status = HikGrabStatus.Error };
            if (timeoutMs <= 0)
            {
                if (cancellationToken.IsCancellationRequested) return new HikGrabResult { Status = HikGrabStatus.Error };
                return GrabOneFrameInternal(0u);
            }
            int step = Math.Min(timeoutMs, 200);
            for (int i = 0; i < timeoutMs; i += step)
            {
                if (cancellationToken.IsCancellationRequested) return new HikGrabResult { Status = HikGrabStatus.Error };
                int chunk = Math.Min(step, timeoutMs - i);
                var result = GrabOneFrameInternal((uint)chunk);
                if (result.Status != HikGrabStatus.Timeout) return result;
            }
            return new HikGrabResult { Status = HikGrabStatus.Timeout };
        }

        /// <summary>获取一帧图像和读码结果，支持取消令牌。</summary>
        /// <param name="timeout">超时时间</param>
        /// <param name="cancellationToken">取消令牌</param>
        public HikGrabResult GrabOneFrame(TimeSpan timeout, CancellationToken cancellationToken)
        {
            // #9: 直接检查 _isConnected，不依赖 int 重载的实现细节
            if (!_isGrabbing || !_isConnected || _disposed) return new HikGrabResult { Status = HikGrabStatus.Error };
            return GrabOneFrame((int)timeout.TotalMilliseconds, cancellationToken);
        }

        /// <summary>批量采集多帧图像。遇到超时帧会跳过，遇到错误帧立即返回已采集的结果。</summary>
        /// <param name="count">要采集的帧数</param>
        /// <param name="timeoutMs">每帧超时毫秒数，默认 1000</param>
        /// <param name="cancellationToken">取消令牌</param>
        /// <returns>已采集的帧列表（可能少于 count）</returns>
        public List<HikGrabResult> GrabFrames(int count, int timeoutMs = 1000, CancellationToken cancellationToken = default)
        {
            if (count <= 0) throw new ArgumentOutOfRangeException(nameof(count), "帧数必须大于 0");
            // #3: 加 _isConnected 检查，与 GrabOneFrame 一致
            if (!_isGrabbing || !_isConnected || _disposed) return new List<HikGrabResult>();
            var results = new List<HikGrabResult>(count);
            for (int i = 0; i < count; i++)
            {
                if (cancellationToken.IsCancellationRequested) break;
                var result = GrabOneFrame(timeoutMs, cancellationToken);
                if (result.Status == HikGrabStatus.Error) break;
                if (result.Status != HikGrabStatus.Timeout) results.Add(result);
            }
            return results;
        }

        /// <summary>后台轮询采集，通过 ImageGrabbed 事件推送结果。</summary>
        public void StartGrabbingLoop(int timeoutMs = 1000)
        {
            CancellationTokenSource cts;
            lock (_grabLock)
            {
                // #5: ThrowIfDisposed 移入锁内，防止 Dispose 并发竞态
                ThrowIfDisposed();
                _grabLoopTimeoutMs = timeoutMs;
                if (!_isConnected) throw new InvalidOperationException("设备未连接");
                // #7: 统一行为：已在采集时抛异常，与 StartGrabbing 一致
                if (_isGrabbing) throw new InvalidOperationException("设备已在采集状态，请先调用 StopGrabbing");
                // #2: 检查 StartGrabbing 返回值
                int nRet = _device.MV_CODEREADER_StartGrabbing_NET();
                if (nRet != MvCodeReader.MV_CODEREADER_OK) throw new HikScannerException("开始采集失败", nRet);
                _isGrabbing = true;
                _activeGrabMode = HikGrabMode.Polling;
                cts = new CancellationTokenSource();
                Interlocked.Exchange(ref _grabCts, cts);
            }
            // #7: 保存 Task 引用并添加异常观察，防止未捕获异常被 GC 静默吞掉
            // REVIEW-FIX: 采集循环改用 token 结构体副本，避免 StopGrabbing/Dispose 释放 CTS 后的 use-after-dispose
            _grabLoopTask = Task.Run(() =>
            {
                var token = cts.Token;
                while (!token.IsCancellationRequested && _isGrabbing)
                {
                    try
                    {
                        var result = GrabOneFrameInternal((uint)timeoutMs);
                        if (result != null) OnImageGrabbed(result);
                    }
                    catch (Exception ex)
                    {
                        Log(LogLevel.Error, "[StartGrabbingLoop] 采集异常", ex);
                        // #1: 设备断线时停止循环，避免无限重试空转
                        if (!_isConnected || _disposed) break;
                        // #7: 使用 WaitHandle.WaitOne 响应取消令牌
                        try { if (token.WaitHandle.WaitOne(100)) break; }
                        catch (ObjectDisposedException) { break; }
                    }
                }
            }, cts.Token);
            _grabLoopTask.ContinueWith(t =>
            {
                // REVIEW-FIX: 采集循环是 CTS 最后的持有者，任务结束后兜底释放
                Interlocked.CompareExchange(ref _grabCts, null, cts);
                try { cts.Dispose(); } catch (ObjectDisposedException) { }
            }, TaskScheduler.Default);
            _grabLoopTask.ContinueWith(t =>
            {
                if (t.IsFaulted && t.Exception != null)
                    Log(LogLevel.Error, "[StartGrabbingLoop] 任务异常", t.Exception);
            }, TaskContinuationOptions.OnlyOnFaulted);
        }

        /// <summary>核心采集方法：通过 SDK 获取一帧并解析。全程持 _bufferLock 防止并发覆盖。</summary>
        private HikGrabResult GrabOneFrameInternal(uint timeoutMs)
        {
            // #3: 入口检查 _isConnected/_disposed，保护所有调用路径（StartGrabbingLoop/RestoreGrabMode 等）
            if (!_isConnected || _disposed || _device == null)
                return new HikGrabResult { Status = HikGrabStatus.Error };
            lock (_bufferLock)
            {
                IntPtr pData = IntPtr.Zero;
                int infoSize = Marshal.SizeOf(typeof(MvCodeReader.MV_CODEREADER_IMAGE_OUT_INFO_EX2));
                if (_pFrameInfo == IntPtr.Zero || _frameInfoSize < infoSize)
                {
                    if (_pFrameInfo != IntPtr.Zero) Marshal.FreeHGlobal(_pFrameInfo);
                    _pFrameInfo = Marshal.AllocHGlobal(infoSize);
                    _frameInfoSize = infoSize;
                }
                IntPtr pFrameInfo = _pFrameInfo;
                Marshal.StructureToPtr(new MvCodeReader.MV_CODEREADER_IMAGE_OUT_INFO_EX2(), pFrameInfo, false);
                int nRet = _device.MV_CODEREADER_GetOneFrameTimeoutEx2_NET(ref pData, pFrameInfo, timeoutMs);
                if (nRet != MvCodeReader.MV_CODEREADER_OK)
                {
                    bool isTimeout = nRet == MvCodeReader.MV_CODEREADER_E_NODATA;
                    return new HikGrabResult { Status = isTimeout ? HikGrabStatus.Timeout : HikGrabStatus.Error, RawErrorCode = nRet };
                }
                var stFrameInfo = (MvCodeReader.MV_CODEREADER_IMAGE_OUT_INFO_EX2)Marshal.PtrToStructure(pFrameInfo, typeof(MvCodeReader.MV_CODEREADER_IMAGE_OUT_INFO_EX2));
                if (stFrameInfo.nFrameLen == 0) return new HikGrabResult { Status = HikGrabStatus.NoData };
                return ParseFrameData(pData, pFrameInfo, stFrameInfo);
            }
        }

        /// <summary>回调模式采集：注册 SDK 图像回调，帧数据通过 ImageGrabbed 事件推送。</summary>
        public void StartGrabbingWithCallback()
        {
            ThrowIfDisposed();
            lock (_grabLock)
            {
                if (!_isConnected) throw new InvalidOperationException("设备未连接");
                // #7: 统一行为：已在采集时抛异常，与 StartGrabbing 一致
                if (_isGrabbing) throw new InvalidOperationException("设备已在采集状态，请先调用 StopGrabbing");

                _imageCallback = new MvCodeReader.cbOutputEx2delegate(OnImageCallback);
                // #7: 检查 _imageCallback null，防止注册 null 回调
                if (_imageCallback == null) throw new InvalidOperationException("图像回调委托创建失败");
                int nRet = _device.MV_CODEREADER_RegisterImageCallBackEx2_NET(_imageCallback, IntPtr.Zero);
                // #2: 回调注册失败时清理回调委托
                if (nRet != MvCodeReader.MV_CODEREADER_OK)
                {
                    _imageCallback = null;
                    throw new HikScannerException("注册图像回调失败", nRet);
                }

                if (_autoReconnect)
                {
                    _exceptionCallback = new MvCodeReader.cbExceptiondelegate(OnExceptionCallback);
                    // #6: 检查异常回调注册返回值
                    int excRet = _device.MV_CODEREADER_RegisterExceptionCallBack_NET(_exceptionCallback, IntPtr.Zero);
                    if (excRet != MvCodeReader.MV_CODEREADER_OK)
                    {
                        // 回调注册失败，注销图像回调并清理
                        _device.MV_CODEREADER_RegisterImageCallBackEx2_NET(null, IntPtr.Zero);
                        _imageCallback = null;
                        _exceptionCallback = null;
                        throw new HikScannerException("注册异常回调失败", excRet);
                    }
                    GC.KeepAlive(_exceptionCallback);
                }

                nRet = _device.MV_CODEREADER_StartGrabbing_NET();
                // #3: 采集开始失败时注销回调并清理
                if (nRet != MvCodeReader.MV_CODEREADER_OK)
                {
                    _device.MV_CODEREADER_RegisterImageCallBackEx2_NET(null, IntPtr.Zero);
                    _imageCallback = null;
                    _exceptionCallback = null;
                    throw new HikScannerException("开始采集失败", nRet);
                }
                _isGrabbing = true;
                _activeGrabMode = HikGrabMode.Callback;
                StartImageChannel();
            }
        }

        private void OnImageCallback(IntPtr pData, IntPtr pstFrameInfoEx2, IntPtr pUser)
        {
            if (pData == IntPtr.Zero || pstFrameInfoEx2 == IntPtr.Zero) return;
            // #7: 捕获回调异常，防止 SDK 回调线程崩溃
            try
            {
                var stFrameInfo = (MvCodeReader.MV_CODEREADER_IMAGE_OUT_INFO_EX2)Marshal.PtrToStructure(pstFrameInfoEx2, typeof(MvCodeReader.MV_CODEREADER_IMAGE_OUT_INFO_EX2));
                if (stFrameInfo.nFrameLen == 0) return;
                // #1: ParseFrameData 必须同步执行（pData/pstFrameInfoEx2 仅在回调期间有效）
                var result = ParseFrameData(pData, pstFrameInfoEx2, stFrameInfo);
                // #1: 通过有界 Channel 投递事件，背压控制（满时丢弃旧帧），避免 Task.Run 无限堆积
                _imageChannel?.Writer.TryWrite(result);
            }
            catch (Exception ex)
            {
                Log(LogLevel.Error, "[OnImageCallback] 回调解析异常", ex);
            }
        }

        #endregion

        #region 参数读写

        /// <summary>检查设备连接状态，未连接时抛出 InvalidOperationException</summary>
        private void EnsureConnected()
        {
            if (!_isConnected || _device == null) throw new InvalidOperationException("设备未连接");
        }

        /// <summary>获取浮点型参数值</summary>
        /// <param name="key">参数名（如 ExposureTime、Gain）</param>
        /// <returns>当前参数值</returns>
        public float GetFloatParam(string key) { EnsureConnected(); var p = new MvCodeReader.MV_CODEREADER_FLOATVALUE(); HikScannerException.Check(_device.MV_CODEREADER_GetFloatValue_NET(key, ref p), $"获取 {key}"); return p.fCurValue; }
        /// <summary>设置浮点型参数值</summary>
        /// <param name="key">参数名</param>
        /// <param name="value">参数值</param>
        public void SetFloatParam(string key, float value) { EnsureConnected(); HikScannerException.Check(_device.MV_CODEREADER_SetFloatValue_NET(key, value), $"设置 {key}"); }
        /// <summary>获取整型参数值</summary>
        /// <param name="key">参数名</param>
        /// <returns>当前参数值</returns>
        public long GetIntParam(string key) { EnsureConnected(); var p = new MvCodeReader.MV_CODEREADER_INTVALUE_EX(); HikScannerException.Check(_device.MV_CODEREADER_GetIntValue_NET(key, ref p), $"获取 {key}"); return p.nCurValue; }
        /// <summary>设置整型参数值</summary>
        /// <param name="key">参数名</param>
        /// <param name="value">参数值</param>
        public void SetIntParam(string key, long value) { EnsureConnected(); HikScannerException.Check(_device.MV_CODEREADER_SetIntValue_NET(key, value), $"设置 {key}"); }
        /// <summary>获取布尔型参数值</summary>
        /// <param name="key">参数名</param>
        /// <returns>当前参数值</returns>
        public bool GetBoolParam(string key) { EnsureConnected(); bool v = false; HikScannerException.Check(_device.MV_CODEREADER_GetBoolValue_NET(key, ref v), $"获取 {key}"); return v; }
        /// <summary>设置布尔型参数值</summary>
        /// <param name="key">参数名</param>
        /// <param name="value">参数值</param>
        public void SetBoolParam(string key, bool value) { EnsureConnected(); HikScannerException.Check(_device.MV_CODEREADER_SetBoolValue_NET(key, value), $"设置 {key}"); }
        /// <summary>获取字符串参数值</summary>
        /// <param name="key">参数名</param>
        /// <returns>当前参数值</returns>
        public string GetStringParam(string key) { EnsureConnected(); var p = new MvCodeReader.MV_CODEREADER_STRINGVALUE(); HikScannerException.Check(_device.MV_CODEREADER_GetStringValue_NET(key, ref p), $"获取 {key}"); return p.chCurValue.TrimEnd('\0'); }
        /// <summary>设置字符串参数值</summary>
        /// <param name="key">参数名</param>
        /// <param name="value">参数值</param>
        public void SetStringParam(string key, string value) { EnsureConnected(); HikScannerException.Check(_device.MV_CODEREADER_SetStringValue_NET(key, value), $"设置 {key}"); }
        /// <summary>设置枚举型参数值（数字）</summary>
        /// <param name="key">参数名</param>
        /// <param name="value">枚举值</param>
        public void SetEnumParam(string key, uint value) { EnsureConnected(); HikScannerException.Check(_device.MV_CODEREADER_SetEnumValue_NET(key, value), $"设置枚举 {key}"); }
        /// <summary>设置枚举型参数值（字符串）</summary>
        /// <param name="key">参数名</param>
        /// <param name="value">枚举字符串值</param>
        public void SetEnumParamByString(string key, string value) { EnsureConnected(); HikScannerException.Check(_device.MV_CODEREADER_SetEnumValueByString_NET(key, value), $"设置枚举 {key}"); }
        /// <summary>执行命令型参数（如 TriggerSoftware）</summary>
        /// <param name="key">命令名</param>
        public void ExecuteCommand(string key) { EnsureConnected(); HikScannerException.Check(_device.MV_CODEREADER_SetCommandValue_NET(key), $"执行命令 {key}"); }

        /// <summary>获取枚举型参数的当前值和可选值列表</summary>
        /// <summary>获取枚举参数信息（含当前值和所有支持值）</summary>
        /// <param name="key">参数名</param>
        /// <returns>枚举信息，包含当前值和所有支持的值</returns>
        public HikEnumInfo GetEnumValue(string key)
        {
            EnsureConnected();
            var p = new MvCodeReader.MV_CODEREADER_ENUMVALUE();
            HikScannerException.Check(_device.MV_CODEREADER_GetEnumValue_NET(key, ref p), $"获取枚举 {key}");
            // REVIEW-FIX: 防御 SDK 返回异常的 nSupportedNum，避免 Array.Copy 越界读取内联数组
            // nSupportValue（SDK 枚举支持值通常远小于该上限，正常场景不受影响）。
            var supportedNum = (int)Math.Min(p.nSupportedNum, MaxEnumSupportValues);
            var info = new HikEnumInfo { CurrentValue = p.nCurValue, SupportedValues = new uint[supportedNum] };
            Array.Copy(p.nSupportValue, info.SupportedValues, supportedNum);
            return info;
        }

        #endregion

        #region 便捷方法

        /// <summary>设置触发模式</summary>
        /// <param name="mode">触发模式（连续/软触发/硬触发）</param>
        public void SetTriggerMode(HikTriggerMode mode) { SetEnumParam("TriggerMode", (uint)mode); _savedTriggerMode = mode; _hasSavedParams = true; }
        /// <summary>设置触发源</summary>
        /// <param name="source">触发源（Line0/Line1/Line2/Counter0/Software）</param>
        public void SetTriggerSource(HikTriggerSource source) { SetEnumParam("TriggerSource", (uint)source); _savedTriggerSource = source; _hasSavedParams = true; }
        /// <summary>执行软触发命令</summary>
        public void TriggerSoftware() { ThrowIfDisposed(); ExecuteCommand("TriggerSoftware"); }

        #endregion

        #region 面单与算法

        /// <summary>启用或禁用面单识别功能</summary>
        /// <param name="enable">true 启用，false 禁用</param>
        public void SetWayBillEnable(bool enable)
        {
            EnsureConnected();
            // #4: 检查返回值，与其他参数方法一致
            HikScannerException.Check(_device.MV_CODEREADER_SetWayBillEnable_NET(enable), "设置面单识别");
            _savedWayBillEnable = enable;
            _hasSavedParams = true;
        }

        /// <summary>设置算法整型参数</summary>
        /// <param name="key">算法参数名</param>
        /// <param name="value">参数值</param>
        public void AlgorithmSetIntValue(string key, int value)
        {
            EnsureConnected();
            // #4: 检查返回值，与其他参数方法一致
            HikScannerException.Check(_device.MV_CODEREADER_Algorithm_SetIntValue_NET(key, value), $"设置算法参数 {key}");
        }

        /// <summary>获取算法整型参数</summary>
        /// <param name="key">算法参数名</param>
        /// <returns>当前参数值</returns>
        public int AlgorithmGetIntValue(string key)
        {
            EnsureConnected();
            int value = 0;
            HikScannerException.Check(_device.MV_CODEREADER_Algorithm_GetIntValue_NET(key, ref value), $"获取算法参数 {key}");
            return value;
        }

        #endregion

        #region 图像保存

        /// <summary>使用 GDI+ 保存图像（无需连接设备）。</summary>
        /// <param name="image">图像数据</param>
        /// <param name="filePath">保存路径，必须包含 .bmp 或 .jpg/.jpeg 扩展名</param>
        public void SaveImage(HikImageData image, string filePath)
        {
            if (image?.RawData == null) throw new ArgumentNullException(nameof(image));
            string ext = Path.GetExtension(filePath).ToLower();
            if (string.IsNullOrEmpty(ext))
                throw new ArgumentException("文件路径必须包含扩展名(.bmp/.jpg/.jpeg)", nameof(filePath));
            if (ext == ".bmp") { using (var bmp = image.ToBitmap()) bmp?.Save(filePath, ImageFormat.Bmp); }
            else
            {
                var jpegData = image.ToJpegBytes();
                // #8: 加长度检查，空数据时不写入空文件
                if (jpegData == null || jpegData.Length == 0)
                    throw new ArgumentException("图像数据无效，无法保存为 JPEG", nameof(image));
                File.WriteAllBytes(filePath, jpegData);
            }
        }

        /// <summary>使用 SDK 原生编码保存图像（需要连接设备）。</summary>
        /// <param name="image">图像数据</param>
        /// <param name="filePath">保存路径，必须包含 .bmp 或 .jpg/.jpeg 扩展名</param>
        /// <param name="jpegQuality">JPEG 质量（1-100），默认 80</param>
        public void SaveImageNative(HikImageData image, string filePath, uint jpegQuality = 80u)
        {
            if (image?.RawData == null) throw new ArgumentNullException(nameof(image));
            EnsureConnected();

            string ext = Path.GetExtension(filePath).ToLower();
            if (string.IsNullOrEmpty(ext))
                throw new ArgumentException("文件路径必须包含扩展名(.bmp/.jpg/.jpeg)", nameof(filePath));
            uint imageType = ext == ".bmp" ? 1u : 2u;

            // #3: 用 long 计算防止 int 溢出，加上限检查
            long bufSizeLong = (long)image.Width * image.Height * 3 + 1024;
            // #4: 复用 MaxFrameSizeBytes 配置，与 ParseFrameData 一致
            if (bufSizeLong > MaxFrameSizeBytes)
                throw new ArgumentException($"图像尺寸过大({image.Width}x{image.Height})，编码缓冲区超限", nameof(image));
            int bufSize = (int)bufSizeLong;
            byte[] outBuf = new byte[bufSize];
            GCHandle hOut = GCHandle.Alloc(outBuf, GCHandleType.Pinned);
            GCHandle hIn = GCHandle.Alloc(image.RawData, GCHandleType.Pinned);
            try
            {
                var param = new MvCodeReader.MV_CODEREADER_SAVE_IMAGE_PARAM_EX
                {
                    pData = hIn.AddrOfPinnedObject(),
                    nDataLen = (uint)image.RawData.Length,
                    nWidth = (ushort)image.Width,
                    nHeight = (ushort)image.Height,
                    pImageBuffer = hOut.AddrOfPinnedObject(),
                    nBufferSize = (uint)bufSize,
                    enImageType = (MvCodeReader.MV_CODEREADER_IAMGE_TYPE)imageType,
                    nJpgQuality = jpegQuality
                };
                HikScannerException.Check(_device.MV_CODEREADER_SaveImage_NET(ref param), "保存图像");
                using var fs = new FileStream(filePath, FileMode.Create);
                fs.Write(outBuf, 0, (int)param.nImageLen);
            }
            finally
            {
                if (hOut.IsAllocated) hOut.Free();
                if (hIn.IsAllocated) hIn.Free();
            }
        }

        #endregion

        #region 内部解析方法

        private static HikDeviceInfo ParseDeviceInfo(MvCodeReader.MV_CODEREADER_DEVICE_INFO stDevInfo)
        {
            var info = new HikDeviceInfo
            {
                DeviceType = stDevInfo.nDeviceType,
                TLayerType = stDevInfo.nTLayerType,
                MacAddress = $"{stDevInfo.nMacAddrHigh >> 16:X4}-{stDevInfo.nMacAddrHigh & 0xFFFF:X4}-{stDevInfo.nMacAddrLow >> 16:X4}-{stDevInfo.nMacAddrLow & 0xFFFF:X4}",
                RawDeviceInfo = stDevInfo
            };

            if (stDevInfo.nTLayerType == MvCodeReader.MV_CODEREADER_GIGE_DEVICE)
            {
                IntPtr ptr = Marshal.UnsafeAddrOfPinnedArrayElement(stDevInfo.SpecialInfo.stGigEInfo, 0);
                var gigE = (MvCodeReader.MV_CODEREADER_GIGE_DEVICE_INFO)Marshal.PtrToStructure(ptr, typeof(MvCodeReader.MV_CODEREADER_GIGE_DEVICE_INFO));
                info.SerialNumber = gigE.chSerialNumber;
                info.ManufacturerName = gigE.chManufacturerName;
                info.ModelName = gigE.chModelName;
                info.UserDefinedName = gigE.chUserDefinedName;
                info.DeviceVersion = gigE.chDeviceVersion;
                info.CurrentIp = IpUintToString(gigE.nCurrentIp);
                info.SubnetMask = IpUintToString(gigE.nCurrentSubNetMask);
                info.DefaultGateway = IpUintToString(gigE.nDefultGateWay);
                info.IpConfigOption = gigE.nIpCfgOption;
                info.IpConfigCurrent = gigE.nIpCfgCurrent;
                info.NetExport = gigE.nNetExport;
            }
            else if (stDevInfo.nTLayerType == MvCodeReader.MV_CODEREADER_USB_DEVICE) // USB3 设备
            {
                var usb = (MvCodeReader.MV_CODEREADER_USB3_DEVICE_INFO)MvCodeReader.ByteToStruct(stDevInfo.SpecialInfo.stUsb3VInfo, typeof(MvCodeReader.MV_CODEREADER_USB3_DEVICE_INFO));
                info.SerialNumber = usb.chSerialNumber;
                info.ManufacturerName = usb.chManufacturerName;
                info.ModelName = usb.chModelName;
                // #6: 移除冗余的 GB2312 编码转换，直接赋值
                info.UserDefinedName = usb.chUserDefinedName;
                info.DeviceNumber = usb.nDeviceNumber;
            }
            return info;
        }

        private HikGrabResult ParseFrameData(IntPtr pData, IntPtr pstFrameInfo, MvCodeReader.MV_CODEREADER_IMAGE_OUT_INFO_EX2 stFrameInfo)
        {
            var result = new HikGrabResult();
            if (stFrameInfo.nFrameLen == 0) return result;
            // 直接分配精确大小的缓冲区并拷贝（每帧一次 Marshal.Copy）
            // 检查帧大小是否超过上限（可配置）
            if (stFrameInfo.nFrameLen > MaxFrameSizeBytes)
            {
                result.Status = HikGrabStatus.BufferOverflow;
                return result;
            }
            // #9: 检查 nFrameLen 是否超过 int.MaxValue，防止强转溢出为负数
            if (stFrameInfo.nFrameLen > int.MaxValue)
            {
                result.Status = HikGrabStatus.BufferOverflow;
                return result;
            }
            int frameLen = (int)stFrameInfo.nFrameLen;
            byte[] data = new byte[frameLen];
            Marshal.Copy(pData, data, 0, frameLen);
            result.Image = new HikImageData
            {
                RawData = data,
                Width = stFrameInfo.nWidth,
                Height = stFrameInfo.nHeight,
                FrameNum = stFrameInfo.nFrameNum,
                TriggerIndex = stFrameInfo.nTriggerIndex,
                ChannelId = stFrameInfo.nChannelID,
                IsMono8 = stFrameInfo.enPixelType == MvCodeReader.MvCodeReaderGvspPixelType.PixelType_CodeReader_Gvsp_Mono8,
                IsJpeg = stFrameInfo.enPixelType == MvCodeReader.MvCodeReaderGvspPixelType.PixelType_CodeReader_Gvsp_Jpeg
            };
            result.IsGetCode = stFrameInfo.bIsGetCode;
            result.Status = HikGrabStatus.Success;

            // 条码解析
            try
            {
                IntPtr pBcrList = stFrameInfo.UnparsedBcrList.pstCodeListEx2;
                if (pBcrList != IntPtr.Zero)
                {
                    var bcr = (MvCodeReader.MV_CODEREADER_RESULT_BCR_EX2)Marshal.PtrToStructure(pBcrList, typeof(MvCodeReader.MV_CODEREADER_RESULT_BCR_EX2));
                    ParseBcrListEx2(bcr, result);
                }
                else
                {
                    IntPtr pBcrListEx = stFrameInfo.pstCodeListEx;
                    if (pBcrListEx != IntPtr.Zero)
                    {
                        var bcr = (MvCodeReader.MV_CODEREADER_RESULT_BCR_EX)Marshal.PtrToStructure(pBcrListEx, typeof(MvCodeReader.MV_CODEREADER_RESULT_BCR_EX));
                        ParseBcrListEx(bcr, result);
                    }
                }
            }
            catch (Exception ex) { Log(LogLevel.Error, "[ParseFrameData] BCR解析异常", ex); }

            // OCR 解析
            try
            {
                if (stFrameInfo.UnparsedOcrList.pstOcrList != IntPtr.Zero)
                {
                    var ocrList = (MvCodeReader.MV_CODEREADER_OCR_INFO_LIST)Marshal.PtrToStructure(stFrameInfo.UnparsedOcrList.pstOcrList, typeof(MvCodeReader.MV_CODEREADER_OCR_INFO_LIST));
                    result.OcrResults = new List<HikOcrResult>();
                    // REVIEW-FIX: 取 SDK 计数与内联数组实际容量的较小值，防止越界读取
                    int ocrCount = (int)Math.Min((long)ocrList.nOCRAllNum, OcrArrayCapacity);
                    for (int i = 0; i < ocrCount; i++)
                    {
                        result.OcrResults.Add(new HikOcrResult
                        {
                            Id = (int)ocrList.stOcrRowInfo[i].nID,
                            Text = Encoding.UTF8.GetString(ocrList.stOcrRowInfo[i].chOcr).TrimEnd('\0'),
                            Length = (int)ocrList.stOcrRowInfo[i].nOcrLen,
                            CharConfidence = ocrList.stOcrRowInfo[i].fCharConfidence,
                            DetectConfidence = ocrList.stOcrRowInfo[i].fDeteConfidence,
                            CenterX = (int)ocrList.stOcrRowInfo[i].nOcrRowCenterX,
                            CenterY = (int)ocrList.stOcrRowInfo[i].nOcrRowCenterY,
                            Width = (int)ocrList.stOcrRowInfo[i].nOcrRowWidth,
                            Height = (int)ocrList.stOcrRowInfo[i].nOcrRowHeight,
                            Angle = ocrList.stOcrRowInfo[i].fOcrRowAngle,
                            AlgorithmCost = (short)ocrList.stOcrRowInfo[i].sOcrAlgoCost
                        });
                    }
                }
            }
            catch (Exception ex) { Log(LogLevel.Error, "[ParseFrameData] OCR解析异常", ex); }

            // 面单解析
            try
            {
                if (stFrameInfo.pstWaybillList != IntPtr.Zero)
                {
                    var waybillList = (MvCodeReader.MV_CODEREADER_WAYBILL_LIST)Marshal.PtrToStructure(stFrameInfo.pstWaybillList, typeof(MvCodeReader.MV_CODEREADER_WAYBILL_LIST));
                    result.Waybills = new List<HikWaybillResult>();
                    // REVIEW-FIX: 取 SDK 计数与内联数组实际容量的较小值，防止越界读取
                    int waybillCount = (int)Math.Min((long)waybillList.nWaybillNum, WaybillArrayCapacity);
                    for (int i = 0; i < waybillCount; i++)
                    {
                        var wb = waybillList.stWaybillInfo[i];
                        var wbResult = new HikWaybillResult
                        {
                            CenterX = wb.fCenterX,
                            CenterY = wb.fCenterY,
                            Width = wb.fWidth,
                            Height = wb.fHeight,
                            Angle = wb.fAngle,
                            Confidence = wb.fConfidence,
                            ImageLength = wb.nImageLen
                        };
                        if (wb.pImageWaybill != IntPtr.Zero && wb.nImageLen != 0)
                        {
                            // #4: 检查 nImageLen 溢出，防止 uint→int 强转变负数
                            if (wb.nImageLen <= int.MaxValue)
                            {
                                wbResult.WaybillImage = new byte[wb.nImageLen];
                                Marshal.Copy(wb.pImageWaybill, wbResult.WaybillImage, 0, (int)wb.nImageLen);
                            }
                        }
                        result.Waybills.Add(wbResult);
                    }
                }
            }
            catch (Exception ex) { Log(LogLevel.Error, "[ParseFrameData] 面单解析异常", ex); }

            return result;
        }

        // REVIEW-FIX: 内联数组实际容量（由结构体字段布局计算得出），
        // 防止 SDK 返回的计数 nCodeNum/nOCRAllNum/nWaybillNum 超出数组边界导致越界读取
        // REVIEW-FIX: 枚举支持值数量防御上限（SDK 实际通常 < 64，此处取 1024 足够宽松且防越界）
        private const int MaxEnumSupportValues = 1024;
        private static readonly int BcrEx2ArrayCapacity = ComputeInlineArrayCapacity(
            typeof(MvCodeReader.MV_CODEREADER_RESULT_BCR_EX2), "stBcrInfoEx2", "nReserved",
            typeof(MvCodeReader.MV_CODEREADER_BCR_INFO_EX2));
        private static readonly int BcrExArrayCapacity = ComputeInlineArrayCapacity(
            typeof(MvCodeReader.MV_CODEREADER_RESULT_BCR_EX), "stBcrInfoEx", "nReserved",
            typeof(MvCodeReader.MV_CODEREADER_BCR_INFO_EX));
        private static readonly int OcrArrayCapacity = ComputeInlineArrayCapacity(
            typeof(MvCodeReader.MV_CODEREADER_OCR_INFO_LIST), "stOcrRowInfo", "nReserved",
            typeof(MvCodeReader.MV_CODEREADER_OCR_ROW_INFO));
        private static readonly int WaybillArrayCapacity = ComputeInlineArrayCapacity(
            typeof(MvCodeReader.MV_CODEREADER_WAYBILL_LIST), "stWaybillInfo", "nOcrAllNum",
            typeof(MvCodeReader.MV_CODEREADER_WAYBILL_INFO));

        /// <summary>REVIEW-FIX: 计算内联数组容量 = (数组字段偏移到下一字段偏移的字节数) / 元素大小</summary>
        private static int ComputeInlineArrayCapacity(Type containerType, string arrayFieldName, string trailingFieldName, Type elementType)
        {
            try
            {
                long start = Marshal.OffsetOf(containerType, arrayFieldName).ToInt64();
                long end = Marshal.OffsetOf(containerType, trailingFieldName).ToInt64();
                long elementSize = Marshal.SizeOf(elementType);
                if (elementSize <= 0) return 0;
                return (int)Math.Max(0, (end - start) / elementSize);
            }
            catch
            {
                // REVIEW-FIX: 无法计算容量时保守返回 0（不遍历任何元素）
                return 0;
            }
        }

        private void ParseBcrListEx2(MvCodeReader.MV_CODEREADER_RESULT_BCR_EX2 bcr, HikGrabResult result)
        {
            result.Barcodes = new List<HikBarcodeResult>();
            // REVIEW-FIX: 取 SDK 计数与内联数组实际容量的较小值，防止越界读取
            int count = (int)Math.Min((long)bcr.nCodeNum, BcrEx2ArrayCapacity);
            for (int i = 0; i < count; i++)
            {
                var info = bcr.stBcrInfoEx2[i];
                result.Barcodes.Add(ParseBcrInfo(info.chCode, (int)info.nBarType, (int)info.nID, (short)info.nAngle, (short)info.sPPM, (short)info.sAlgoCost, (short)info.sSharpness, (int)info.nTotalProcCost, info.stCodeQuality.nOverQuality, (int)info.nIDRScore, new PointF[4] { new(info.pt[0].x, info.pt[0].y), new(info.pt[1].x, info.pt[1].y), new(info.pt[2].x, info.pt[2].y), new(info.pt[3].x, info.pt[3].y) }));
            }
        }

        private void ParseBcrListEx(MvCodeReader.MV_CODEREADER_RESULT_BCR_EX bcr, HikGrabResult result)
        {
            result.Barcodes = new List<HikBarcodeResult>();
            // REVIEW-FIX: 取 SDK 计数与内联数组实际容量的较小值，防止越界读取
            int count = (int)Math.Min((long)bcr.nCodeNum, BcrExArrayCapacity);
            for (int i = 0; i < count; i++)
            {
                var info = bcr.stBcrInfoEx[i];
                result.Barcodes.Add(ParseBcrInfo(info.chCode, (int)info.nBarType, (int)info.nID, (short)info.nAngle, (short)info.sPPM, (short)info.sAlgoCost, (short)info.sSharpness, (int)info.nTotalProcCost, info.stCodeQuality.nOverQuality, (int)info.nIDRScore, new PointF[4] { new(info.pt[0].x, info.pt[0].y), new(info.pt[1].x, info.pt[1].y), new(info.pt[2].x, info.pt[2].y), new(info.pt[3].x, info.pt[3].y) }));
            }
        }

        /// <summary>#10: 解析条码信息（参数精简，4 顶点合并为 PointF[]）</summary>
        private static HikBarcodeResult ParseBcrInfo(byte[] code, int barType, int id, short angle, short ppm, short algoCost, short sharpness, int totalCost, int overQuality, int idrScore, PointF[] boundingPoints)
        {
            return new HikBarcodeResult
            {
                Code = BcrCodeToString(code),
                CodeType = barType,
                CodeTypeName = HikBarcodeResult.GetCodeTypeName(barType),
                CodeId = id,
                Angle = angle,
                PPM = ppm,
                AlgorithmCost = algoCost,
                Sharpness = sharpness,
                TotalProcCost = totalCost,
                OverQuality = overQuality,
                IDRScore = idrScore,
                BoundingPoints = boundingPoints ?? Array.Empty<PointF>()
            };
        }

        // #10: 使用 Lazy<Encoding> 保证线程安全初始化，兼容 .NET 8
        private static readonly Lazy<Encoding> _gb2312EncodingLazy = new(() =>
        {
            try { return Encoding.GetEncoding("GB2312"); }
            catch { return Encoding.Default; } // 回退到系统默认编码
        });
        private static Encoding GB2312Encoding => _gb2312EncodingLazy.Value;

        private static string BcrCodeToString(byte[] chCode)
        {
            if (chCode == null || chCode.Length == 0) return "";
            bool isAscii = true;
            for (int i = 0; i < chCode.Length; i++) { if (chCode[i] >= 128) { isAscii = false; break; } }
            if (isAscii) return Encoding.ASCII.GetString(chCode).TrimEnd('\0');
            if (IsTextUtf8(chCode)) return Encoding.UTF8.GetString(chCode).TrimEnd('\0');
            // #7: 使用缓存的 GB2312 Encoding，避免每次查找编码提供者
            return GB2312Encoding.GetString(chCode).TrimEnd('\0');
        }

        #endregion

        #region 事件触发

        internal void OnImageGrabbed(HikGrabResult result)
        {
            EventHandler<HikGrabResult> handler;
            lock (_eventLock) { handler = ImageGrabbed; }
            handler?.Invoke(this, result);
        }

        internal void OnDeviceDisconnectedInternal()
        {
            // #1: 加锁保护状态变更，防止 OnExceptionCallback 与 Connect/Disconnect 并发竞态
            lock (_grabLock)
            {
                _isGrabbing = false;
                _isConnected = false;
            }
            OnConnectionStateChanged(HikConnectionState.Disconnected);
            EventHandler handler;
            lock (_eventLock) { handler = DeviceDisconnected; }
            handler?.Invoke(this, EventArgs.Empty);
        }

        internal void OnDeviceReconnectedInternal()
        {
            OnConnectionStateChanged(HikConnectionState.Connected);
            EventHandler handler;
            lock (_eventLock) { handler = DeviceReconnected; }
            handler?.Invoke(this, EventArgs.Empty);
        }

        /// <summary>触发连接状态变化事件</summary>
        private void OnConnectionStateChanged(HikConnectionState newState)
        {
            // #6: 加锁保护 _connectionState 读写，防止多线程并发触发错误事件
            HikConnectionState prevState;
            bool stateChanged;
            lock (_stateLock)
            {
                prevState = _connectionState;
                stateChanged = prevState != newState;
                if (stateChanged) _connectionState = newState;
            }
            if (!stateChanged) return;
            EventHandler<HikConnectionStateChangedEventArgs> handler;
            lock (_eventLock) { handler = ConnectionStateChanged; }
            handler?.Invoke(this, new HikConnectionStateChangedEventArgs { State = newState, PreviousState = prevState });
        }

        /// <summary>通知开始重连（由 Reconnect 调用）</summary>
        internal void OnReconnectingInternal() { OnConnectionStateChanged(HikConnectionState.Reconnecting); }

        #endregion

        #region IP 工具

        private static string IpUintToString(uint ip) => $"{(ip >> 24) & 0xFF}.{(ip >> 16) & 0xFF}.{(ip >> 8) & 0xFF}.{ip & 0xFF}";

        /// <summary>IP 字符串转 uint，带范围检查</summary>
        internal static uint IpStringToUint(string ip)
        {
            if (string.IsNullOrEmpty(ip)) throw new ArgumentException("IP 地址不能为空");
            var parts = ip.Split('.');
            if (parts.Length != 4) throw new ArgumentException($"无效的IP格式: {ip}");
            uint result = 0;
            for (int i = 0; i < 4; i++)
            {
                if (!byte.TryParse(parts[i], out byte b))
                    throw new ArgumentException($"无效的IP段: {parts[i]}");
                result = (result << 8) | b;
            }
            return result;
        }

        #endregion

        #region IDisposable

        public void Dispose()
        {
            if (_disposed) return;
            _disposed = true;
            _autoReconnect = false;

            // FIX(2026-08-13): 只 Cancel 不立即 Dispose。原实现 Cancel 后立刻 Dispose，
            // 若采集/重连循环仍持有 token 引用（SDK 阻塞调用中），WaitHandle.WaitOne 会抛
            // ObjectDisposedException 导致循环异常退出。CTS 由各循环任务结束后的
            // ContinueWith 兜底释放（见 StartGrabbingLoop），进程退出时由 GC/进程终止兜底。
            CancellationTokenSource reconnectCts = Interlocked.Exchange(ref _reconnectCts, null);
            if (reconnectCts != null) { try { reconnectCts.Cancel(); } catch (ObjectDisposedException) { } }

            CancellationTokenSource grabCts = Interlocked.Exchange(ref _grabCts, null);
            if (grabCts != null) { try { grabCts.Cancel(); } catch (ObjectDisposedException) { } }

            CancelFileAccessPolling();

            // 尝试获取 _grabLock（5 秒超时），避免 Dispose 被阻塞的 GrabOneFrame 卡死。
            // 注意：锁内只做 StopGrabbing，设备句柄关闭统一走锁外的 ForceCloseDevice，
            // 避免锁超时时整个关闭流程被静默跳过。
            bool lockTaken = false;
            try
            {
                Monitor.TryEnter(_grabLock, 5000, ref lockTaken);
                if (lockTaken && _isGrabbing)
                {
                    _isGrabbing = false;
                    try { _device?.MV_CODEREADER_StopGrabbing_NET(); }
                    catch (Exception ex) { Log(LogLevel.Error, "[Dispose] StopGrabbing 异常", ex); }
                }
            }
            finally
            {
                if (lockTaken) Monitor.Exit(_grabLock);
            }

            // FIX(2026-08-13): 设备句柄强制关闭。原实现把 CloseDevice/DestroyHandle 放在
            // _grabLock 内，采集线程卡在 SDK 网络调用导致锁超时时整个分支被静默跳过，
            // 设备句柄永不释放 → 程序退出后 SDK 驱动仍认为设备被占用（MVS 等无法打开），
            // 直至 SDK 内部会话超时（表现为"退出后长时间占用设备"）。
            // 现改为锁外无条件执行，确保句柄一定关闭；SDK 关闭接口自身线程安全，
            // 且程序即将退出，与采集线程的并发竞争可接受。
            ForceCloseDevice();

            // FIX(2026-08-13): 有限等待采集循环任务退出（SDK 阻塞调用最坏情况 2 秒），
            // 使 SDK 内部资源在进程退出前完成清理，缩短设备被占时间。
            try { _grabLoopTask?.Wait(2000); }
            catch (AggregateException) { }
            catch (ObjectDisposedException) { }

            // #6: JoinMscThreads 在 _grabLock 外执行，避免阻塞其他线程获取锁
            JoinMscThreads();

            // 释放预分配的非托管缓冲区
            lock (_bufferLock)
            {
                if (_pFrameInfo != IntPtr.Zero) { Marshal.FreeHGlobal(_pFrameInfo); _pFrameInfo = IntPtr.Zero; }
            }

            // #1: 兜底释放 MSC 预分配缓冲区（StopGrabbing/JoinMscThreads 可能因锁超时未执行）
            lock (_mscBufferLock0) { if (_mscBuffer0 != IntPtr.Zero) { Marshal.FreeHGlobal(_mscBuffer0); _mscBuffer0 = IntPtr.Zero; } }
            lock (_mscBufferLock1) { if (_mscBuffer1 != IntPtr.Zero) { Marshal.FreeHGlobal(_mscBuffer1); _mscBuffer1 = IntPtr.Zero; } }

            _device = null;
            Log(LogLevel.Information, "[Dispose] HikScanner 释放完成（设备句柄已强制关闭）");
        }

        /// <summary>
        /// FIX(2026-08-13): 无条件关闭 SDK 设备句柄（注销回调 → CloseDevice → DestroyHandle）。
        /// 不依赖 _grabLock：即使采集线程卡在 SDK 阻塞调用中，也保证句柄被关闭，
        /// 避免程序退出后设备仍被 SDK 驱动占用。检查并记录 SDK 返回码便于排查。
        /// </summary>
        private void ForceCloseDevice()
        {
            var device = _device;
            if (device == null) return;
            try
            {
                if (_imageCallback != null)
                {
                    try { device.MV_CODEREADER_RegisterImageCallBackEx2_NET(null, IntPtr.Zero); }
                    catch (Exception ex) { Log(LogLevel.Error, "[Dispose] 注销图像回调失败", ex); }
                }
                if (_exceptionCallback != null)
                {
                    try { device.MV_CODEREADER_RegisterExceptionCallBack_NET(null, IntPtr.Zero); }
                    catch (Exception ex) { Log(LogLevel.Error, "[Dispose] 注销异常回调失败", ex); }
                }
                if (_isConnected)
                {
                    int closeRet = device.MV_CODEREADER_CloseDevice_NET();
                    if (closeRet != MvCodeReader.MV_CODEREADER_OK)
                        Log(LogLevel.Warning, $"[Dispose] CloseDevice 返回 0x{closeRet:X8}");
                }
                int destroyRet = device.MV_CODEREADER_DestroyHandle_NET();
                if (destroyRet != MvCodeReader.MV_CODEREADER_OK)
                    Log(LogLevel.Warning, $"[Dispose] DestroyHandle 返回 0x{destroyRet:X8}");
                _isConnected = false;
                Log(LogLevel.Information, "[Dispose] SDK 设备句柄关闭成功");
            }
            catch (Exception ex)
            {
                Log(LogLevel.Error, "[Dispose] 强制关闭设备失败", ex);
            }
            _imageCallback = null;
            _exceptionCallback = null;
            _mscCallback0 = null;
            _mscCallback1 = null;
        }

        #endregion
    }
}
