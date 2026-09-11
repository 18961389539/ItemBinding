using MainAPP.Models;
using Xunit;

namespace MainAPP.Tests;

/// <summary>
/// LogEntry 模型单元测试示例，验证时间戳格式化与日志级别着色逻辑。
/// </summary>
public class LogEntryTests
{
    [Fact]
    public void Constructor_Default_SetsTimestampToNow()
    {
        var before = DateTime.Now.AddSeconds(-1);
        var entry = new LogEntry();
        var after = DateTime.Now.AddSeconds(1);

        Assert.InRange(entry.Timestamp, before, after);
    }

    [Fact]
    public void Constructor_WithLevelAndMessage_SetsProperties()
    {
        var entry = new LogEntry("INFO", "启动完成");

        Assert.Equal("INFO", entry.Level);
        Assert.Equal("启动完成", entry.Message);
        Assert.Equal("启动完成", entry.RenderedMessage);
    }

    [Theory]
    [InlineData("ERROR", 213, 94, 0)]      // 朱红色 #D55E00（Wong 2011 色盲友好）
    [InlineData("WARNING", 230, 159, 0)]   // 橙色 #E69F00
    [InlineData("INFO", 0, 158, 115)]      // 绿色 #009E73
    [InlineData("DEBUG", 0, 114, 178)]     // 蓝色 #0072B2
    public void GetColorByLevel_ReturnsExpectedBrush(string level, byte r, byte g, byte b)
    {
        var brush = LogEntry.GetColorByLevel(level);

        Assert.Equal(r, brush.Color.R);
        Assert.Equal(g, brush.Color.G);
        Assert.Equal(b, brush.Color.B);
    }

    [Theory]
    [InlineData("error")]   // 大小写不敏感
    [InlineData("Error")]
    [InlineData("ERROR")]
    public void GetColorByLevel_IsCaseInsensitive(string level)
    {
        var brush = LogEntry.GetColorByLevel(level);

        Assert.Equal(213, brush.Color.R); // 朱红色 #D55E00（Wong 2011 色盲友好）
    }

    [Fact]
    public void GetColorByLevel_UnknownLevel_ReturnsDefaultBlack()
    {
        var brush = LogEntry.GetColorByLevel("UNKNOWN");

        Assert.Equal(0, brush.Color.R);
        Assert.Equal(0, brush.Color.G);
        Assert.Equal(0, brush.Color.B);
    }

    [Fact]
    public void TimestampText_FormatsWithMilliseconds()
    {
        var entry = new LogEntry
        {
            Timestamp = new DateTime(2026, 7, 15, 10, 30, 45, 123)
        };

        Assert.Equal("2026-07-15 10:30:45.123", entry.TimestampText);
    }

    [Fact]
    public void TimestampText_IsCachedAcrossCalls()
    {
        var entry = new LogEntry
        {
            Timestamp = new DateTime(2026, 7, 15, 0, 0, 0)
        };

        var first = entry.TimestampText;
        var second = entry.TimestampText;

        Assert.Same(first, second);
    }
}
