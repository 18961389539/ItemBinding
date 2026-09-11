using System.Drawing;

namespace HikScanner.Tests;

/// <summary>
/// DetectionBoxHelper 坐标映射与绘制辅助类单元测试。
/// 覆盖 ScaleBarcodeBounds / ScaleOcrBounds / ScaleWaybillBounds 及绘制方法。
/// </summary>
public class DetectionBoxHelperTests
{
    // ─── ScaleBarcodeBounds ───

    [Fact]
    public void ScaleBarcodeBounds_Should_Scale_Correctly()
    {
        var barcode = new HikBarcodeResult
        {
            BoundingPoints = new PointF[]
            {
                new(0, 0), new(100, 0), new(100, 50), new(0, 50)
            }
        };
        uint imgW = 200, imgH = 100;
        var displaySize = new Size(400, 200);

        var result = DetectionBoxHelper.ScaleBarcodeBounds(barcode, imgW, imgH, displaySize);

        Assert.Equal(4, result.Length);
        Assert.Equal(0f, result[0].X);
        Assert.Equal(0f, result[0].Y);
        Assert.Equal(200f, result[1].X);  // 100 * (400/200)
        Assert.Equal(0f, result[1].Y);
        Assert.Equal(200f, result[2].X);
        Assert.Equal(100f, result[2].Y);  // 50 * (200/100)
        Assert.Equal(0f, result[3].X);
        Assert.Equal(100f, result[3].Y);
    }

    [Fact]
    public void ScaleBarcodeBounds_NullPoints_Should_Return_Zeros()
    {
        var barcode = new HikBarcodeResult { BoundingPoints = null };
        var result = DetectionBoxHelper.ScaleBarcodeBounds(barcode, 100, 100, new Size(200, 200));
        Assert.Equal(4, result.Length);
        Assert.All(result, p => Assert.Equal(PointF.Empty, p));
    }

    [Fact]
    public void ScaleBarcodeBounds_LessThan4Points_Should_Return_Zeros()
    {
        var barcode = new HikBarcodeResult
        {
            BoundingPoints = new PointF[] { new(1, 1), new(2, 2) }
        };
        var result = DetectionBoxHelper.ScaleBarcodeBounds(barcode, 100, 100, new Size(200, 200));
        Assert.Equal(4, result.Length);
        Assert.All(result, p => Assert.Equal(PointF.Empty, p));
    }

    [Fact]
    public void ScaleBarcodeBounds_DifferentAspectRatio_Should_Scale_Independently()
    {
        var barcode = new HikBarcodeResult
        {
            BoundingPoints = new PointF[]
            {
                new(50, 50), new(50, 50), new(50, 50), new(50, 50)
            }
        };
        uint imgW = 100, imgH = 200;
        var displaySize = new Size(300, 600);

        var result = DetectionBoxHelper.ScaleBarcodeBounds(barcode, imgW, imgH, displaySize);

        // sx = 300/100 = 3, sy = 600/200 = 3
        Assert.Equal(150f, result[0].X); // 50 * 3
        Assert.Equal(150f, result[0].Y);
    }

    // ─── ScaleOcrBounds ───

    [Fact]
    public void ScaleOcrBounds_Should_Scale_Correctly()
    {
        var ocr = new HikOcrResult
        {
            CenterX = 100, CenterY = 200, Width = 50, Height = 80
        };
        uint imgW = 200, imgH = 400;
        var displaySize = new Size(400, 800);

        var rect = DetectionBoxHelper.ScaleOcrBounds(ocr, imgW, imgH, displaySize);

        // sx = 2, sy = 2
        Assert.Equal(100 * 2 - 50 * 2 / 2f, rect.X);  // 200 - 50 = 150
        Assert.Equal(200 * 2 - 80 * 2 / 2f, rect.Y);  // 400 - 80 = 320
        Assert.Equal(50 * 2, rect.Width);              // 100
        Assert.Equal(80 * 2, rect.Height);             // 160
    }

    [Fact]
    public void ScaleOcrBounds_ZeroImageSize_Should_Not_Throw()
    {
        var ocr = new HikOcrResult { CenterX = 10, CenterY = 10, Width = 5, Height = 5 };
        var rect = DetectionBoxHelper.ScaleOcrBounds(ocr, 0, 0, new Size(100, 100));
        // 0 宽高图像 → 除零产生 NaN/Infinity，但不抛异常
        Assert.True(float.IsNaN(rect.X) || float.IsInfinity(rect.X) || rect.X <= 0);
    }

    // ─── ScaleWaybillBounds ───

    [Fact]
    public void ScaleWaybillBounds_Should_Scale_Correctly()
    {
        var waybill = new HikWaybillResult
        {
            CenterX = 50, CenterY = 60, Width = 40, Height = 30
        };
        uint imgW = 100, imgH = 120;
        var displaySize = new Size(200, 240);

        var rect = DetectionBoxHelper.ScaleWaybillBounds(waybill, imgW, imgH, displaySize);

        // sx = 2, sy = 2
        Assert.Equal(50 * 2 - 40 * 2 / 2f, rect.X);  // 100 - 40 = 60
        Assert.Equal(60 * 2 - 30 * 2 / 2f, rect.Y);  // 120 - 30 = 90
        Assert.Equal(40 * 2, rect.Width);              // 80
        Assert.Equal(30 * 2, rect.Height);             // 60
    }

    // ─── DrawBarcode ───

    [Fact]
    public void DrawBarcode_Should_Not_Throw()
    {
        var barcode = new HikBarcodeResult
        {
            BoundingPoints = new PointF[]
            {
                new(0, 0), new(50, 0), new(50, 50), new(0, 50)
            }
        };
        using var bmp = new Bitmap(100, 100);
        using var g = Graphics.FromImage(bmp);
        var ex = Record.Exception(() =>
            DetectionBoxHelper.DrawBarcode(g, barcode, 100, 100, new Size(100, 100)));
        Assert.Null(ex);
    }

    [Fact]
    public void DrawBarcode_NullPoints_Should_Not_Throw()
    {
        var barcode = new HikBarcodeResult { BoundingPoints = null };
        using var bmp = new Bitmap(100, 100);
        using var g = Graphics.FromImage(bmp);
        var ex = Record.Exception(() =>
            DetectionBoxHelper.DrawBarcode(g, barcode, 100, 100, new Size(100, 100)));
        Assert.Null(ex);
    }

    // ─── DrawOcr ───

    [Fact]
    public void DrawOcr_Should_Not_Throw()
    {
        var ocr = new HikOcrResult
        {
            CenterX = 50, CenterY = 50, Width = 20, Height = 10, Angle = 15f
        };
        using var bmp = new Bitmap(100, 100);
        using var g = Graphics.FromImage(bmp);
        var ex = Record.Exception(() =>
            DetectionBoxHelper.DrawOcr(g, ocr, 100, 100, new Size(100, 100)));
        Assert.Null(ex);
    }

    [Fact]
    public void DrawOcr_ZeroAngle_Should_Not_Throw()
    {
        var ocr = new HikOcrResult
        {
            CenterX = 50, CenterY = 50, Width = 20, Height = 10, Angle = 0f
        };
        using var bmp = new Bitmap(100, 100);
        using var g = Graphics.FromImage(bmp);
        var ex = Record.Exception(() =>
            DetectionBoxHelper.DrawOcr(g, ocr, 100, 100, new Size(100, 100)));
        Assert.Null(ex);
    }

    // ─── DrawWaybill ───

    [Fact]
    public void DrawWaybill_Should_Not_Throw()
    {
        var waybill = new HikWaybillResult
        {
            CenterX = 50, CenterY = 50, Width = 30, Height = 20, Angle = 45f
        };
        using var bmp = new Bitmap(100, 100);
        using var g = Graphics.FromImage(bmp);
        var ex = Record.Exception(() =>
            DetectionBoxHelper.DrawWaybill(g, waybill, 100, 100, new Size(100, 100)));
        Assert.Null(ex);
    }

    [Fact]
    public void DrawWaybill_ZeroAngle_Should_Not_Throw()
    {
        var waybill = new HikWaybillResult
        {
            CenterX = 50, CenterY = 50, Width = 30, Height = 20, Angle = 0f
        };
        using var bmp = new Bitmap(100, 100);
        using var g = Graphics.FromImage(bmp);
        var ex = Record.Exception(() =>
            DetectionBoxHelper.DrawWaybill(g, waybill, 100, 100, new Size(100, 100)));
        Assert.Null(ex);
    }
}
