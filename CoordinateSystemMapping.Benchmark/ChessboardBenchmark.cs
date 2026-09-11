using BenchmarkDotNet.Attributes;
using OpenCvSharp;

namespace CoordinateSystemMapping.Benchmark;

[MemoryDiagnoser]
[SimpleJob(warmupCount: 2, iterationCount: 5)]
public class ChessboardBenchmark
{
    private Mat _board10mm = null!;
    private Mat _board15mm = null!;
    private Mat _board20mm = null!;
    private int _cols10, _rows10;
    private int _cols15, _rows15;
    private int _cols20, _rows20;

    [GlobalSetup]
    public void Setup()
    {
        _board10mm = Chessboard.GenerateForA4(10, 300);
        _board15mm = Chessboard.GenerateForA4(15, 300);
        _board20mm = Chessboard.GenerateForA4(20, 300);

        _cols10 = (int)Math.Floor(190.0 / 10); _rows10 = (int)Math.Floor(277.0 / 10);
        _cols15 = (int)Math.Floor(190.0 / 15); _rows15 = (int)Math.Floor(277.0 / 15);
        _cols20 = (int)Math.Floor(190.0 / 20); _rows20 = (int)Math.Floor(277.0 / 20);
    }

    [GlobalCleanup]
    public void Cleanup()
    {
        _board10mm.Dispose();
        _board15mm.Dispose();
        _board20mm.Dispose();
    }

    [Benchmark]
    public Mat GenerateForA4_10mm_300DPI() => Chessboard.GenerateForA4(10, 300);

    [Benchmark]
    public Mat GenerateForA4_15mm_300DPI() => Chessboard.GenerateForA4(15, 300);

    [Benchmark]
    public Mat GenerateForA4_10mm_600DPI() => Chessboard.GenerateForA4(10, 600);

    [Benchmark]
    public Point2f[] Find_10mm() => Chessboard.Find(_board10mm, new Size(_cols10 - 1, _rows10 - 1));

    [Benchmark]
    public Point2f[] Find_20mm() => Chessboard.Find(_board20mm, new Size(_cols20 - 1, _rows20 - 1));

    [Benchmark]
    public List<RotatedRect> GetThreePoints_10mm()
        => Chessboard.GetThreePoints(_board10mm, patternSize: new Size(_cols10 - 1, _rows10 - 1));

    [Benchmark]
    public List<RotatedRect> GetThreePoints_15mm()
        => Chessboard.GetThreePoints(_board15mm, patternSize: new Size(_cols15 - 1, _rows15 - 1));
}
