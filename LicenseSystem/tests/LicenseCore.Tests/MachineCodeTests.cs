using LicenseCore;
using Xunit;

namespace LicenseCore.Tests;

public class MachineCodeTests
{
    [Fact]
    public void Create_ReturnsNonEmptyGroupedCode()
    {
        var code = MachineCode.Create();
        Assert.False(string.IsNullOrWhiteSpace(code));
        Assert.Contains('-', code);
    }

    [Fact]
    public void Encode_IsStableForSameFingerprint()
    {
        var fp = new byte[] { 1, 2, 3, 4, 5, 6, 7, 8, 9, 10, 11, 12 };
        var a = MachineCode.Encode(fp);
        var b = MachineCode.Encode(fp);
        Assert.Equal(a, b);
    }

    [Fact]
    public void Encode_ChangesWithFingerprint()
    {
        var a = MachineCode.Encode(new byte[] { 1, 2, 3, 4, 5, 6, 7, 8, 9, 10, 11, 12 });
        var b = MachineCode.Encode(new byte[] { 1, 2, 3, 4, 5, 6, 7, 8, 9, 10, 11, 13 });
        Assert.NotEqual(a, b);
    }

    [Fact]
    public void TryParse_RoundTrips()
    {
        var fp = new byte[] { 9, 8, 7, 6, 5, 4, 3, 2, 1, 0, 1, 2 };
        var code = MachineCode.Encode(fp);
        var parsed = MachineCode.TryParse(code);
        Assert.NotNull(parsed);
        Assert.Equal(fp, parsed);
    }

    [Fact]
    public void TryParse_RejectsWrongLength()
    {
        Assert.Null(MachineCode.TryParse("ABCDE-FGHIJ")); // 10 字符 → 6 字节 ≠ 12
    }

    [Fact]
    public void TryParse_RejectsInvalidCharacters()
    {
        Assert.Null(MachineCode.TryParse("ABCDE-OOOOO-12345-MNPQR"));
    }

    [Fact]
    public void EqualsText_IgnoresSeparatorsAndCase()
    {
        Assert.True(MachineCode.EqualsText("ABCDE-FGHIJ-12345", "abcde fghij 12345"));
    }

    [Fact]
    public void ComputeHash_IsStableAndShort()
    {
        var fp = new byte[] { 1, 2, 3, 4, 5, 6, 7, 8, 9, 10, 11, 12 };
        var h1 = MachineCode.ComputeHash(fp);
        var h2 = MachineCode.ComputeHash(fp);
        Assert.Equal(8, h1.Length);
        Assert.Equal(h1, h2);
    }
}
