using CoordinateSystemMapping;
using MainAPP.Application;
using OpenCvSharp;
using Xunit;

namespace MainAPP.Tests.Application;

/// <summary>
/// GrabPointCalculator 单元测试（2026-09-15 抓取点可配置）。
///
/// 回归背景：抓取点原先固定为掩码最小外接旋转矩形的中心，且"世界坐标系常量平移补偿"方向固定、
/// 只在单一产品角度下成立。现改为在<b>产品局部坐标系</b>定义偏移，随产品角度旋转。
///
/// 重点验证：
/// <list type="bullet">
///   <item>偏移全 0 时结果<b>逐位等于矩形中心</b>（保证默认行为与改造前一致，现有产线不受影响）；</item>
///   <item>头尾翻转时长轴方向取反（长轴是无向轴，不叠加符号会有 50% 概率偏到反方向）；</item>
///   <item>偏移方向随产品角度旋转（这正是旧的世界系常量做不到的）；</item>
///   <item>方向退化时安全退回中心，不抛异常。</item>
/// </list>
/// </summary>
public class GrabPointCalculatorTests
{
    /// <summary>100 像素 = 10 mm 的正交标定 → 10 像素/mm。</summary>
    private static CoordinateTransformer CreateCalibratedTransformer()
    {
        var transformer = new CoordinateTransformer();
        transformer.Initialize(new Point2f(0f, 0f), new Point2f(100f, 0f), new Point2f(0f, 100f), 10.0, 10.0);
        return transformer;
    }

    /// <summary>偏移全 0 时必须原样返回中心（默认行为与改造前一致）。</summary>
    [Fact]
    public void ComputeImagePoint_NoOffset_ReturnsCenterExactly()
    {
        var (x, y) = GrabPointCalculator.ComputeImagePoint(
            123.456, -78.9, 37.0, headFlipped: true,
            offsetLongMm: 0, offsetShortMm: 0,
            pixelsPerMmLong: 10, pixelsPerMmShort: 10);

        Assert.Equal(123.456, x);
        Assert.Equal(-78.9, y);
    }

    /// <summary>带标定的重载在无偏移时也必须短路为中心，且不依赖标定是否初始化。</summary>
    [Fact]
    public void ComputeImagePoint_WithTransformer_NoOffset_ReturnsCenter()
    {
        var (x, y) = GrabPointCalculator.ComputeImagePoint(
            CreateCalibratedTransformer(), 50.0, 60.0, 1.0, 0.0, headFlipped: false,
            offsetLongMm: 0, offsetShortMm: 0);

        Assert.Equal(50.0, x);
        Assert.Equal(60.0, y);
    }

    /// <summary>长轴偏移在未翻转时沿长轴正方向；翻转时取反。</summary>
    [Theory]
    [InlineData(false, 110.0)]
    [InlineData(true, 90.0)]
    public void ComputeImagePoint_LongOffset_FollowsHeadDirection(bool headFlipped, double expectedX)
    {
        // 长轴角 0° → 方向 (1,0)；5mm × 2px/mm = 10px
        var (x, y) = GrabPointCalculator.ComputeImagePoint(
            100.0, 100.0, longAxisAngleDeg: 0.0, headFlipped,
            offsetLongMm: 5, offsetShortMm: 0,
            pixelsPerMmLong: 2, pixelsPerMmShort: 2);

        Assert.Equal(expectedX, x, 6);
        Assert.Equal(100.0, y, 6);
    }

    /// <summary>短轴偏移：长轴角 0° 时短轴为 +Y；头尾翻转后整体反向（两轴同时反向）。</summary>
    [Theory]
    [InlineData(false, 110.0)]
    [InlineData(true, 90.0)]
    public void ComputeImagePoint_ShortOffset_FlipsTogetherWithHead(bool headFlipped, double expectedY)
    {
        var (x, y) = GrabPointCalculator.ComputeImagePoint(
            100.0, 100.0, longAxisAngleDeg: 0.0, headFlipped,
            offsetLongMm: 0, offsetShortMm: 5,
            pixelsPerMmLong: 2, pixelsPerMmShort: 2);

        Assert.Equal(100.0, x, 6);
        Assert.Equal(expectedY, y, 6);
    }

    /// <summary>偏移方向必须随产品角度旋转 —— 这正是旧的世界系常量补偿做不到的。</summary>
    [Theory]
    [InlineData(0.0, 110.0, 100.0)]     // 长轴指向 +X
    [InlineData(90.0, 100.0, 110.0)]    // 长轴指向 +Y
    [InlineData(180.0, 90.0, 100.0)]    // 长轴指向 -X
    [InlineData(270.0, 100.0, 90.0)]    // 长轴指向 -Y
    public void ComputeImagePoint_LongOffset_RotatesWithProductAngle(
        double angleDeg, double expectedX, double expectedY)
    {
        var (x, y) = GrabPointCalculator.ComputeImagePoint(
            100.0, 100.0, angleDeg, headFlipped: false,
            offsetLongMm: 5, offsetShortMm: 0,
            pixelsPerMmLong: 2, pixelsPerMmShort: 2);

        Assert.Equal(expectedX, x, 6);
        Assert.Equal(expectedY, y, 6);
    }

    /// <summary>方向退化（如单点凸包产生的退化矩形）时安全退回中心，不应抛异常。</summary>
    [Fact]
    public void ComputeImagePoint_DegenerateDirection_ReturnsCenter()
    {
        var (x, y) = GrabPointCalculator.ComputeImagePoint(
            100.0, 100.0, longAxisDirX: 0.0, longAxisDirY: 0.0, headFlipped: false,
            offsetLongMm: 5, offsetShortMm: 5,
            pixelsPerMmLong: 2, pixelsPerMmShort: 2);

        Assert.Equal(100.0, x);
        Assert.Equal(100.0, y);
    }

    /// <summary>偏移向量：长度正确，且随角度旋转。</summary>
    [Fact]
    public void ComputeImageOffset_ReturnsLengthAlongAxis()
    {
        // 5mm × 2px/mm = 10px，方向 (1,0)
        var (dx, dy) = GrabPointCalculator.ComputeImageOffset(
            longAxisDirX: 1.0, longAxisDirY: 0.0, headFlipped: false,
            offsetLongMm: 5, offsetShortMm: 0,
            pixelsPerMmLong: 2, pixelsPerMmShort: 2);

        Assert.Equal(10.0, dx, 6);
        Assert.Equal(0.0, dy, 6);
    }

    /// <summary>未标定时坐标变换是恒等映射，1 单位 = 1 像素（与未标定分支的既有语义一致）。</summary>
    [Fact]
    public void PixelsPerMmAlong_Uncalibrated_ReturnsOne()
    {
        var transformer = new CoordinateTransformer();

        var k = GrabPointCalculator.PixelsPerMmAlong(transformer, 10.0, 10.0, 1.0, 0.0);

        Assert.Equal(1.0, k);
    }

    /// <summary>已标定时按变换反推局部比例：100px = 10mm → 10 像素/mm。</summary>
    [Fact]
    public void PixelsPerMmAlong_Calibrated_ReturnsScaleFromTransform()
    {
        var transformer = CreateCalibratedTransformer();

        Assert.Equal(10.0, GrabPointCalculator.PixelsPerMmAlong(transformer, 0.0, 0.0, 1.0, 0.0), 6);
        Assert.Equal(10.0, GrabPointCalculator.PixelsPerMmAlong(transformer, 0.0, 0.0, 0.0, 1.0), 6);
    }

    /// <summary>
    /// 带标定的端到端：5mm 偏移在 10px/mm 标定下应产生 50px 位移（与方向、镜像无关，仅看长度）。
    /// </summary>
    [Fact]
    public void ComputeImagePoint_Calibrated_ConvertsMillimetersToPixels()
    {
        var transformer = CreateCalibratedTransformer();

        var (x, y) = GrabPointCalculator.ComputeImagePoint(
            transformer, 200.0, 300.0, longAxisDirX: 1.0, longAxisDirY: 0.0, headFlipped: false,
            offsetLongMm: 5, offsetShortMm: 0);

        // 标定变换内部用 Point2f（float），存在 ~1e-4 px 量级的浮点误差，故精度放到 3 位小数。
        Assert.Equal(250.0, x, 3);
        Assert.Equal(300.0, y, 3);
    }
}
