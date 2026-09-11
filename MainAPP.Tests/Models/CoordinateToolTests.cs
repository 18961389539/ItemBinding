using MainAPP.Models;
using OpenCvSharp;
using Xunit;

namespace MainAPP.Tests.Models;

/// <summary>
/// CoordinateTool 配置模型测试，验证默认值与 PatternSize 边界条件。
/// </summary>
public class CoordinateToolTests
{
    [Fact]
    public void Defaults_AreSetCorrectly()
    {
        var tool = new CoordinateTool();

        Assert.Equal(new Point2f(0, 0), tool.Origin);
        Assert.Equal(new Point2f(100, 0), tool.XPoint);
        Assert.Equal(new Point2f(0, 100), tool.YPoint);
        Assert.Equal(100, tool.SquareSize);
        Assert.Equal(13, tool.PatternWidth);
        Assert.Equal(9, tool.PatternHeight);
        Assert.Equal(50, tool.RoiPadding);
        Assert.Equal(100, tool.MinArea);
        Assert.Equal(500_000, tool.MaxArea);
        Assert.Equal(0.85, tool.MinCircularity);
        Assert.True(tool.EnableCLAHE);
        Assert.True(tool.EnableMultiStrategy);
    }

    [Fact]
    public void PatternSize_WhenBothPositive_ReturnsSize()
    {
        var tool = new CoordinateTool { PatternWidth = 11, PatternHeight = 7 };

        Assert.Equal(new Size(11, 7), tool.PatternSize);
    }

    [Fact]
    public void PatternSize_WhenWidthZero_ReturnsNull()
    {
        var tool = new CoordinateTool { PatternWidth = 0, PatternHeight = 9 };

        Assert.Null(tool.PatternSize);
    }

    [Fact]
    public void PatternSize_WhenHeightZero_ReturnsNull()
    {
        var tool = new CoordinateTool { PatternWidth = 13, PatternHeight = 0 };

        Assert.Null(tool.PatternSize);
    }

    [Fact]
    public void PatternSize_WhenBothNegative_ReturnsNull()
    {
        var tool = new CoordinateTool { PatternWidth = -1, PatternHeight = -1 };

        Assert.Null(tool.PatternSize);
    }

    [Fact]
    public void PatternSize_IsJsonIgnore_SerializationSkipsIt()
    {
        // 验证 PatternSize 不会序列化到 JSON（JsonIgnore 特性）
        var tool = new CoordinateTool { PatternWidth = 5, PatternHeight = 5 };
        var json = System.Text.Json.JsonSerializer.Serialize(tool);

        Assert.DoesNotContain("PatternSize", json);
    }
}
