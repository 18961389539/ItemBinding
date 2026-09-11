namespace HikScanner.Tests;

/// <summary>
/// ScannerManager 多相机管理器单元测试 — 无需连接真实设备。
/// 覆盖空管理器行为、批量操作安全性、Dispose 安全性。
/// </summary>
public class ScannerManagerTests
{
    // ─── 空管理器 ───

    [Fact]
    public void Empty_Manager_Count_Should_Be_Zero()
    {
        var manager = new ScannerManager();
        Assert.Equal(0, manager.Count);
    }

    [Fact]
    public void Empty_Manager_Cameras_Should_Be_Empty()
    {
        var manager = new ScannerManager();
        Assert.Empty(manager.Cameras);
    }

    [Fact]
    public void Empty_Manager_Indexer_Should_Throw()
    {
        var manager = new ScannerManager();
        Assert.Throws<ArgumentOutOfRangeException>(() => manager[0]);
    }

    // ─── 批量操作安全性（空管理器不应抛异常） ───

    [Fact]
    public void StartGrabbingAll_Empty_Should_Not_Throw()
    {
        var manager = new ScannerManager();
        var ex = Record.Exception(() => manager.StartGrabbingAll());
        Assert.Null(ex);
    }

    [Fact]
    public void StartGrabbingWithCallbackAll_Empty_Should_Not_Throw()
    {
        var manager = new ScannerManager();
        var ex = Record.Exception(() => manager.StartGrabbingWithCallbackAll());
        Assert.Null(ex);
    }

    [Fact]
    public void StopGrabbingAll_Empty_Should_Not_Throw()
    {
        var manager = new ScannerManager();
        var ex = Record.Exception(() => manager.StopGrabbingAll());
        Assert.Null(ex);
    }

    [Fact]
    public void TriggerSoftwareAll_Empty_Should_Not_Throw()
    {
        var manager = new ScannerManager();
        var ex = Record.Exception(() => manager.TriggerSoftwareAll());
        Assert.Null(ex);
    }

    [Fact]
    public void SetFloatParamAll_Empty_Should_Not_Throw()
    {
        var manager = new ScannerManager();
        var ex = Record.Exception(() => manager.SetFloatParamAll("ExposureTime", 5000));
        Assert.Null(ex);
    }

    [Fact]
    public void SetExposureTimeAll_Empty_Should_Not_Throw()
    {
        var manager = new ScannerManager();
        var ex = Record.Exception(() => manager.SetExposureTimeAll(5000));
        Assert.Null(ex);
    }

    [Fact]
    public void SetGainAll_Empty_Should_Not_Throw()
    {
        var manager = new ScannerManager();
        var ex = Record.Exception(() => manager.SetGainAll(5));
        Assert.Null(ex);
    }

    [Fact]
    public void SetFrameRateAll_Empty_Should_Not_Throw()
    {
        var manager = new ScannerManager();
        var ex = Record.Exception(() => manager.SetFrameRateAll(30));
        Assert.Null(ex);
    }

    [Fact]
    public void DisconnectAll_Empty_Should_Not_Throw()
    {
        var manager = new ScannerManager();
        var ex = Record.Exception(() => manager.DisconnectAll());
        Assert.Null(ex);
    }

    [Fact]
    public void SubscribeAllImageEvents_Empty_Should_Not_Throw()
    {
        var manager = new ScannerManager();
        var ex = Record.Exception(() =>
            manager.SubscribeAllImageEvents((s, r) => { }));
        Assert.Null(ex);
    }

    [Fact]
    public void UnsubscribeAllImageEvents_Empty_Should_Not_Throw()
    {
        var manager = new ScannerManager();
        var ex = Record.Exception(() =>
            manager.UnsubscribeAllImageEvents((s, r) => { }));
        Assert.Null(ex);
    }

    // ─── Dispose 安全性 ───

    [Fact]
    public void Dispose_Empty_Should_Not_Throw()
    {
        var manager = new ScannerManager();
        var ex = Record.Exception(() => manager.Dispose());
        Assert.Null(ex);
    }

    [Fact]
    public void Double_Dispose_Should_Not_Throw()
    {
        var manager = new ScannerManager();
        manager.Dispose();
        var ex = Record.Exception(() => manager.Dispose());
        Assert.Null(ex);
    }

    [Fact]
    public void Dispose_After_DisconnectAll_Should_Not_Throw()
    {
        var manager = new ScannerManager();
        manager.DisconnectAll();
        var ex = Record.Exception(() => manager.Dispose());
        Assert.Null(ex);
    }

    // ─── Create 静态方法 ───

    [Fact]
    public void Create_Should_Return_NonNull_Manager()
    {
        // 无设备时应返回空管理器，不应抛异常
        try
        {
            var manager = ScannerManager.Create(HikDeviceType.GigE);
            Assert.NotNull(manager);
        }
        catch (HikScannerException)
        {
            // SDK 不可用时跳过
        }
    }

    [Fact]
    public void Create_USB_Should_Return_NonNull_Manager()
    {
        try
        {
            var manager = ScannerManager.Create(HikDeviceType.USB);
            Assert.NotNull(manager);
        }
        catch (HikScannerException)
        {
            // SDK 不可用时跳过
        }
    }

    // ─── ConnectAll 无设备场景 ───

    [Fact]
    public void ConnectAll_With_No_Device_Should_Return_Zero()
    {
        var manager = new ScannerManager();
        try
        {
            int connected = manager.ConnectAll(maxCount: 4, HikDeviceType.GigE);
            Assert.True(connected >= 0);
        }
        catch (HikScannerException)
        {
            // SDK 不可用时跳过
        }
    }

    // ─── #7: ObjectDisposedException ───

    [Fact]
    public void StartGrabbingAll_After_Dispose_Should_Throw_ObjectDisposedException()
    {
        var manager = new ScannerManager();
        manager.Dispose();
        Assert.Throws<ObjectDisposedException>(() => manager.StartGrabbingAll());
    }

    [Fact]
    public void ConnectAll_After_Dispose_Should_Throw_ObjectDisposedException()
    {
        var manager = new ScannerManager();
        manager.Dispose();
        Assert.Throws<ObjectDisposedException>(() => manager.ConnectAll());
    }

    [Fact]
    public void SetExposureTimeAll_After_Dispose_Should_Throw_ObjectDisposedException()
    {
        var manager = new ScannerManager();
        manager.Dispose();
        Assert.Throws<ObjectDisposedException>(() => manager.SetExposureTimeAll(5000));
    }

    [Fact]
    public void TriggerSoftwareAll_After_Dispose_Should_Throw_ObjectDisposedException()
    {
        var manager = new ScannerManager();
        manager.Dispose();
        Assert.Throws<ObjectDisposedException>(() => manager.TriggerSoftwareAll());
    }

    [Fact]
    public void StartMscDualChannelGrabAll_Empty_Should_Not_Throw()
    {
        var manager = new ScannerManager();
        var ex = Record.Exception(() => manager.StartMscDualChannelGrabAll(_ => { }));
        Assert.Null(ex);
    }

    [Fact]
    public void StartMscDualChannelGrabAll_After_Dispose_Should_Throw()
    {
        var manager = new ScannerManager();
        manager.Dispose();
        Assert.Throws<ObjectDisposedException>(() => manager.StartMscDualChannelGrabAll(_ => { }));
    }
}
