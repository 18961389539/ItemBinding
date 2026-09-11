using MainAPP.Application;
using Xunit;

namespace MainAPP.Tests.Application;

/// <summary>
/// 二维码头尾校正（2026-09-11）的单元测试。
/// <para>需求：头尾（180° 方向）优先用二维码位置校正，无二维码时才回退灰度亮暗判向。</para>
/// <para>约定：二维码所在的端 = 头端，头端必须与角度正向对齐；因此二维码在角度负向半区时把角度 +180°。
/// 该约定与 <c>ApplyBrightnessHeadDirectionIfEnabled</c> 的"头端与角度正向一致"完全一致，两者可互换。</para>
/// <para>只单测纯几何判据（<see cref="DetectionRecordService.TryApplyBarcodeHeadDirection"/> 与
/// <see cref="DetectionRecordService.IsBarcodeFarEnoughForHeadDecision"/>）；
/// 调用点的坐标系换算（世界 mm / 推理图坐标）无法脱离标定与分割结果单测。</para>
/// </summary>
public class BarcodeHeadDirectionTests
{
    /// <summary>长轴 1000px 的产品，二维码离质心 200px —— 足以判定头尾的常规样本。</summary>
    private const double LongAxisPx = 1000.0;
    private const double FarOffsetPx = 200.0;

    /// <summary>二维码在角度正向半区 → 头端已与角度正向对齐 → 不翻转，原角返回。</summary>
    [Theory]
    [InlineData(0.0)]
    [InlineData(45.0)]
    [InlineData(90.0)]
    [InlineData(-137.0)]
    public void BarcodeOnPositiveSide_KeepsAngle(double baseAngle)
    {
        // 头端向量严格沿角度正向，cos = 1
        double rad = baseAngle * Math.PI / 180.0;
        double dx = Math.Cos(rad) * FarOffsetPx;
        double dy = Math.Sin(rad) * FarOffsetPx;

        double? result = DetectionRecordService.TryApplyBarcodeHeadDirection(
            baseAngle, hasBarcode: true, dx, dy, FarOffsetPx, LongAxisPx);

        Assert.NotNull(result);
        Assert.Equal(baseAngle, result!.Value, 6);
    }

    /// <summary>二维码在角度负向半区 → 头端与正方向反 → 翻转 180°，使头端对齐角度正向。</summary>
    [Theory]
    [InlineData(0.0)]
    [InlineData(45.0)]
    [InlineData(90.0)]
    [InlineData(-137.0)]
    public void BarcodeOnNegativeSide_Flips180(double baseAngle)
    {
        // 头端向量严格逆着角度正向，cos = -1
        double rad = baseAngle * Math.PI / 180.0;
        double dx = -Math.Cos(rad) * FarOffsetPx;
        double dy = -Math.Sin(rad) * FarOffsetPx;

        double? result = DetectionRecordService.TryApplyBarcodeHeadDirection(
            baseAngle, hasBarcode: true, dx, dy, FarOffsetPx, LongAxisPx);

        Assert.NotNull(result);
        Assert.Equal(baseAngle + 180.0, result!.Value, 6);
    }

    /// <summary>无论翻转与否，改变量恒为 0 或 180° —— 即"只改方向，不改角度"。</summary>
    [Fact]
    public void FlipAmountIsAlwaysZeroOr180()
    {
        for (int deg = -180; deg < 180; deg += 7)
        {
            double rad = deg * Math.PI / 180.0;
            // 任意方向上的头端向量（保持足够长的距离）
            double dx = Math.Cos(rad + 0.4) * FarOffsetPx;
            double dy = Math.Sin(rad + 0.4) * FarOffsetPx;

            double? result = DetectionRecordService.TryApplyBarcodeHeadDirection(
                deg, hasBarcode: true, dx, dy, FarOffsetPx, LongAxisPx);
            if (result is null)
            {
                continue; // 几何上不可判（|cos| 太小）时返回 null，由调用方回退灰度
            }

            double delta = Math.Abs(result.Value - deg);
            Assert.True(delta < 1e-6 || Math.Abs(delta - 180.0) < 1e-6,
                $"朝向 {deg}° 时改变量 {delta:F4}° 既不是 0 也不是 180°");
        }
    }

    /// <summary>未绑定二维码（noread / 空）时返回 null，由调用方回退灰度亮暗判向。</summary>
    [Fact]
    public void NoBarcode_ReturnsNull()
    {
        Assert.Null(DetectionRecordService.TryApplyBarcodeHeadDirection(
            0.0, hasBarcode: false, FarOffsetPx, 0.0, FarOffsetPx, LongAxisPx));
    }

    /// <summary>二维码离产品质心过近时（相对长轴不足 5%）返回 null——此时"在哪一端"由噪声决定。</summary>
    [Fact]
    public void BarcodeTooCloseToCentroid_ReturnsNull()
    {
        // 长轴 1000px → 下限 50px；此处仅偏移 30px
        double? result = DetectionRecordService.TryApplyBarcodeHeadDirection(
            0.0, hasBarcode: true, dxHead: 30.0, dyHead: 0.0, headOffsetPx: 30.0, longAxisPx: LongAxisPx);

        Assert.Null(result);
    }

    /// <summary>二维码几乎垂直于长轴时投影没有区分度，返回 null 而不是硬猜一个方向。</summary>
    [Fact]
    public void BarcodeNearlyPerpendicularToAxis_ReturnsNull()
    {
        // 头端向量 (10, 200) 与长轴 (1, 0) 的 cos ≈ 0.05 < 0.30
        double offsetPx = Math.Sqrt(10.0 * 10.0 + 200.0 * 200.0);
        double? result = DetectionRecordService.TryApplyBarcodeHeadDirection(
            0.0, hasBarcode: true, dxHead: 10.0, dyHead: 200.0, headOffsetPx: offsetPx, longAxisPx: 500.0);

        Assert.Null(result);
    }

    /// <summary>距离保护的边界语义：恰好达到 5% 长轴即视为可判，低于则不可判；长轴无效时一律不可判。</summary>
    [Theory]
    [InlineData(1000.0, 49.9, false)]
    [InlineData(1000.0, 50.0, true)]
    [InlineData(1000.0, 80.0, true)]
    [InlineData(1000.0, 0.0, false)]
    [InlineData(0.0, 100.0, false)]     // 长轴无效
    [InlineData(1.0, 100.0, false)]     // 长轴无效（边界值）
    [InlineData(-10.0, 100.0, false)]   // 长轴无效
    public void IsBarcodeFarEnoughForHeadDecision_Boundary(double longAxisPx, double offsetPx, bool expected)
    {
        Assert.Equal(expected,
            DetectionRecordService.IsBarcodeFarEnoughForHeadDecision(offsetPx, longAxisPx));
    }

    /// <summary>
    /// <see cref="DetectionRecordService.IsHeadOppositeDegrees"/>：判定"实发角相对基准轴是否翻转了 180°"。
    /// <para>这是 UI 方向箭头与服务端实发角同源的唯一依据（画面箭头据此复现朝向）。</para>
    /// </summary>
    [Theory]
    [InlineData(0.0, 0.0, false)]        // 同向
    [InlineData(0.0, 180.0, true)]       // 翻转 180°
    [InlineData(-137.0, 43.0, true)]     // -137+180 = 43
    [InlineData(170.0, -10.0, true)]     // 170+180 = 350 ≡ -10（跨 ±180 边界）
    [InlineData(0.0, -180.0, true)]      // 负向等价的 180°
    [InlineData(0.0, 90.0, false)]       // 差 90° 不是头尾相反
    [InlineData(-2.0, 178.0, true)]      // 配方 offsetAngle=-2 场景：同偏移不影响翻转判定
    public void IsHeadOppositeDegrees_DetectsFlip(double baseAngle, double angle, bool expected)
    {
        Assert.Equal(expected, DetectionRecordService.IsHeadOppositeDegrees(angle, baseAngle));
    }

    /// <summary>翻转量恒为 180°，仅留 1° 容差吸收浮点误差；容差外不得误判。</summary>
    [Theory]
    [InlineData(179.5, true)]
    [InlineData(180.5, true)]
    [InlineData(179.0, true)]
    [InlineData(178.0, false)]
    [InlineData(1.0, false)]
    public void IsHeadOppositeDegrees_ToleranceIsOneDegree(double delta, bool expected)
    {
        Assert.Equal(expected, DetectionRecordService.IsHeadOppositeDegrees(delta, 0.0));
    }

    /// <summary>角度非法（NaN，算不出主轴时）一律视为"未翻转"，避免画面出现随机朝向。</summary>
    [Fact]
    public void IsHeadOppositeDegrees_NaNIsFalse()
    {
        Assert.False(DetectionRecordService.IsHeadOppositeDegrees(double.NaN, 0.0));
        Assert.False(DetectionRecordService.IsHeadOppositeDegrees(0.0, double.NaN));
        Assert.False(DetectionRecordService.IsHeadOppositeDegrees(double.NaN, double.NaN));
    }

    /// <summary>与二维码判定串起来：二维码在负向半区 → 结果角满足"头尾相反"；在正向半区 → 不相反。</summary>
    [Theory]
    [InlineData(0.0)]
    [InlineData(45.0)]
    [InlineData(-137.0)]
    public void BarcodeFlip_IsDetectedByHeadOppositeJudge(double baseAngle)
    {
        double rad = baseAngle * Math.PI / 180.0;

        double? negative = DetectionRecordService.TryApplyBarcodeHeadDirection(
            baseAngle, hasBarcode: true, -Math.Cos(rad) * FarOffsetPx, -Math.Sin(rad) * FarOffsetPx,
            FarOffsetPx, LongAxisPx);
        double? positive = DetectionRecordService.TryApplyBarcodeHeadDirection(
            baseAngle, hasBarcode: true, Math.Cos(rad) * FarOffsetPx, Math.Sin(rad) * FarOffsetPx,
            FarOffsetPx, LongAxisPx);

        Assert.NotNull(negative);
        Assert.NotNull(positive);
        Assert.True(DetectionRecordService.IsHeadOppositeDegrees(negative!.Value, baseAngle));
        Assert.False(DetectionRecordService.IsHeadOppositeDegrees(positive!.Value, baseAngle));
    }
}
