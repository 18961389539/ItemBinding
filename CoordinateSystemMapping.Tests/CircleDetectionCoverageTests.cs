using OpenCvSharp;

namespace CoordinateSystemMapping.Tests;

/// <summary>
/// 圆检测覆盖率测试 — 用不同尺寸和 DPI 生成棋盘，验证 GetThreePoints 检出率
/// </summary>
public class CircleDetectionCoverageTests
{
    public static IEnumerable<object[]> BoardConfigs()
    {
        int[] sizes = { 8, 10, 15, 20 };
        int[] dpis = { 150, 300 };
        foreach (int size in sizes)
            foreach (int dpi in dpis)
                yield return new object[] { size, dpi };
    }

    [Theory]
    [MemberData(nameof(BoardConfigs))]
    public void GetThreePoints_Should_Detect_Three_Circles(int squareSizeMM, int dpi)
    {
        using var board = Chessboard.GenerateForA4(squareSizeMM, dpi);
        int cols = (int)Math.Floor(190.0 / squareSizeMM);
        int rows = (int)Math.Floor(277.0 / squareSizeMM);

        // 跳过无法形成棋盘的情况
        if (cols < 3 || rows < 3)
        {
            Assert.True(true, $"跳过 {squareSizeMM}mm@{dpi}DPI: 棋盘太小 ({cols}x{rows})");
            return;
        }

        var circles = Chessboard.GetThreePoints(board, patternSize: new Size(cols - 1, rows - 1));

        Assert.Equal(3, circles.Count);

        // 验证：三个圆心应当组成 L 形
        var cs = Chessboard.GetCoordinateSystem(circles.Select(c => c.Center));
        double originToX = Distance(cs.Origin, cs.XPoint);
        double originToY = Distance(cs.Origin, cs.YPoint);
        Assert.True(originToX > originToY * 0.5,
            $"XPoint 应至少是 YPoint 距离的一半: X={originToX:F0}, Y={originToY:F0}");
    }

    [Fact]
    public void GetThreePoints_Without_PatternSize_Still_Works()
    {
        using var board = Chessboard.GenerateForA4(10, 300);
        var circles = Chessboard.GetThreePoints(board, patternSize: null);
        Assert.Equal(3, circles.Count);
    }

    [Fact]
    public void GetThreePoints_With_Small_ROI_Padding()
    {
        using var board = Chessboard.GenerateForA4(10, 300);
        int cols = (int)Math.Floor(190.0 / 10);
        int rows = (int)Math.Floor(277.0 / 10);

        // 极小的 ROI 填充值可能裁剪掉边缘圆
        var circles = Chessboard.GetThreePoints(board,
            patternSize: new Size(cols - 1, rows - 1),
            roiPadding: 10);
        Assert.Equal(3, circles.Count);
    }

    [Fact]
    public void GetThreePoints_With_Large_ROI_Padding()
    {
        using var board = Chessboard.GenerateForA4(10, 300);
        int cols = (int)Math.Floor(190.0 / 10);
        int rows = (int)Math.Floor(277.0 / 10);

        var circles = Chessboard.GetThreePoints(board,
            patternSize: new Size(cols - 1, rows - 1),
            roiPadding: 200);
        Assert.Equal(3, circles.Count);
    }

    [Fact]
    public void GetThreePoints_Single_Strategy_Mode_Should_Still_Work()
    {
        using var board = Chessboard.GenerateForA4(10, 300);
        int cols = (int)Math.Floor(190.0 / 10);
        int rows = (int)Math.Floor(277.0 / 10);

        var circles = Chessboard.GetThreePoints(board,
            patternSize: new Size(cols - 1, rows - 1),
            enableMultiStrategy: false);
        Assert.Equal(3, circles.Count);
    }

    [Fact]
    public void GetThreePoints_Three_Returned_Circles_Are_Same_Size()
    {
        using var board = Chessboard.GenerateForA4(10, 300);
        int cols = (int)Math.Floor(190.0 / 10);
        int rows = (int)Math.Floor(277.0 / 10);

        var circles = Chessboard.GetThreePoints(board,
            patternSize: new Size(cols - 1, rows - 1));
        Assert.Equal(3, circles.Count);

        double area0 = circles[0].Size.Width * circles[0].Size.Height;
        double area1 = circles[1].Size.Width * circles[1].Size.Height;
        double area2 = circles[2].Size.Width * circles[2].Size.Height;

        double maxArea = Math.Max(area0, Math.Max(area1, area2));
        double minArea = Math.Min(area0, Math.Min(area1, area2));
        double ratio = minArea / maxArea;

        Assert.True(ratio > 0.5, $"三个圆面积应相近: ratio={ratio:F2}");
    }

    private static double Distance(Point2f p1, Point2f p2)
        => Math.Sqrt(Math.Pow(p1.X - p2.X, 2) + Math.Pow(p1.Y - p2.Y, 2));
}
