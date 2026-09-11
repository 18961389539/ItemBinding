using BenchmarkDotNet.Attributes;
using Extensions;
using OpenCvSharp;

namespace MainAPP.Benchmarks.Benchmarks;

/// <summary>
/// PointExtensions 点运算性能基准测试。
/// LineAngle/Distance/Rotate 在条码定位、坐标系变换中被高频调用。
/// 同时对比 Point2f / Point2d / OpenCvSharp.Point 三种类型的性能差异。
/// </summary>
[MemoryDiagnoser]
[SimpleJob(warmupCount: 3, iterationCount: 5)]
public class PointExtensionsBenchmarks
{
    private Point2f _p2fA;
    private Point2f _p2fB;
    private Point2d _p2dA;
    private Point2d _p2dB;
    private Point _pIA;
    private Point _pIB;
    private Point2f _lineStart2f;
    private Point2f _lineEnd2f;
    private Point2f _point2f;

    [GlobalSetup]
    public void Setup()
    {
        _p2fA = new Point2f(100.5f, 200.3f);
        _p2fB = new Point2f(300.2f, 400.8f);
        _p2dA = new Point2d(100.5, 200.3);
        _p2dB = new Point2d(300.2, 400.8);
        _pIA = new Point(100, 200);
        _pIB = new Point(300, 400);
        _lineStart2f = new Point2f(0, 0);
        _lineEnd2f = new Point2f(1000, 500);
        _point2f = new Point2f(250, 300);
    }

    [Benchmark(Description = "LineAngle - Point2f")]
    public double LineAngle_Point2f() => _p2fA.LineAngle(_p2fB);

    [Benchmark(Description = "LineAngle - Point2d")]
    public double LineAngle_Point2d() => _p2dA.LineAngle(_p2dB);

    [Benchmark(Description = "LineAngle - Point(int)")]
    public double LineAngle_PointInt() => _pIA.LineAngle(_pIB);

    [Benchmark(Description = "Distance - Point2f")]
    public double Distance_Point2f() => _p2fA.Distance(_p2fB);

    [Benchmark(Description = "Distance - Point2d")]
    public double Distance_Point2d() => _p2dA.Distance(_p2dB);

    [Benchmark(Description = "Distance - Point(int)")]
    public double Distance_PointInt() => _pIA.Distance(_pIB);

    [Benchmark(Description = "Midpoint - Point2f")]
    public Point2f Midpoint_Point2f() => _p2fA.Midpoint(_p2fB);

    [Benchmark(Description = "Midpoint - Point2d")]
    public Point2d Midpoint_Point2d() => _p2dA.Midpoint(_p2dB);

    [Benchmark(Description = "Rotate - Point2f (45度)")]
    public Point2f Rotate_Point2f() => _p2fA.Rotate(_p2fB, 45.0);

    [Benchmark(Description = "Rotate - Point2d (45度)")]
    public Point2d Rotate_Point2d() => _p2dA.Rotate(_p2dB, 45.0);

    [Benchmark(Description = "Rotate - Point(int) (45度)")]
    public Point Rotate_PointInt() => _pIA.Rotate(_pIB, 45.0);

    [Benchmark(Description = "DistanceToLine - Point2f")]
    public double DistanceToLine_Point2f() => _point2f.DistanceToLine(_lineStart2f, _lineEnd2f);

    [Benchmark(Description = "ProjectToLine - Point2f")]
    public Point2f ProjectToLine_Point2f() => _point2f.ProjectToLine(_lineStart2f, _lineEnd2f);

    [Benchmark(Description = "ApproximatelyEqual - Point2f")]
    public bool ApproximatelyEqual_Point2f() => _p2fA.ApproximatelyEqual(_p2fB, 1e-6);

    [Benchmark(Description = "IsOnSegment - Point2f")]
    public bool IsOnSegment_Point2f() => _point2f.IsOnSegment(_lineStart2f, _lineEnd2f);
}
