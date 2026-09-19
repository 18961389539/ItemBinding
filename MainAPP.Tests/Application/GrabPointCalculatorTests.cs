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

    // ───────────────── 统一入口 ResolveOriginalImage*（生产 / 画面 / 配方页共用） ─────────────────

    /// <summary>统一入口在无偏移时必须逐位返回中心（默认行为与改造前一致）。</summary>
    [Fact]
    public void ResolveOriginalImagePoint_NoOffset_ReturnsCenterExactly()
    {
        var (x, y) = GrabPointCalculator.ResolveOriginalImagePoint(
            new CoordinateTransformer(), 88.125, -12.75, rectAngleDeg: 41.0,
            isResize: true, resizeScaleX: 4, resizeScaleY: 2, headFlipped: true,
            offsetLongMm: 0, offsetShortMm: 0);

        Assert.Equal(88.125, x);
        Assert.Equal(-12.75, y);
    }

    /// <summary>
    /// 关键回归：<b>非等比缩放会改变长轴方向</b>，偏移必须落在折算后的方向上。
    /// 推理图 45° + 缩放 (4, 2) ⇒ 原图方向 atan2(2,4) ≈ 26.57°，而不是 45°。
    /// 这条路径原先在生产、画面标记、配方页预览三处各写一遍且无测试覆盖。
    /// </summary>
    [Fact]
    public void ResolveOriginalImageOffset_NonUniformResize_RotatesDirectionAccordingly()
    {
        // 未标定 ⇒ 尺度为 1 像素/mm，便于直接断言像素量
        var (dx, dy) = GrabPointCalculator.ResolveOriginalImageOffset(
            new CoordinateTransformer(), centerOriginalX: 0, centerOriginalY: 0,
            rectAngleDeg: 45.0, isResize: true, resizeScaleX: 4, resizeScaleY: 2,
            headFlipped: false, offsetLongMm: 10, offsetShortMm: 0);

        // 方向 = normalize(4, 2) = (0.894427, 0.447214)，长度 10
        Assert.Equal(8.944272, dx, 5);
        Assert.Equal(4.472136, dy, 5);
        // 折算后的角度约 26.57°，明确不是 45°
        var angleDeg = Math.Atan2(dy, dx) * 180.0 / Math.PI;
        Assert.Equal(26.565051, angleDeg, 4);
    }

    /// <summary>等比缩放不改变方向，偏移仍沿原角度。</summary>
    [Fact]
    public void ResolveOriginalImageOffset_UniformResize_KeepsDirection()
    {
        var (dx, dy) = GrabPointCalculator.ResolveOriginalImageOffset(
            new CoordinateTransformer(), 0, 0,
            rectAngleDeg: 45.0, isResize: true, resizeScaleX: 4, resizeScaleY: 4,
            headFlipped: false, offsetLongMm: 10, offsetShortMm: 0);

        Assert.Equal(7.071068, dx, 5);
        Assert.Equal(7.071068, dy, 5);
    }

    /// <summary>不缩放时方向即矩形角度本身。</summary>
    [Fact]
    public void ResolveOriginalImageOffset_NoResize_UsesRectAngleDirectly()
    {
        var (dx, dy) = GrabPointCalculator.ResolveOriginalImageOffset(
            new CoordinateTransformer(), 0, 0,
            rectAngleDeg: 0.0, isResize: false, resizeScaleX: 1, resizeScaleY: 1,
            headFlipped: false, offsetLongMm: 7, offsetShortMm: 0);

        Assert.Equal(7.0, dx, 6);
        Assert.Equal(0.0, dy, 6);
    }

    /// <summary>已标定时统一入口应按标定尺度换算（100px = 10mm ⇒ 5mm = 50px）。</summary>
    [Fact]
    public void ResolveOriginalImagePoint_Calibrated_ConvertsMillimetersToPixels()
    {
        var (x, y) = GrabPointCalculator.ResolveOriginalImagePoint(
            CreateCalibratedTransformer(), centerOriginalX: 200, centerOriginalY: 300,
            rectAngleDeg: 0.0, isResize: false, resizeScaleX: 1, resizeScaleY: 1,
            headFlipped: false, offsetLongMm: 5, offsetShortMm: 0);

        Assert.Equal(250.0, x, 3);
        Assert.Equal(300.0, y, 3);
    }

    // ───────────────── 反算（画面示教） ─────────────────

    /// <summary>往返一致性：用 ComputeImagePoint 算出示教点，再反算回 mm，应回到原偏移。</summary>
    [Fact]
    public void ComputeOffsetsFromImagePoint_RoundTripsWithComputeImagePoint()
    {
        const double centerX = 320.5, centerY = 220.25;
        const double angleRad = 33.0 * Math.PI / 180.0;
        var dirX = Math.Cos(angleRad);
        var dirY = Math.Sin(angleRad);

        // 正向：由偏移算示教点（含头尾翻转，两轴同时反向）
        var (dx, dy) = GrabPointCalculator.ComputeImageOffset(
            dirX, dirY, headFlipped: true, offsetLongMm: 12.0, offsetShortMm: -6.0, 1.8, 2.4);
        var taughtX = centerX + dx;
        var taughtY = centerY + dy;

        // 反向：由示教点反算偏移
        var (longMm, shortMm) = GrabPointCalculator.ComputeOffsetsFromImagePoint(
            taughtX, taughtY, centerX, centerY, dirX, dirY, headFlipped: true, 1.8, 2.4);

        Assert.Equal(12.0, longMm, 6);
        Assert.Equal(-6.0, shortMm, 6);
    }

    /// <summary>轴向对齐的示教点：10px ÷ 2px/mm = 5mm。</summary>
    [Fact]
    public void ComputeOffsetsFromImagePoint_AxisAlignedPoint_Works()
    {
        var (longMm, shortMm) = GrabPointCalculator.ComputeOffsetsFromImagePoint(
            grabPointImageX: 110.0, grabPointImageY: 100.0,
            centerImageX: 100.0, centerImageY: 100.0,
            longAxisDirX: 1.0, longAxisDirY: 0.0, headFlipped: false,
            pixelsPerMmLong: 2.0, pixelsPerMmShort: 2.0);

        Assert.Equal(5.0, longMm, 6);
        Assert.Equal(0.0, shortMm, 6);
    }

    /// <summary>方向退化时反算返回 (0,0)，与正向计算的退化行为一致。</summary>
    [Fact]
    public void ComputeOffsetsFromImagePoint_DegenerateDirection_ReturnsZero()
    {
        var (longMm, shortMm) = GrabPointCalculator.ComputeOffsetsFromImagePoint(
            grabPointImageX: 110.0, grabPointImageY: 100.0,
            centerImageX: 100.0, centerImageY: 100.0,
            longAxisDirX: 0.0, longAxisDirY: 0.0, headFlipped: false,
            pixelsPerMmLong: 2.0, pixelsPerMmShort: 2.0);

        Assert.Equal(0.0, longMm, 6);
        Assert.Equal(0.0, shortMm, 6);
    }

    /// <summary>带标定的反算：50px ÷ 10px/mm = 5mm。</summary>
    [Fact]
    public void ComputeOffsetsFromImagePoint_Calibrated_ConvertsPixelsToMillimeters()
    {
        var (longMm, shortMm) = GrabPointCalculator.ComputeOffsetsFromImagePoint(
            CreateCalibratedTransformer(),
            grabPointImageX: 150.0, grabPointImageY: 100.0,
            centerImageX: 100.0, centerImageY: 100.0,
            rectAngleDeg: 0.0, isResize: false, resizeScaleX: 1, resizeScaleY: 1,
            headFlipped: false);

        // 标定变换内部用 Point2f（float），精度放到 3 位小数
        Assert.Equal(5.0, longMm, 3);
        Assert.Equal(0.0, shortMm, 3);
    }

    /// <summary>
    /// 往返一致性（带标定 + 缩放 + 翻转）：正向算出抓取点后反算，应回到原偏移。
    /// 锁定画面示教坐标系 bug：反算曾把未归一化的方向（长度 = |ResizeScale|）传给
    /// PixelsPerMmAlong，尺度缩掉 L 倍 → 偏移放大 L 倍 → 十字 ROI 落点偏离点击点。
    /// 非等比缩放（4/2）同时锁住推理图→原图的方向折算口径。
    /// </summary>
    [Fact]
    public void ComputeOffsetsFromImagePoint_CalibratedResize_RoundTripsWithResolve()
    {
        var transformer = CreateCalibratedTransformer();
        const double centerOriginalX = 200.0, centerOriginalY = 300.0;
        const double offsetLongMm = 12.0, offsetShortMm = -6.0;

        var (px, py) = GrabPointCalculator.ResolveOriginalImagePoint(
            transformer,
            centerOriginalX, centerOriginalY,
            rectAngleDeg: 41.0,
            isResize: true, resizeScaleX: 4, resizeScaleY: 2,
            headFlipped: true,
            offsetLongMm, offsetShortMm);

        var (longMm, shortMm) = GrabPointCalculator.ComputeOffsetsFromImagePoint(
            transformer,
            px, py,
            centerOriginalX, centerOriginalY,
            rectAngleDeg: 41.0,
            isResize: true, resizeScaleX: 4, resizeScaleY: 2,
            headFlipped: true);

        Assert.Equal(offsetLongMm, longMm, 3);
        Assert.Equal(offsetShortMm, shortMm, 3);
    }
}
