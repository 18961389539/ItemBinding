namespace HikScanner.Tests;

/// <summary>
/// HikGrabResult 采集结果模型单元测试，覆盖默认状态、便捷属性和 RawErrorCode。
/// </summary>
public class HikGrabResultTests
{
    [Fact]
    public void Default_Status_Should_Be_Success()
    {
        var result = new HikGrabResult();
        Assert.Equal(HikGrabStatus.Success, result.Status);
    }

    [Fact]
    public void Default_RawErrorCode_Should_Be_Zero()
    {
        var result = new HikGrabResult();
        Assert.Equal(0, result.RawErrorCode);
    }

    [Fact]
    public void Default_IsGetCode_Should_Be_False()
    {
        var result = new HikGrabResult();
        Assert.False(result.IsGetCode);
    }

    // ─── HasBarcode ───

    [Fact]
    public void HasBarcode_Should_Be_False_When_Null()
    {
        var result = new HikGrabResult();
        Assert.False(result.HasBarcode);
    }

    [Fact]
    public void HasBarcode_Should_Be_False_When_Empty()
    {
        var result = new HikGrabResult { Barcodes = new List<HikBarcodeResult>() };
        Assert.False(result.HasBarcode);
    }

    [Fact]
    public void HasBarcode_Should_Be_True_When_NotEmpty()
    {
        var result = new HikGrabResult
        {
            Barcodes = new List<HikBarcodeResult>
            {
                new() { Code = "TEST123", CodeType = 0 }
            }
        };
        Assert.True(result.HasBarcode);
    }

    // ─── HasOcr ───

    [Fact]
    public void HasOcr_Should_Be_False_When_Null()
    {
        var result = new HikGrabResult();
        Assert.False(result.HasOcr);
    }

    [Fact]
    public void HasOcr_Should_Be_False_When_Empty()
    {
        var result = new HikGrabResult { OcrResults = new List<HikOcrResult>() };
        Assert.False(result.HasOcr);
    }

    [Fact]
    public void HasOcr_Should_Be_True_When_NotEmpty()
    {
        var result = new HikGrabResult
        {
            OcrResults = new List<HikOcrResult> { new() { Text = "ABC" } }
        };
        Assert.True(result.HasOcr);
    }

    // ─── HasWaybill ───

    [Fact]
    public void HasWaybill_Should_Be_False_When_Null()
    {
        var result = new HikGrabResult();
        Assert.False(result.HasWaybill);
    }

    [Fact]
    public void HasWaybill_Should_Be_False_When_Empty()
    {
        var result = new HikGrabResult { Waybills = new List<HikWaybillResult>() };
        Assert.False(result.HasWaybill);
    }

    [Fact]
    public void HasWaybill_Should_Be_True_When_NotEmpty()
    {
        var result = new HikGrabResult
        {
            Waybills = new List<HikWaybillResult> { new() { Confidence = 0.95f } }
        };
        Assert.True(result.HasWaybill);
    }

    // ─── Combined results ───

    [Fact]
    public void All_HasXxx_Should_Be_True_When_All_Populated()
    {
        var result = new HikGrabResult
        {
            Status = HikGrabStatus.Success,
            Barcodes = new List<HikBarcodeResult> { new() { Code = "A" } },
            OcrResults = new List<HikOcrResult> { new() { Text = "B" } },
            Waybills = new List<HikWaybillResult> { new() { Confidence = 1.0f } },
            RawErrorCode = unchecked((int)0x8002000B),
            IsGetCode = true,
            Image = new HikImageData { Width = 640, Height = 480 }
        };

        Assert.True(result.HasBarcode);
        Assert.True(result.HasOcr);
        Assert.True(result.HasWaybill);
        Assert.True(result.IsGetCode);
        Assert.Equal(unchecked((int)0x8002000B), result.RawErrorCode);
        Assert.NotNull(result.Image);
        Assert.Equal((uint)640, result.Image!.Width);
    }

    [Fact]
    public void Status_Should_Be_Settable_To_All_Values()
    {
        foreach (HikGrabStatus status in Enum.GetValues<HikGrabStatus>())
        {
            var result = new HikGrabResult { Status = status };
            Assert.Equal(status, result.Status);
        }
    }
}
