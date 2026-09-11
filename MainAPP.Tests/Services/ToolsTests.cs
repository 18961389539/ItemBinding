using Extensions;
using HikScanner;
using OpenCvSharp;
using System.Drawing;
using Xunit;

namespace MainAPP.Tests.Services;

/// <summary>
/// Tools 静态工具类单元测试。
/// 重点验证 DrawBarcodeResults 的参数校验、空数组、正常绘制路径。
/// UpdateShow 涉及 WriteableBitmap（需 STAThread），仅验证参数校验。
/// 注意：Tools 类已从 MainAPP.Services 迁移至 Extensions 命名空间。
/// </summary>
public class ToolsTests
{
    [Fact]
    public void UpdateShow_NullImage_ThrowsArgumentNullException()
    {
        Assert.Throws<ArgumentNullException>(() => Tools.UpdateShow(null!));
    }

    [Fact]
    public void DrawBarcodeResults_NullMat_ThrowsArgumentNullException()
    {
        Assert.Throws<ArgumentNullException>(() =>
            Tools.DrawBarcodeResults(null!, Array.Empty<HikBarcodeResult>()));
    }

    [Fact]
    public void DrawBarcodeResults_NullResults_ThrowsArgumentNullException()
    {
        using var mat = new Mat(100, 100, MatType.CV_8UC3, Scalar.Black);
        Assert.Throws<ArgumentNullException>(() =>
            Tools.DrawBarcodeResults(mat, null!));
    }

    [Fact]
    public void DrawBarcodeResults_EmptyResults_DoesNotThrow()
    {
        using var mat = new Mat(100, 100, MatType.CV_8UC3, Scalar.Black);

        var exception = Record.Exception(() =>
            Tools.DrawBarcodeResults(mat, Array.Empty<HikBarcodeResult>()));

        Assert.Null(exception);
    }

    [Fact]
    public void DrawBarcodeResults_ResultWithEmptyLocation_SkippedSafely()
    {
        using var mat = new Mat(100, 100, MatType.CV_8UC3, Scalar.Black);
        var results = new[]
        {
            new HikBarcodeResult { Code = "A", BoundingPoints = Array.Empty<PointF>() }
        };

        var exception = Record.Exception(() => Tools.DrawBarcodeResults(mat, results));

        Assert.Null(exception);
    }

    [Fact]
    public void DrawBarcodeResults_ResultWithNullLocation_SkippedSafely()
    {
        using var mat = new Mat(100, 100, MatType.CV_8UC3, Scalar.Black);
        var results = new[]
        {
            new HikBarcodeResult { Code = "A", BoundingPoints = null! }
        };

        var exception = Record.Exception(() => Tools.DrawBarcodeResults(mat, results));

        Assert.Null(exception);
    }

    [Fact]
    public void DrawBarcodeResults_ValidResults_DrawsWithoutThrowing()
    {
        using var mat = new Mat(500, 500, MatType.CV_8UC3, Scalar.Black);
        var results = new[]
        {
            new HikBarcodeResult
            {
                Code = "ABC123",
                BoundingPoints = new[]
                {
                    new PointF(100, 100),
                    new PointF(200, 100),
                    new PointF(200, 200),
                    new PointF(100, 200)
                },
                IDRScore = 95
            }
        };

        var exception = Record.Exception(() => Tools.DrawBarcodeResults(mat, results));

        Assert.Null(exception);
    }

    [Fact]
    public void DrawBarcodeResults_MultipleResults_DrawsAll()
    {
        using var mat = new Mat(500, 500, MatType.CV_8UC3, Scalar.Black);
        var results = new[]
        {
            new HikBarcodeResult
            {
                Code = "A",
                BoundingPoints = new[]
                {
                    new PointF(50, 50), new PointF(100, 50),
                    new PointF(100, 100), new PointF(50, 100)
                }
            },
            new HikBarcodeResult
            {
                Code = "B",
                BoundingPoints = new[]
                {
                    new PointF(200, 200), new PointF(300, 200),
                    new PointF(300, 300), new PointF(200, 300)
                }
            }
        };

        var exception = Record.Exception(() => Tools.DrawBarcodeResults(mat, results));

        Assert.Null(exception);
    }

    [Fact]
    public void DrawBarcodeResults_PreservesMatDimensions()
    {
        using var mat = new Mat(640, 480, MatType.CV_8UC3, Scalar.Black);
        var originalWidth = mat.Width;
        var originalHeight = mat.Height;
        var results = new[]
        {
            new HikBarcodeResult
            {
                Code = "X",
                BoundingPoints = new[]
                {
                    new PointF(10, 10), new PointF(50, 10),
                    new PointF(50, 50), new PointF(10, 50)
                }
            }
        };

        Tools.DrawBarcodeResults(mat, results);

        Assert.Equal(originalWidth, mat.Width);
        Assert.Equal(originalHeight, mat.Height);
    }
}
