using MainAPP.Models;
using Xunit;

namespace MainAPP.Tests.Models;

/// <summary>
/// AlgorithmSettings 子配置类单元测试。
/// 验证默认值、属性读写与边界条件。
/// </summary>
public class AlgorithmSettingsTests
{
    [Fact]
    public void Defaults_AreSetCorrectly()
    {
        var settings = new AlgorithmSettings();

        Assert.True(settings.DedupEnabled);
        Assert.Equal("Y", settings.DedupTrackAxis);
        Assert.Equal(3.0, settings.DedupPositionThreshold);
        Assert.Equal(2.0, settings.DedupAngleThreshold);
        Assert.Equal(50, settings.EdgeMarginLeftPixels);
        Assert.Equal(50, settings.EdgeMarginTopPixels);
        Assert.Equal(50, settings.EdgeMarginRightPixels);
        Assert.Equal(50, settings.EdgeMarginBottomPixels);
    }

    [Fact]
    public void Properties_CanBeAssigned()
    {
        var settings = new AlgorithmSettings
        {
            DedupEnabled = false,
            DedupTrackAxis = "X",
            DedupPositionThreshold = 5.5,
            DedupAngleThreshold = 1.25,
            EdgeMarginLeftPixels = 5,
            EdgeMarginTopPixels = 10,
            EdgeMarginRightPixels = 15,
            EdgeMarginBottomPixels = 20
        };

        Assert.False(settings.DedupEnabled);
        Assert.Equal("X", settings.DedupTrackAxis);
        Assert.Equal(5.5, settings.DedupPositionThreshold);
        Assert.Equal(1.25, settings.DedupAngleThreshold);
        Assert.Equal(5, settings.EdgeMarginLeftPixels);
        Assert.Equal(10, settings.EdgeMarginTopPixels);
        Assert.Equal(15, settings.EdgeMarginRightPixels);
        Assert.Equal(20, settings.EdgeMarginBottomPixels);
    }

    [Fact]
    public void EdgeMargins_AcceptZeroAndSmallValues()
    {
        var settings = new AlgorithmSettings();

        settings.EdgeMarginLeftPixels = 0;
        Assert.Equal(0, settings.EdgeMarginLeftPixels);

        settings.EdgeMarginTopPixels = 0.5;
        Assert.Equal(0.5, settings.EdgeMarginTopPixels);

        settings.EdgeMarginRightPixels = 0.25;
        Assert.Equal(0.25, settings.EdgeMarginRightPixels);

        settings.EdgeMarginBottomPixels = 1.5;
        Assert.Equal(1.5, settings.EdgeMarginBottomPixels);
    }

    [Fact]
    public void DedupPositionThreshold_AcceptsZeroAndSmallValues()
    {
        var settings = new AlgorithmSettings();

        settings.DedupPositionThreshold = 0.0;
        Assert.Equal(0.0, settings.DedupPositionThreshold);

        settings.DedupPositionThreshold = 0.001;
        Assert.Equal(0.001, settings.DedupPositionThreshold);
    }

    [Fact]
    public void DedupTrackAxis_CanBeNullOrEmpty()
    {
        var settings = new AlgorithmSettings();

        settings.DedupTrackAxis = "";
        Assert.Equal("", settings.DedupTrackAxis);

        settings.DedupTrackAxis = null!;
        Assert.Null(settings.DedupTrackAxis);
    }
}
