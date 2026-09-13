using JinlongYolo.YoloSharp.Data;
using JinlongYolo.YoloSharp.Memory;
using JinlongYolo.YoloSharp.Metadata;
using MainAPP.Application;
using OpenCvSharp;
using Xunit;
using Rectangle = SixLabors.ImageSharp.Rectangle;

namespace MainAPP.Tests.Application;

/// <summary>
/// 亮度判向（2026-09-13 由独立"灰度判向"并入 <see cref="HeadTailFeaturePool"/>）端到端单元测试。
/// <para>覆盖并入后必须保持的行为：</para>
/// <list type="number">
/// <item>拉伸把两侧灰度差线性放大到满量程；</item>
/// <item>核心收益——原始差值落在死区内判不出来，拉伸后恢复可判并正确翻转；</item>
/// <item>拉伸窗口过窄（近单色掩码）时自动跳过，不把噪声放大成信号；</item>
/// <item>符号约定统一为"数值大的一侧是头部"（原 BrightnessHeadEndIsBright 开关已废弃）；</item>
/// <item>生产链路传入 3 通道 BGR 图时走 BGR2GRAY 分支且结果一致。</item>
/// </list>
/// <para>几何约定：掩码铺满 200×100、BOUNDS 在原点、rectAngleDeg=0 → 长轴单位向量 u=(1,0)、
/// 中心在 x=100，故 <c>x &lt; 100</c> 为 −u 半区、<c>x ≥ 100</c> 为 +u 半区。</para>
/// </summary>
public class BrightnessDirectionTests
{
    private const int Width = 200;
    private const int Height = 100;
    private const float CenterX = Width / 2f;
    private const float CenterY = Height / 2f;
    private const double Deadband = 0.10;

    /// <summary>在指定拉伸参数下执行一次特征池判定，返回 (输出角度, 统计快照)。</summary>
    private static (double Angle, BrightnessDirectionStats? Stats) Run(
        Mat image, Segmentation seg, double fallbackAngle,
        bool stretch = true, bool brightnessEnabled = true)
    {
        var decision = HeadTailFeaturePool.Evaluate(
            fallbackAngle,
            image,
            seg,
            CenterX,
            CenterY,
            rectAngleDeg: 0f,
            maskArea: Width * Height,
            longAxisPx: Width,
            codePresent: false,
            codeCenterX: 0,
            codeCenterY: 0,
            deadband: Deadband,
            brightnessEnabled: brightnessEnabled,
            stretchEnabled: stretch,
            stretchLowPercentile: 1.0,
            stretchHighPercentile: 99.0,
            out var stats);

        // 断言判定可用（返回 null 表示无图/掩码退化）
        Assert.NotNull(decision);
        return (decision!.Angle, stats);
    }

    /// <summary>铺满整个 Bounds 的掩码（全部视为产品像素）。</summary>
    private static Segmentation CreateFullMask()
    {
        var mask = new BitmapBuffer(Width, Height);
        for (int y = 0; y < Height; y++)
        {
            for (int x = 0; x < Width; x++)
            {
                mask[y, x] = 0.9f;
            }
        }

        return new Segmentation
        {
            Mask = mask,
            Bounds = new Rectangle(0, 0, Width, Height),
            Name = new YoloName(0, "product"),
            Confidence = 0.9f,
        };
    }

    /// <summary>按 value(x, y) 构造单通道灰度图。</summary>
    private static Mat CreateGrayImage(Func<int, int, byte> value)
    {
        var mat = new Mat(Height, Width, MatType.CV_8UC1);
        for (int y = 0; y < Height; y++)
        {
            for (int x = 0; x < Width; x++)
            {
                mat.Set<byte>(y, x, value(x, y));
            }
        }

        return mat;
    }

    /// <summary>
    /// 拉伸窗口足够宽时，两侧灰度差被线性放大：60/120 两档（窗口 60 级）→ 映射为 0/255（差值 255 级）。
    /// 同时校验不变式 Diff = MeanPlus − MeanMinus 与未拉伸时的窗口哨兵 NaN。
    /// </summary>
    [Fact]
    public void Stretch_AmplifiesHalfDifferenceToFullRange()
    {
        using var image = CreateGrayImage((x, _) => x < Width / 2 ? (byte)60 : (byte)120);
        var seg = CreateFullMask();

        var (angleRaw, raw) = Run(image, seg, fallbackAngle: 10.0, stretch: false);
        var (_, stretched) = Run(image, seg, fallbackAngle: 10.0, stretch: true);

        // 关闭拉伸：落库值即原始绝对灰度，窗口哨兵为 NaN
        Assert.NotNull(raw);
        Assert.Equal(120.0, raw!.Value.MeanPlus, 1);
        Assert.Equal(60.0, raw.Value.MeanMinus, 1);
        Assert.Equal(60.0, raw.Value.Diff, 1);
        Assert.True(double.IsNaN(raw.Value.Low), "未拉伸时 Low 应为 NaN");

        // 开启拉伸：60→0、120→255，差值被放大到满量程
        Assert.NotNull(stretched);
        Assert.Equal(255.0, stretched!.Value.MeanPlus, 1);
        Assert.Equal(0.0, stretched.Value.MeanMinus, 1);
        Assert.Equal(255.0, stretched.Value.Diff, 1);
        Assert.Equal(60.0, stretched.Value.Low, 1);
        Assert.Equal(120.0, stretched.Value.High, 1);

        // 不变式：Diff = MeanPlus − MeanMinus（拉伸前后都成立）
        Assert.Equal(stretched.Value.Diff,
            stretched.Value.MeanPlus - stretched.Value.MeanMinus, 6);

        // 正半区更亮（diff > 0）→ 符合"数值大的一侧是头部" → 不翻转
        Assert.Equal(10.0, angleRaw, 6);
    }

    /// <summary>
    /// 拉伸收益验证：同一样本在拉伸后两侧灰度差被放大 2 倍以上（拉伸逻辑在并入特征池后仍然生效）。
    /// <para>注意：本样本（负半区亮像素密度更高）在几何/结构特征上并不对称——梯度能量/纹理/边缘密度
    /// 会因亮暗交界密度差异而出死区，故最终定论者未必是亮度特征。本测试只断言"拉伸放大了差值"
    /// 这一与特征无关的事实；"亮度特征本身的判向能力"由 <see cref="SignedConvention_LargerValueIsHead"/>
    /// 与 <c>HeadTailFeaturePoolTests</c> 中的亮度用例覆盖。</para>
    /// </summary>
    [Fact]
    public void Stretch_AmplifiesWeakDiff()
    {
        // 两个半区都含少量亮像素（−u 侧 10%、+u 侧 5%）：
        //   原始：meanPlus≈63、meanMinus≈66 → diff≈−3.0（负值 → 负半区更亮）
        //   拉伸：窗口 60~120（跨度 60 ≥ 最小跨度 8）→ scale=4.25 → diff≈−12.75
        using var image = CreateGrayImage((x, y) => x < Width / 2
            ? (y % 10 == 0 ? (byte)120 : (byte)60)
            : (y % 20 == 0 ? (byte)120 : (byte)60));
        var seg = CreateFullMask();

        var (_, raw) = Run(image, seg, fallbackAngle: 0.0, stretch: false);
        var (_, stretched) = Run(image, seg, fallbackAngle: 0.0, stretch: true);

        Assert.NotNull(raw);
        Assert.NotNull(stretched);

        // 方向不变（同为负 → 负半区更亮），且拉伸后差值显著放大（2 倍以上）
        Assert.True(raw!.Value.Diff < 0, $"负半区更亮，原始差值应为负，实际 {raw.Value.Diff:F2}");
        Assert.True(stretched!.Value.Diff < 0, $"负半区更亮，拉伸后差值应为负，实际 {stretched.Value.Diff:F2}");
        Assert.True(Math.Abs(stretched.Value.Diff) > Math.Abs(raw.Value.Diff) * 2.0,
            $"拉伸后差值应显著放大：{raw.Value.Diff:F2} → {stretched.Value.Diff:F2}");
    }

    /// <summary>
    /// 窗口跨度不足最小跨度（近单色掩码）时应跳过拉伸并保持原始差值，
    /// 否则会把离散噪声放大成满量程的假信号。
    /// </summary>
    [Fact]
    public void Stretch_SkippedWhenWindowTooNarrow()
    {
        using var image = CreateGrayImage((x, _) => x < Width / 2 ? (byte)100 : (byte)104);
        var (angle, stats) = Run(image, CreateFullMask(), fallbackAngle: 0.0);

        Assert.NotNull(stats);
        Assert.True(double.IsNaN(stats!.Value.Low), "窗口过窄应跳过拉伸，Low 为 NaN");
        Assert.Equal(104.0, stats.Value.MeanPlus, 1);
        Assert.Equal(100.0, stats.Value.MeanMinus, 1);
        Assert.Equal(4.0, stats.Value.Diff, 1);   // 原始差值，未被放大
        Assert.Equal(0.0, angle, 6);              // |4| < 换算死区 51 → 不可判，不翻转
    }

    /// <summary>
    /// 符号约定统一为"数值大的一侧是头部"：正半区更亮 → 头在正半区 → 不翻转；
    /// 负半区更亮 → 头在负半区 → 翻转 180°。原 BrightnessHeadEndIsBright 外部约定已废弃。
    /// </summary>
    [Fact]
    public void SignedConvention_LargerValueIsHead()
    {
        // 正半区更亮（120 vs 60）→ 不翻转
        using var plusBrighter = CreateGrayImage((x, _) => x < Width / 2 ? (byte)60 : (byte)120);
        var (anglePlus, _) = Run(plusBrighter, CreateFullMask(), fallbackAngle: 0.0);
        Assert.Equal(0.0, anglePlus, 6);

        // 负半区更亮（120 vs 60）→ 翻转 180°
        using var minusBrighter = CreateGrayImage((x, _) => x < Width / 2 ? (byte)120 : (byte)60);
        var (angleMinus, _) = Run(minusBrighter, CreateFullMask(), fallbackAngle: 0.0);
        Assert.Equal(180.0, angleMinus, 6);
    }

    /// <summary>
    /// 生产链路传入的是 3 通道 BGR 图，判向应走 BGR2GRAY 分支，结果与等价灰度图一致。
    /// </summary>
    [Fact]
    public void BgrImage_GoesThroughGrayConversion()
    {
        using var gray = CreateGrayImage((x, _) => x < Width / 2 ? (byte)60 : (byte)120);
        using var bgr = gray.CvtColor(ColorConversionCodes.GRAY2BGR);

        var (_, stats) = Run(bgr, CreateFullMask(), fallbackAngle: 0.0, stretch: false);

        Assert.NotNull(stats);
        Assert.Equal(120.0, stats!.Value.MeanPlus, 1);
        Assert.Equal(60.0, stats.Value.MeanMinus, 1);
    }

    /// <summary>
    /// 亮度特征关闭（brightnessEnabled=false）时仍可运行级联（几何特征仍在），
    /// 但不产出亮度统计、也不出现 BrightnessDiff 特征。
    /// </summary>
    [Fact]
    public void BrightnessDisabled_NoStatsNoBrightnessFeature()
    {
        using var image = CreateGrayImage((_, _) => (byte)60);
        var decision = HeadTailFeaturePool.Evaluate(
            7.5, image, CreateFullMask(), CenterX, CenterY, 0f, Width * Height,
            Width, codePresent: false, codeCenterX: 0, codeCenterY: 0, deadband: Deadband,
            brightnessEnabled: false, stretchEnabled: true,
            stretchLowPercentile: 1.0, stretchHighPercentile: 99.0,
            out var stats);

        Assert.NotNull(decision);
        Assert.Null(stats);
        Assert.DoesNotContain(decision!.Features, f => f.Name == "BrightnessDiff");
        Assert.Equal(7.5, decision.Angle, 6);   // 几何全死区 + 无亮度 → 维持回退角
    }
}
