using JinlongYolo.YoloSharp.Decoders.Base;
using JinlongYolo.YoloSharp.Utilities;
using SixLabors.ImageSharp;
using Xunit;

namespace JinlongYolo.Tests;

/// <summary>
/// RawBoundingBoxOperations 静态工具类单元测试。
/// 提供 RawBoundingBox 数组的排序检查、Top-K 限制和优先队列操作。
/// </summary>
public class RawBoundingBoxOperationsTests
{
    private static RawBoundingBox CreateBox(float confidence, float x = 0, float y = 0, float w = 10, float h = 10)
    {
        return new RawBoundingBox
        {
            Index = 0,
            NameIndex = 0,
            Confidence = confidence,
            Bounds = new RectangleF(x, y, w, h)
        };
    }

    private static RawBoundingBox[] CreateSortedDescending(int count)
    {
        var boxes = new RawBoundingBox[count];
        for (int i = 0; i < count; i++)
            boxes[i] = CreateBox(confidence: 1f - i * 0.01f);
        return boxes;
    }

    // === IsSortedDescending ===

    [Fact]
    public void IsSortedDescending_EmptyArray_ReturnsTrue()
    {
        var boxes = Array.Empty<RawBoundingBox>();

        Assert.True(RawBoundingBoxOperations.IsSortedDescending(boxes));
    }

    [Fact]
    public void IsSortedDescending_SingleElement_ReturnsTrue()
    {
        var boxes = new[] { CreateBox(0.9f) };

        Assert.True(RawBoundingBoxOperations.IsSortedDescending(boxes));
    }

    [Fact]
    public void IsSortedDescending_SortedDesc_ReturnsTrue()
    {
        var boxes = new[]
        {
            CreateBox(0.9f),
            CreateBox(0.7f),
            CreateBox(0.5f)
        };

        Assert.True(RawBoundingBoxOperations.IsSortedDescending(boxes));
    }

    [Fact]
    public void IsSortedDescending_Unsorted_ReturnsFalse()
    {
        var boxes = new[]
        {
            CreateBox(0.5f),
            CreateBox(0.9f),
            CreateBox(0.7f)
        };

        Assert.False(RawBoundingBoxOperations.IsSortedDescending(boxes));
    }

    [Fact]
    public void IsSortedDescending_EqualConfidences_ReturnsTrue()
    {
        var boxes = new[]
        {
            CreateBox(0.5f),
            CreateBox(0.5f)
        };

        Assert.True(RawBoundingBoxOperations.IsSortedDescending(boxes));
    }

    // === Limit ===

    [Fact]
    public void Limit_LimitZeroOrNegative_ReturnsOriginalArray()
    {
        var boxes = new[] { CreateBox(0.9f), CreateBox(0.8f) };

        var result = RawBoundingBoxOperations.Limit(boxes, 0);

        Assert.Same(boxes, result);
    }

    [Fact]
    public void Limit_LimitExceedsLength_ReturnsOriginalArray()
    {
        var boxes = new[] { CreateBox(0.9f), CreateBox(0.8f) };

        var result = RawBoundingBoxOperations.Limit(boxes, 10);

        Assert.Same(boxes, result);
    }

    [Fact]
    public void Limit_LimitEqualsLength_ReturnsOriginalArray()
    {
        var boxes = new[] { CreateBox(0.9f), CreateBox(0.8f) };

        var result = RawBoundingBoxOperations.Limit(boxes, 2);

        Assert.Same(boxes, result);
    }

    [Fact]
    public void Limit_LimitLessThanLength_SortsAndTruncates()
    {
        var boxes = new[]
        {
            CreateBox(0.5f),
            CreateBox(0.9f),
            CreateBox(0.7f),
            CreateBox(0.95f)
        };

        var result = RawBoundingBoxOperations.Limit(boxes, 2);

        Assert.Equal(2, result.Length);
        // 应保留置信度最高的两个
        Assert.Equal(0.95f, result[0].Confidence);
        Assert.Equal(0.9f, result[1].Confidence);
    }

    [Fact]
    public void Limit_EmptyArray_ReturnsEmptyArray()
    {
        var boxes = Array.Empty<RawBoundingBox>();

        var result = RawBoundingBoxOperations.Limit(boxes, 5);

        Assert.Empty(result);
    }

    // === EnqueueTop + DrainDescending ===

    [Fact]
    public void EnqueueTop_NullQueue_CreatesQueue()
    {
        PriorityQueue<RawBoundingBox, float>? queue = null;
        var box = CreateBox(0.9f);

        RawBoundingBoxOperations.EnqueueTop(ref queue, box, limit: 10);

        Assert.NotNull(queue);
        var drained = RawBoundingBoxOperations.DrainDescending(queue);
        Assert.Single(drained);
        Assert.Equal(0.9f, drained[0].Confidence);
    }

    [Fact]
    public void EnqueueTop_LimitZero_DoesNotCreateQueue()
    {
        PriorityQueue<RawBoundingBox, float>? queue = null;
        var box = CreateBox(0.9f);

        RawBoundingBoxOperations.EnqueueTop(ref queue, box, limit: 0);

        Assert.Null(queue);
    }

    [Fact]
    public void EnqueueTop_BelowLimit_EnqueuesAll()
    {
        PriorityQueue<RawBoundingBox, float>? queue = null;

        for (int i = 0; i < 5; i++)
            RawBoundingBoxOperations.EnqueueTop(ref queue, CreateBox(i * 0.1f), limit: 10);

        var drained = RawBoundingBoxOperations.DrainDescending(queue);
        Assert.Equal(5, drained.Length);
        // 应按降序排列
        Assert.Equal(0.4f, drained[0].Confidence);
        Assert.Equal(0.0f, drained[4].Confidence);
    }

    [Fact]
    public void EnqueueTop_ExceedsLimit_KeepsTopK()
    {
        PriorityQueue<RawBoundingBox, float>? queue = null;

        for (int i = 0; i < 20; i++)
            RawBoundingBoxOperations.EnqueueTop(ref queue, CreateBox(i * 0.05f), limit: 5);

        var drained = RawBoundingBoxOperations.DrainDescending(queue);
        Assert.Equal(5, drained.Length);
        // 应保留置信度最高的 5 个
        Assert.Equal(0.95f, drained[0].Confidence);
        Assert.Equal(0.75f, drained[4].Confidence);
    }

    [Fact]
    public void DrainDescending_NullQueue_ReturnsEmptyArray()
    {
        PriorityQueue<RawBoundingBox, float>? queue = null;

        var result = RawBoundingBoxOperations.DrainDescending(queue);

        Assert.Empty(result);
    }

    [Fact]
    public void DrainDescending_EmptyQueue_ReturnsEmptyArray()
    {
        var queue = new PriorityQueue<RawBoundingBox, float>();

        var result = RawBoundingBoxOperations.DrainDescending(queue);

        Assert.Empty(result);
    }
}
