using MainAPP.Messages;
using Xunit;

namespace MainAPP.Tests.Messages;

/// <summary>
/// AddBarcodeMessage 消息校验测试，验证 null/空白字符串防护。
/// </summary>
public class AddBarcodeMessageTests
{
    [Fact]
    public void Constructor_ValidValue_StoresValue()
    {
        var msg = new AddBarcodeMessage("ABC123");

        Assert.Equal("ABC123", msg.Value);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("\t\n")]
    public void Constructor_NullOrWhitespace_ThrowsArgumentException(string? value)
    {
        Assert.Throws<ArgumentException>(() => new AddBarcodeMessage(value!));
    }

    [Fact]
    public void Constructor_WhitespaceAroundValue_IsPreserved()
    {
        // AddBarcodeMessage 只校验 IsNullOrWhiteSpace，不 Trim，外部应保留原值
        var msg = new AddBarcodeMessage(" ABC ");

        Assert.Equal(" ABC ", msg.Value);
    }
}
