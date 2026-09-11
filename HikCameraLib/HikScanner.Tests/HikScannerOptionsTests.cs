namespace HikScanner.Tests;

/// <summary>
/// HikScannerOptions 配置选项类单元测试，覆盖默认值和属性设置。
/// </summary>
public class HikScannerOptionsTests
{
    [Fact]
    public void Defaults_Should_Be_Correct()
    {
        var options = new HikScannerOptions();

        Assert.False(options.AutoReconnect);
        Assert.Equal(2000, options.ReconnectIntervalMs);
        Assert.Equal(0, options.MaxReconnectAttempts);
        Assert.True(options.ExponentialBackoff);
        Assert.Equal(30000, options.MaxReconnectIntervalMs);
        Assert.Equal(1000, options.DefaultTimeoutMs);
        Assert.Equal(HikGrabMode.Polling, options.GrabMode);
        Assert.Null(options.OnDeviceDisconnected);
        Assert.Null(options.OnDeviceReconnected);
        Assert.Null(options.OnFrameGrabbed);
    }

    [Fact]
    public void AutoReconnect_Should_Be_Settable()
    {
        var options = new HikScannerOptions { AutoReconnect = true };
        Assert.True(options.AutoReconnect);
    }

    [Fact]
    public void ReconnectIntervalMs_Should_Be_Settable()
    {
        var options = new HikScannerOptions { ReconnectIntervalMs = 5000 };
        Assert.Equal(5000, options.ReconnectIntervalMs);
    }

    [Fact]
    public void ReconnectIntervalMs_Should_Accept_Zero()
    {
        var options = new HikScannerOptions { ReconnectIntervalMs = 0 };
        Assert.Equal(0, options.ReconnectIntervalMs);
    }

    [Fact]
    public void MaxReconnectAttempts_Should_Be_Settable()
    {
        var options = new HikScannerOptions { MaxReconnectAttempts = 10 };
        Assert.Equal(10, options.MaxReconnectAttempts);
    }

    [Fact]
    public void ExponentialBackoff_Should_Be_Settable_To_False()
    {
        var options = new HikScannerOptions { ExponentialBackoff = false };
        Assert.False(options.ExponentialBackoff);
    }

    [Fact]
    public void MaxReconnectIntervalMs_Should_Be_Settable()
    {
        var options = new HikScannerOptions { MaxReconnectIntervalMs = 60000 };
        Assert.Equal(60000, options.MaxReconnectIntervalMs);
    }

    [Fact]
    public void ApplyTo_Should_Set_New_Reconnect_Options()
    {
        using var cam = new HikScanner();
        var options = new HikScannerOptions
        {
            MaxReconnectAttempts = 3,
            ExponentialBackoff = false,
            MaxReconnectIntervalMs = 10000
        };
        cam.ApplyOptions(options);
        Assert.Equal(3, cam.MaxReconnectAttempts);
        Assert.False(cam.ExponentialBackoff);
        Assert.Equal(10000, cam.MaxReconnectIntervalMs);
    }

    [Fact]
    public void DefaultTimeoutMs_Should_Be_Settable()
    {
        var options = new HikScannerOptions { DefaultTimeoutMs = 3000 };
        Assert.Equal(3000, options.DefaultTimeoutMs);
    }

    [Theory]
    [InlineData(HikGrabMode.Polling)]
    [InlineData(HikGrabMode.Callback)]
    [InlineData(HikGrabMode.MscDualChannel)]
    public void GrabMode_Should_Accept_All_Values(HikGrabMode mode)
    {
        var options = new HikScannerOptions { GrabMode = mode };
        Assert.Equal(mode, options.GrabMode);
    }

    [Fact]
    public void Callbacks_Should_Be_Settable_To_Null()
    {
        var options = new HikScannerOptions
        {
            OnDeviceDisconnected = null,
            OnDeviceReconnected = null,
            OnFrameGrabbed = null
        };

        Assert.Null(options.OnDeviceDisconnected);
        Assert.Null(options.OnDeviceReconnected);
        Assert.Null(options.OnFrameGrabbed);
    }

    [Fact]
    public void Callbacks_Should_Be_Settable_To_Actions()
    {
        bool disconnected = false, reconnected = false;

        var options = new HikScannerOptions
        {
            OnDeviceDisconnected = () => disconnected = true,
            OnDeviceReconnected = () => reconnected = true,
            OnFrameGrabbed = r => { }
        };

        options.OnDeviceDisconnected?.Invoke();
        options.OnDeviceReconnected?.Invoke();

        Assert.True(disconnected);
        Assert.True(reconnected);
    }

    [Fact]
    public void Negative_ReconnectInterval_Should_Be_Accepted()
    {
        // 值类型不做校验，依赖调用方传入合法值
        var options = new HikScannerOptions { ReconnectIntervalMs = -1 };
        Assert.Equal(-1, options.ReconnectIntervalMs);
    }
}
