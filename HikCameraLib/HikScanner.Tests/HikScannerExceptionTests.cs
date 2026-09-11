namespace HikScanner.Tests;

/// <summary>
/// HikScannerException 异常类单元测试，覆盖错误码描述映射、异常消息格式和 Check 方法。
/// </summary>
public class HikScannerExceptionTests
{
    [Fact]
    public void Constructor_Should_Format_Message_With_Hex_Error_Code()
    {
        var ex = new HikScannerException("操作失败", unchecked((int)0x80020004));

        Assert.Equal(unchecked((int)0x80020004), ex.ErrorCode);
        Assert.Contains("操作失败", ex.Message);
        Assert.Contains("0x80020004", ex.Message);
        Assert.Contains("参数错误", ex.Message);
    }

    [Fact]
    public void Constructor_Should_Include_Error_Description_In_Message()
    {
        var ex = new HikScannerException("打开设备失败", unchecked((int)0x80020203));

        Assert.Contains("设备无访问权限", ex.Message);
    }

    // ─── 通用错误码 (0x80020000–0x800200FF) ───

    [Theory]
    [InlineData(unchecked((int)0x80020000), "错误或无效的句柄")]
    [InlineData(unchecked((int)0x80020001), "不支持的功能")]
    [InlineData(unchecked((int)0x80020002), "缓存已满")]
    [InlineData(unchecked((int)0x80020003), "函数调用顺序错误")]
    [InlineData(unchecked((int)0x80020004), "参数错误")]
    [InlineData(unchecked((int)0x80020005), "资源申请失败")]
    [InlineData(unchecked((int)0x80020006), "无数据")]
    [InlineData(unchecked((int)0x80020007), "前置条件错误或运行环境已变化")]
    [InlineData(unchecked((int)0x80020008), "版本不匹配")]
    [InlineData(unchecked((int)0x80020009), "传入的内存空间不足")]
    [InlineData(unchecked((int)0x8002000A), "异常图像，可能是丢包导致图像不完整")]
    [InlineData(unchecked((int)0x8002000B), "动态导入DLL失败，缺少运行时库或驱动未安装")]
    [InlineData(unchecked((int)0x8002000C), "没有可输出的缓存")]
    [InlineData(unchecked((int)0x8002000F), "文件路径错误")]
    [InlineData(unchecked((int)0x800200FF), "未知错误")]
    public void GetErrorDescription_Should_Return_Correct_For_General_Errors(int code, string expected)
    {
        var desc = HikScannerException.GetErrorDescription(code);
        Assert.Equal(expected, desc);
    }

    // ─── GenICam 系列错误 (0x80020100–0x800201FF) ───

    [Theory]
    [InlineData(unchecked((int)0x80020100), "GenICam通用错误")]
    [InlineData(unchecked((int)0x80020101), "GenICam参数非法")]
    [InlineData(unchecked((int)0x80020102), "GenICam值超出范围")]
    [InlineData(unchecked((int)0x80020103), "GenICam属性错误")]
    [InlineData(unchecked((int)0x80020104), "GenICam运行环境有问题")]
    [InlineData(unchecked((int)0x80020105), "GenICam逻辑错误")]
    [InlineData(unchecked((int)0x80020106), "GenICam节点访问条件错误")]
    [InlineData(unchecked((int)0x80020107), "GenICam超时")]
    [InlineData(unchecked((int)0x80020108), "GenICam转换异常")]
    [InlineData(unchecked((int)0x800201FF), "GenICam未知错误")]
    public void GetErrorDescription_Should_Return_Correct_For_GenICam_Errors(int code, string expected)
    {
        var desc = HikScannerException.GetErrorDescription(code);
        Assert.Equal(expected, desc);
    }

    // ─── GigE 网络错误 (0x80020200–0x800202FF) ───

    [Theory]
    [InlineData(unchecked((int)0x80020200), "命令不被设备支持")]
    [InlineData(unchecked((int)0x80020201), "访问的目标地址不存在")]
    [InlineData(unchecked((int)0x80020202), "目标地址不可写")]
    [InlineData(unchecked((int)0x80020203), "设备无访问权限")]
    [InlineData(unchecked((int)0x80020204), "设备忙或网络断开")]
    [InlineData(unchecked((int)0x80020205), "网络包数据错误")]
    [InlineData(unchecked((int)0x80020206), "网络错误")]
    [InlineData(unchecked((int)0x80020221), "设备IP冲突")]
    public void GetErrorDescription_Should_Return_Correct_For_GigE_Errors(int code, string expected)
    {
        var desc = HikScannerException.GetErrorDescription(code);
        Assert.Equal(expected, desc);
    }

    // ─── USB 错误 (0x80020300–0x800203FF) ───

    [Theory]
    [InlineData(unchecked((int)0x80020300), "USB读取出错")]
    [InlineData(unchecked((int)0x80020301), "USB写入出错")]
    [InlineData(unchecked((int)0x80020302), "USB设备异常")]
    [InlineData(unchecked((int)0x80020303), "USB GenICam相关错误")]
    [InlineData(unchecked((int)0x80020304), "USB带宽不足")]
    [InlineData(unchecked((int)0x80020305), "USB驱动不匹配或未安装")]
    [InlineData(unchecked((int)0x800203FF), "USB未知错误")]
    public void GetErrorDescription_Should_Return_Correct_For_USB_Errors(int code, string expected)
    {
        var desc = HikScannerException.GetErrorDescription(code);
        Assert.Equal(expected, desc);
    }

    // ─── 升级错误 (0x80020400–0x800204FF) ───

    [Theory]
    [InlineData(unchecked((int)0x80020400), "升级固件不匹配")]
    [InlineData(unchecked((int)0x80020401), "升级固件语言不匹配")]
    [InlineData(unchecked((int)0x80020402), "设备已在升级中")]
    [InlineData(unchecked((int)0x80020403), "升级时相机内部错误")]
    [InlineData(unchecked((int)0x800204FF), "升级未知错误")]
    public void GetErrorDescription_Should_Return_Correct_For_Upgrade_Errors(int code, string expected)
    {
        var desc = HikScannerException.GetErrorDescription(code);
        Assert.Equal(expected, desc);
    }

    // ─── 网络组件错误 (0x80020500–0x800205FF) ───

    [Theory]
    [InlineData(unchecked((int)0x80020500), "创建Socket失败")]
    [InlineData(unchecked((int)0x80020501), "绑定Socket失败")]
    [InlineData(unchecked((int)0x80020504), "网络写入失败")]
    [InlineData(unchecked((int)0x80020505), "网络读取失败")]
    [InlineData(unchecked((int)0x80020507), "网络超时")]
    [InlineData(unchecked((int)0x800205FF), "网络组件未知错误")]
    public void GetErrorDescription_Should_Return_Correct_For_Network_Errors(int code, string expected)
    {
        var desc = HikScannerException.GetErrorDescription(code);
        Assert.Equal(expected, desc);
    }

    // ─── 未知错误码 ───

    [Fact]
    public void GetErrorDescription_Should_Return_Default_For_Unknown_Code()
    {
        var desc = HikScannerException.GetErrorDescription(0x12345678);
        Assert.Equal("未知错误码", desc);
    }

    [Fact]
    public void GetErrorDescription_Should_Return_Default_For_Zero()
    {
        var desc = HikScannerException.GetErrorDescription(0);
        Assert.Equal("未知错误码", desc); // 0 != OK, 未在 switch 中所以走 default
    }

    [Fact]
    public void GetErrorDescription_Should_Return_Default_For_Success_Code()
    {
        // MV_CODEREADER_OK = 0x00000000, 但 switch 中未包含
        var desc = HikScannerException.GetErrorDescription(
            MvCodeReaderSDKNet.MvCodeReader.MV_CODEREADER_OK);
        Assert.Equal("未知错误码", desc);
    }

    // ─── Check 方法 ───

    [Fact]
    public void Check_Should_Throw_When_Error_Code_Not_OK()
    {
        var ex = Assert.Throws<HikScannerException>(() =>
            HikScannerException.Check(unchecked((int)0x8002000B), "枚举设备"));

        Assert.Contains("枚举设备", ex.Message);
        Assert.Contains("动态导入DLL失败", ex.Message);
    }

    [Fact]
    public void Check_Should_Not_Throw_When_Error_Code_Is_OK()
    {
        var exception = Record.Exception(() =>
            HikScannerException.Check(MvCodeReaderSDKNet.MvCodeReader.MV_CODEREADER_OK, "操作"));

        Assert.Null(exception);
    }

    // ─── ErrorCode 属性 ───

    [Fact]
    public void ErrorCode_Property_Should_Store_Value()
    {
        var ex = new HikScannerException("测试", unchecked((int)0x80020507));
        Assert.Equal(unchecked((int)0x80020507), ex.ErrorCode);
    }

    [Fact]
    public void ErrorCode_Should_Handle_Negative_Int_Mapping()
    {
        // 0xFFFFFFFF = -1 as signed int
        var ex = new HikScannerException("测试", -1);
        Assert.Equal("未知错误码", HikScannerException.GetErrorDescription(-1));
    }
}
