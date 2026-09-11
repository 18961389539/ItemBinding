namespace HikScanner.Tests;

/// <summary>
/// Types.cs 数据模型和枚举单元测试，覆盖 HikDeviceInfo.ToString、枚举值正确性、FileAccessProgress 计算。
/// </summary>
public class TypesTests
{
    // ─── HikDeviceInfo.ToString ───

    [Fact]
    public void HikDeviceInfo_ToString_With_UserDefinedName()
    {
        var info = new HikDeviceInfo
        {
            UserDefinedName = "Camera01",
            ManufacturerName = "Hikrobot",
            ModelName = "MV-ID2016",
            SerialNumber = "ABCD1234",
            TLayerType = (uint)HikDeviceType.GigE
        };

        Assert.Equal("GEV: Camera01 (ABCD1234)", info.ToString());
    }

    [Fact]
    public void HikDeviceInfo_ToString_Without_UserDefinedName()
    {
        var info = new HikDeviceInfo
        {
            UserDefinedName = "",
            ManufacturerName = "Hikrobot",
            ModelName = "MV-ID2016",
            SerialNumber = "ABCD1234",
            TLayerType = (uint)HikDeviceType.GigE
        };

        Assert.Equal("GEV: Hikrobot MV-ID2016 (ABCD1234)", info.ToString());
    }

    [Fact]
    public void HikDeviceInfo_ToString_USB_Device()
    {
        var info = new HikDeviceInfo
        {
            UserDefinedName = "",
            ManufacturerName = "Hikrobot",
            ModelName = "MV-MC1000",
            SerialNumber = "USB001",
            TLayerType = (uint)HikDeviceType.USB
        };

        Assert.Equal("USB: Hikrobot MV-MC1000 (USB001)", info.ToString());
    }

    [Fact]
    public void HikDeviceInfo_ToString_Null_Properties_Should_Not_Throw()
    {
        var info = new HikDeviceInfo
        {
            TLayerType = (uint)HikDeviceType.GigE
        };
        var str = info.ToString();
        Assert.NotNull(str);
    }

    // ─── 枚举值正确性 ───

    [Theory]
    [InlineData(HikDeviceType.GigE, 1)]
    [InlineData(HikDeviceType.USB, 3)]
    public void HikDeviceType_Should_Have_Correct_Values(HikDeviceType value, int expected)
    {
        Assert.Equal(expected, (int)value);
    }

    [Theory]
    [InlineData(HikIpConfigType.Static, 0)]
    [InlineData(HikIpConfigType.DHCP, 1)]
    [InlineData(HikIpConfigType.LLA, 2)]
    public void HikIpConfigType_Should_Have_Correct_Values(HikIpConfigType value, int expected)
    {
        Assert.Equal(expected, (int)value);
    }

    [Theory]
    [InlineData(HikAccessMode.Exclusive, 1)]
    [InlineData(HikAccessMode.Control, 2)]
    public void HikAccessMode_Should_Have_Correct_Values(HikAccessMode value, int expected)
    {
        Assert.Equal(expected, (int)value);
    }

    [Theory]
    [InlineData(HikTriggerMode.Continuous, 0)]
    [InlineData(HikTriggerMode.Trigger, 1)]
    public void HikTriggerMode_Should_Have_Correct_Values(HikTriggerMode value, int expected)
    {
        Assert.Equal(expected, (int)value);
    }

    [Theory]
    [InlineData(HikTriggerSource.Line0, 0)]
    [InlineData(HikTriggerSource.Line1, 1)]
    [InlineData(HikTriggerSource.Line2, 2)]
    [InlineData(HikTriggerSource.Line3, 3)]
    [InlineData(HikTriggerSource.Counter, 4)]
    [InlineData(HikTriggerSource.Software, 7)]
    [InlineData(HikTriggerSource.SerialStart, 8)]
    [InlineData(HikTriggerSource.SelfTrigger, 9)]
    public void HikTriggerSource_Should_Have_Correct_Values(HikTriggerSource value, int expected)
    {
        Assert.Equal(expected, (int)value);
    }

    [Theory]
    [InlineData(HikGrabStatus.Success, 0)]
    [InlineData(HikGrabStatus.Timeout, 1)]
    [InlineData(HikGrabStatus.NoData, 2)]
    [InlineData(HikGrabStatus.BufferOverflow, 3)]
    [InlineData(HikGrabStatus.Error, 4)]
    public void HikGrabStatus_Should_Have_Correct_Values(HikGrabStatus value, int expected)
    {
        Assert.Equal(expected, (int)value);
    }

    [Theory]
    [InlineData(HikGrabMode.Polling, 0)]
    [InlineData(HikGrabMode.Callback, 1)]
    [InlineData(HikGrabMode.MscDualChannel, 2)]
    public void HikGrabMode_Should_Have_Correct_Values(HikGrabMode value, int expected)
    {
        Assert.Equal(expected, (int)value);
    }

    // ─── FileAccessProgress ───

    [Fact]
    public void FileAccessProgress_Percent_Halfwise()
    {
        var progress = new FileAccessProgress { Completed = 50, Total = 100 };
        Assert.Equal(50.0, progress.Percent);
    }

    [Fact]
    public void FileAccessProgress_Percent_Zero_Total_Should_Return_Zero()
    {
        var progress = new FileAccessProgress { Completed = 100, Total = 0 };
        Assert.Equal(0.0, progress.Percent);
    }

    [Fact]
    public void FileAccessProgress_Percent_Completed_Greater_Than_Total()
    {
        var progress = new FileAccessProgress { Completed = 200, Total = 100 };
        Assert.Equal(200.0, progress.Percent);
    }

    [Fact]
    public void FileAccessProgress_Percent_Exact_Completion()
    {
        var progress = new FileAccessProgress { Completed = 1024, Total = 1024 };
        Assert.Equal(100.0, progress.Percent);
    }

    [Fact]
    public void FileAccessProgress_Percent_Zero_Numerator()
    {
        var progress = new FileAccessProgress { Completed = 0, Total = 100 };
        Assert.Equal(0.0, progress.Percent);
    }

    [Fact]
    public void FileAccessProgress_Completed_Should_Be_Mutable()
    {
        var progress = new FileAccessProgress { Completed = 0, Total = 100 };
        progress.Completed = 30;
        Assert.Equal(30.0, progress.Percent);
    }

    // ─── HikImageData ───

    [Fact]
    public void HikImageData_Defaults()
    {
        var img = new HikImageData();
        Assert.Null(img.RawData);
        Assert.Equal(0u, img.Width);
        Assert.Equal(0u, img.Height);
        Assert.Equal(0u, img.FrameNum);
        Assert.Equal(0u, img.TriggerIndex);
        Assert.Equal(0u, img.ChannelId);
        Assert.False(img.IsMono8);
        Assert.False(img.IsJpeg);
    }

    [Fact]
    public void HikImageData_Mono8_Flag()
    {
        var img = new HikImageData { IsMono8 = true };
        Assert.True(img.IsMono8);
        Assert.False(img.IsJpeg);
    }

    [Fact]
    public void HikImageData_Jpeg_Flag()
    {
        var img = new HikImageData { IsJpeg = true };
        Assert.True(img.IsJpeg);
        Assert.False(img.IsMono8);
    }

    // ─── HikBarcodeResult.GetCodeTypeName ───

    [Fact]
    public void GetCodeTypeName_Should_Return_NonEmpty_For_All_SDK_Enum_Values()
    {
        // 验证所有 SDK 定义的条码类型都有对应的名称
        var codeTypes = new (int codeType, string expected)[]
        {
            ((int)MvCodeReaderSDKNet.MvCodeReader.MV_CODEREADER_CODE_TYPE.MV_CODEREADER_TDCR_DM, "DM码"),
            ((int)MvCodeReaderSDKNet.MvCodeReader.MV_CODEREADER_CODE_TYPE.MV_CODEREADER_TDCR_QR, "QR码"),
            ((int)MvCodeReaderSDKNet.MvCodeReader.MV_CODEREADER_CODE_TYPE.MV_CODEREADER_BCR_EAN8, "EAN8码"),
            ((int)MvCodeReaderSDKNet.MvCodeReader.MV_CODEREADER_CODE_TYPE.MV_CODEREADER_BCR_UPCE, "UPCE码"),
            ((int)MvCodeReaderSDKNet.MvCodeReader.MV_CODEREADER_CODE_TYPE.MV_CODEREADER_BCR_UPCA, "UPCA码"),
            ((int)MvCodeReaderSDKNet.MvCodeReader.MV_CODEREADER_CODE_TYPE.MV_CODEREADER_BCR_EAN13, "EAN13码"),
            ((int)MvCodeReaderSDKNet.MvCodeReader.MV_CODEREADER_CODE_TYPE.MV_CODEREADER_BCR_ISBN13, "ISBN13码"),
            ((int)MvCodeReaderSDKNet.MvCodeReader.MV_CODEREADER_CODE_TYPE.MV_CODEREADER_BCR_CODABAR, "库德巴码"),
            ((int)MvCodeReaderSDKNet.MvCodeReader.MV_CODEREADER_CODE_TYPE.MV_CODEREADER_BCR_ITF25, "交叉25码"),
            ((int)MvCodeReaderSDKNet.MvCodeReader.MV_CODEREADER_CODE_TYPE.MV_CODEREADER_BCR_CODE39, "Code 39码"),
            ((int)MvCodeReaderSDKNet.MvCodeReader.MV_CODEREADER_CODE_TYPE.MV_CODEREADER_BCR_CODE93, "Code 93码"),
            ((int)MvCodeReaderSDKNet.MvCodeReader.MV_CODEREADER_CODE_TYPE.MV_CODEREADER_BCR_CODE128, "Code 128码"),
            ((int)MvCodeReaderSDKNet.MvCodeReader.MV_CODEREADER_CODE_TYPE.MV_CODEREADER_TDCR_PDF417, "PDF417码"),
            ((int)MvCodeReaderSDKNet.MvCodeReader.MV_CODEREADER_CODE_TYPE.MV_CODEREADER_BCR_MATRIX25, "MATRIX25码"),
            ((int)MvCodeReaderSDKNet.MvCodeReader.MV_CODEREADER_CODE_TYPE.MV_CODEREADER_BCR_MSI, "MSI码"),
            ((int)MvCodeReaderSDKNet.MvCodeReader.MV_CODEREADER_CODE_TYPE.MV_CODEREADER_BCR_CODE11, "Code 11码"),
            ((int)MvCodeReaderSDKNet.MvCodeReader.MV_CODEREADER_CODE_TYPE.MV_CODEREADER_BCR_INDUSTRIAL25, "Industrial25码"),
            ((int)MvCodeReaderSDKNet.MvCodeReader.MV_CODEREADER_CODE_TYPE.MV_CODEREADER_BCR_CHINAPOST, "中国邮政码"),
            ((int)MvCodeReaderSDKNet.MvCodeReader.MV_CODEREADER_CODE_TYPE.MV_CODEREADER_BCR_ITF14, "交叉14码"),
            ((int)MvCodeReaderSDKNet.MvCodeReader.MV_CODEREADER_CODE_TYPE.MV_CODEREADER_TDCR_ECC140, "ECC140码"),
        };

        foreach (var (codeType, expected) in codeTypes)
        {
            var actual = HikBarcodeResult.GetCodeTypeName(codeType);
            Assert.Equal(expected, actual);
        }
    }

    [Fact]
    public void GetCodeTypeName_Unknown_Code_Should_Return_Unknown()
    {
        Assert.Equal("未知码", HikBarcodeResult.GetCodeTypeName(999));
        Assert.Equal("未知码", HikBarcodeResult.GetCodeTypeName(-1));
    }

    // ─── HikReconnectErrorEventArgs ───

    [Fact]
    public void HikReconnectErrorEventArgs_Should_Store_Values()
    {
        var args = new HikReconnectErrorEventArgs
        {
            Step = "创建句柄",
            ErrorCode = unchecked((int)0x8002000B),
            ErrorDescription = "动态导入DLL失败"
        };

        Assert.Equal("创建句柄", args.Step);
        Assert.Equal(unchecked((int)0x8002000B), args.ErrorCode);
        Assert.Equal("动态导入DLL失败", args.ErrorDescription);
    }

    // ─── HikEnumInfo ───

    [Fact]
    public void HikEnumInfo_Defaults()
    {
        var info = new HikEnumInfo();
        Assert.Equal(0u, info.CurrentValue);
        Assert.Null(info.SupportedValues);
    }

    [Fact]
    public void HikEnumInfo_With_Values()
    {
        var info = new HikEnumInfo
        {
            CurrentValue = 7,
            SupportedValues = new uint[] { 0, 1, 2, 3, 7 }
        };

        Assert.Equal(7u, info.CurrentValue);
        Assert.Equal(5, info.SupportedValues!.Length);
    }
}
