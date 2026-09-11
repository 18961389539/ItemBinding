using BenchmarkDotNet.Attributes;

namespace JinlongYolo.Benchmarks.Benchmarks;

/// <summary>
/// NonMaxSuppression 性能基准测试。
/// NMS 是目标检测后处理的核心算法，每帧都需要对大量检测框执行。
/// </summary>
[MemoryDiagnoser]
[SimpleJob(warmupCount: 3, iterationCount: 5)]
public class NonMaxSuppressionBenchmarks
{
    private NonMaxSuppression _nms = null!;
    private RawBoundingBox[] _boxes = null!;
    private RawBoundingBox[] _denseBoxes = null!;
    private RawBoundingBox[] _sparseBoxes = null!;

    [Params(50, 200, 1000)]
    public int BoxCount { get; set; }

    [GlobalSetup]
    public void Setup()
    {
        _nms = new NonMaxSuppression();
        _boxes = GenerateBoxes(BoxCount, overlapRatio: 0.3f);
        _denseBoxes = GenerateBoxes(BoxCount, overlapRatio: 0.7f);
        _sparseBoxes = GenerateBoxes(BoxCount, overlapRatio: 0.05f);
    }

    private static RawBoundingBox[] GenerateBoxes(int count, float overlapRatio)
    {
        var rng = new Random(42);
        var boxes = new RawBoundingBox[count];
        for (int i = 0; i < count; i++)
        {
            // 按重叠比例聚集框位置
            int clusterId = (int)(i * overlapRatio);
            float baseX = clusterId * 200 % 2000;
            float baseY = clusterId * 150 % 2000;
            float x = baseX + rng.Next(0, 50);
            float y = baseY + rng.Next(0, 50);
            float w = 50 + rng.Next(0, 100);
            float h = 50 + rng.Next(0, 100);

            boxes[i] = new RawBoundingBox
            {
                Index = i,
                NameIndex = 0,
                Confidence = (float)(1.0 - i * 0.0005),
                Bounds = new RectangleF(x, y, w, h)
            };
        }
        return boxes;
    }

    [Benchmark(Description = "NMS - 默认阈值 0.5")]
    public void Apply_DefaultThreshold() => _nms.Apply(_boxes, 0.5f);

    [Benchmark(Description = "NMS - 高重叠（密集框）")]
    public void Apply_DenseBoxes() => _nms.Apply(_denseBoxes, 0.5f);

    [Benchmark(Description = "NMS - 低重叠（稀疏框）")]
    public void Apply_SparseBoxes() => _nms.Apply(_sparseBoxes, 0.5f);

    [Benchmark(Description = "NMS - 低阈值 0.3")]
    public void Apply_LowThreshold() => _nms.Apply(_boxes, 0.3f);

    [Benchmark(Description = "NMS - 高阈值 0.7")]
    public void Apply_HighThreshold() => _nms.Apply(_boxes, 0.7f);
}
