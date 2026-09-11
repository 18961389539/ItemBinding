using System.Drawing;

namespace HikScanner.Tests;

/// <summary>
/// 数据模型属性单元测试，覆盖 HikBarcodeResult / HikOcrResult / HikWaybillResult / HikDeviceInfo 全部属性。
/// </summary>
public class DataModelTests
{
    // ─── HikBarcodeResult 属性 ───

    [Fact]
    public void HikBarcodeResult_Defaults_Should_Be_Null_Or_Zero()
    {
        var r = new HikBarcodeResult();
        Assert.Null(r.Code);
        Assert.Equal(0, r.CodeType);
        Assert.Null(r.CodeTypeName);
        Assert.Equal(0, r.CodeId);
        Assert.NotNull(r.BoundingPoints);
        Assert.Empty(r.BoundingPoints);
        Assert.Equal(0, r.Angle);
        Assert.Equal(0, r.PPM);
        Assert.Equal(0, r.AlgorithmCost);
        Assert.Equal(0, r.Sharpness);
        Assert.Equal(0, r.TotalProcCost);
        Assert.Equal(0, r.OverQuality);
        Assert.Equal(0, r.IDRScore);
    }

    [Fact]
    public void HikBarcodeResult_All_Properties_Should_Be_Settable()
    {
        var r = new HikBarcodeResult
        {
            Code = "ABC123",
            CodeType = 42,
            CodeTypeName = "QR码",
            CodeId = 7,
            BoundingPoints = new PointF[] { new(1, 2), new(3, 4), new(5, 6), new(7, 8) },
            Angle = 90,
            PPM = 15,
            AlgorithmCost = 30,
            Sharpness = 80,
            TotalProcCost = 120,
            OverQuality = 95,
            IDRScore = 88
        };

        Assert.Equal("ABC123", r.Code);
        Assert.Equal(42, r.CodeType);
        Assert.Equal("QR码", r.CodeTypeName);
        Assert.Equal(7, r.CodeId);
        Assert.Equal(4, r.BoundingPoints!.Length);
        Assert.Equal(90, r.Angle);
        Assert.Equal(15, r.PPM);
        Assert.Equal(30, r.AlgorithmCost);
        Assert.Equal(80, r.Sharpness);
        Assert.Equal(120, r.TotalProcCost);
        Assert.Equal(95, r.OverQuality);
        Assert.Equal(88, r.IDRScore);
    }

    [Fact]
    public void HikBarcodeResult_BoundingPoints_Should_Preserve_Values()
    {
        var pts = new PointF[]
        {
            new(10.5f, 20.3f), new(30.1f, 40.2f),
            new(50.9f, 60.7f), new(70.4f, 80.6f)
        };
        var r = new HikBarcodeResult { BoundingPoints = pts };

        for (int i = 0; i < 4; i++)
        {
            Assert.Equal(pts[i].X, r.BoundingPoints![i].X);
            Assert.Equal(pts[i].Y, r.BoundingPoints[i].Y);
        }
    }

    // ─── HikOcrResult 属性 ───

    [Fact]
    public void HikOcrResult_Defaults_Should_Be_Null_Or_Zero()
    {
        var r = new HikOcrResult();
        Assert.Equal(0, r.Id);
        Assert.Null(r.Text);
        Assert.Equal(0, r.Length);
        Assert.Equal(0f, r.CharConfidence);
        Assert.Equal(0f, r.DetectConfidence);
        Assert.Equal(0, r.CenterX);
        Assert.Equal(0, r.CenterY);
        Assert.Equal(0, r.Width);
        Assert.Equal(0, r.Height);
        Assert.Equal(0f, r.Angle);
        Assert.Equal(0, r.AlgorithmCost);
    }

    [Fact]
    public void HikOcrResult_All_Properties_Should_Be_Settable()
    {
        var r = new HikOcrResult
        {
            Id = 5,
            Text = "Hello World",
            Length = 11,
            CharConfidence = 0.95f,
            DetectConfidence = 0.88f,
            CenterX = 100,
            CenterY = 200,
            Width = 50,
            Height = 30,
            Angle = 12.5f,
            AlgorithmCost = 45
        };

        Assert.Equal(5, r.Id);
        Assert.Equal("Hello World", r.Text);
        Assert.Equal(11, r.Length);
        Assert.Equal(0.95f, r.CharConfidence);
        Assert.Equal(0.88f, r.DetectConfidence);
        Assert.Equal(100, r.CenterX);
        Assert.Equal(200, r.CenterY);
        Assert.Equal(50, r.Width);
        Assert.Equal(30, r.Height);
        Assert.Equal(12.5f, r.Angle);
        Assert.Equal(45, r.AlgorithmCost);
    }

    // ─── HikWaybillResult 属性 ───

    [Fact]
    public void HikWaybillResult_Defaults_Should_Be_Null_Or_Zero()
    {
        var r = new HikWaybillResult();
        Assert.Equal(0f, r.CenterX);
        Assert.Equal(0f, r.CenterY);
        Assert.Equal(0f, r.Width);
        Assert.Equal(0f, r.Height);
        Assert.Equal(0f, r.Angle);
        Assert.Equal(0f, r.Confidence);
        Assert.Null(r.WaybillImage);
        Assert.Equal(0u, r.ImageLength);
    }

    [Fact]
    public void HikWaybillResult_All_Properties_Should_Be_Settable()
    {
        var img = new byte[] { 0xFF, 0xD8, 0xFF };
        var r = new HikWaybillResult
        {
            CenterX = 150.5f,
            CenterY = 250.3f,
            Width = 300.0f,
            Height = 200.0f,
            Angle = 45.0f,
            Confidence = 0.92f,
            WaybillImage = img,
            ImageLength = (uint)img.Length
        };

        Assert.Equal(150.5f, r.CenterX);
        Assert.Equal(250.3f, r.CenterY);
        Assert.Equal(300.0f, r.Width);
        Assert.Equal(200.0f, r.Height);
        Assert.Equal(45.0f, r.Angle);
        Assert.Equal(0.92f, r.Confidence);
        Assert.Same(img, r.WaybillImage);
        Assert.Equal(3u, r.ImageLength);
    }

    // ─── HikDeviceInfo 属性 ───

    [Fact]
    public void HikDeviceInfo_Defaults_Should_Be_Null_Or_Zero()
    {
        var info = new HikDeviceInfo();
        Assert.Null(info.SerialNumber);
        Assert.Null(info.ManufacturerName);
        Assert.Null(info.ModelName);
        Assert.Null(info.UserDefinedName);
        Assert.Null(info.DeviceVersion);
        Assert.Equal(0u, info.DeviceType);
        Assert.Equal(0u, info.TLayerType);
        Assert.Null(info.CurrentIp);
        Assert.Null(info.SubnetMask);
        Assert.Null(info.DefaultGateway);
        Assert.Null(info.MacAddress);
        Assert.Equal(0u, info.DeviceNumber);
        Assert.Equal(0u, info.IpConfigOption);
        Assert.Equal(0u, info.IpConfigCurrent);
        Assert.Equal(0u, info.NetExport);
    }

    [Fact]
    public void HikDeviceInfo_All_Properties_Should_Be_Settable()
    {
        var info = new HikDeviceInfo
        {
            SerialNumber = "SN12345678",
            ManufacturerName = "Hikrobot",
            ModelName = "MV-ID2016",
            UserDefinedName = "Reader01",
            DeviceVersion = "1.0.5",
            DeviceType = 1u,
            TLayerType = (uint)HikDeviceType.GigE,
            CurrentIp = "192.168.1.100",
            SubnetMask = "255.255.255.0",
            DefaultGateway = "192.168.1.1",
            MacAddress = "00-11-22-33-44-55",
            DeviceNumber = 1u,
            IpConfigOption = 1u,
            IpConfigCurrent = 0u,
            NetExport = 2u
        };

        Assert.Equal("SN12345678", info.SerialNumber);
        Assert.Equal("Hikrobot", info.ManufacturerName);
        Assert.Equal("MV-ID2016", info.ModelName);
        Assert.Equal("Reader01", info.UserDefinedName);
        Assert.Equal("1.0.5", info.DeviceVersion);
        Assert.Equal(1u, info.DeviceType);
        Assert.Equal((uint)HikDeviceType.GigE, info.TLayerType);
        Assert.Equal("192.168.1.100", info.CurrentIp);
        Assert.Equal("255.255.255.0", info.SubnetMask);
        Assert.Equal("192.168.1.1", info.DefaultGateway);
        Assert.Equal("00-11-22-33-44-55", info.MacAddress);
        Assert.Equal(1u, info.DeviceNumber);
        Assert.Equal(1u, info.IpConfigOption);
        Assert.Equal(0u, info.IpConfigCurrent);
        Assert.Equal(2u, info.NetExport);
    }

    // ─── HikDeviceInfo.ToString 补充场景 ───

    [Fact]
    public void HikDeviceInfo_ToString_With_All_Null_Names_Should_Not_Throw()
    {
        var info = new HikDeviceInfo
        {
            TLayerType = (uint)HikDeviceType.GigE,
            SerialNumber = "SN001"
        };
        var str = info.ToString();
        Assert.Contains("SN001", str);
        Assert.Contains("GEV", str);
    }

    [Fact]
    public void HikDeviceInfo_ToString_USB_With_UserDefinedName()
    {
        var info = new HikDeviceInfo
        {
            UserDefinedName = "USBReader",
            SerialNumber = "USB001",
            TLayerType = (uint)HikDeviceType.USB
        };
        Assert.Equal("USB: USBReader (USB001)", info.ToString());
    }

    [Fact]
    public void HikDeviceInfo_ToString_Unknown_Type_Should_Fall_To_USB()
    {
        var info = new HikDeviceInfo
        {
            UserDefinedName = "Test",
            SerialNumber = "X001",
            TLayerType = 99u
        };
        // 非 GigE 一律走 USB 分支
        Assert.Equal("USB: Test (X001)", info.ToString());
    }

    // ─── HikImageData 属性补充 ───

    [Fact]
    public void HikImageData_All_Properties_Should_Be_Settable()
    {
        var img = new HikImageData
        {
            RawData = new byte[] { 1, 2, 3, 4 },
            Width = 640,
            Height = 480,
            FrameNum = 12345,
            TriggerIndex = 3,
            ChannelId = 1,
            IsMono8 = true,
            IsJpeg = false
        };

        Assert.Equal(4, img.RawData!.Length);
        Assert.Equal(640u, img.Width);
        Assert.Equal(480u, img.Height);
        Assert.Equal(12345u, img.FrameNum);
        Assert.Equal(3u, img.TriggerIndex);
        Assert.Equal(1u, img.ChannelId);
        Assert.True(img.IsMono8);
        Assert.False(img.IsJpeg);
    }

    // ─── HikGrabResult 属性补充 ───

    [Fact]
    public void HikGrabResult_Image_Property_Should_Be_Settable()
    {
        var img = new HikImageData { Width = 100, Height = 100 };
        var result = new HikGrabResult { Image = img };
        Assert.Same(img, result.Image);
        Assert.Equal(100u, result.Image!.Width);
    }

    [Fact]
    public void HikGrabResult_All_Lists_Empty_By_Default()
    {
        var result = new HikGrabResult();
        // #14: 列表默认初始化为空集合
        Assert.NotNull(result.Barcodes);
        Assert.NotNull(result.OcrResults);
        Assert.NotNull(result.Waybills);
        Assert.Empty(result.Barcodes);
        Assert.Empty(result.OcrResults);
        Assert.Empty(result.Waybills);
        Assert.False(result.HasBarcode);
        Assert.False(result.HasOcr);
        Assert.False(result.HasWaybill);
    }

    // ─── HikEnumInfo 补充 ───

    [Fact]
    public void HikEnumInfo_Empty_Array_Should_Have_Zero_Length()
    {
        var info = new HikEnumInfo
        {
            CurrentValue = 0,
            SupportedValues = Array.Empty<uint>()
        };
        Assert.Empty(info.SupportedValues!);
    }

    // ─── FileAccessProgress 补充 ───

    [Fact]
    public void FileAccessProgress_Defaults_Should_Be_Zero()
    {
        var p = new FileAccessProgress();
        Assert.Equal(0, p.Completed);
        Assert.Equal(0, p.Total);
        Assert.Equal(0.0, p.Percent);
    }

    [Fact]
    public void FileAccessProgress_Negative_Completed_Should_Return_Negative_Percent()
    {
        var p = new FileAccessProgress { Completed = -10, Total = 100 };
        Assert.Equal(-10.0, p.Percent);
    }

    // ─── HikReconnectErrorEventArgs 补充 ───

    [Fact]
    public void HikReconnectErrorEventArgs_Defaults_Should_Be_Null_Or_Zero()
    {
        var args = new HikReconnectErrorEventArgs();
        Assert.Null(args.Step);
        Assert.Equal(0, args.ErrorCode);
        Assert.Null(args.ErrorDescription);
    }

    [Fact]
    public void HikReconnectErrorEventArgs_All_Properties_Should_Be_Settable()
    {
        var args = new HikReconnectErrorEventArgs
        {
            Step = "打开设备",
            ErrorCode = unchecked((int)0x80020204),
            ErrorDescription = "设备忙或网络断开"
        };

        Assert.Equal("打开设备", args.Step);
        Assert.Equal(unchecked((int)0x80020204), args.ErrorCode);
        Assert.Equal("设备忙或网络断开", args.ErrorDescription);
    }
}
