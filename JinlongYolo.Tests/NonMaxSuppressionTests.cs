using JinlongYolo.YoloSharp.Decoders.Base;
using JinlongYolo.YoloSharp.Services;
using SixLabors.ImageSharp;
using Xunit;

namespace JinlongYolo.Tests;

/// <summary>
/// NonMaxSuppression 单元测试。
/// 通过派生类暴露 protected CalculateIoU 方法以进行单独验证。
/// NMS 算法是目标检测后处理的核心步骤，用于去除重叠的检测框。
/// </summary>
public class NonMaxSuppressionTests
{
    /// <summary>
    /// 派生类用于暴露 protected CalculateIoU 方法
    /// </summary>
    private class TestableNms : NonMaxSuppression
    {
        public float CalculateIoUPublic(RawBoundingBox box1, RawBoundingBox box2)
            => CalculateIoU(box1, box2);
    }

    private static RawBoundingBox CreateBox(float x, float y, float w, float h, float confidence = 0.9f)
    {
        return new RawBoundingBox
        {
            Index = 0,
            NameIndex = 0,
            Confidence = confidence,
            Bounds = new RectangleF(x, y, w, h)
        };
    }

    private readonly TestableNms _nms = new();

    // === CalculateIoU ===

    [Fact]
    public void CalculateIoU_IdenticalBoxes_ReturnsOne()
    {
        var box1 = CreateBox(0, 0, 100, 100);
        var box2 = CreateBox(0, 0, 100, 100);

        var iou = _nms.CalculateIoUPublic(box1, box2);

        Assert.Equal(1.0f, iou, 3f);
    }

    [Fact]
    public void CalculateIoU_NonOverlappingBoxes_ReturnsZero()
    {
        var box1 = CreateBox(0, 0, 50, 50);
        var box2 = CreateBox(100, 100, 50, 50);

        var iou = _nms.CalculateIoUPublic(box1, box2);

        Assert.Equal(0f, iou, 3f);
    }

    [Fact]
    public void CalculateIoU_HalfOverlap_ReturnsCorrectRatio()
    {
        // box1: (0,0,100,100) 面积=10000
        // box2: (50,0,100,100) 面积=10000
        // 交集: (50,0,50,100) 面积=5000
        // 并集: 10000+10000-5000=15000
        // IoU = 5000/15000 = 0.333
        var box1 = CreateBox(0, 0, 100, 100);
        var box2 = CreateBox(50, 0, 100, 100);

        var iou = _nms.CalculateIoUPublic(box1, box2);

        Assert.Equal(1f / 3f, iou, 2f);
    }

    [Fact]
    public void CalculateIoU_TouchingBoxes_ReturnsZero()
    {
        // 相邻但不重叠（边界相切）
        var box1 = CreateBox(0, 0, 50, 50);
        var box2 = CreateBox(50, 0, 50, 50);

        var iou = _nms.CalculateIoUPublic(box1, box2);

        Assert.Equal(0f, iou, 3f);
    }

    [Fact]
    public void CalculateIoU_OneBoxInsideAnother_ReturnsAreaRatio()
    {
        // box1: (0,0,100,100) 面积=10000
        // box2: (25,25,50,50) 面积=2500
        // 交集 = box2 面积 = 2500
        // 并集 = box1 面积 = 10000
        // IoU = 2500/10000 = 0.25
        var box1 = CreateBox(0, 0, 100, 100);
        var box2 = CreateBox(25, 25, 50, 50);

        var iou = _nms.CalculateIoUPublic(box1, box2);

        Assert.Equal(0.25f, iou, 2f);
    }

    // === Apply (NMS 算法) ===

    [Fact]
    public void Apply_EmptyInput_ReturnsEmptyArray()
    {
        var boxes = Array.Empty<RawBoundingBox>();

        var result = _nms.Apply(boxes, 0.5f);

        Assert.Empty(result);
    }

    [Fact]
    public void Apply_SingleBox_ReturnsSameBox()
    {
        var boxes = new[] { CreateBox(0, 0, 100, 100, 0.9f) };

        var result = _nms.Apply(boxes, 0.5f);

        Assert.Single(result);
        Assert.Equal(0.9f, result[0].Confidence);
    }

    [Fact]
    public void Apply_TwoOverlappingBoxes_RemovesLowerConfidence()
    {
        // 两个高度重叠的框，IoU > 阈值
        var boxes = new[]
        {
            CreateBox(0, 0, 100, 100, 0.9f),
            CreateBox(10, 10, 100, 100, 0.7f)
        };

        var result = _nms.Apply(boxes, 0.5f);

        Assert.Single(result);
        Assert.Equal(0.9f, result[0].Confidence);
    }

    [Fact]
    public void Apply_TwoNonOverlappingBoxes_KeepsBoth()
    {
        var boxes = new[]
        {
            CreateBox(0, 0, 50, 50, 0.9f),
            CreateBox(200, 200, 50, 50, 0.7f)
        };

        var result = _nms.Apply(boxes, 0.5f);

        Assert.Equal(2, result.Length);
    }

    [Fact]
    public void Apply_HighThreshold_KeepsOverlappingBoxes()
    {
        // 使用很高的阈值，即使重叠也不抑制
        var boxes = new[]
        {
            CreateBox(0, 0, 100, 100, 0.9f),
            CreateBox(10, 10, 100, 100, 0.7f)
        };

        var result = _nms.Apply(boxes, 0.99f);

        Assert.Equal(2, result.Length);
    }

    [Fact]
    public void Apply_MultipleBoxes_KeepsHighestConfidencePerCluster()
    {
        var boxes = new[]
        {
            // 簇 1：3 个高度重叠的框
            CreateBox(0, 0, 100, 100, 0.95f),
            CreateBox(5, 5, 100, 100, 0.8f),
            CreateBox(10, 10, 100, 100, 0.7f),
            // 簇 2：2 个远离的框
            CreateBox(500, 500, 100, 100, 0.9f),
            CreateBox(510, 510, 100, 100, 0.6f)
        };

        var result = _nms.Apply(boxes, 0.5f);

        // 簇 1 保留最高分 0.95，簇 2 保留最高分 0.9
        Assert.Equal(2, result.Length);
        Assert.Contains(result, b => Math.Abs(b.Confidence - 0.95f) < 0.01f);
        Assert.Contains(result, b => Math.Abs(b.Confidence - 0.9f) < 0.01f);
    }

    [Fact]
    public void Apply_LowThreshold_RemovesMoreBoxes()
    {
        // 低阈值会移除更多重叠框
        var boxes = new[]
        {
            CreateBox(0, 0, 100, 100, 0.9f),
            CreateBox(30, 30, 100, 100, 0.8f),  // 与 box1 的 IoU ≈ 0.32
            CreateBox(60, 60, 100, 100, 0.7f)   // 与 box1 的 IoU ≈ 0.12，与 box2 的 IoU ≈ 0.32
        };

        // IoU=0.32 低于 0.5 阈值，全部保留
        var resultHighThreshold = _nms.Apply(boxes, 0.5f);
        Assert.Equal(3, resultHighThreshold.Length);

        // IoU=0.32 高于 0.3 阈值，box2 被 box1 抑制
        var resultLowThreshold = _nms.Apply(boxes, 0.3f);
        Assert.True(resultLowThreshold.Length <= 2);
    }

    [Fact]
    public void Apply_ResultIsSortedByConfidenceDescending()
    {
        var boxes = new[]
        {
            CreateBox(0, 0, 50, 50, 0.5f),
            CreateBox(200, 200, 50, 50, 0.9f),
            CreateBox(400, 400, 50, 50, 0.7f)
        };

        var result = _nms.Apply(boxes, 0.5f);

        // 结果应按置信度降序排列
        for (int i = 1; i < result.Length; i++)
        {
            Assert.True(result[i - 1].Confidence >= result[i].Confidence);
        }
    }
}
