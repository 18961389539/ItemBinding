using LicenseCore;
using Xunit;

namespace LicenseCore.Tests;

public class Base32Tests
{
    [Theory]
    [InlineData(new byte[] { })]
    [InlineData(new byte[] { 0 })]
    [InlineData(new byte[] { 0xFF, 0x00, 0xAA, 0x55 })]
    [InlineData(new byte[] { 1, 2, 3, 4, 5, 6, 7, 8, 9, 10 })]
    public void RoundTrip_EncodesAndDecodes(byte[] data)
    {
        var encoded = Base32.Encode(data);
        var decoded = Base32.Decode(encoded);
        Assert.Equal(data, decoded);
    }

    [Fact]
    public void Grouped_IsParsable()
    {
        var raw = new byte[] { 0x12, 0x34, 0x56, 0x78, 0x9A, 0xBC, 0xDE, 0xF0, 0x11, 0x22 };
        var grouped = Base32.EncodeGrouped(raw, 5);
        Assert.Contains('-', grouped);
        var decoded = Base32.Decode(grouped);
        Assert.Equal(raw, decoded);
    }

    [Fact]
    public void Decode_ToleratesSeparatorsAndCase()
    {
        var raw = new byte[] { 0xAB, 0xCD, 0xEF, 0x12, 0x34, 0x56, 0x78, 0x90 };
        var grouped = Base32.EncodeGrouped(raw, 5);
        var lowercaseWithSpaces = grouped.Replace("-", " ").ToLowerInvariant();
        Assert.Equal(raw, Base32.Decode(lowercaseWithSpaces));
    }

    [Fact]
    public void Decode_RejectsInvalidCharacter()
    {
        Assert.Throws<FormatException>(() => Base32.Decode("ABCDE-FGHIJ-12345-OOOOO")); // O 不在 Crockford 字符表
    }

    [Fact]
    public void StripSeparators_NormalizesInput()
    {
        Assert.Equal("ABCDEFGHIJ12345", Base32.StripSeparators("ab-cde fgh\tIJ12345"));
    }

    [Fact]
    public void Alphabet_ExcludesAmbiguousCharacters()
    {
        // Crockford 变体不允许 I/L/O/U，避免人工抄写混淆
        foreach (char c in "ILOU")
        {
            Assert.DoesNotContain(c, "0123456789ABCDEFGHJKMNPQRSTVWXYZ");
        }
    }
}
