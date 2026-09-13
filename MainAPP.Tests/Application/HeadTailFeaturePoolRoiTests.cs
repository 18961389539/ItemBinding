using JinlongYolo.YoloSharp.Data;
using JinlongYolo.YoloSharp.Memory;
using JinlongYolo.YoloSharp.Metadata;
using MainAPP.Application;
using OpenCvSharp;
using Xunit;
using Rectangle = SixLabors.ImageSharp.Rectangle;

namespace MainAPP.Tests.Application;

/// <summary>
/// 派生图 ROI 裁剪（2026-09-13）的端到端测试。
/// <para><b>被测行为</b>：三张派生图（Sobel/局部标准差/Canny）裁到「掩码外接框 + 2px 核半径余量」
/// 并与图像求交，扫描时把图像坐标映射到 ROI 局部坐标。须保证：</para>
/// <list type="number">
/// <item>掩码为图像子区域（ROI 严格小于图像）时，判定结果与预期一致（索引映射正确）；</item>
/// <item><b>平移不变性</b>——同一相对内容放在不同画幅/不同偏移下，特征值逐位一致
/// （这是 ROI 局部索引正确性的最强检验：若 lx/ly 算错，平移后必然不一致）；</item>
/// <item>外接框触及图像边界、部分越界（需裁剪丢弃越界像素）时正常工作；</item>
/// <item>外接框与图像完全无交集时返回 null（退化防御）。</item>
/// </list>
/// </summary>
public class HeadTailFeaturePoolRoiTests
{
    private const double Deadband = 0.10;
    private const byte DarkGray = 50;
    private const byte BrightGray = 200;

    /// <summary>
    /// 构造「左暗右亮」两档灰度图 + 矩形掩码。
    /// splitX 为明暗分界：x &lt; splitX 取 leftGray，否则 rightGray（三通道同值，走 BGR2GRAY 分支）。
    /// </summary>
    private static (Mat Image, Segmentation Seg, float CenterX, float CenterY) Build(
        int width, int height, int maskX, int maskY, int maskW, int maskH,
        int splitX, bool threeChannel = true)
    {
        var mat = new Mat(height, width, threeChannel ? MatType.CV_8UC3 : MatType.CV_8UC1);
        for (int y = 0; y < height; y++)
        {
            for (int x = 0; x < width; x++)
            {
                var v = x < splitX ? DarkGray : BrightGray;
                if (threeChannel)
                {
                    mat.Set(y, x, new Vec3b(v, v, v));
                }
                else
                {
                    mat.Set<byte>(y, x, v);
                }
            }
        }

        var mask = new BitmapBuffer(maskW, maskH);
        for (int y = 0; y < maskH; y++)
        {
            for (int x = 0; x < maskW; x++)
            {
                mask[y, x] = 0.9f;
            }
        }

        var seg = new Segmentation
        {
            Mask = mask,
            Bounds = new Rectangle(maskX, maskY, maskW, maskH),
            Name = new YoloName(0, "product"),
            Confidence = 0.9f,
        };

        // 生产口径：中心/长轴来自掩码最小外接矩形，即掩码自身的中心与宽度
        return (mat, seg, maskX + maskW / 2f, maskY + maskH / 2f);
    }

    private static (HeadTailPoolDecision Decision, BrightnessDirectionStats? Stats) Evaluate(
        Mat image, Segmentation seg, float centerX, float centerY, double fallbackAngle = 10.0)
    {
        var decision = HeadTailFeaturePool.Evaluate(
            fallbackAngle,
            image,
            seg,
            centerX,
            centerY,
            rectAngleDeg: 0f,
            maskArea: seg.Bounds.Width * (long)seg.Bounds.Height,
            longAxisPx: seg.Bounds.Width,
            codePresent: false,
            codeCenterX: 0,
            codeCenterY: 0,
            deadband: Deadband,
            brightnessEnabled: true,
            stretchEnabled: true,
            stretchLowPercentile: 1.0,
            stretchHighPercentile: 99.0,
            out var stats);

        Assert.NotNull(decision);
        return (decision!, stats);
    }

    /// <summary>
    /// 掩码为图像子区域（ROI 严格小于图像、四周有富余）→ 亮度特征按预期定头尾。
    /// ROI 局部索引若有偏差，此用例的统计值会立刻失真。
    /// </summary>
    [Fact]
    public void SubRegionMask_BrightnessDecides_NoFlip()
    {
        // 200x100，明暗分界 x=100；掩码 x∈[40,160) → 掩码中心恰在分界上
        using var image = Build(200, 100, maskX: 40, maskY: 0, maskW: 120, maskH: 100,
            splitX: 100).Image;
        var (_, seg, cx, cy) = Build(200, 100, 40, 0, 120, 100, 100);

        var (decision, stats) = Evaluate(image, seg, cx, cy);

        Assert.True(decision.Decisive);
        Assert.Equal("BrightnessDiff", decision.SourceFeature);
        // +u 半区（x≥100）更亮 → 头在 +u → 不翻转
        Assert.False(decision.Flipped);
        Assert.Equal(10.0, decision.Angle, 6);

        // 拉伸后：暗侧 0、亮侧 255
        Assert.NotNull(stats);
        Assert.Equal(255.0, stats!.Value.MeanPlus, 3);
        Assert.Equal(0.0, stats.Value.MeanMinus, 3);
    }

    /// <summary>
    /// ★ 平移不变性：同一相对内容放到不同画幅/不同偏移下，特征值必须逐位一致。
    /// A：200 宽、分界 100、掩码 x∈[40,160)；B：240 宽、分界 120、掩码 x∈[60,180)。
    /// 两者掩码内都是「60 列暗 + 60 列亮」，但 ROI 原点不同——若 lx/ly 映射有误必不一致。
    /// </summary>
    [Fact]
    public void Roi_IsTranslationInvariant()
    {
        using var imageA = Build(200, 100, 40, 0, 120, 100, splitX: 100).Image;
        var (_, segA, cxA, cyA) = Build(200, 100, 40, 0, 120, 100, 100);

        using var imageB = Build(240, 100, 60, 0, 120, 100, splitX: 120).Image;
        var (_, segB, cxB, cyB) = Build(240, 100, 60, 0, 120, 100, 120);

        var (decA, statsA) = Evaluate(imageA, segA, cxA, cyA);
        var (decB, statsB) = Evaluate(imageB, segB, cxB, cyB);

        Assert.Equal(decA.SourceFeature, decB.SourceFeature);
        Assert.Equal(decA.Flipped, decB.Flipped);
        Assert.Equal(decA.Angle, decB.Angle, 6);

        Assert.NotNull(statsA);
        Assert.NotNull(statsB);
        Assert.Equal(statsA!.Value.MeanPlus, statsB.Value.MeanPlus, 6);
        Assert.Equal(statsA.Value.MeanMinus, statsB.Value.MeanMinus, 6);
    }

    /// <summary>
    /// 掩码外接框部分越出图像左缘（bounds.X = −30）→ 越界像素被丢弃、框内像素正常统计。
    /// 可见部分 x∈[0,99]（100 列），掩码中心 x=35 → 质心偏移 v = 14.5/65 ≈ 0.223 →
    /// 质心特征裁决、头在 +u、不翻转。若 ROI 裁剪把可见像素也丢了，此用例会失真。
    /// </summary>
    [Fact]
    public void MaskPartiallyOutsideImage_ClipsAndStillDecides()
    {
        // 整幅可见区域都是暗色（splitX=200 → 无亮像素），使亮度特征沉默、由质心特征裁决
        using var image = Build(200, 100, maskX: -30, maskY: 0, maskW: 130, maskH: 100,
            splitX: 200).Image;
        var (_, seg, cx, cy) = Build(200, 100, -30, 0, 130, 100, 200);

        var (decision, _) = Evaluate(image, seg, cx, cy);

        Assert.True(decision.Decisive);
        Assert.Equal("CentroidOffset", decision.SourceFeature);
        Assert.False(decision.Flipped);
        Assert.Equal(10.0, decision.Angle, 6);
    }

    /// <summary>掩码外接框触及图像左/上边界（ROI 被图像边界截断）→ 正常工作。</summary>
    [Fact]
    public void MaskTouchingImageBorder_Works()
    {
        // 200x100，明暗分界 x=50；掩码 x∈[0,100)（贴左缘）→ +u 半区（x≥50）亮
        using var image = Build(200, 100, maskX: 0, maskY: 0, maskW: 100, maskH: 100,
            splitX: 50).Image;
        var (_, seg, cx, cy) = Build(200, 100, 0, 0, 100, 100, 50);

        var (decision, _) = Evaluate(image, seg, cx, cy);

        Assert.True(decision.Decisive);
        Assert.Equal("BrightnessDiff", decision.SourceFeature);
        Assert.False(decision.Flipped);
    }

    /// <summary>掩码外接框与图像完全无交集 → 返回 null（退化防御，不得抛异常）。</summary>
    [Fact]
    public void MaskEntirelyOutsideImage_ReturnsNull()
    {
        using var image = Build(200, 100, maskX: 500, maskY: 0, maskW: 100, maskH: 100,
            splitX: 100).Image;
        var (_, seg, cx, cy) = Build(200, 100, 500, 0, 100, 100, 100);

        var decision = HeadTailFeaturePool.Evaluate(
            10.0, image, seg, cx, cy, rectAngleDeg: 0f,
            maskArea: 100 * 100, longAxisPx: 100,
            codePresent: false, codeCenterX: 0, codeCenterY: 0,
            deadband: Deadband, brightnessEnabled: true,
            stretchEnabled: true, stretchLowPercentile: 1.0, stretchHighPercentile: 99.0,
            out _);

        Assert.Null(decision);
    }

    /// <summary>单通道图（走 Clone 分支）在 ROI 裁剪下同样正确——覆盖非 BGR 输入路径。</summary>
    [Fact]
    public void SingleChannelImage_RoiPath_Works()
    {
        using var image = Build(200, 100, 40, 0, 120, 100, splitX: 100, threeChannel: false).Image;
        var (_, seg, cx, cy) = Build(200, 100, 40, 0, 120, 100, 100);

        var (decision, stats) = Evaluate(image, seg, cx, cy);

        Assert.True(decision.Decisive);
        Assert.Equal("BrightnessDiff", decision.SourceFeature);
        Assert.False(decision.Flipped);
        Assert.NotNull(stats);
        Assert.Equal(255.0, stats!.Value.MeanPlus, 3);
        Assert.Equal(0.0, stats.Value.MeanMinus, 3);
    }
}
