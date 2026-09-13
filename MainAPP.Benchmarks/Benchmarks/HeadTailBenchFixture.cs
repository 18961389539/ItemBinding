using JinlongYolo.YoloSharp.Data;
using JinlongYolo.YoloSharp.Memory;
using JinlongYolo.YoloSharp.Metadata;
using OpenCvSharp;
using Rectangle = SixLabors.ImageSharp.Rectangle;

namespace MainAPP.Benchmarks.Benchmarks;

/// <summary>
/// 头尾判定基准的合成输入（BDN 基准与快速探针共用，2026-09-13）。
/// <para>抽出来共用的原因：BDN 会为基准方法生成独立工程并<b>重新构建 MainAPP</b>，
/// 而本仓的 MainAPP 需要 <c>SuppressTfmSupportBuildWarnings</c> 才能构建通过——
/// 该属性传不进 BDN 的生成工程（它默认报错而非警告）。因此另提供不依赖 BDN 的
/// 快速探针模式（<c>--probe</c>），两者必须用<b>同一份</b>输入构造逻辑，
/// 否则两处数字不可比。</para>
/// </summary>
internal sealed class HeadTailBenchFixture : IDisposable
{
    private HeadTailBenchFixture(
        Mat image, Segmentation segmentation,
        float centerX, float centerY, float angleDeg, float maskArea, double longAxisPx)
    {
        Image = image;
        Segmentation = segmentation;
        CenterX = centerX;
        CenterY = centerY;
        AngleDeg = angleDeg;
        MaskArea = maskArea;
        LongAxisPx = longAxisPx;
    }

    public Mat Image { get; }
    public Segmentation Segmentation { get; }
    public float CenterX { get; }
    public float CenterY { get; }
    public float AngleDeg { get; }
    public float MaskArea { get; }
    public double LongAxisPx { get; }

    public static HeadTailBenchFixture Create(int width, int height)
        => Create(width, height, maskScale: 1.0);

    /// <summary>
    /// 构造一帧合成输入。
    /// <para><b>图像内容</b>：垂直渐变（给亮度特征真实信号）+ 每 16 px 一道条纹
    /// （给 Sobel/Canny 真实工作量）。用纯色图会测出偏乐观的数——Canny 在无边缘图上会退化成极快。</para>
    /// <para><b>掩码</b>：居中椭圆，默认占画幅宽 75% × 高 60%（约 45% 面积），
    /// 接近"产品不满幅"的实际形态。遮罩外的像素不参与 ROI 扫描，
    /// 但三张派生图仍在<b>整幅图</b>上计算——故 <paramref name="maskScale"/> 可用来
    /// 分离"全图派生图"（与掩码无关）与"ROI 扫描"（与掩码面积成正比）两类成本。</para>
    /// </summary>
    /// <param name="maskScale">掩码缩放：1.0 = 默认（75%×60%），0.5 = 一半尺寸（约 1/4 面积）。</param>
    public static HeadTailBenchFixture Create(int width, int height, double maskScale)
    {
        var image = new Mat(height, width, MatType.CV_8UC3);
        for (int y = 0; y < height; y++)
        {
            var baseVal = 40 + 120.0 * y / height;
            for (int x = 0; x < width; x++)
            {
                var v = (byte)Math.Clamp(baseVal + (((x / 16) % 2 == 0) ? 45 : 0), 0, 255);
                image.Set(y, x, new Vec3b(v, v, v));
            }
        }

        var maskW = Math.Max(8, (int)(width * 0.75 * maskScale));
        var maskH = Math.Max(8, (int)(height * 0.60 * maskScale));
        var offX = (width - maskW) / 2;
        var offY = (height - maskH) / 2;

        var mask = new BitmapBuffer(maskW, maskH);
        long area = 0;
        var rx = maskW / 2.0;
        var ry = maskH / 2.0;
        for (int my = 0; my < maskH; my++)
        {
            for (int mx = 0; mx < maskW; mx++)
            {
                var nx = (mx - rx) / rx;
                var ny = (my - ry) / ry;
                if (nx * nx + ny * ny <= 1.0)
                {
                    mask[my, mx] = 1.0f;
                    area++;
                }
            }
        }

        var segmentation = new Segmentation
        {
            Name = new YoloName(0, "product"),
            Confidence = 0.95f,
            Bounds = new Rectangle(offX, offY, maskW, maskH),
            Mask = mask,
        };

        return new HeadTailBenchFixture(
            image, segmentation,
            width / 2f, height / 2f, 30f, area, Math.Max(maskW, maskH) * 0.9);
    }

    /// <summary>调用一次特征池判定（out 参数丢弃）。</summary>
    public bool Evaluate(bool brightness)
    {
        var decision = MainAPP.Application.HeadTailFeaturePool.Evaluate(
            fallbackAngle: 30.0,
            bgrImage: Image,
            edgeResult: Segmentation,
            rectCenterX: CenterX,
            rectCenterY: CenterY,
            rectAngleDeg: AngleDeg,
            maskArea: MaskArea,
            longAxisPx: LongAxisPx,
            codePresent: false,
            codeCenterX: 0,
            codeCenterY: 0,
            deadband: 0.10,
            brightnessEnabled: brightness,
            stretchEnabled: true,
            stretchLowPercentile: 1.0,
            stretchHighPercentile: 99.0,
            out _);

        return decision?.Decisive ?? false;
    }

    public void Dispose()
    {
        Image.Dispose();
        Segmentation.Dispose();
    }
}
