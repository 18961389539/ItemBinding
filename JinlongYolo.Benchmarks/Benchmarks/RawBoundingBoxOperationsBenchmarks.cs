using BenchmarkDotNet.Attributes;

namespace JinlongYolo.Benchmarks.Benchmarks;

/// <summary>
/// RawBoundingBoxOperations 性能基准测试。
/// 这些工具方法在解码器中被高频调用，用于 Top-K 筛选和排序验证。
/// </summary>
[MemoryDiagnoser]
[SimpleJob(warmupCount: 3, iterationCount: 5)]
public class RawBoundingBoxOperationsBenchmarks
{
    private RawBoundingBox[] _sortedBoxes = null!;
    private RawBoundingBox[] _unsortedBoxes = null!;

    [Params(100, 1000, 10000)]
    public int Count { get; set; }

    [GlobalSetup]
    public void Setup()
    {
        _sortedBoxes = new RawBoundingBox[Count];
        _unsortedBoxes = new RawBoundingBox[Count];
        var rng = new Random(42);
        for (int i = 0; i < Count; i++)
        {
            var sorted = new RawBoundingBox
            {
                Index = i,
                NameIndex = 0,
                Confidence = 1f - i * (1f / Count),
                Bounds = new RectangleF(rng.Next(0, 1000), rng.Next(0, 1000), 50, 50)
            };
            _sortedBoxes[i] = sorted;
            _unsortedBoxes[i] = sorted with { Confidence = rng.NextSingle() };
        }
    }

    [Benchmark(Description = "IsSortedDescending - 已排序")]
    public bool IsSortedDescending_Sorted() => RawBoundingBoxOperations.IsSortedDescending(_sortedBoxes);

    [Benchmark(Description = "IsSortedDescending - 未排序")]
    public bool IsSortedDescending_Unsorted() => RawBoundingBoxOperations.IsSortedDescending(_unsortedBoxes);

    [Benchmark(Description = "Limit - Top-10")]
    public void Limit_Top10() => RawBoundingBoxOperations.Limit(_unsortedBoxes, 10);

    [Benchmark(Description = "Limit - Top-100")]
    public void Limit_Top100() => RawBoundingBoxOperations.Limit(_unsortedBoxes, 100);

    [Benchmark(Description = "EnqueueTop + DrainDescending - Top-10")]
    public void EnqueueAndDrain_Top10()
    {
        PriorityQueue<RawBoundingBox, float>? queue = null;
        for (int i = 0; i < _unsortedBoxes.Length; i++)
            RawBoundingBoxOperations.EnqueueTop(ref queue, _unsortedBoxes[i], 10);
        RawBoundingBoxOperations.DrainDescending(queue);
    }
}
