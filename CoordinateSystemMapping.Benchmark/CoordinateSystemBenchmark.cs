using BenchmarkDotNet.Attributes;
using OpenCvSharp;

namespace CoordinateSystemMapping.Benchmark;

[MemoryDiagnoser]
[SimpleJob(warmupCount: 3, iterationCount: 10)]
public class CoordinateSystemBenchmark
{
    private List<Point2f> _points = null!;
    private Mat _drawMat = null!;

    [GlobalSetup]
    public void Setup()
    {
        _points =
        [
            new(100, 200), // Origin
            new(300, 200), // XPoint
            new(100, 100)  // YPoint
        ];
        _drawMat = new Mat(400, 400, MatType.CV_8UC3, Scalar.White);
    }

    [GlobalCleanup]
    public void Cleanup()
    {
        _drawMat.Dispose();
    }

    [Benchmark]
    public ThreePointCoordinateSystem GetCoordinateSystem()
        => Chessboard.GetCoordinateSystem(_points);

    [Benchmark]
    public void DrawOnMat()
    {
        var cs = new ThreePointCoordinateSystem
        {
            Origin = new Point2f(100, 200),
            XPoint = new Point2f(300, 200),
            YPoint = new Point2f(100, 100)
        };
        cs.DrawOnMat(_drawMat);
    }
}

[MemoryDiagnoser]
[SimpleJob(warmupCount: 2, iterationCount: 5)]
public class FullPipelineBenchmark
{
    private Mat _board = null!;
    private int _cols, _rows;

    [GlobalSetup]
    public void Setup()
    {
        _board = Chessboard.GenerateForA4(10, 300);
        _cols = (int)Math.Floor(190.0 / 10);
        _rows = (int)Math.Floor(277.0 / 10);
    }

    [GlobalCleanup]
    public void Cleanup()
    {
        _board.Dispose();
    }

    [Benchmark]
    public Point2f FullPipeline()
    {
        // 1. Find corners
        var corners = Chessboard.Find(_board, new Size(_cols - 1, _rows - 1));

        // 2. Detect circles
        var circles = Chessboard.GetThreePoints(_board, patternSize: new Size(_cols - 1, _rows - 1));

        // 3. Build coordinate system
        var cs = Chessboard.GetCoordinateSystem(circles.Select(c => c.Center));

        // 4. Initialize transformer
        var ct = new CoordinateTransformer();
        ct.Initialize(cs.Origin, cs.XPoint, cs.YPoint, 20, 10);

        // 5. Transform a test point
        return ct.ImageToPhysical(new Point2f(200, 150));
    }
}
