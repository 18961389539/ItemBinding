using OpenCvSharp;

namespace CoordinateSystemMapping.Tests;

public class ChessboardGenerateTests
{
    [Fact]
    public void GenerateForA4_Default_Params_Should_Return_Valid_Mat()
    {
        using var mat = Chessboard.GenerateForA4(10);

        Assert.NotNull(mat);
        Assert.False(mat.Empty());
        Assert.Equal(MatType.CV_8UC1, mat.Type());
        Assert.True(mat.Width > 0);
        Assert.True(mat.Height > 0);
    }

    [Fact]
    public void GenerateForA4_Should_Be_Single_Channel()
    {
        using var mat = Chessboard.GenerateForA4(15);
        Assert.Equal(1, mat.Channels());
    }

    [Fact]
    public void GenerateForA4_Small_Vs_Large_SquareSize_Should_Produce_Different_Images()
    {
        using var smallSq = Chessboard.GenerateForA4(5, 300); // ~38 cols
        using var largeSq = Chessboard.GenerateForA4(10, 300); // ~19 cols
        Assert.True(smallSq.Width >= largeSq.Width);
    }

    [Fact]
    public void GenerateForA4_Various_DPI_Should_Scale_Proportionally()
    {
        using var lowDpi = Chessboard.GenerateForA4(10, 150);
        using var highDpi = Chessboard.GenerateForA4(10, 600);
        Assert.True(highDpi.Width > lowDpi.Width);
        Assert.True(highDpi.Height > lowDpi.Height);
    }

    [Fact]
    public void GenerateForA4_Min_SquareSize_Should_Not_Throw()
    {
        var ex = Record.Exception(() =>
        {
            using var mat = Chessboard.GenerateForA4(1);
            Assert.False(mat.Empty());
        });
        Assert.Null(ex);
    }

    [Fact]
    public void GenerateForA4_Extreme_SquareSize_Should_Throw()
    {
        Assert.Throws<ArgumentException>(() => Chessboard.GenerateForA4(297));
    }
}

public class ChessboardFindTests
{
    private Mat GenerateTypicalBoard()
    {
        // 生成 10mm 方块的 A4 棋盘用于测试
        return Chessboard.GenerateForA4(10, 300);
    }

    [Fact]
    public void Find_Generated_Board_With_Expected_Size_Should_Return_Corners()
    {
        using var board = GenerateTypicalBoard();
        int expectedCols = (int)Math.Floor(190.0 / 10);
        int expectedRows = (int)Math.Floor(277.0 / 10);

        // 用实际内角点数 (cols-1, rows-1) 来查
        Point2f[] corners = Chessboard.Find(board, new Size(expectedCols - 1, expectedRows - 1));
        Assert.NotNull(corners);
        Assert.True(corners.Length > 0);
    }
}
