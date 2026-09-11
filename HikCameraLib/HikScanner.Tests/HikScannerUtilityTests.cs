namespace HikScanner.Tests;

/// <summary>
/// HikScanner 静态工具方法单元测试，覆盖 IpStringToUint 和 IsTextUtf8。
/// </summary>
public class HikCameraUtilityTests
{
    // ─── IpStringToUint ───

    [Theory]
    [InlineData("192.168.1.100", (uint)0xC0A80164)]
    [InlineData("0.0.0.0", (uint)0x00000000)]
    [InlineData("255.255.255.255", (uint)0xFFFFFFFF)]
    [InlineData("10.0.0.1", (uint)0x0A000001)]
    [InlineData("127.0.0.1", (uint)0x7F000001)]
    [InlineData("172.16.254.1", (uint)0xAC10FE01)]
    public void IpStringToUint_Should_Convert_Correctly(string ip, uint expected)
    {
        var result = HikScanner.IpStringToUint(ip);
        Assert.Equal(expected, result);
    }

    [Theory]
    [InlineData("")]
    [InlineData("192.168.1")]
    [InlineData("192.168.1.100.200")]
    public void IpStringToUint_Should_Throw_For_Invalid_Segment_Count(string ip)
    {
        Assert.Throws<ArgumentException>(() => HikScanner.IpStringToUint(ip));
    }

    [Theory]
    [InlineData("abc.def.ghi.jkl")]
    [InlineData("...")]
    [InlineData("192..168.1")]
    public void IpStringToUint_Should_Throw_For_NonNumeric(string ip)
    {
        // #8: 新实现使用 byte.TryParse，统一抛 ArgumentException
        Assert.Throws<ArgumentException>(() => HikScanner.IpStringToUint(ip));
    }

    [Fact]
    public void IpStringToUint_TrailingDot_Should_Throw_ArgumentException()
    {
        // "192.168.1." splits to ["192","168","1",""] → 4 segments, but "" can't parse
        Assert.Throws<ArgumentException>(() => HikScanner.IpStringToUint("192.168.1."));
    }

    [Fact]
    public void IpStringToUint_LeadingDot_Should_Throw_ArgumentException()
    {
        // ".192.168.1" splits to ["","192","168","1"] → 4 segments, but "" can't parse
        Assert.Throws<ArgumentException>(() => HikScanner.IpStringToUint(".192.168.1"));
    }

    [Theory]
    [InlineData("256.168.1.1")]  // 256 > 255
    [InlineData("192.168.1.300")] // 300 > 255
    [InlineData("-1.168.1.1")]   // negative
    public void IpStringToUint_Should_Throw_For_Out_Of_Range(string ip)
    {
        // #8: 新增 IP 范围检查（byte.TryParse 拒绝 > 255 的值）
        Assert.Throws<ArgumentException>(() => HikScanner.IpStringToUint(ip));
    }

    // ─── IsTextUtf8 ───

    [Theory]
    [InlineData(new byte[] { 0xE4, 0xB8, 0xAD }, true)]  // "中" UTF-8
    [InlineData(new byte[] { 0x48, 0x65, 0x6C, 0x6C, 0x6F }, false)] // "Hello" ASCII
    [InlineData(new byte[] { 0xE6, 0xB5, 0x8B, 0xE8, 0xAF, 0x95 }, true)] // "测试" UTF-8
    [InlineData(new byte[] { 0xC3, 0xA9 }, true)] // "é" UTF-8
    public void IsTextUtf8_Should_Detect_Correctly(byte[] data, bool expected)
    {
        Assert.Equal(expected, HikScanner.IsTextUtf8(data));
    }

    [Fact]
    public void IsTextUtf8_Empty_Array_Should_Return_False()
    {
        Assert.False(HikScanner.IsTextUtf8(Array.Empty<byte>()));
    }

    [Fact]
    public void IsTextUtf8_SingleByte_Above127_Should_Return_False()
    {
        // 单字节 0xC2 如果单独出现不是有效的 UTF-8
        Assert.False(HikScanner.IsTextUtf8(new byte[] { 0xC2 }));
    }

    [Fact]
    public void IsTextUtf8_AllAscii_Below127_Should_Return_False()
    {
        var ascii = System.Text.Encoding.ASCII.GetBytes("Hello World 12345");
        Assert.False(HikScanner.IsTextUtf8(ascii));
    }

    [Fact]
    public void IsTextUtf8_BOM_Should_Return_True()
    {
        var bom = new byte[] { 0xEF, 0xBB, 0xBF, 0xE4, 0xB8, 0xAD };
        Assert.True(HikScanner.IsTextUtf8(bom));
    }

    [Fact]
    public void IsTextUtf8_InvalidSequence_Should_Return_False()
    {
        // 0xFF 在 UTF-8 中无效
        Assert.False(HikScanner.IsTextUtf8(new byte[] { 0xFF, 0xFE }));
    }

    [Fact]
    public void IpStringToUint_RoundTrip_Loopback()
    {
        var ip = "127.0.0.1";
        var result = HikScanner.IpStringToUint(ip);
        // 验证与大端序 IP 编码一致
        Assert.Equal((uint)((127 << 24) | (0 << 16) | (0 << 8) | 1), result);
    }
}
