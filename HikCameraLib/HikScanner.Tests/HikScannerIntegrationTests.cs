namespace HikScanner.Tests;

/// <summary>
/// 集成测试 — 需要连接真实海康读码器。
/// 设备未连接时自动跳过。运行前关闭官方 Demo，避免 Exclusive 冲突。
/// xUnit 按 test class 顺序执行，每个测试方法结束后自动调用 Dispose 释放设备。
/// </summary>
[Collection("HikCamera_Integration")]  // 禁止并行执行
public class HikCameraIntegrationTests : IDisposable
{
    private HikScanner? _camera;
    private HikDeviceInfo? _deviceInfo;
    private bool _deviceAvailable;

    public HikCameraIntegrationTests()
    {
        // 尝试枚举设备，自动检测 GigE 或 USB
        foreach (var type in new[] { HikDeviceType.GigE, HikDeviceType.USB })
        {
            try
            {
                var devices = HikScanner.EnumerateDevices(type);
                if (devices.Count > 0)
                {
                    _deviceInfo = devices[0];
                    _deviceAvailable = true;
                    break;
                }
            }
            catch
            {
                // SDK 初始化失败也跳过
            }
        }
    }

    private bool HasDevice => _deviceAvailable && _deviceInfo != null;

    private HikScanner Connect()
    {
        if (!HasDevice) return new HikScanner(); // 无设备时测试空过
        _camera = new HikScanner();
        _camera.Connect(_deviceInfo!);
        Assert.True(_camera.IsConnected);
        return _camera;
    }

    public void Dispose()
    {
        if (_camera != null)
        {
            try { _camera.StopGrabbing(); } catch { }
            try { _camera.Disconnect(); } catch { }
            _camera.Dispose();
            _camera = null;
            // 给 SDK 缓冲释放设备锁的时间
            Thread.Sleep(200);
        }
    }

    #region 1. SDK 版本 & 设备枚举

    [Fact]
    public void GetSDKVersion_Should_Return_NonZero()
    {
        var version = HikScanner.GetSDKVersion();
        Assert.True(version > 0, $"SDK 版本异常: 0x{version:X8}");
    }

    [Fact]
    public void Enumerate_GigE_Should_Not_Throw()
    {
        var devices = HikScanner.EnumerateDevices(HikDeviceType.GigE);
        Assert.NotNull(devices);
    }

    [Fact]
    public void Enumerate_USB_Should_Not_Throw()
    {
        var devices = HikScanner.EnumerateDevices(HikDeviceType.USB);
        Assert.NotNull(devices);
    }

    [Fact]
    public void Enumerate_Devices_Should_Have_Valid_Info()
    {
                if (!HasDevice) return;

        var info = _deviceInfo!;
        Assert.False(string.IsNullOrEmpty(info.SerialNumber), "序列号不应为空");
        Assert.False(string.IsNullOrEmpty(info.ManufacturerName), "制造商不应为空");
        Assert.False(string.IsNullOrEmpty(info.ModelName), "型号不应为空");
        Assert.True(info.TLayerType == (uint)HikDeviceType.GigE || info.TLayerType == (uint)HikDeviceType.USB);
    }

    [Fact]
    public void DeviceInfo_ToString_Should_Not_Be_Empty()
    {
                if (!HasDevice) return;
        var str = _deviceInfo!.ToString();
        Assert.False(string.IsNullOrEmpty(str));
        Assert.Contains(_deviceInfo.SerialNumber!, str);
    }

    #endregion

    #region 2. 设备可达性

    [Fact]
    public void IsDeviceAccessible_Should_Return_True()
    {
                if (!HasDevice) return;
        Assert.True(HikScanner.IsDeviceAccessible(_deviceInfo!));
    }

    [Fact]
    public void IsDeviceAccessible_With_Control_Should_Return_True()
    {
                if (!HasDevice) return;
        Assert.True(HikScanner.IsDeviceAccessible(_deviceInfo!, HikAccessMode.Control));
    }

    #endregion

    #region 3. 连接 & 断开

    [Fact]
    public void Connect_Should_Succeed()
    {
        var camera = Connect();
        Assert.True(camera.IsConnected);
        Assert.NotNull(camera.DeviceInfo);
        Assert.Equal(_deviceInfo!.SerialNumber, camera.DeviceInfo.SerialNumber);
    }

    [Fact]
    public void Disconnect_Should_Work()
    {
        var camera = Connect();
        camera.Disconnect();
        Assert.False(camera.IsConnected);
    }

    [Fact]
    public void Reconnect_After_Disconnect_Should_Succeed()
    {
        var camera = Connect();
        camera.Disconnect();
        camera.Connect(_deviceInfo!);
        Assert.True(camera.IsConnected);
    }

    [Fact]
    public void ConnectBySerialNumber_Should_Succeed()
    {
                if (!HasDevice) return;

        using var cam = new HikScanner();
        cam.ConnectBySerialNumber(_deviceInfo!.SerialNumber!);
        Assert.True(cam.IsConnected);
        cam.Disconnect();
    }

    [Fact]
    public void Connect_Twice_Without_Disconnect_Should_Reconnect()
    {
        var camera = Connect();
        // 二次 Connect 应当先断开再重连
        camera.Connect(_deviceInfo!);
        Assert.True(camera.IsConnected);
    }

    #endregion

    #region 4. 参数读写

    [Fact]
    public void GetExposureTime_Should_Return_Valid_Range()
    {
        var camera = Connect();
        var exposure = camera.ExposureTime;
        Assert.True(exposure > 0, $"曝光时间异常: {exposure}");
        Assert.True(exposure < 10000000, $"曝光时间过大: {exposure}");  // < 10s
    }

    [Fact]
    public void SetExposureTime_Should_Work()
    {
        var camera = Connect();
        var original = camera.ExposureTime;

        try
        {
            camera.ExposureTime = 5000;
            Assert.True(Math.Abs(camera.ExposureTime - 5000) < 100,
                $"设置曝光时间失败: 期望 5000, 实际 {camera.ExposureTime}");
        }
        finally
        {
            camera.ExposureTime = original;
        }
    }

    [Fact]
    public void GetGain_Should_Return_Valid_Range()
    {
        var camera = Connect();
        var gain = camera.Gain;
        Assert.True(gain >= 0, $"增益异常: {gain}");
        Assert.True(gain <= 40, $"增益过大: {gain}");
    }

    [Fact]
    public void SetGain_And_ReadBack()
    {
        var camera = Connect();
        var original = camera.Gain;

        try
        {
            camera.Gain = 10;
            Assert.True(Math.Abs(camera.Gain - 10) < 1, $"设置增益失败");
        }
        finally
        {
            camera.Gain = original;
        }
    }

    [Fact]
    public void GetFloatParam_Should_Work()
    {
        var camera = Connect();
        var val = camera.GetFloatParam("ExposureTime");
        Assert.True(val > 0);
    }

    [Fact]
    public void GetIntParam_Should_Work()
    {
        var camera = Connect();
        var val = camera.GetIntParam("Width");
        Assert.True(val > 0);
    }

    [Fact]
    public void GetBoolParam_Should_Not_Throw()
    {
        var camera = Connect();
        // 尝试常见参数，WayBillEnable 并非所有固件支持
        var val = camera.GetBoolParam("ReverseX");
        Assert.True(val == true || val == false);
    }

    [Fact]
    public void GetStringParam_Should_Return_NonEmpty()
    {
        var camera = Connect();
        var modelName = camera.GetStringParam("DeviceModelName");
        Assert.False(string.IsNullOrEmpty(modelName));
    }

    [Fact]
    public void GetEnumValue_Should_Return_Valid()
    {
        var camera = Connect();
        var enumInfo = camera.GetEnumValue("TriggerMode");
        Assert.NotNull(enumInfo);
        Assert.NotNull(enumInfo.SupportedValues);
        Assert.True(enumInfo.SupportedValues!.Length > 0);
    }

    [Fact]
    public void SetAndRestore_ExposureTime_Should_Preserve_Value()
    {
        var camera = Connect();
        var original = camera.ExposureTime;

        camera.ExposureTime = original; // 写回原值不抛异常
        Assert.True(Math.Abs(camera.ExposureTime - original) < 100);
    }

    #endregion

    #region 5. 触发模式

    [Fact]
    public void TriggerMode_Switch_Should_Work()
    {
        var camera = Connect();
        // 确保从连续模式开始
        camera.SetTriggerMode(HikTriggerMode.Continuous);
        // 切换到触发模式
        camera.SetTriggerMode(HikTriggerMode.Trigger);
        // 恢复
        camera.SetTriggerMode(HikTriggerMode.Continuous);
    }

    [Fact]
    public void SetTriggerSource_Should_Not_Throw()
    {
        var camera = Connect();
        // 先切到触发模式才能设触发源
        camera.SetTriggerMode(HikTriggerMode.Trigger);
        camera.SetTriggerSource(HikTriggerSource.Software);
        camera.SetTriggerSource(HikTriggerSource.Line0);
        camera.SetTriggerMode(HikTriggerMode.Continuous);
    }

    [Fact]
    public void TriggerSoftware_In_Trigger_Mode_Should_Not_Throw()
    {
        var camera = Connect();

        try
        {
            camera.SetTriggerMode(HikTriggerMode.Trigger);
            camera.SetTriggerSource(HikTriggerSource.Software);
            camera.TriggerSoftware();
        }
        finally
        {
            camera.SetTriggerMode(HikTriggerMode.Continuous);
        }
    }

    #endregion

    #region 6. 图像采集 — 轮询模式

    [Fact]
    public void GrabOneFrame_In_Polling_Should_Return_Success()
    {
        var camera = Connect();
        camera.SetTriggerMode(HikTriggerMode.Continuous);
        camera.StartGrabbing();

        var result = camera.GrabOneFrame(timeoutMs: 3000);
        Assert.Equal(HikGrabStatus.Success, result.Status);

        camera.StopGrabbing();
    }

    [Fact]
    public void GrabOneFrame_Image_Should_Have_Valid_Dimensions()
    {
        var camera = Connect();
        camera.SetTriggerMode(HikTriggerMode.Continuous);
        camera.StartGrabbing();

        var result = camera.GrabOneFrame(3000);
        if (result.Status == HikGrabStatus.Success)
        {
            Assert.NotNull(result.Image);
            Assert.True(result.Image!.Width > 0);
            Assert.True(result.Image.Height > 0);
            Assert.True(result.Image.RawData!.Length > 0);
        }

        camera.StopGrabbing();
    }

    [Fact]
    public void GrabOneFrame_Should_Not_Always_Timeout()
    {
        var camera = Connect();
        camera.SetTriggerMode(HikTriggerMode.Continuous);
        camera.StartGrabbing();

        int success = 0, total = 5;
        for (int i = 0; i < total; i++)
        {
            var result = camera.GrabOneFrame(2000);
            if (result.Status == HikGrabStatus.Success) success++;
        }

        camera.StopGrabbing();

        // 至少应有一帧成功
        Assert.True(success > 0, $"5 帧全部失败，设备可能未正常工作");
    }

    [Fact]
    public void GrabOneFrame_Without_StartGrabbing_Should_Not_Hang()
    {
        var camera = Connect();
        // 未调用 StartGrabbing 就取帧
        var result = camera.GrabOneFrame(500);
        Assert.NotEqual(HikGrabStatus.Success, result.Status);
    }

    #endregion

    #region 7. 图像采集 — 回调模式

    [Fact]
    public void StartGrabbingWithCallback_Should_Deliver_Frames()
    {
        var camera = Connect();
        var received = new List<HikGrabResult>();
        var mre = new ManualResetEventSlim();

        camera.ImageGrabbed += (s, r) =>
        {
            lock (received)
            {
                received.Add(r);
                if (received.Count >= 3) mre.Set();
            }
        };

        try
        {
            camera.SetTriggerMode(HikTriggerMode.Continuous);
            camera.StartGrabbingWithCallback();

            bool gotFrames = mre.Wait(10000);
            Assert.True(gotFrames, $"10 秒内未收到 3 帧回调, 实际收到 {received.Count} 帧");

            Assert.True(received.All(r => r.Status == HikGrabStatus.Success),
                $"有不成功的帧: {string.Join(", ", received.Select(r => r.Status))}");
        }
        finally
        {
            camera.StopGrabbing();
        }
    }

    #endregion

    #region 8. 事件回调验证

    [Fact]
    public void ImageGrabbed_Event_Should_Fire_In_Polling_Mode()
    {
        var camera = Connect();
        bool fired = false;
        var mre = new ManualResetEventSlim();

        camera.ImageGrabbed += (s, r) =>
        {
            fired = true;
            mre.Set();
        };

        camera.SetTriggerMode(HikTriggerMode.Continuous);
        camera.StartGrabbingLoop(timeoutMs: 500);  // 后台轮询采集线程

        mre.Wait(10000);
        camera.StopGrabbing();

        Assert.True(fired, "ImageGrabbed 事件未触发");
    }

    [Fact]
    public void DeviceDisconnected_Should_Not_Fire_During_Normal_Disconnect()
    {
        var camera = Connect();
        bool disconnected = false;
        camera.DeviceDisconnected += (s, e) => disconnected = true;

        camera.Disconnect();

        Assert.False(disconnected, "主动断开不应触发 DeviceDisconnected 事件");
    }

    #endregion

    #region 9. 多帧稳定性

    [Fact]
    public void ContinuousGrab_100Frames_Should_Not_Crash()
    {
        var camera = Connect();
        camera.SetTriggerMode(HikTriggerMode.Continuous);
        camera.StartGrabbing();

        int success = 0, timeout = 0, errors = 0;
        for (int i = 0; i < 100; i++)
        {
            var result = camera.GrabOneFrame(500);
            switch (result.Status)
            {
                case HikGrabStatus.Success: success++; break;
                case HikGrabStatus.Timeout: timeout++; break;
                default: errors++; break;
            }
        }

        camera.StopGrabbing();

        Assert.True(success > 0, "100 帧中无任何成功帧");
        Assert.True(errors == 0, $"存在 {errors} 个异常帧");
    }

    #endregion

    #region 10. 错误处理

    [Fact]
    public void Connect_With_Invalid_Serial_Should_Throw()
    {
                if (!HasDevice) return;
        using var cam = new HikScanner();
        var ex = Assert.Throws<HikScannerException>(() =>
            cam.ConnectBySerialNumber("INVALID_SN_99999"));
        Assert.True(ex.ErrorCode != 0);
    }

    [Fact]
    public void Grab_Without_Connection_Should_Not_Throw_Unhandled()
    {
        using var cam = new HikScanner();
        var result = cam.GrabOneFrame(100);
        Assert.Equal(HikGrabStatus.Error, result.Status);
    }

    [Fact]
    public void StopGrabbing_Before_Start_Should_Not_Throw()
    {
        var camera = Connect();
        var ex = Record.Exception(() => camera.StopGrabbing());
        Assert.Null(ex);
    }

    #endregion

    #region 11. 参数边界

    [Fact]
    public void ExecuteCommand_Should_Not_Throw_Unhandled()
    {
        var camera = Connect();
        // AcquisitionStop 在停止状态可安全执行
        var ex = Record.Exception(() => camera.ExecuteCommand("AcquisitionStop"));
        Assert.Null(ex);
    }

    [Fact]
    public void SetFloatParam_With_Invalid_Name_Should_Throw()
    {
        var camera = Connect();
        Assert.Throws<HikScannerException>(() =>
            camera.SetFloatParam("NonExistentParamName_XYZ", 100));
    }

    [Fact]
    public void GetFloatParam_With_Invalid_Name_Should_Throw()
    {
        var camera = Connect();
        Assert.Throws<HikScannerException>(() =>
            camera.GetFloatParam("NonExistentParamName_XYZ"));
    }

    #endregion

    #region 12. ApplyOptions

    [Fact]
    public void ApplyOptions_Should_Configure_AutoReconnect()
    {
        var camera = Connect();

        var options = new HikScannerOptions
        {
            AutoReconnect = true,
            ReconnectIntervalMs = 3000,
            DefaultTimeoutMs = 2000
        };

        camera.ApplyOptions(options);

        Assert.True(camera.AutoReconnect);
        Assert.Equal(3000, camera.ReconnectIntervalMs);
    }

    #endregion

    #region 13. Dispose 安全性

    [Fact]
    public void Dispose_Should_Not_Throw()
    {
                if (!HasDevice) return;
        var cam = new HikScanner();
        cam.Connect(_deviceInfo!);

        var ex = Record.Exception(() => cam.Dispose());
        Assert.Null(ex);
    }

    [Fact]
    public void Double_Dispose_Should_Not_Throw()
    {
                if (!HasDevice) return;
        var cam = new HikScanner();
        cam.Connect(_deviceInfo!);
        cam.Dispose();
        cam.Dispose(); // 二次 Dispose
    }

    #endregion
}
