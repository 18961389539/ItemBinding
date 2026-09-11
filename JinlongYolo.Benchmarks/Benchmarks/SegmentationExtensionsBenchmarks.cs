using BenchmarkDotNet.Attributes;
using SixLabors.ImageSharp;
using Rectangle = SixLabors.ImageSharp.Rectangle;

namespace JinlongYolo.Benchmarks.Benchmarks;

/// <summary>
/// SegmentationExtensions 性能基准测试。
/// 这些方法在每帧每个分割结果上调用，是后处理的热点。
/// </summary>
[MemoryDiagnoser]
[SimpleJob(warmupCount: 3, iterationCount: 5)]
public class SegmentationExtensionsBenchmarks
{
    private Segmentation _uniformSeg = null!;
    private Segmentation _partialSeg = null!;
    private Segmentation _diagonalSeg = null!;

    [Params(64, 128, 256)]
    public int MaskSize { get; set; }

    [GlobalSetup]
    public void Setup()
    {
        _uniformSeg = CreateUniformSegmentation(0, 0, MaskSize, MaskSize);
        _partialSeg = CreatePartialSegmentation(0, 0, MaskSize, MaskSize);
        _diagonalSeg = CreateDiagonalSegmentation(0, 0, MaskSize, MaskSize);
    }

    private static Segmentation CreateUniformSegmentation(int bx, int by, int w, int h)
    {
        var mask = new BitmapBuffer(w, h);
        for (int y = 0; y < h; y++)
            for (int x = 0; x < w; x++)
                mask[y, x] = 0.9f;

        return new Segmentation
        {
            Mask = mask,
            Bounds = new Rectangle(bx, by, w, h),
            Name = new YoloName(0, "test"),
            Confidence = 0.95f
        };
    }

    private static Segmentation CreatePartialSegmentation(int bx, int by, int w, int h)
    {
        var mask = new BitmapBuffer(w, h);
        for (int y = 0; y < h; y++)
            for (int x = 0; x < w; x++)
                mask[y, x] = (x >= w / 4 && x < 3 * w / 4 && y >= h / 4 && y < 3 * h / 4) ? 0.9f : 0f;

        return new Segmentation
        {
            Mask = mask,
            Bounds = new Rectangle(bx, by, w, h),
            Name = new YoloName(0, "test"),
            Confidence = 0.95f
        };
    }

    private static Segmentation CreateDiagonalSegmentation(int bx, int by, int w, int h)
    {
        var mask = new BitmapBuffer(w, h);
        for (int y = 0; y < h; y++)
            for (int x = 0; x < w; x++)
                mask[y, x] = (Math.Abs(x - y) < 5) ? 0.9f : 0f;

        return new Segmentation
        {
            Mask = mask,
            Bounds = new Rectangle(bx, by, w, h),
            Name = new YoloName(0, "test"),
            Confidence = 0.95f
        };
    }

    [Benchmark(Description = "GetMaskCentroid - 均匀掩码")]
    public PointF GetMaskCentroid_Uniform() => _uniformSeg.GetMaskCentroid();

    [Benchmark(Description = "GetMaskCentroid - 部分掩码")]
    public PointF GetMaskCentroid_Partial() => _partialSeg.GetMaskCentroid();

    [Benchmark(Description = "GetMaskCentroid - 对角线掩码")]
    public PointF GetMaskCentroid_Diagonal() => _diagonalSeg.GetMaskCentroid();

    [Benchmark(Description = "GetMaskBoundingRect - 均匀掩码")]
    public RectangleF GetMaskBoundingRect_Uniform() => _uniformSeg.GetMaskBoundingRect();

    [Benchmark(Description = "GetMaskBoundingRect - 部分掩码")]
    public RectangleF GetMaskBoundingRect_Partial() => _partialSeg.GetMaskBoundingRect();

    [Benchmark(Description = "GetMaskMinAreaRect - 均匀掩码")]
    public SegmentationExtensions.MinAreaRect GetMaskMinAreaRect_Uniform() => _uniformSeg.GetMaskMinAreaRect();

    [Benchmark(Description = "GetMaskMinAreaRect - 部分掩码")]
    public SegmentationExtensions.MinAreaRect GetMaskMinAreaRect_Partial() => _partialSeg.GetMaskMinAreaRect();

    [Benchmark(Description = "GetMaskMinAreaRect - 对角线掩码")]
    public SegmentationExtensions.MinAreaRect GetMaskMinAreaRect_Diagonal() => _diagonalSeg.GetMaskMinAreaRect();
}
