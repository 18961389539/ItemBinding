using BenchmarkDotNet.Attributes;
using BenchmarkDotNet.Engines;

namespace JinlongYolo.Benchmarks.Benchmarks;

/// <summary>
/// 内存泄漏检测基准 — 长时间运行，测量 GC 压力和累积分配。
/// </summary>
[MemoryDiagnoser]
[SimpleJob(RunStrategy.Monitoring, warmupCount: 1, iterationCount: 10, invocationCount: 100)]
public class MemoryLeakDetectionBenchmarks
{
    // === BitmapBuffer 泄漏测试 ===
    // BitmapBuffer 未实现 IDisposable，依赖 GC 回收 new float[]

    [Benchmark(Description = "BitmapBuffer 分配/抛弃 (256x256)")]
    public BitmapBuffer BitmapBuffer_Allocate_And_Discard()
    {
        return new BitmapBuffer(256, 256);
    }

    [Benchmark(Description = "BitmapBuffer 分配/抛弃 (512x512)")]
    public BitmapBuffer BitmapBuffer_Allocate_And_Discard_Large()
    {
        return new BitmapBuffer(512, 512);
    }

    // === NMS 长期运行 ===

    private NonMaxSuppression _nms = null!;
    private RawBoundingBox[] _boxes100 = null!;
    private RawBoundingBox[] _boxes1000 = null!;

    [GlobalSetup]
    public void Setup()
    {
        _nms = new NonMaxSuppression();
        _boxes100 = GenerateRawBoxes(100);
        _boxes1000 = GenerateRawBoxes(1000);
    }

    [Benchmark(Description = "NMS 100 框×100 次")]
    public void NMS_100Boxes_100Iterations()
    {
        for (int i = 0; i < 100; i++)
        {
            var boxes = new RawBoundingBox[_boxes100.Length];
            Array.Copy(_boxes100, boxes, _boxes100.Length);
            _nms.Apply(boxes, 0.5f);
        }
    }

    [Benchmark(Description = "NMS 1000 框×100 次")]
    public void NMS_1000Boxes_100Iterations()
    {
        for (int i = 0; i < 100; i++)
        {
            var boxes = new RawBoundingBox[_boxes1000.Length];
            Array.Copy(_boxes1000, boxes, _boxes1000.Length);
            _nms.Apply(boxes, 0.5f);
        }
    }

    // === RawBoundingBox 排序/去重 ===

    private RawBoundingBox[] _rawBoxes1000 = null!;

    [GlobalSetup(Target = nameof(RawBoundingBox_EnqueueTop_1000))]
    public void SetupRawBoxes()
    {
        _rawBoxes1000 = GenerateRawBoxes(1000);
    }

    [Benchmark(Description = "EnqueueTop+DrainDescending 1000")]
    public void RawBoundingBox_EnqueueTop_1000()
    {
        PriorityQueue<RawBoundingBox, float>? queue = null;
        foreach (var box in _rawBoxes1000)
            RawBoundingBoxOperations.EnqueueTop(ref queue, box, 100);
    }

    // === Helpers ===

    private static RawBoundingBox[] GenerateRawBoxes(int count)
    {
        var rng = new Random(42);
        var boxes = new RawBoundingBox[count];
        for (int i = 0; i < count; i++)
        {
            float x = rng.NextSingle() * 600;
            float y = rng.NextSingle() * 600;
            float w = rng.NextSingle() * 100 + 10;
            float h = rng.NextSingle() * 100 + 10;
            boxes[i] = new RawBoundingBox
            {
                Index = i,
                NameIndex = 0,
                Confidence = rng.NextSingle(),
                Bounds = new RectangleF(x, y, w, h)
            };
        }
        return boxes;
    }
}
