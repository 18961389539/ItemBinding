using HikScanner;
using MainAPP.Models;
using Xunit;

namespace MainAPP.Tests.Models;

/// <summary>
/// FrameResult 单元测试，覆盖 From 工厂方法、兼容属性、条码缓存与 ArrayPool 释放。
/// </summary>
public class FrameResultTests
{
    [Fact]
    public void From_NullSource_ThrowsArgumentNullException()
    {
        Assert.Throws<ArgumentNullException>(() => FrameResult.From(null!));
    }

    [Fact]
    public void From_WithImage_CopiesImageData()
    {
        var rawData = new byte[] { 1, 2, 3, 4 };
        var source = new HikGrabResult
        {
            Image = new HikImageData
            {
                RawData = rawData,
                Width = 2,
                Height = 2,
                FrameNum = 7,
                IsMono8 = true
            }
        };

        var frame = FrameResult.From(source);

        Assert.NotNull(frame.Image);
        Assert.Equal(2u, frame.Width);
        Assert.Equal(2u, frame.Height);
        Assert.Equal(7u, frame.FrameNumber);
        Assert.True(frame.IsMono8);
        Assert.False(frame.IsJpeg);
        // ArrayPool.Rent 返回的缓冲区可能大于请求长度，仅比较有效部分
        Assert.True(frame.ImageData!.Length >= rawData.Length);
        Assert.Equal(rawData, frame.ImageData.Take(rawData.Length).ToArray());
        // 修改源不应影响副本
        rawData[0] = 99;
        Assert.Equal(1, frame.ImageData[0]);
    }

    [Fact]
    public void From_WithoutImage_ReturnsNullImageData()
    {
        var source = new HikGrabResult();

        var frame = FrameResult.From(source);

        Assert.Null(frame.Image);
        Assert.Null(frame.ImageData);
        Assert.Equal(0u, frame.Width);
        Assert.Equal(0u, frame.Height);
        Assert.Equal(0u, frame.FrameNumber);
        Assert.False(frame.IsMono8);
        Assert.False(frame.IsJpeg);
    }

    [Fact]
    public void From_CopiesBarcodeList()
    {
        var source = new HikGrabResult
        {
            Barcodes = new List<HikBarcodeResult>
            {
                new() { Code = "A" },
                new() { Code = "B" }
            },
            IsGetCode = true
        };

        var frame = FrameResult.From(source);

        Assert.True(frame.HasBarcodeResults);
        Assert.Equal(2, frame.BarcodeResults.Length);
        Assert.Equal("A", frame.BarcodeResults[0].Code);
        Assert.Equal("B", frame.BarcodeResults[1].Code);
        // 浅拷贝：修改源列表不应影响副本
        source.Barcodes.Add(new HikBarcodeResult { Code = "C" });
        Assert.Equal(2, frame.BarcodeResults.Length);
    }

    [Fact]
    public void From_NullBarcodeList_YieldsEmptyCachedArray()
    {
        var source = new HikGrabResult { Barcodes = null! };

        var frame = FrameResult.From(source);

        Assert.Empty(frame.BarcodeResults);
        Assert.False(frame.HasBarcodeResults);
    }

    [Fact]
    public void BarcodeResults_IsCachedAcrossCalls()
    {
        var source = new HikGrabResult
        {
            Barcodes = new List<HikBarcodeResult> { new() { Code = "X" } }
        };
        var frame = FrameResult.From(source);

        var first = frame.BarcodeResults;
        var second = frame.BarcodeResults;

        Assert.Same(first, second);
    }

    [Fact]
    public void PixelFormatName_ReflectsImageFlags()
    {
        var jpegFrame = FrameResult.From(new HikGrabResult
        {
            Image = new HikImageData { RawData = new byte[] { 1 }, IsJpeg = true }
        });
        var monoFrame = FrameResult.From(new HikGrabResult
        {
            Image = new HikImageData { RawData = new byte[] { 1 }, IsMono8 = true }
        });
        var rgbFrame = FrameResult.From(new HikGrabResult
        {
            Image = new HikImageData { RawData = new byte[] { 1, 2, 3 } }
        });

        Assert.Equal("Jpeg", jpegFrame.PixelFormatName);
        Assert.Equal("Mono8", monoFrame.PixelFormatName);
        Assert.Equal("RGB8_Packed", rgbFrame.PixelFormatName);
    }

    [Fact]
    public void IsRgb8Packed_TrueForNonJpegNonMono8WithImageData()
    {
        var frame = FrameResult.From(new HikGrabResult
        {
            Image = new HikImageData { RawData = new byte[] { 1, 2, 3 } }
        });

        Assert.True(frame.IsRgb8Packed);
    }

    [Fact]
    public void IsRgb8Packed_FalseForMono8OrJpeg()
    {
        var monoFrame = FrameResult.From(new HikGrabResult
        {
            Image = new HikImageData { RawData = new byte[] { 1 }, IsMono8 = true }
        });
        var jpegFrame = FrameResult.From(new HikGrabResult
        {
            Image = new HikImageData { RawData = new byte[] { 1 }, IsJpeg = true }
        });

        Assert.False(monoFrame.IsRgb8Packed);
        Assert.False(jpegFrame.IsRgb8Packed);
    }

    [Fact]
    public void ReleaseImageData_ReturnsBufferToPool()
    {
        var frame = FrameResult.From(new HikGrabResult
        {
            Image = new HikImageData { RawData = new byte[] { 1, 2, 3, 4 } }
        });
        Assert.NotNull(frame.ImageData);

        frame.ReleaseImageData();

        Assert.Null(frame.ImageData);
        // 幂等：再次调用不应抛异常
        frame.ReleaseImageData();
    }

    [Fact]
    public void ReleaseImageData_NoImage_DoesNothing()
    {
        var frame = FrameResult.From(new HikGrabResult());

        frame.ReleaseImageData();

        Assert.Null(frame.Image);
    }

    [Fact]
    public void From_PreservesStatusAndFlags()
    {
        var source = new HikGrabResult
        {
            Status = HikGrabStatus.Timeout,
            RawErrorCode = 42,
            IsGetCode = true
        };

        var frame = FrameResult.From(source);

        Assert.Equal(HikGrabStatus.Timeout, frame.Status);
        Assert.Equal(42, frame.RawErrorCode);
        Assert.True(frame.IsGetCode);
    }

    [Fact]
    public void Defaults_ForNewFrameResult()
    {
        var before = DateTime.Now.AddSeconds(-1);
        var frame = new FrameResult();
        var after = DateTime.Now.AddSeconds(1);

        Assert.InRange(frame.EncoderReceivedTime, before, after);
        Assert.Equal(0u, frame.EncoderValue);
        Assert.False(frame.NeedDrop);
        Assert.False(frame.IsFrameLoss);
        Assert.False(frame.IsManualShot);
    }
}
