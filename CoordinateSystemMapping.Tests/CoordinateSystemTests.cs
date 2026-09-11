using OpenCvSharp;

namespace CoordinateSystemMapping.Tests;

public class GetCoordinateSystemTests
{
    [Fact]
    public void GetCoordinateSystem_LShape_Should_Identify_Origin_At_RightAngle()
    {
        // 模拟 L 形布局：Origin(100,200), YPoint(100,100)上方1格, XPoint(300,200)右侧2格
        var points = new List<Point2f>
        {
            new(100, 200),  // Origin
            new(100, 100),  // YPoint
            new(300, 200)   // XPoint
        };

        var cs = Chessboard.GetCoordinateSystem(points);

        Assert.Equal(100f, cs.Origin.X, 1f);
        Assert.Equal(200f, cs.Origin.Y, 1f);
        Assert.Equal(300f, cs.XPoint.X, 1f);
        Assert.Equal(100f, cs.YPoint.Y, 1f);
    }

    [Fact]
    public void GetCoordinateSystem_Permuted_Input_Should_Produce_Same_Origin()
    {
        // 无论输入顺序如何，都应正确识别原点为直角顶点
        var points = new List<Point2f>
        {
            new(300, 200),  // XPoint first
            new(100, 100),  // YPoint second
            new(100, 200)   // Origin last
        };

        var cs = Chessboard.GetCoordinateSystem(points);
        Assert.Equal(100f, cs.Origin.X, 1f);
        Assert.Equal(200f, cs.Origin.Y, 1f);
    }

    [Fact]
    public void GetCoordinateSystem_XPoint_Should_Be_Farther_From_Origin_Than_YPoint()
    {
        var points = new List<Point2f>
        {
            new(100, 200),
            new(100, 100),
            new(300, 200)
        };

        var cs = Chessboard.GetCoordinateSystem(points);

        double distOriginToX = Math.Sqrt(Math.Pow(cs.XPoint.X - cs.Origin.X, 2) + Math.Pow(cs.XPoint.Y - cs.Origin.Y, 2));
        double distOriginToY = Math.Sqrt(Math.Pow(cs.YPoint.X - cs.Origin.X, 2) + Math.Pow(cs.YPoint.Y - cs.Origin.Y, 2));

        Assert.True(distOriginToX > distOriginToY,
            $"XPoint应比YPoint离原点远, X={distOriginToX:F0}, Y={distOriginToY:F0}");
    }

    [Theory]
    [InlineData(0)]
    [InlineData(1)]
    [InlineData(2)]
    [InlineData(4)]
    [InlineData(10)]
    public void GetCoordinateSystem_Wrong_Point_Count_Should_Throw(int count)
    {
        var points = Enumerable.Range(0, count).Select(i => new Point2f(i * 10, i * 10));
        Assert.Throws<ArgumentException>(() => Chessboard.GetCoordinateSystem(points));
    }

    [Fact]
    public void GetCoordinateSystem_All_Six_Permutations_Should_Yield_Same_Origin()
    {
        var a = new Point2f(100, 200); // Origin
        var b = new Point2f(300, 200); // XPoint
        var c = new Point2f(100, 100); // YPoint

        var allPermutations = new[]
        {
            new[] { a, b, c },
            new[] { a, c, b },
            new[] { b, a, c },
            new[] { b, c, a },
            new[] { c, a, b },
            new[] { c, b, a }
        };

        foreach (var perm in allPermutations)
        {
            var cs = Chessboard.GetCoordinateSystem(new List<Point2f>(perm));
            Assert.Equal(100f, cs.Origin.X, 1f);
            Assert.Equal(200f, cs.Origin.Y, 1f);
        }
    }

    [Fact]
    public void GetCoordinateSystem_Collinear_Points_Should_Still_Return_Without_Crashing()
    {
        // 三点共线时，算法仍应运行（不会崩溃），但结果可能不准确
        var points = new List<Point2f>
        {
            new(0, 0),
            new(100, 100),
            new(200, 200)
        };

        var ex = Record.Exception(() => Chessboard.GetCoordinateSystem(points));
        Assert.Null(ex); // 不应崩溃
    }
}

public class ThreePointCoordinateSystemTests
{
    [Fact]
    public void Default_Struct_All_Points_Are_Zero()
    {
        var cs = default(ThreePointCoordinateSystem);
        Assert.Equal(0f, cs.Origin.X);
        Assert.Equal(0f, cs.Origin.Y);
        Assert.Equal(0f, cs.XPoint.X);
        Assert.Equal(0f, cs.XPoint.Y);
        Assert.Equal(0f, cs.YPoint.X);
        Assert.Equal(0f, cs.YPoint.Y);
    }

    [Fact]
    public void DrawOnMat_Null_Should_Throw_ArgumentNullException()
    {
        var cs = new ThreePointCoordinateSystem();
        Assert.Throws<ArgumentNullException>(() => cs.DrawOnMat(null!));
    }

    [Fact]
    public void DrawOnMat_Valid_Mat_Should_Not_Throw()
    {
        var cs = new ThreePointCoordinateSystem
        {
            Origin = new Point2f(100, 200),
            XPoint = new Point2f(300, 200),
            YPoint = new Point2f(100, 100)
        };

        using var mat = new Mat(400, 400, MatType.CV_8UC3, Scalar.White);
        var ex = Record.Exception(() => cs.DrawOnMat(mat));
        Assert.Null(ex);
    }

    [Fact]
    public void DrawOnMat_Empty_Mat_Should_Not_Throw()
    {
        var cs = new ThreePointCoordinateSystem
        {
            Origin = new Point2f(10, 10),
            XPoint = new Point2f(50, 10),
            YPoint = new Point2f(10, 50)
        };

        using var mat = new Mat(100, 100, MatType.CV_8UC3, Scalar.Black);
        var ex = Record.Exception(() => cs.DrawOnMat(mat));
        Assert.Null(ex);
    }

    [Fact]
    public void DrawOnMat_Points_Outside_Bounds_Should_Not_Throw()
    {
        var cs = new ThreePointCoordinateSystem
        {
            Origin = new Point2f(500, 500),
            XPoint = new Point2f(800, 500),
            YPoint = new Point2f(500, 800)
        };

        using var mat = new Mat(100, 100, MatType.CV_8UC3, Scalar.White);
        var ex = Record.Exception(() => cs.DrawOnMat(mat));
        Assert.Null(ex); // 越界绘制不抛异常
    }

    [Fact]
    public void GetCoordinateSystem_Then_DrawOnMat_Integration()
    {
        var points = new List<Point2f>
        {
            new(100, 200), new(100, 100), new(300, 200)
        };
        var cs = Chessboard.GetCoordinateSystem(points);

        using var mat = new Mat(400, 400, MatType.CV_8UC3, Scalar.White);
        cs.DrawOnMat(mat);

        // 验证图像非空
        Assert.False(mat.Empty());
    }
}
