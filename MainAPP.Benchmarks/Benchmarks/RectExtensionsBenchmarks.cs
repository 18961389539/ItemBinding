using BenchmarkDotNet.Attributes;
using Extensions;
using OpenCvSharp;

namespace MainAPP.Benchmarks.Benchmarks;

/// <summary>
/// RectExtensions 几何运算性能基准测试。
/// 这些方法在检测循环中被高频调用（每帧每个检测框都会触发），是性能热点。
/// </summary>
[MemoryDiagnoser]
[SimpleJob(warmupCount: 3, iterationCount: 5)]
public class RectExtensionsBenchmarks
{
    private Rect _rectA;
    private Rect _rectB;
    private Rect _rectOverlap;
    private Point _pointInside;
    private Point _pointOutside;
    private Size _container;
    private List<Rect> _rectList = null!;

    [Params(10, 100, 1000)]
    public int MergeCount { get; set; }

    [GlobalSetup]
    public void Setup()
    {
        _rectA = new Rect(0, 0, 100, 100);
        _rectB = new Rect(200, 200, 50, 50);
        _rectOverlap = new Rect(50, 50, 100, 100);
        _pointInside = new Point(50, 50);
        _pointOutside = new Point(500, 500);
        _container = new Size(1920, 1080);

        _rectList = new List<Rect>(MergeCount);
        var rng = new Random(42);
        for (int i = 0; i < MergeCount; i++)
        {
            _rectList.Add(new Rect(
                rng.Next(0, 1000),
                rng.Next(0, 1000),
                rng.Next(10, 200),
                rng.Next(10, 200)));
        }
    }

    [Benchmark(Description = "Intersect - 重叠矩形")]
    public Rect Intersect_Overlapping() => _rectA.Intersect(_rectOverlap);

    [Benchmark(Description = "Intersect - 不相交矩形")]
    public Rect Intersect_Disjoint() => _rectA.Intersect(_rectB);

    [Benchmark(Description = "Union - 合并矩形")]
    public Rect Union_TwoRects() => _rectA.Union(_rectB);

    [Benchmark(Description = "Contains - 点在矩形内")]
    public bool Contains_Inside() => _rectA.Contains(_pointInside);

    [Benchmark(Description = "Contains - 点在矩形外")]
    public bool Contains_Outside() => _rectA.Contains(_pointOutside);

    [Benchmark(Description = "Overlaps - 检测重叠")]
    public bool Overlaps_Check() => _rectA.Overlaps(_rectOverlap);

    [Benchmark(Description = "DistanceTo - 计算距离")]
    public double DistanceTo_Disjoint() => _rectA.DistanceTo(_rectB);

    [Benchmark(Description = "ClipTo - 裁剪到容器")]
    public Rect ClipTo_Container() => _rectA.ClipTo(_container);

    [Benchmark(Description = "Merge - 合并多个矩形")]
    public Rect Merge_Multiple() => _rectList.Merge();

    [Benchmark(Description = "Area - 计算面积")]
    public long Area_Single() => _rectA.Area();

    [Benchmark(Description = "Center - 计算中心点")]
    public Point Center_Single() => _rectA.Center();

    [Benchmark(Description = "Scale - 按比例缩放")]
    public Rect Scale_Single() => _rectA.Scale(1.5f);

    [Benchmark(Description = "InflateToAspectRatio - 扩展宽高比")]
    public Rect InflateToAspectRatio_Single() => _rectA.InflateToAspectRatio(1.7777f);
}
