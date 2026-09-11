using JinlongYolo.YoloSharp.Data;
using JinlongYolo.YoloSharp.Memory;
using JinlongYolo.YoloSharp.Metadata;
using MainAPP.Application;
using MainAPP.Models;
using OpenCvSharp;
using Xunit;
using Rectangle = SixLabors.ImageSharp.Rectangle;

namespace MainAPP.Tests.Application;

/// <summary>
/// 灰度判向（含 2026-09-11 新增的"掩码内对比度拉伸"）单元测试。
/// <para>覆盖 <see cref="DetectionRecordService.ApplyBrightnessHeadDirectionIfEnabled"/> 的：</para>
/// <list type="number">
/// <item>拉伸把两侧灰度差线性放大到满量程；</item>
/// <item>核心收益——原始差值落在死区内判不出来，拉伸后恢复可判并正确翻转；</item>
/// <item>拉伸窗口过窄（近单色掩码）时自动跳过，不把噪声放大成信号；</item>
/// <item>头端明暗约定控制翻转方向；</item>
/// <item>生产链路传入 3 通道 BGR 图时走 BGR2GRAY 分支且结果一致。</item>
/// </list>
/// <para>该方法读 <c>Settings.Instance.Algorithm</c>，测试内整体替换 Algorithm 实例并在 finally 还原。
/// MainAPP.Tests 已通过 AssemblyInfo 关闭测试并行（DisableTestParallelization），替换全局状态是安全的。</para>
/// <para>几何约定：掩码铺满 200×100、BOUNDS 在原点、rectAngleDeg=0 → 长轴单位向量 u=(1,0)、
/// 中心在 x=100，故 <c>x &lt; 100</c> 为 −u 半区、<c>x ≥ 100</c> 为 +u 半区。</para>
/// </summary>
public class BrightnessDirectionTests
{
    private const int Width = 200;
    private const int Height = 100;
    private const float CenterX = Width / 2f;
    private const float CenterY = Height / 2f;

    /// <summary>在指定算法参数下执行一次判向，返回 (输出角度, 统计快照)。</summary>
    private static (double Angle, DetectionRecordService.BrightnessDirectionStats? Stats) Run(
        Mat image, Segmentation seg, double fallbackAngle, AlgorithmSettings alg)
    {
        var original = Settings.Instance.Algorithm;
        Settings.Instance.Algorithm = alg;
        try
        {
            double angle = DetectionRecordService.ApplyBrightnessHeadDirectionIfEnabled(
                fallbackAngle,
                image,
                seg,
                CenterX,
                CenterY,
                rectAngleDeg: 0f,
                maskArea: Width * Height,
                brightnessEnabled: true,
                out var stats);
            return (angle, stats);
        }
        finally
        {
            Settings.Instance.Algorithm = original;
        }
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

    private static AlgorithmSettings NewSettings(bool stretch = true, double deadband = 5.0)
        => new()
        {
            BrightnessDirectionDeadband = deadband,
            BrightnessContrastStretchEnabled = stretch,
            BrightnessStretchLowPercentile = 1.0,
            BrightnessStretchHighPercentile = 99.0,
            BrightnessHeadEndIsBright = true,
        };

    /// <summary>
    /// 拉伸窗口足够宽时，两侧灰度差被线性放大：60/120 两档（窗口 60 级）→ 映射为 0/255（差值 255 级）。
    /// 同时校验不变式 Diff = MeanPlus − MeanMinus 与未拉伸时的窗口哨兵 NaN。
    /// </summary>
    [Fact]
    public void Stretch_AmplifiesHalfDifferenceToFullRange()
    {
        using var image = CreateGrayImage((x, _) => x < Width / 2 ? (byte)60 : (byte)120);
        var seg = CreateFullMask();

        var (angleRaw, raw) = Run(image, seg, fallbackAngle: 10.0, NewSettings(stretch: false));
        var (_, stretched) = Run(image, seg, fallbackAngle: 10.0, NewSettings(stretch: true));

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

        // diff > 0 且头端偏亮 → 头端在 +u 半区 → 不翻转
        Assert.Equal(10.0, angleRaw, 6);
    }

    /// <summary>
    /// 核心收益验证：原始灰度差落在死区内（判为"不可判"、不翻转），
    /// 掩码内拉伸后放大到死区之上，从而恢复判向能力并得到正确的 180° 翻转。
    /// </summary>
    [Fact]
    public void Stretch_RescuesSampleThatRawDiffCannotJudge()
    {
        // 两个半区都含少量亮像素（−u 侧 10%、+u 侧 5%），故两侧均值接近：
        //   原始：meanPlus≈63、meanMinus≈66 → diff≈−3.0（|diff| < 死区 5，不可判）
        //   拉伸：窗口 60~120（跨度 60 ≥ 最小跨度 8）→ scale=4.25 → diff≈−12.75（可判 → 翻转 180°）
        using var image = CreateGrayImage((x, y) => x < Width / 2
            ? (y % 10 == 0 ? (byte)120 : (byte)60)
            : (y % 20 == 0 ? (byte)120 : (byte)60));
        var seg = CreateFullMask();

        var (angleRaw, raw) = Run(image, seg, fallbackAngle: 0.0, NewSettings(stretch: false));
        var (angleStretched, stretched) = Run(image, seg, fallbackAngle: 0.0, NewSettings(stretch: true));

        // 关闭拉伸：不可判 → 维持原角度
        Assert.NotNull(raw);
        Assert.True(Math.Abs(raw!.Value.Diff) < 5.0,
            $"原始差值应落在死区内，实际 {raw.Value.Diff:F2}");
        Assert.Equal(0.0, angleRaw, 6);

        // 开启拉伸：可判 → diff < 0 且头端偏亮（头端在 −u 半区）→ 翻转 180°
        Assert.NotNull(stretched);
        Assert.True(Math.Abs(stretched!.Value.Diff) >= 5.0,
            $"拉伸后差值应超出死区，实际 {stretched.Value.Diff:F2}");
        Assert.True(Math.Abs(stretched.Value.Diff) > Math.Abs(raw.Value.Diff) * 2.0,
            $"拉伸后差值应显著放大：{raw.Value.Diff:F2} → {stretched.Value.Diff:F2}");
        Assert.Equal(180.0, angleStretched, 6);
    }

    /// <summary>
    /// 窗口跨度不足最小跨度（近单色掩码）时应跳过拉伸并保持原始差值，
    /// 否则会把离散噪声放大成满量程的假信号。
    /// </summary>
    [Fact]
    public void Stretch_SkippedWhenWindowTooNarrow()
    {
        using var image = CreateGrayImage((x, _) => x < Width / 2 ? (byte)100 : (byte)104);
        var (angle, stats) = Run(image, CreateFullMask(), fallbackAngle: 0.0, NewSettings());

        Assert.NotNull(stats);
        Assert.True(double.IsNaN(stats!.Value.Low), "窗口过窄应跳过拉伸，Low 为 NaN");
        Assert.Equal(104.0, stats.Value.MeanPlus, 1);
        Assert.Equal(100.0, stats.Value.MeanMinus, 1);
        Assert.Equal(4.0, stats.Value.Diff, 1);   // 原始差值，未被放大
        Assert.Equal(0.0, angle, 6);              // |4| < 死区 5 → 不可判，不翻转
    }

    /// <summary>
    /// 头端明暗约定（BrightnessHeadEndIsBright）控制翻转方向：同一张图，约定相反时结论相反。
    /// </summary>
    [Fact]
    public void HeadEndBrightnessConvention_ControlsFlipDirection()
    {
        using var image = CreateGrayImage((x, _) => x < Width / 2 ? (byte)60 : (byte)120);
        var seg = CreateFullMask();

        // 头端偏亮 = 默认：较亮的 +u 半区即头端 → 头端与角度正向一致 → 不翻转
        var (angleBrightSide, _) = Run(image, seg, fallbackAngle: 0.0, NewSettings());
        Assert.Equal(0.0, angleBrightSide, 6);

        // 头端偏暗：头端落在 −u 半区 → 翻转 180°
        var darkHead = NewSettings();
        darkHead.BrightnessHeadEndIsBright = false;
        var (angleDarkSide, _) = Run(image, seg, fallbackAngle: 0.0, darkHead);
        Assert.Equal(180.0, angleDarkSide, 6);
    }

    /// <summary>
    /// 生产链路传入的是 3 通道 BGR 图，判向应走 BGR2GRAY 分支，结果与等价灰度图一致。
    /// </summary>
    [Fact]
    public void BgrImage_GoesThroughGrayConversion()
    {
        using var gray = CreateGrayImage((x, _) => x < Width / 2 ? (byte)60 : (byte)120);
        using var bgr = gray.CvtColor(ColorConversionCodes.GRAY2BGR);

        var (_, stats) = Run(bgr, CreateFullMask(), fallbackAngle: 0.0, NewSettings(stretch: false));

        Assert.NotNull(stats);
        Assert.Equal(120.0, stats!.Value.MeanPlus, 1);
        Assert.Equal(60.0, stats.Value.MeanMinus, 1);
    }

    /// <summary>
    /// 判向开关关闭时直接返回回退角，且不产生统计快照（不落库）。
    /// </summary>
    [Fact]
    public void Disabled_ReturnsFallbackWithoutStats()
    {
        using var image = CreateGrayImage((_, _) => (byte)60);
        var original = Settings.Instance.Algorithm;
        Settings.Instance.Algorithm = NewSettings();
        try
        {
            double angle = DetectionRecordService.ApplyBrightnessHeadDirectionIfEnabled(
                7.5, image, CreateFullMask(), CenterX, CenterY, 0f, Width * Height,
                brightnessEnabled: false, out var stats);

            Assert.Equal(7.5, angle, 6);
            Assert.Null(stats);
        }
        finally
        {
            Settings.Instance.Algorithm = original;
        }
    }
}
