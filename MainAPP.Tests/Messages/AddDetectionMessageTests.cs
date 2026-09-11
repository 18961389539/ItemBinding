using MainAPP.Messages;
using Xunit;

namespace MainAPP.Tests.Messages;

/// <summary>
/// AddDetectionMessage 消息校验测试，验证非负数防护。
/// </summary>
public class AddDetectionMessageTests
{
    [Fact]
    public void Constructor_PositiveCount_StoresValue()
    {
        var msg = new AddDetectionMessage(5);

        Assert.Equal(5, msg.Value);
    }

    [Fact]
    public void Constructor_ZeroCount_StoresValue()
    {
        var msg = new AddDetectionMessage(0);

        Assert.Equal(0, msg.Value);
    }

    [Theory]
    [InlineData(-1)]
    [InlineData(-100)]
    public void Constructor_NegativeCount_ThrowsArgumentOutOfRangeException(int count)
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => new AddDetectionMessage(count));
    }
}
