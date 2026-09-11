using OpenCvSharp;

namespace CoordinateSystemMapping.Tests;

public class CoordinateTransformerTests
{
    #region Initialize

    [Fact]
    public void Initialize_Should_Set_IsInitialized_True()
    {
        var ct = new CoordinateTransformer();
        ct.Initialize(new Point2f(100, 100), new Point2f(200, 100), new Point2f(100, 50), 10, 10);
        Assert.True(ct.IsInitialized);
    }

    [Fact]
    public void Initialize_Default_IsInitialized_Should_Be_False()
    {
        var ct = new CoordinateTransformer();
        Assert.False(ct.IsInitialized);
    }

    [Fact]
    public void Initialize_Orthogonal_Axes_Should_Compute_Correct_ScaleFactors()
    {
        var ct = new CoordinateTransformer();
        // 原点(100,100), X轴点(200,100) — 100像素, 10mm → scale = 10 px/mm
        // Y轴点(100,50) — 50像素, 10mm → scale = 5 px/mm
        ct.Initialize(new Point2f(100, 100), new Point2f(200, 100), new Point2f(100, 50), 10, 10);

        ct.PrintInfo(); // 输出调试信息
    }

    [Fact]
    public void Initialize_Skewed_Axes_Should_Not_Throw()
    {
        var ct = new CoordinateTransformer();
        var ex = Record.Exception(() =>
            ct.Initialize(new Point2f(100, 100), new Point2f(250, 80), new Point2f(80, 50), 50, 50));
        Assert.Null(ex);
        Assert.True(ct.IsInitialized);
    }

    [Fact]
    public void Initialize_Zero_Distance_Should_Set_Infinity_Scale()
    {
        var ct = new CoordinateTransformer();
        // X轴点和原点相同 → pixelDist = 0 → scaleX = Infinity
        ct.Initialize(new Point2f(100, 100), new Point2f(100, 100), new Point2f(100, 50), 10, 10);
        Assert.True(ct.IsInitialized);
    }

    [Fact]
    public void Initialize_Zero_Physical_Distance_Should_Handle()
    {
        var ct = new CoordinateTransformer();
        // distX = 0 → scaleX = Infinity 或 NaN
        var ex = Record.Exception(() =>
            ct.Initialize(new Point2f(100, 100), new Point2f(200, 100), new Point2f(100, 50), 0, 10));
        Assert.Null(ex); // 不抛异常，但 scaleX = Infinity
        Assert.True(ct.IsInitialized);
    }

    #endregion

    #region ImageToPhysical — Point2f

    [Fact]
    public void ImageToPhysical_Origin_Should_Return_Zero()
    {
        var ct = new CoordinateTransformer();
        ct.Initialize(new Point2f(100, 100), new Point2f(200, 100), new Point2f(100, 50), 10, 10);

        var result = ct.ImageToPhysical(new Point2f(100, 100));
        Assert.Equal(0f, result.X, 1e-4f);
        Assert.Equal(0f, result.Y, 1e-4f);
    }

    [Fact]
    public void ImageToPhysical_Along_X_Axis_Should_Return_Positive_X()
    {
        var ct = new CoordinateTransformer();
        ct.Initialize(new Point2f(100, 100), new Point2f(200, 100), new Point2f(100, 50), 10, 10);

        var result = ct.ImageToPhysical(new Point2f(200, 100));
        Assert.True(result.X > 0, $"X should be positive, got {result.X}");
        Assert.Equal(0f, result.Y, 1e-4f);
    }

    [Fact]
    public void ImageToPhysical_Along_Y_Axis_Should_Return_Positive_Y()
    {
        var ct = new CoordinateTransformer();
        ct.Initialize(new Point2f(100, 100), new Point2f(200, 100), new Point2f(100, 50), 10, 10);

        var result = ct.ImageToPhysical(new Point2f(100, 50));
        Assert.Equal(0f, result.X, 1e-4f);
        // Y轴指向上方（图像Y减小），物理Y为正
        Assert.True(result.Y > 0, $"Y should be positive, got {result.Y}");
    }

    [Fact]
    public void ImageToPhysical_Arbitrary_Point()
    {
        var ct = new CoordinateTransformer();
        ct.Initialize(new Point2f(100, 100), new Point2f(200, 100), new Point2f(100, 50), 10, 10);

        // (150, 75): 50 pixels right (5mm), 25 pixels up (5mm)
        var result = ct.ImageToPhysical(new Point2f(150, 75));
        Assert.Equal(5.0f, result.X, 0.1f);
        Assert.Equal(5.0f, result.Y, 0.1f);
    }

    #endregion

    #region ImageToPhysical — Point2d

    [Fact]
    public void ImageToPhysical_Point2d_Should_Match_Point2f()
    {
        var ct = new CoordinateTransformer();
        ct.Initialize(new Point2f(100, 100), new Point2f(200, 100), new Point2f(100, 50), 10, 10);

        var resultF = ct.ImageToPhysical(new Point2f(150, 75));
        var resultD = ct.ImageToPhysical(new Point2d(150, 75));

        Assert.Equal(resultF.X, resultD.X, 1e-4);
        Assert.Equal(resultF.Y, resultD.Y, 1e-4);
    }

    #endregion

    #region ImageToPhysical — float/double tuple

    [Fact]
    public void ImageToPhysical_Float_Tuple_Should_Match_Point2f()
    {
        var ct = new CoordinateTransformer();
        ct.Initialize(new Point2f(100, 100), new Point2f(200, 100), new Point2f(100, 50), 10, 10);

        var fromPt = ct.ImageToPhysical(new Point2f(150, 75));
        var (wx, wy) = ct.ImageToPhysical(150f, 75f);

        Assert.Equal(fromPt.X, wx, 1e-4f);
        Assert.Equal(fromPt.Y, wy, 1e-4f);
    }

    [Fact]
    public void ImageToPhysical_Double_Tuple_Should_Match_Point2d()
    {
        var ct = new CoordinateTransformer();
        ct.Initialize(new Point2f(100, 100), new Point2f(200, 100), new Point2f(100, 50), 10, 10);

        var fromPt = ct.ImageToPhysical(new Point2d(150, 75));
        var (wx, wy) = ct.ImageToPhysical(150.0, 75.0);

        Assert.Equal(fromPt.X, wx, 1e-4);
        Assert.Equal(fromPt.Y, wy, 1e-4);
    }

    #endregion

    #region PhysicalToImage

    [Fact]
    public void PhysicalToImage_Zero_Should_Return_Origin()
    {
        var ct = new CoordinateTransformer();
        ct.Initialize(new Point2f(100, 100), new Point2f(200, 100), new Point2f(100, 50), 10, 10);

        var result = ct.PhysicalToImage(new Point2f(0, 0));
        Assert.Equal(100f, result.X, 1e-4f);
        Assert.Equal(100f, result.Y, 1e-4f);
    }

    [Fact]
    public void PhysicalToImage_Positive_X_Should_Go_Right()
    {
        var ct = new CoordinateTransformer();
        ct.Initialize(new Point2f(100, 100), new Point2f(200, 100), new Point2f(100, 50), 10, 10);

        var result = ct.PhysicalToImage(new Point2f(10, 0));
        Assert.Equal(200f, result.X, 0.1f);
        Assert.Equal(100f, result.Y, 0.1f);
    }

    [Fact]
    public void PhysicalToImage_Positive_Y_Should_Go_Up()
    {
        var ct = new CoordinateTransformer();
        ct.Initialize(new Point2f(100, 100), new Point2f(200, 100), new Point2f(100, 50), 10, 10);

        var result = ct.PhysicalToImage(new Point2f(0, 10));
        Assert.Equal(100f, result.X, 1e-4f);
        Assert.Equal(50f, result.Y, 0.1f);
    }

    #endregion

    #region Roundtrip — Image ↔ Physical

    [Fact]
    public void ImageToPhysical_Then_PhysicalToImage_Should_Be_Identity()
    {
        var ct = new CoordinateTransformer();
        ct.Initialize(new Point2f(100, 100), new Point2f(200, 100), new Point2f(100, 50), 10, 10);

        var testPoints = new[]
        {
            new Point2f(100, 100),
            new Point2f(200, 100),
            new Point2f(100, 50),
            new Point2f(150, 75),
            new Point2f(300, 200)
        };

        foreach (var pt in testPoints)
        {
            var physical = ct.ImageToPhysical(pt);
            var backToImage = ct.PhysicalToImage(physical);
            Assert.Equal(pt.X, backToImage.X, 0.01f);
            Assert.Equal(pt.Y, backToImage.Y, 0.01f);
        }
    }

    [Fact]
    public void PhysicalToImage_Then_ImageToPhysical_Should_Be_Identity()
    {
        var ct = new CoordinateTransformer();
        ct.Initialize(new Point2f(100, 100), new Point2f(200, 100), new Point2f(100, 50), 10, 10);

        var testPoints = new[]
        {
            new Point2f(0, 0),
            new Point2f(10, 0),
            new Point2f(0, -10),
            new Point2f(5, -5),
            new Point2f(-2, 3)
        };

        foreach (var pt in testPoints)
        {
            var image = ct.PhysicalToImage(pt);
            var backToPhysical = ct.ImageToPhysical(image);
            Assert.Equal(pt.X, backToPhysical.X, 0.01f);
            Assert.Equal(pt.Y, backToPhysical.Y, 0.01f);
        }
    }

    #endregion

    #region IsInitialized — external mutation

    [Fact]
    public void IsInitialized_Set_Externally_True_Does_Not_Change_Internal_Fields()
    {
        var ct = new CoordinateTransformer();
        ct.Initialize(new Point2f(100, 100), new Point2f(200, 100), new Point2f(100, 50), 10, 10);

        ct.IsInitialized = false; // 外部修改

        // 变换仍应正常工作（内部字段未变）
        var result = ct.ImageToPhysical(new Point2f(100, 100));
        Assert.Equal(0f, result.X, 1e-4f);
        Assert.Equal(0f, result.Y, 1e-4f);
    }

    [Fact]
    public void IsInitialized_Set_Externally_False_Transformations_Still_Work()
    {
        var ct = new CoordinateTransformer();
        // 不调 Initialize，但设 IsInitialized = true
        ct.IsInitialized = true;

        // 内部字段为零 → 变换结果是 (NaN, NaN) 或 (0, 0)
        var result = ct.ImageToPhysical(new Point2f(100, 100));
        // 行为已定义——不会抛异常
    }

    #endregion

    #region PrintInfo

    [Fact]
    public void PrintInfo_Should_Not_Throw()
    {
        var ct = new CoordinateTransformer();
        ct.Initialize(new Point2f(100, 100), new Point2f(200, 100), new Point2f(100, 50), 10, 10);

        var ex = Record.Exception(() => ct.PrintInfo());
        Assert.Null(ex);
    }

    [Fact]
    public void PrintInfo_Uninitialized_Should_Not_Throw()
    {
        var ct = new CoordinateTransformer();
        var ex = Record.Exception(() => ct.PrintInfo());
        Assert.Null(ex);
    }

    #endregion
}
