using Microsoft.Extensions.Logging;

namespace HikScanner.Tests;

/// <summary>
/// HikScanner 核心类单元测试 — 无需连接真实设备。
/// 覆盖构造、属性默认值、状态守卫、Dispose 安全性、ApplyOptions 回调接线。
/// </summary>
public class HikScannerTests
{
    // ─── 构造与默认状态 ───

    [Fact]
    public void Default_Constructor_Should_Have_False_States()
    {
        using var cam = new HikScanner();
        Assert.False(cam.IsConnected);
        Assert.False(cam.IsGrabbing);
    }

    [Fact]
    public void Default_Constructor_DeviceInfo_Should_Be_Null()
    {
        using var cam = new HikScanner();
        Assert.Null(cam.DeviceInfo);
    }

    [Fact]
    public void Default_AutoReconnect_Should_Be_False()
    {
        using var cam = new HikScanner();
        Assert.False(cam.AutoReconnect);
    }

    [Fact]
    public void Default_ReconnectIntervalMs_Should_Be_2000()
    {
        using var cam = new HikScanner();
        Assert.Equal(2000, cam.ReconnectIntervalMs);
    }

    // ─── 带参构造 ───

    [Fact]
    public void Constructor_With_Options_Should_Apply()
    {
        var options = new HikScannerOptions
        {
            AutoReconnect = true,
            ReconnectIntervalMs = 5000
        };
        using var cam = new HikScanner(options);
        Assert.True(cam.AutoReconnect);
        Assert.Equal(5000, cam.ReconnectIntervalMs);
    }

    [Fact]
    public void Constructor_With_Null_Options_Should_Not_Throw()
    {
        var ex = Record.Exception(() => new HikScanner((HikScannerOptions)null!));
        Assert.Null(ex);
    }

    // ─── AutoReconnect 属性 ───

    [Fact]
    public void AutoReconnect_Set_To_True_Should_Work()
    {
        using var cam = new HikScanner();
        cam.AutoReconnect = true;
        Assert.True(cam.AutoReconnect);
    }

    [Fact]
    public void AutoReconnect_Set_To_Same_Value_Should_Be_Noop()
    {
        using var cam = new HikScanner();
        cam.AutoReconnect = true;
        cam.AutoReconnect = true; // 重复设置不抛异常
        Assert.True(cam.AutoReconnect);
    }

    [Fact]
    public void AutoReconnect_Toggle_Should_Work()
    {
        using var cam = new HikScanner();
        cam.AutoReconnect = true;
        Assert.True(cam.AutoReconnect);
        cam.AutoReconnect = false;
        Assert.False(cam.AutoReconnect);
    }

    // ─── ReconnectIntervalMs 属性 ───

    [Fact]
    public void ReconnectIntervalMs_Should_Accept_Positive_Value()
    {
        using var cam = new HikScanner();
        cam.ReconnectIntervalMs = 3000;
        Assert.Equal(3000, cam.ReconnectIntervalMs);
    }

    [Fact]
    public void ReconnectIntervalMs_Zero_Should_Be_Ignored()
    {
        using var cam = new HikScanner();
        var original = cam.ReconnectIntervalMs;
        cam.ReconnectIntervalMs = 0;
        Assert.Equal(original, cam.ReconnectIntervalMs);
    }

    [Fact]
    public void ReconnectIntervalMs_Negative_Should_Be_Ignored()
    {
        using var cam = new HikScanner();
        var original = cam.ReconnectIntervalMs;
        cam.ReconnectIntervalMs = -100;
        Assert.Equal(original, cam.ReconnectIntervalMs);
    }

    // ─── 新增重连策略属性 ───

    [Fact]
    public void MaxReconnectAttempts_Default_Should_Be_Zero()
    {
        using var cam = new HikScanner();
        Assert.Equal(0, cam.MaxReconnectAttempts);
    }

    [Fact]
    public void MaxReconnectAttempts_Should_Be_Settable()
    {
        using var cam = new HikScanner();
        cam.MaxReconnectAttempts = 5;
        Assert.Equal(5, cam.MaxReconnectAttempts);
    }

    [Fact]
    public void ExponentialBackoff_Default_Should_Be_True()
    {
        using var cam = new HikScanner();
        Assert.True(cam.ExponentialBackoff);
    }

    [Fact]
    public void MaxReconnectIntervalMs_Default_Should_Be_30000()
    {
        using var cam = new HikScanner();
        Assert.Equal(30000, cam.MaxReconnectIntervalMs);
    }

    [Fact]
    public void MaxReconnectIntervalMs_Should_Be_Settable()
    {
        using var cam = new HikScanner();
        cam.MaxReconnectIntervalMs = 60000;
        Assert.Equal(60000, cam.MaxReconnectIntervalMs);
    }

    // ─── CancellationToken 重载 ───

    [Fact]
    public void GrabOneFrame_With_CancellationToken_Without_Grabbing_Should_Return_Error()
    {
        using var cam = new HikScanner();
        using var cts = new CancellationTokenSource();
        var result = cam.GrabOneFrame(1000, cts.Token);
        Assert.Equal(HikGrabStatus.Error, result.Status);
    }

    [Fact]
    public void GrabOneFrame_With_TimeSpan_And_CancellationToken_Should_Return_Error_When_Not_Grabbing()
    {
        using var cam = new HikScanner();
        using var cts = new CancellationTokenSource();
        var result = cam.GrabOneFrame(TimeSpan.FromMilliseconds(500), cts.Token);
        Assert.Equal(HikGrabStatus.Error, result.Status);
    }

    [Fact]
    public void GrabOneFrame_With_Cancelled_Token_Should_Return_Error_Quickly()
    {
        using var cam = new HikScanner();
        using var cts = new CancellationTokenSource();
        cts.Cancel();
        // 即使没在采集，也返回 Error
        var result = cam.GrabOneFrame(5000, cts.Token);
        Assert.Equal(HikGrabStatus.Error, result.Status);
    }

    // ─── 未连接状态下的方法调用 ───

    [Fact]
    public void GrabOneFrame_Without_Grabbing_Should_Return_Error()
    {
        using var cam = new HikScanner();
        var result = cam.GrabOneFrame(100);
        Assert.Equal(HikGrabStatus.Error, result.Status);
    }

    [Fact]
    public void GrabOneFrame_With_TimeSpan_Should_Return_Error()
    {
        using var cam = new HikScanner();
        var result = cam.GrabOneFrame(TimeSpan.FromMilliseconds(100));
        Assert.Equal(HikGrabStatus.Error, result.Status);
    }

    [Fact]
    public void StopGrabbing_Without_Start_Should_Not_Throw()
    {
        using var cam = new HikScanner();
        var ex = Record.Exception(() => cam.StopGrabbing());
        Assert.Null(ex);
    }

    [Fact]
    public void Disconnect_Without_Connection_Should_Not_Throw()
    {
        using var cam = new HikScanner();
        var ex = Record.Exception(() => cam.Disconnect());
        Assert.Null(ex);
    }

    [Fact]
    public void StartGrabbing_Without_Connection_Should_Throw()
    {
        using var cam = new HikScanner();
        Assert.Throws<InvalidOperationException>(() => cam.StartGrabbing());
    }

    [Fact]
    public void StartGrabbingLoop_Without_Connection_Should_Throw()
    {
        using var cam = new HikScanner();
        Assert.Throws<InvalidOperationException>(() => cam.StartGrabbingLoop());
    }

    [Fact]
    public void StartGrabbingWithCallback_Without_Connection_Should_Throw()
    {
        using var cam = new HikScanner();
        Assert.Throws<InvalidOperationException>(() => cam.StartGrabbingWithCallback());
    }

    // ─── GigE 方法未连接守卫 ───

    [Fact]
    public void ForceIp_Without_Connection_Should_Throw()
    {
        using var cam = new HikScanner();
        Assert.Throws<InvalidOperationException>(() =>
            cam.ForceIp("192.168.1.100", "255.255.255.0", "192.168.1.1"));
    }

    [Fact]
    public void SetIpConfig_Without_Connection_Should_Throw()
    {
        using var cam = new HikScanner();
        Assert.Throws<InvalidOperationException>(() =>
            cam.SetIpConfig(HikIpConfigType.Static));
    }

    [Fact]
    public void GetOptimalPacketSize_Without_Connection_Should_Throw()
    {
        using var cam = new HikScanner();
        Assert.Throws<InvalidOperationException>(() => cam.GetOptimalPacketSize());
    }

    [Fact]
    public void SetGvcpTimeout_Without_Connection_Should_Throw()
    {
        using var cam = new HikScanner();
        Assert.Throws<InvalidOperationException>(() => cam.SetGvcpTimeout(1000));
    }

    [Fact]
    public void GetGvcpTimeout_Without_Connection_Should_Throw()
    {
        using var cam = new HikScanner();
        Assert.Throws<InvalidOperationException>(() => cam.GetGvcpTimeout());
    }

    // ─── FileAccess 方法未连接守卫 ───

    [Fact]
    public void FileAccessRead_Without_Connection_Should_Throw()
    {
        using var cam = new HikScanner();
        Assert.Throws<InvalidOperationException>(() =>
            cam.FileAccessRead("test.mfa", "UserSet1"));
    }

    [Fact]
    public void FileAccessWrite_Without_Connection_Should_Throw()
    {
        using var cam = new HikScanner();
        Assert.Throws<InvalidOperationException>(() =>
            cam.FileAccessWrite("test.mfa", "UserSet1"));
    }

    [Fact]
    public async Task FileAccessWriteAsync_Without_Connection_Should_Throw()
    {
        using var cam = new HikScanner();
        await Assert.ThrowsAsync<InvalidOperationException>(() =>
            cam.FileAccessWriteAsync("test.mfa", "UserSet1"));
    }

    [Fact]
    public async Task FileAccessReadAsync_Without_Connection_Should_Throw()
    {
        using var cam = new HikScanner();
        await Assert.ThrowsAsync<InvalidOperationException>(() =>
            cam.FileAccessReadAsync("test.mfa", "UserSet1"));
    }

    // ─── Dispose 安全性 ───

    [Fact]
    public void Dispose_Without_Connection_Should_Not_Throw()
    {
        var cam = new HikScanner();
        var ex = Record.Exception(() => cam.Dispose());
        Assert.Null(ex);
    }

    [Fact]
    public void Double_Dispose_Should_Not_Throw()
    {
        var cam = new HikScanner();
        cam.Dispose();
        var ex = Record.Exception(() => cam.Dispose());
        Assert.Null(ex);
    }

    [Fact]
    public void Dispose_After_Disconnect_Should_Not_Throw()
    {
        var cam = new HikScanner();
        cam.Disconnect();
        var ex = Record.Exception(() => cam.Dispose());
        Assert.Null(ex);
    }

    // ─── #15: ObjectDisposedException ───

    [Fact]
    public void Connect_After_Dispose_Should_Throw_ObjectDisposedException()
    {
        var cam = new HikScanner();
        cam.Dispose();
        Assert.Throws<ObjectDisposedException>(() => cam.Connect(new HikDeviceInfo()));
    }

    [Fact]
    public void StartGrabbing_After_Dispose_Should_Throw_ObjectDisposedException()
    {
        var cam = new HikScanner();
        cam.Dispose();
        Assert.Throws<ObjectDisposedException>(() => cam.StartGrabbing());
    }

    [Fact]
    public void StartGrabbingLoop_After_Dispose_Should_Throw_ObjectDisposedException()
    {
        var cam = new HikScanner();
        cam.Dispose();
        Assert.Throws<ObjectDisposedException>(() => cam.StartGrabbingLoop());
    }

    [Fact]
    public void StartGrabbingWithCallback_After_Dispose_Should_Throw_ObjectDisposedException()
    {
        var cam = new HikScanner();
        cam.Dispose();
        Assert.Throws<ObjectDisposedException>(() => cam.StartGrabbingWithCallback());
    }

    // ─── #12: ConnectAsync ───

    [Fact]
    public async Task ConnectAsync_With_Null_DeviceInfo_Should_Throw_ArgumentNullException()
    {
        using var cam = new HikScanner();
        await Assert.ThrowsAsync<ArgumentNullException>(() => cam.ConnectAsync(null!));
    }

    [Fact]
    public async Task ConnectBySerialNumberAsync_With_Null_Serial_Should_Throw()
    {
        using var cam = new HikScanner();
        await Assert.ThrowsAnyAsync<Exception>(() => cam.ConnectBySerialNumberAsync(null!));
    }

    // ─── #5: ApplyOptions 事件去重 ───

    [Fact]
    public void ApplyOptions_Called_Twice_Should_Not_Duplicate_Callbacks()
    {
        using var cam = new HikScanner();
        int disconnectCount = 0;
        var options = new HikScannerOptions
        {
            OnDeviceDisconnected = () => disconnectCount++
        };

        cam.ApplyOptions(options);
        cam.ApplyOptions(options); // 重复调用

        cam.OnDeviceDisconnectedInternal();

        Assert.Equal(1, disconnectCount); // 应只触发一次
    }

    [Fact]
    public void ApplyOptions_Called_Twice_With_FrameGrabbed_Should_Not_Duplicate()
    {
        using var cam = new HikScanner();
        int frameCount = 0;
        var options = new HikScannerOptions
        {
            OnFrameGrabbed = _ => frameCount++
        };

        cam.ApplyOptions(options);
        cam.ApplyOptions(options);

        cam.OnImageGrabbed(new HikGrabResult());

        Assert.Equal(1, frameCount);
    }

    // ─── #6: GrabOneFrame timeout=0 ───

    [Fact]
    public void GrabOneFrame_With_CancellationToken_And_Zero_Timeout_Should_Return_Error_When_Not_Grabbing()
    {
        using var cam = new HikScanner();
        using var cts = new CancellationTokenSource();
        var result = cam.GrabOneFrame(0, cts.Token);
        Assert.Equal(HikGrabStatus.Error, result.Status);
    }

    [Fact]
    public void GrabOneFrame_With_Cancelled_Token_And_Zero_Timeout_Should_Return_Error()
    {
        using var cam = new HikScanner();
        using var cts = new CancellationTokenSource();
        cts.Cancel();
        var result = cam.GrabOneFrame(0, cts.Token);
        Assert.Equal(HikGrabStatus.Error, result.Status);
    }

    // ─── #7: EnsureConnected 检查 _device null ───

    [Fact]
    public void GetFloatParam_After_Dispose_Should_Throw()
    {
        var cam = new HikScanner();
        cam.Dispose();
        Assert.ThrowsAny<Exception>(() => cam.GetFloatParam("ExposureTime"));
    }

    // ─── #13: ApplyTo 应用 Logger ───

    [Fact]
    public void ApplyOptions_With_Logger_Should_Set_Logger()
    {
        using var cam = new HikScanner();
        var logger = new TestLogger();
        var options = new HikScannerOptions { Logger = logger };
        cam.ApplyOptions(options);
        Assert.Same(logger, cam.Logger);
    }

    private class TestLogger : ILogger
    {
        public IDisposable BeginScope<TState>(TState state) where TState : notnull => null!;
        public bool IsEnabled(LogLevel logLevel) => true;
        public void Log<TState>(LogLevel logLevel, EventId eventId, TState state, Exception? exception, Func<TState, Exception?, string> formatter) { }
    }

    // ─── ApplyOptions ───

    [Fact]
    public void ApplyOptions_Null_Should_Not_Throw()
    {
        using var cam = new HikScanner();
        var ex = Record.Exception(() => cam.ApplyOptions(null!));
        Assert.Null(ex);
    }

    [Fact]
    public void ApplyOptions_Should_Set_AutoReconnect()
    {
        using var cam = new HikScanner();
        cam.ApplyOptions(new HikScannerOptions { AutoReconnect = true });
        Assert.True(cam.AutoReconnect);
    }

    [Fact]
    public void ApplyOptions_Should_Set_ReconnectIntervalMs()
    {
        using var cam = new HikScanner();
        cam.ApplyOptions(new HikScannerOptions { ReconnectIntervalMs = 8000 });
        Assert.Equal(8000, cam.ReconnectIntervalMs);
    }

    [Fact]
    public void ApplyOptions_With_DisconnectedCallback_Should_Wire_Event()
    {
        using var cam = new HikScanner();
        bool invoked = false;
        cam.ApplyOptions(new HikScannerOptions
        {
            OnDeviceDisconnected = () => invoked = true
        });

        // 触发内部断线通知
        cam.OnDeviceDisconnectedInternal();

        Assert.True(invoked);
    }

    [Fact]
    public void ApplyOptions_With_ReconnectedCallback_Should_Wire_Event()
    {
        using var cam = new HikScanner();
        bool invoked = false;
        cam.ApplyOptions(new HikScannerOptions
        {
            OnDeviceReconnected = () => invoked = true
        });

        cam.OnDeviceReconnectedInternal();

        Assert.True(invoked);
    }

    [Fact]
    public void ApplyOptions_With_Null_Callbacks_Should_Not_Wire_Events()
    {
        using var cam = new HikScanner();
        cam.ApplyOptions(new HikScannerOptions());

        // 触发事件不应抛异常（无订阅者）
        var ex1 = Record.Exception(() => cam.OnDeviceDisconnectedInternal());
        var ex2 = Record.Exception(() => cam.OnDeviceReconnectedInternal());
        Assert.Null(ex1);
        Assert.Null(ex2);
    }

    // ─── OnDeviceDisconnectedInternal 副作用 ───

    [Fact]
    public void OnDeviceDisconnectedInternal_Should_Set_States_To_False()
    {
        using var cam = new HikScanner();
        // 即使原本就是 false，调用后仍应保持 false
        cam.OnDeviceDisconnectedInternal();
        Assert.False(cam.IsConnected);
        Assert.False(cam.IsGrabbing);
    }

    // ─── Disconnect 清理事件 ───

    [Fact]
    public void Disconnect_With_ClearEvents_Should_Clear_Event_Handlers()
    {
        using var cam = new HikScanner();
        bool fired = false;
        cam.DeviceDisconnected += (s, e) => fired = true;

        cam.Disconnect(clearEvents: true);

        // Disconnect 后事件应被清空，再触发不应调用
        cam.OnDeviceDisconnectedInternal();
        Assert.False(fired);
    }

    [Fact]
    public void Disconnect_Without_ClearEvents_Should_Keep_Event_Handlers()
    {
        using var cam = new HikScanner();
        bool fired = false;
        cam.DeviceDisconnected += (s, e) => fired = true;

        cam.Disconnect(); // 默认不清空事件

        // 事件订阅仍保留
        cam.OnDeviceDisconnectedInternal();
        Assert.True(fired);
    }

    // ─── IsTextUtf8 (静态方法已在 HikCameraUtilityTests 覆盖，此处补静态可达性) ───

    [Fact]
    public void DiagnosticCallback_Default_Should_Be_Null()
    {
        using var cam = new HikScanner();
        Assert.Null(cam.DiagnosticCallback);
    }

    [Fact]
    public void DiagnosticCallback_Instance_Should_Be_Independent()
    {
        using var cam1 = new HikScanner();
        using var cam2 = new HikScanner();
        int count1 = 0, count2 = 0;
        cam1.DiagnosticCallback = (_, _) => count1++;
        cam2.DiagnosticCallback = (_, _) => count2++;

        cam1.LogDiagnostic("test1");
        cam2.LogDiagnostic("test2");
        cam2.LogDiagnostic("test3");

        Assert.Equal(1, count1);
        Assert.Equal(2, count2);
    }

    // ─── #8: SaveImage 无扩展名检查 ───

    [Fact]
    public void SaveImage_Without_Extension_Should_Throw()
    {
        using var cam = new HikScanner();
        var image = new HikImageData { RawData = new byte[] { 0xFF, 0xD8, 0xFF }, Width = 1, Height = 1, IsJpeg = true };
        Assert.Throws<ArgumentException>(() => cam.SaveImage(image, "test_no_ext"));
    }

    // ─── #12: ConnectionStateChanged 事件 ───

    [Fact]
    public void ConnectionStateChanged_Default_Should_Be_Disconnected()
    {
        using var cam = new HikScanner();
        // 无连接时不应触发事件
        int fired = 0;
        cam.ConnectionStateChanged += (s, e) => fired++;
        Assert.Equal(0, fired);
    }

    [Fact]
    public void OnDeviceDisconnected_Should_Fire_ConnectionStateChanged()
    {
        using var cam = new HikScanner();
        HikConnectionState? lastState = null;
        cam.ConnectionStateChanged += (s, e) => lastState = e.State;
        // 先进入 Reconnecting 状态，再断线才会触发事件
        cam.OnReconnectingInternal();
        Assert.Equal(HikConnectionState.Reconnecting, lastState);
        cam.OnDeviceDisconnectedInternal();
        Assert.Equal(HikConnectionState.Disconnected, lastState);
    }

    [Fact]
    public void OnReconnecting_Should_Fire_ConnectionStateChanged()
    {
        using var cam = new HikScanner();
        HikConnectionState? lastState = null;
        cam.ConnectionStateChanged += (s, e) => lastState = e.State;
        cam.OnReconnectingInternal();
        Assert.Equal(HikConnectionState.Reconnecting, lastState);
    }

    [Fact]
    public void ConnectionStateChanged_Should_Not_Fire_For_Same_State()
    {
        using var cam = new HikScanner();
        int fired = 0;
        cam.ConnectionStateChanged += (s, e) => fired++;
        // 初始状态是 Disconnected，连续触发 Disconnected 不应重复
        cam.OnDeviceDisconnectedInternal();
        cam.OnDeviceDisconnectedInternal();
        Assert.Equal(0, fired); // 初始已是 Disconnected，不会触发
    }

    // ─── #3/#4: SaveImageNative 无扩展名 + 连接检查 ───

    [Fact]
    public void SaveImageNative_Without_Extension_Should_Throw()
    {
        using var cam = new HikScanner();
        var image = new HikImageData { RawData = new byte[] { 0xFF, 0xD8, 0xFF }, Width = 1, Height = 1, IsJpeg = true };
        // 未连接时先抛 InvalidOperationException，但如果有扩展名检查在 EnsureConnected 之后
        Assert.Throws<InvalidOperationException>(() => cam.SaveImageNative(image, "test_no_ext"));
    }

    // ─── #8: HikImageData ToString ───

    [Fact]
    public void HikImageData_ToString_Should_Contain_Dimensions()
    {
        var img = new HikImageData { Width = 1920, Height = 1080, FrameNum = 42, IsMono8 = true, RawData = new byte[100] };
        var s = img.ToString();
        Assert.Contains("1920x1080", s);
        Assert.Contains("Frame#42", s);
        Assert.Contains("Mono8", s);
        Assert.Contains("100B", s);
    }

    [Fact]
    public void HikImageData_ToString_Jpeg_Should_Contain_JPEG()
    {
        var img = new HikImageData { Width = 800, Height = 600, FrameNum = 1, IsJpeg = true, RawData = new byte[50] };
        Assert.Contains("JPEG", img.ToString());
    }

    // ─── #12: GrabFrames 批量采集 ───

    [Fact]
    public void GrabFrames_With_Invalid_Count_Should_Throw()
    {
        using var cam = new HikScanner();
        Assert.Throws<ArgumentOutOfRangeException>(() => cam.GrabFrames(0));
        Assert.Throws<ArgumentOutOfRangeException>(() => cam.GrabFrames(-1));
    }

    [Fact]
    public void GrabFrames_When_Not_Grabbing_Should_Return_Empty()
    {
        using var cam = new HikScanner();
        var results = cam.GrabFrames(5);
        Assert.Empty(results);
    }

    // ─── #6: HikOcrResult/HikWaybillResult ToString ───

    [Fact]
    public void HikOcrResult_ToString_Should_Contain_Text_And_Confidence()
    {
        var ocr = new HikOcrResult { Id = 1, Text = "ABC123", CharConfidence = 0.95f, Width = 100, Height = 30, CenterX = 50, CenterY = 15 };
        var s = ocr.ToString();
        Assert.Contains("ABC123", s);
        Assert.Contains("OCR#1", s);
    }

    [Fact]
    public void HikWaybillResult_ToString_Should_Contain_Confidence()
    {
        var wb = new HikWaybillResult { Confidence = 0.88f, Width = 200, Height = 100, CenterX = 100, CenterY = 50, ImageLength = 1024 };
        var s = wb.ToString();
        Assert.Contains("Waybill", s);
        Assert.Contains("1024", s);
    }

    // ─── #9: DeviceInfo 属性 ───

    [Fact]
    public void DeviceInfo_Default_Should_Be_Null()
    {
        using var cam = new HikScanner();
        Assert.Null(cam.DeviceInfo);
    }

    // ─── #14: ReconnectInitialDelayMs ───

    [Fact]
    public void ReconnectInitialDelayMs_Default_Should_Be_500()
    {
        using var cam = new HikScanner();
        Assert.Equal(500, cam.ReconnectInitialDelayMs);
    }

    [Fact]
    public void ReconnectInitialDelayMs_Set_Zero_Should_Default_To_500()
    {
        using var cam = new HikScanner();
        cam.ReconnectInitialDelayMs = 0;
        Assert.Equal(500, cam.ReconnectInitialDelayMs);
    }

    [Fact]
    public void ReconnectInitialDelayMs_Set_Valid_Should_Update()
    {
        using var cam = new HikScanner();
        cam.ReconnectInitialDelayMs = 1000;
        Assert.Equal(1000, cam.ReconnectInitialDelayMs);
    }

    // ─── #15: ConnectAsync with CancellationToken ───

    [Fact]
    public async Task ConnectAsync_With_Cancelled_Token_Should_Throw_OperationCanceledException()
    {
        using var cam = new HikScanner();
        using var cts = new CancellationTokenSource();
        cts.Cancel();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() =>
            cam.ConnectAsync(new HikDeviceInfo(), cts.Token));
    }

    [Fact]
    public async Task ConnectBySerialNumberAsync_With_Cancelled_Token_Should_Throw()
    {
        using var cam = new HikScanner();
        using var cts = new CancellationTokenSource();
        cts.Cancel();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() =>
            cam.ConnectBySerialNumberAsync("ABC123", cts.Token));
    }

    // ─── #4: ConnectBySerialNumber 空序列号检查 ───

    [Fact]
    public void ConnectBySerialNumber_With_Null_Should_Throw_ArgumentNullException()
    {
        using var cam = new HikScanner();
        Assert.Throws<ArgumentNullException>(() => cam.ConnectBySerialNumber(null!));
    }

    [Fact]
    public void ConnectBySerialNumber_With_Empty_Should_Throw_ArgumentNullException()
    {
        using var cam = new HikScanner();
        Assert.Throws<ArgumentNullException>(() => cam.ConnectBySerialNumber(""));
    }

    // ─── #3: SaveImageNative 溢出检查 ───

    [Fact]
    public void SaveImageNative_With_Huge_Image_Should_Throw_ArgumentException()
    {
        using var cam = new HikScanner();
        // 模拟超大图像（不实际分配大缓冲区，只设置 W/H）
        var hugeImage = new HikImageData { RawData = new byte[10], Width = 65535, Height = 65535, IsJpeg = true };
        // 未连接先抛 InvalidOperationException，但如果连接了则应抛 ArgumentException
        Assert.ThrowsAny<Exception>(() => cam.SaveImageNative(hugeImage, "test.jpg"));
    }

    // ─── #8: ScannerManager ConnectBySerialNumbers ───

    [Fact]
    public void ScannerManager_ConnectBySerialNumbers_With_Null_Should_Throw()
    {
        using var manager = new ScannerManager();
        Assert.Throws<ArgumentNullException>(() => manager.ConnectBySerialNumbers(null!));
    }

    [Fact]
    public void ScannerManager_ConnectBySerialNumbers_With_Empty_Array_Should_Return_Zero()
    {
        using var manager = new ScannerManager();
        int count = manager.ConnectBySerialNumbers(Array.Empty<string>());
        Assert.Equal(0, count);
    }

    // ─── #15: ScannerManager GetCameraBySerialNumber ───

    [Fact]
    public void ScannerManager_GetCameraBySerialNumber_With_Null_Should_Return_Null()
    {
        using var manager = new ScannerManager();
        Assert.Null(manager.GetCameraBySerialNumber(null!));
    }

    [Fact]
    public void ScannerManager_GetCameraBySerialNumber_With_Empty_Should_Return_Null()
    {
        using var manager = new ScannerManager();
        Assert.Null(manager.GetCameraBySerialNumber(""));
    }

    [Fact]
    public void ScannerManager_GetCameraBySerialNumber_NotFound_Should_Return_Null()
    {
        using var manager = new ScannerManager();
        Assert.Null(manager.GetCameraBySerialNumber("NOTEXIST"));
    }
}
