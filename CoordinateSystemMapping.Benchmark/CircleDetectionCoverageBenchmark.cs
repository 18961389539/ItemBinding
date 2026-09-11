using BenchmarkDotNet.Attributes;
using OpenCvSharp;

namespace CoordinateSystemMapping.Benchmark;

/// <summary>
/// 圆检测覆盖矩阵 — 测试不同方块尺寸 x DPI x 算法组合的检出率
/// </summary>
[MemoryDiagnoser]
[SimpleJob(warmupCount: 1, iterationCount: 3)]
public class CircleDetectionCoverageBenchmark
{
    [Params(5, 8, 10, 15, 20, 25)]
    public int SquareSizeMM { get; set; }

    [Params(150, 300, 600)]
    public int Dpi { get; set; }

    private Mat _board = null!;
    private int _cols, _rows;

    [GlobalSetup]
    public void Setup()
    {
        _board = Chessboard.GenerateForA4(SquareSizeMM, Dpi);
        _cols = (int)Math.Floor(190.0 / SquareSizeMM);
        _rows = (int)Math.Floor(277.0 / SquareSizeMM);
    }

    [GlobalCleanup]
    public void Cleanup()
    {
        _board.Dispose();
    }

    [Benchmark]
    public List<RotatedRect> GetThreePoints()
    {
        if (_cols < 3 || _rows < 3)
            return []; // 跳过无法形成棋盘的组合
        return Chessboard.GetThreePoints(_board, patternSize: new Size(_cols - 1, _rows - 1));
    }
}
