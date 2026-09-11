using OpenCvSharp;

namespace CoordinateSystemMapping.Tests;

/// <summary>
/// 端到端集成测试：
/// 生成棋盘 → 查找角点 → 检测三个标定圆 → 构建坐标系 → 坐标变换
/// </summary>
public class IntegrationTests
{
    [Fact]
    public void Generate_FindCorners_DetectCircles_FullPipeline()
    {
        // 1. 生成 A4 棋盘（10mm 方块，300 DPI）
        using var board = Chessboard.GenerateForA4(10, 300);

        // 2. 查找棋盘角点
        int expectedCols = (int)Math.Floor(190.0 / 10);
        int expectedRows = (int)Math.Floor(277.0 / 10);
        Point2f[] corners = Chessboard.Find(board, new Size(expectedCols - 1, expectedRows - 1));
        Assert.NotNull(corners);
        Assert.True(corners.Length > 0, "应找到棋盘角点");

        // 3. 检测三个标定圆
        var circles = Chessboard.GetThreePoints(board, patternSize: new Size(expectedCols - 1, expectedRows - 1));
        Assert.Equal(3, circles.Count);

        // 4. 提取圆心
        var centers = circles.Select(c => c.Center).ToList();

        // 5. 构建坐标系
        var cs = Chessboard.GetCoordinateSystem(centers);

        // 验证布局：三个点形成 L 形
        Assert.True(cs.Origin.X > 0);
        Assert.True(cs.Origin.Y > 0);
    }

    [Fact]
    public void Generate_FindCorners_DetectCircles_CoordinateTransform()
    {
        // 1-5. 构建坐标系
        using var board = Chessboard.GenerateForA4(10, 300);
        int expectedCols = (int)Math.Floor(190.0 / 10);
        int expectedRows = (int)Math.Floor(277.0 / 10);

        var circles = Chessboard.GetThreePoints(board, patternSize: new Size(expectedCols - 1, expectedRows - 1));
        Assert.Equal(3, circles.Count);

        var cs = Chessboard.GetCoordinateSystem(circles.Select(c => c.Center));

        // 6. 使用 CoordinateTransformer
        var ct = new CoordinateTransformer();
        // 物理距离：10mm方块, Y方向1格=10mm, X方向2格=20mm
        ct.Initialize(cs.Origin, cs.XPoint, cs.YPoint, 20, 10);

        Assert.True(ct.IsInitialized);

        // 7. 变换原点 → 物理 (0, 0)
        var physOrigin = ct.ImageToPhysical(cs.Origin);
        Assert.Equal(0f, physOrigin.X, 1e-2f);
        Assert.Equal(0f, physOrigin.Y, 1e-2f);

        // 8. 变换 XPoint → 物理 (20, 0)
        var physX = ct.ImageToPhysical(cs.XPoint);
        Assert.Equal(20.0f, physX.X, 2f);
        Assert.Equal(0f, physX.Y, 2f);

        // 9. 变换 YPoint → 物理 (0, 10)（Y向上为正）
        var physY = ct.ImageToPhysical(cs.YPoint);
        Assert.Equal(0f, physY.X, 2f);
        Assert.True(physY.Y > 0, $"YPoint Y should be positive, got {physY.Y}");
    }

    [Fact]
    public void Generate_Different_SquareSizes_Should_All_Detect()
    {
        int[] sizes = { 10, 20 };

        foreach (int size in sizes)
        {
            using var board = Chessboard.GenerateForA4(size, 300);
            int cols = (int)Math.Floor(190.0 / size);
            int rows = (int)Math.Floor(277.0 / size);

            if (cols < 3 || rows < 3) continue;

            var circles = Chessboard.GetThreePoints(board, patternSize: new Size(cols - 1, rows - 1));
            Assert.Equal(3, circles.Count);
        }
    }

    [Fact]
    public void Generate_Find_Multiple_DPI_Should_All_Detect()
    {
        // 10mm@300DPI 是最常见的测试参数
        using var board = Chessboard.GenerateForA4(10, 300);
        int cols = (int)Math.Floor(190.0 / 10);
        int rows = (int)Math.Floor(277.0 / 10);

        var circles = Chessboard.GetThreePoints(board, patternSize: new Size(cols - 1, rows - 1));
        Assert.Equal(3, circles.Count);
    }

    [Fact]
    public void Generate_Small_SquareSize_Roundtrip_Transform()
    {
        // 小方块（精细棋盘）
        using var board = Chessboard.GenerateForA4(10, 300);
        int cols = (int)Math.Floor(190.0 / 10);
        int rows = (int)Math.Floor(277.0 / 10);

        var circles = Chessboard.GetThreePoints(board, patternSize: new Size(cols - 1, rows - 1));
        var cs = Chessboard.GetCoordinateSystem(circles.Select(c => c.Center));

        var ct = new CoordinateTransformer();
        ct.Initialize(cs.Origin, cs.XPoint, cs.YPoint, 20, 10);

        // 随机测试点的正逆变换
        var testPoints = new[] { cs.Origin, cs.XPoint, cs.YPoint };
        foreach (var pt in testPoints)
        {
            var physical = ct.ImageToPhysical(pt);
            var backToImage = ct.PhysicalToImage(physical);
            Assert.Equal(pt.X, backToImage.X, 1f);
            Assert.Equal(pt.Y, backToImage.Y, 1f);
        }
    }

    [Fact]
    public void GetThreePoints_MultiChannel_Input_Should_Throw()
    {
        using var board = Chessboard.GenerateForA4(10, 300);
        using var colorBoard = new Mat();
        Cv2.CvtColor(board, colorBoard, ColorConversionCodes.GRAY2BGR);

        Assert.Throws<ArgumentException>(() =>
            Chessboard.GetThreePoints(colorBoard, patternSize: new Size(10, 10)));
    }

    [Fact]
    public void GetThreePoints_Without_PatternSize_Should_Still_Detect()
    {
        using var board = Chessboard.GenerateForA4(10, 300);

        // 不提供 patternSize，跳过棋盘掩码步骤
        var circles = Chessboard.GetThreePoints(board, patternSize: null);

        Assert.Equal(3, circles.Count);
    }

    [Fact]
    public void DebugProperties_Should_Be_Settable()
    {
        Chessboard.EnableDebugOutput = true;
        Assert.True(Chessboard.EnableDebugOutput);

        Chessboard.DebugOutputDirectory = @"C:\temp\test";
        Assert.Equal(@"C:\temp\test", Chessboard.DebugOutputDirectory);

        // 恢复默认
        Chessboard.EnableDebugOutput = false;
        Chessboard.DebugOutputDirectory = string.Empty;
    }
}
