using JinlongYolo.YoloSharp.Data;
using JinlongYolo.YoloSharp.Extensions;
using JinlongYolo.YoloSharp.Memory;
using JinlongYolo.YoloSharp.Metadata;
using SixLabors.ImageSharp;
using Xunit;
using Rectangle = SixLabors.ImageSharp.Rectangle;

namespace JinlongYolo.Tests;

/// <summary>
/// SegmentationExtensions 单元测试。
/// 测试 GetMaskCentroid、GetMaskBoundingRect、GetMaskMinAreaRect 三个扩展方法。
/// 这些方法用于从 YOLO 分割结果的掩码中提取几何特征（质心、外接矩形、最小外接旋转矩形）。
/// </summary>
public class SegmentationExtensionsTests
{
    /// <summary>
    /// 创建一个均匀填充的分割结果
    /// </summary>
    private static Segmentation CreateSegmentation(int boundsX, int boundsY, int maskWidth, int maskHeight, float fillValue = 0.9f)
    {
        var mask = new BitmapBuffer(maskWidth, maskHeight);
        for (int y = 0; y < maskHeight; y++)
            for (int x = 0; x < maskWidth; x++)
                mask[y, x] = fillValue;

        return new Segmentation
        {
            Mask = mask,
            Bounds = new Rectangle(boundsX, boundsY, maskWidth, maskHeight),
            Name = new YoloName(0, "test"),
            Confidence = 0.95f
        };
    }

    /// <summary>
    /// 创建一个自定义掩码的分割结果
    /// </summary>
    private static Segmentation CreateSegmentationWithMask(int boundsX, int boundsY, int maskWidth, int maskHeight, Action<BitmapBuffer> maskSetup)
    {
        var mask = new BitmapBuffer(maskWidth, maskHeight);
        maskSetup(mask);

        return new Segmentation
        {
            Mask = mask,
            Bounds = new Rectangle(boundsX, boundsY, maskWidth, maskHeight),
            Name = new YoloName(0, "test"),
            Confidence = 0.95f
        };
    }

    // === GetMaskCentroid ===

    [Fact]
    public void GetMaskCentroid_UniformMask_ReturnsCenter()
    {
        // 均匀掩码的质心应在掩码中心
        var seg = CreateSegmentation(100, 200, 100, 100);

        var centroid = seg.GetMaskCentroid();

        // 质心 = 掩码中心 + Bounds 偏移
        // 掩码中心 = (50, 50)，Bounds 偏移 = (100, 200)
        Assert.Equal(150f, centroid.X, 1f);
        Assert.Equal(250f, centroid.Y, 1f);
    }

    [Fact]
    public void GetMaskCentroid_EmptyMask_FallsBackToBoundsCenter()
    {
        // 全部低于阈值的掩码，应回退到 Bounds 中心
        var seg = CreateSegmentationWithMask(100, 200, 50, 50, mask =>
        {
            for (int y = 0; y < 50; y++)
                for (int x = 0; x < 50; x++)
                    mask[y, x] = 0.1f; // 低于默认阈值 0.5
        });

        var centroid = seg.GetMaskCentroid();

        // 回退到 Bounds 中心：(100+25, 200+25)
        Assert.Equal(125f, centroid.X, 1f);
        Assert.Equal(225f, centroid.Y, 1f);
    }

    [Fact]
    public void GetMaskCentroid_AsymmetricMask_ReturnsWeightedCentroid()
    {
        // 只在右上角有有效像素
        var seg = CreateSegmentationWithMask(0, 0, 100, 100, mask =>
        {
            for (int y = 0; y < 100; y++)
                for (int x = 0; x < 100; x++)
                    mask[y, x] = x >= 50 && y < 50 ? 1.0f : 0f;
        });

        var centroid = seg.GetMaskCentroid();

        // 有效区域：x=[50,99], y=[0,49]，质心 ≈ (74.5, 24.5)
        Assert.Equal(74.5f, centroid.X, 1f);
        Assert.Equal(24.5f, centroid.Y, 1f);
    }

    [Fact]
    public void GetMaskCentroid_CustomThreshold_RespectsThreshold()
    {
        // 使用高阈值，使部分像素被过滤
        var seg = CreateSegmentationWithMask(0, 0, 100, 100, mask =>
        {
            for (int y = 0; y < 100; y++)
                for (int x = 0; x < 100; x++)
                    mask[y, x] = x >= 50 ? 0.7f : 0.3f;
        });

        var centroid = seg.GetMaskCentroid(threshold: 0.5f);

        // 只有 x>=50 的像素高于阈值
        Assert.True(centroid.X >= 50f);
    }

    // === GetMaskBoundingRect ===

    [Fact]
    public void GetMaskBoundingRect_UniformMask_ReturnsFullBounds()
    {
        var seg = CreateSegmentation(50, 60, 100, 80);

        var rect = seg.GetMaskBoundingRect();

        // 均匀掩码的外接矩形应覆盖整个掩码区域 + Bounds 偏移
        Assert.Equal(50f, rect.X, 1f);
        Assert.Equal(60f, rect.Y, 1f);
        Assert.Equal(100f, rect.Width, 1f);
        Assert.Equal(80f, rect.Height, 1f);
    }

    [Fact]
    public void GetMaskBoundingRect_PartialMask_ReturnsActualBounds()
    {
        // 只在掩码中心区域有有效像素
        var seg = CreateSegmentationWithMask(100, 100, 100, 100, mask =>
        {
            for (int y = 0; y < 100; y++)
                for (int x = 0; x < 100; x++)
                    mask[y, x] = (x >= 30 && x < 70 && y >= 20 && y < 80) ? 0.9f : 0f;
        });

        var rect = seg.GetMaskBoundingRect();

        // 有效区域 x=[30,69], y=[20,79] + Bounds 偏移 (100,100)
        Assert.Equal(130f, rect.X, 1f);
        Assert.Equal(120f, rect.Y, 1f);
        Assert.Equal(40f, rect.Width, 1f);
        Assert.Equal(60f, rect.Height, 1f);
    }

    [Fact]
    public void GetMaskBoundingRect_EmptyMask_ReturnsBounds()
    {
        // 全部低于阈值时回退到 Bounds
        var seg = CreateSegmentationWithMask(50, 60, 100, 80, mask =>
        {
            for (int y = 0; y < 80; y++)
                for (int x = 0; x < 100; x++)
                    mask[y, x] = 0.1f;
        });

        var rect = seg.GetMaskBoundingRect();

        // 回退到 Bounds：(50, 60, 100, 80)
        Assert.Equal(50f, rect.X, 1f);
        Assert.Equal(60f, rect.Y, 1f);
        Assert.Equal(100f, rect.Width, 1f);
        Assert.Equal(80f, rect.Height, 1f);
    }

    // === GetMaskMinAreaRect ===

    [Fact]
    public void GetMaskMinAreaRect_UniformMask_ReturnsAxisAlignedRect()
    {
        // 均匀掩码的最小外接矩形应与轴对齐 Bounds 相同
        var seg = CreateSegmentation(0, 0, 100, 100);

        var minRect = seg.GetMaskMinAreaRect();

        // 均匀掩码应为轴对齐矩形
        Assert.Equal(0f, minRect.Angle, 0.1f);
        Assert.Equal(10000f, minRect.MaskArea, 1f);
        // Area = Width * Height
        Assert.Equal(minRect.Width * minRect.Height, minRect.Area, 1f);
    }

    [Fact]
    public void GetMaskMinAreaRect_ReturnsFourCornerPoints()
    {
        var seg = CreateSegmentation(10, 20, 50, 60);

        var minRect = seg.GetMaskMinAreaRect();
        var corners = minRect.GetCorners();

        Assert.Equal(4, corners.Length);
    }

    [Fact]
    public void GetMaskMinAreaRect_EmptyMask_FallsBackToBounds()
    {
        var seg = CreateSegmentationWithMask(50, 60, 100, 80, mask =>
        {
            for (int y = 0; y < 80; y++)
                for (int x = 0; x < 100; x++)
                    mask[y, x] = 0.1f;
        });

        var minRect = seg.GetMaskMinAreaRect();

        // 空掩码回退到 Bounds，角度为 0
        Assert.Equal(0f, minRect.Angle, 0.01f);
        Assert.Equal(0f, minRect.MaskArea);
    }

    // === GetMaskStats（REVIEW 2026-08-05: 单次遍历同时返回质心与最小外接矩形） ===

    [Fact]
    public void GetMaskStats_MatchesIndividualMethods()
    {
        // GetMaskStats 的结果应与分别调用 GetMaskCentroid / GetMaskMinAreaRect 一致
        var seg = CreateSegmentation(10, 20, 50, 60);

        var (centroid, minRect) = seg.GetMaskStats();
        var expectedCentroid = seg.GetMaskCentroid();
        var expectedRect = seg.GetMaskMinAreaRect();

        Assert.Equal(expectedCentroid.X, centroid.X, 0.01f);
        Assert.Equal(expectedCentroid.Y, centroid.Y, 0.01f);
        Assert.Equal(expectedRect.Angle, minRect.Angle, 0.01f);
        Assert.Equal(expectedRect.MaskArea, minRect.MaskArea, 0.01f);
        Assert.Equal(expectedRect.Center.X, minRect.Center.X, 0.01f);
        Assert.Equal(expectedRect.Center.Y, minRect.Center.Y, 0.01f);
    }

    [Fact]
    public void GetMaskStats_EmptyMask_FallsBackToBounds()
    {
        var seg = CreateSegmentationWithMask(50, 60, 100, 80, mask =>
        {
            for (int y = 0; y < 80; y++)
                for (int x = 0; x < 100; x++)
                    mask[y, x] = 0.1f;
        });

        var (centroid, minRect) = seg.GetMaskStats();

        // 空掩码：质心回退 Bounds 中心，矩形回退 Bounds（角度 0）
        Assert.Equal(0f, minRect.Angle, 0.01f);
        Assert.Equal(0f, minRect.MaskArea);
        Assert.Equal(50f + 50f, centroid.X, 0.01f);
        Assert.Equal(60f + 40f, centroid.Y, 0.01f);
    }

    // === 轴向符号规范化（2026-09-11） ===
    // 背景：旋转卡壳对"矩形类"掩码存在对边等价二义——同一条长边的两个方向（θ 与 θ+180°）
    // 给出相同面积，选中哪个取决于凸包起点，导致同一几何可能返回相差 180° 的角度。
    // 规范化把无向轴映射到固定半平面，保证同一根轴恒返回同一代表值。

    /// <summary>
    /// 判断点是否落在以 (cx,cy) 为中心、长 w 短 h、逆时针旋转 angleDeg 的矩形内。
    /// </summary>
    private static bool InRotatedRect(int x, int y, double cx, double cy, double w, double h, double angleDeg)
    {
        double r = angleDeg * Math.PI / 180.0;
        double dx = x - cx, dy = y - cy;
        double u = dx * Math.Cos(r) + dy * Math.Sin(r);
        double v = -dx * Math.Sin(r) + dy * Math.Cos(r);
        return Math.Abs(u) <= w / 2.0 && Math.Abs(v) <= h / 2.0;
    }

    private static Segmentation CreateRotatedRectSegmentation(int size, double cx, double cy, double w, double h, double angleDeg, bool pointReflect = false)
    {
        return CreateSegmentationWithMask(0, 0, size, size, mask =>
        {
            for (int y = 0; y < size; y++)
            {
                for (int x = 0; x < size; x++)
                {
                    int sx = pointReflect ? size - 1 - x : x;
                    int sy = pointReflect ? size - 1 - y : y;
                    mask[y, x] = InRotatedRect(sx, sy, cx, cy, w, h, angleDeg) ? 0.9f : 0.0f;
                }
            }
        });
    }

    /// <summary>
    /// 长轴是无向轴：掩码做 180° 中心对称后，最小外接矩形的角度必须完全一致。
    /// 这是"旋转卡壳对边等价导致同一几何返回 θ 或 θ+180°"的回归测试。
    /// </summary>
    [Fact]
    public void GetMaskMinAreaRect_PointReflectedMask_ReturnsSameAngle()
    {
        const int size = 120;
        var a = CreateRotatedRectSegmentation(size, 30, 34, 45, 12, 30.0).GetMaskMinAreaRect();
        var b = CreateRotatedRectSegmentation(size, 30, 34, 45, 12, 30.0, pointReflect: true).GetMaskMinAreaRect();

        // 中心对称保持长轴方向不变 → 角度必须一致（尺寸也应相符）
        Assert.Equal(a.Width, b.Width, 0.5f);
        Assert.Equal(a.Height, b.Height, 0.5f);
        Assert.Equal(a.Angle, b.Angle, 0.01f);
    }

    /// <summary>
    /// 任意朝向的长轴，规范化后的角度都必须落在固定值域 (−135°, 45°] 内。
    /// </summary>
    [Fact]
    public void GetMaskMinAreaRect_Angle_AlwaysInCanonicalRange()
    {
        for (int deg = 0; deg < 180; deg += 7)
        {
            var rect = CreateRotatedRectSegmentation(120, 60, 60, 70, 18, deg).GetMaskMinAreaRect();

            Assert.True(
                rect.Angle > -135.0f - 0.01f && rect.Angle <= 45.0f + 0.01f,
                $"朝向 {deg}° 时角度 {rect.Angle:F2}° 越出规范值域 (−135°,45°]");
        }
    }

    /// <summary>
    /// 规范化必须保持"宽度轴 = 长轴"（沿用宽≥高归一化语义），且角度确为期望朝向的规范代表值。
    /// 45° 框架下 (−135°,45°] 内的朝向保持不变，故 33° 的矩形应返回 ≈33°（而非 33±180）。
    /// </summary>
    [Theory]
    [InlineData(0.0)]
    [InlineData(20.0)]
    [InlineData(33.0)]
    [InlineData(-60.0)]
    [InlineData(-120.0)]
    public void GetMaskMinAreaRect_CanonicalAngle_MatchesExpectedOrientation(double expectDeg)
    {
        // 把期望朝向折算到规范值域 (−135°, 45°]
        double expect = ((expectDeg + 135.0) % 180.0 + 180.0) % 180.0 - 135.0;
        if (expect > 45.0) expect -= 180.0;

        var rect = CreateRotatedRectSegmentation(140, 70, 70, 80, 20, expectDeg).GetMaskMinAreaRect();

        Assert.True(rect.Width >= rect.Height, $"宽度轴应为长轴，实际 W={rect.Width:F1} H={rect.Height:F1}");
        Assert.True(Math.Abs(rect.Angle - expect) < 1.5f,
            $"朝向 {expectDeg}° 应返回规范代表 {expect:F2}°，实际 {rect.Angle:F2}°");

        // 宽度轴方向（cos, sin）应与期望朝向平行（允许 180° 反向，因为轴无向）
        double a = rect.Angle * Math.PI / 180.0;
        double dot = Math.Abs(Math.Cos(a) * Math.Cos(expectDeg * Math.PI / 180.0)
                            + Math.Sin(a) * Math.Sin(expectDeg * Math.PI / 180.0));
        Assert.True(dot > 0.99, $"宽度轴与期望朝向夹角过大：Angle={rect.Angle:F2}°, dot={dot:F4}");
    }
}
