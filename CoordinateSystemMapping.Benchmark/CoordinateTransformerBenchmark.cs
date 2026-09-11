using BenchmarkDotNet.Attributes;
using OpenCvSharp;

namespace CoordinateSystemMapping.Benchmark;

[MemoryDiagnoser]
[SimpleJob(warmupCount: 3, iterationCount: 10)]
public class CoordinateTransformerBenchmark
{
    private CoordinateTransformer _orthogonal = null!;
    private CoordinateTransformer _skewed = null!;
    private Point2f _testPoint = new(150, 75);

    [GlobalSetup]
    public void Setup()
    {
        _orthogonal = new CoordinateTransformer();
        _orthogonal.Initialize(new Point2f(100, 100), new Point2f(200, 100), new Point2f(100, 50), 10, 10);

        _skewed = new CoordinateTransformer();
        _skewed.Initialize(new Point2f(100, 100), new Point2f(250, 80), new Point2f(80, 50), 50, 30);
    }

    [Benchmark]
    public void Initialize_Orthogonal()
    {
        var ct = new CoordinateTransformer();
        ct.Initialize(new Point2f(100, 100), new Point2f(200, 100), new Point2f(100, 50), 10, 10);
    }

    [Benchmark]
    public void Initialize_Skewed()
    {
        var ct = new CoordinateTransformer();
        ct.Initialize(new Point2f(100, 100), new Point2f(250, 80), new Point2f(80, 50), 50, 30);
    }

    [Benchmark]
    public Point2f ImageToPhysical_Point2f() => _orthogonal.ImageToPhysical(_testPoint);

    [Benchmark]
    public Point2d ImageToPhysical_Point2d() => _orthogonal.ImageToPhysical(new Point2d(_testPoint.X, _testPoint.Y));

    [Benchmark]
    public (float X, float Y) ImageToPhysical_Float_Tuple() => _orthogonal.ImageToPhysical(_testPoint.X, _testPoint.Y);

    [Benchmark]
    public (double X, double Y) ImageToPhysical_Double_Tuple() => _orthogonal.ImageToPhysical((double)_testPoint.X, _testPoint.Y);

    [Benchmark]
    public Point2f PhysicalToImage() => _orthogonal.PhysicalToImage(new Point2f(5, -5));

    [Benchmark]
    public Point2f Roundtrip_Image_Physical_Image()
    {
        var phys = _orthogonal.ImageToPhysical(_testPoint);
        return _orthogonal.PhysicalToImage(phys);
    }

    [Benchmark]
    public Point2f ImageToPhysical_Skewed() => _skewed.ImageToPhysical(_testPoint);
}
