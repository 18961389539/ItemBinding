using JinlongYolo.YoloSharp.Data;
using OpenCvSharp;
using System;
using System.Collections.Generic;
using System.Linq;

namespace MainAPP.Application
{
    /// <summary>单个候选特征的判定样本：有符号值 + 死区 + 是否 decisive（级联在此特征定头尾）。</summary>
    public sealed record HeadTailFeatureSample(string Name, double SignedValue, double Deadband, bool Decisive);

    /// <summary>
    /// 特征池头尾判定结果。Decisive=false 时 Angle=fallbackAngle（级联全部死区，调用方
    /// 应回退灰度兜底）；Decisive=true 时 Angle 已按"数值大的一侧是头部"完成 +180° 翻转修正。
    /// </summary>
    public sealed record HeadTailPoolDecision(
        double Angle,
        bool Flipped,
        string SourceFeature,
        bool Decisive,
        IReadOnlyList<HeadTailFeatureSample> Features);

    /// <summary>掩码单遍扫描的半区聚合（BuildFeatures 的输入；internal 供单测构造）。</summary>
    internal sealed record HeadTailAggregates(
        long SampleCount,
        long CountPlus,
        long CountMinus,
        double SumT,
        double SumT2,
        double SumT3,
        double SumAbsSPlus,
        double SumAbsSMinus,
        long PhotoCountPlus,
        long PhotoCountMinus,
        double SumGradPlus,
        double SumGradMinus,
        double SumStdPlus,
        double SumStdMinus,
        long EdgePlus,
        long EdgeMinus);

    /// <summary>
    /// 头尾特征池（2026-09-13）：掩码回退路径的"零定标"头尾判定级联。
    /// <para><b>核心约定："数值大的一侧是头部"</b>——每个候选特征按主轴方向 u 把掩码分成
    /// 正/负两个半区，计算有符号相对值；数值大（正号）的半区即头部。该规则利用"产品的
    /// 物理不对称性随产品一起旋转"这一事实自定向，无需任何符号定标/约定冻结。</para>
    /// <para>级联按固定优先级排序（几何 → 结构 → 光度）：几何特征对光照完全免疫故最优先；
    /// 第一个 |相对差| ≥ 死区的特征即定头尾，全部死区则返回非 decisive 结果（调用方回退灰度兜底）。</para>
    /// <para>配套自检（在线统计，见 FeatureSamples 设计）：① 出死区率（特征对当前产品有无区分力）；
    /// ② QR 一致率（有码帧上与二维码真值的一致性，验证"大=头"约定）；③ 头向-位置相关性
    /// （非零 = 被场景光照梯度污染，该特征应弃用）。</para>
    /// </summary>
    public static class HeadTailFeaturePool
    {
        /// <summary>偏度特征量纲与相对差不同，死区按倍数放大。</summary>
        private const double SkewDeadbandMultiplier = 2.0;

        /// <summary>二维码邻域剔除半径（相对产品长轴）——保证有码/无码变体特征同口径。</summary>
        private const double CodeExclusionRadiusRatio = 0.15;

        /// <summary>
        /// 特征池级联判定。返回 null = 无法运行（无图像/掩码退化）；返回非 null 时
        /// Decisive 标识级联是否给出了头尾结论（未 decisive 时 Angle = fallbackAngle 原样返回）。
        /// </summary>
        /// <param name="fallbackAngle">掩码主轴回退角（未消歧，含配方 OffsetAngle 补偿）。</param>
        /// <param name="bgrImage">角度源图（推理图坐标系；3ch BGR 或 1ch 灰度）。</param>
        /// <param name="edgeResult">本产品的分割结果（掩码 + Bounds）。</param>
        /// <param name="rectCenterX/Y">掩码最小外接旋转矩形中心（推理图坐标）。</param>
        /// <param name="rectAngleDeg">掩码宽度轴方向（推理图系；宽≥高归一化，宽度轴=长轴）。</param>
        /// <param name="maskArea">掩码面积。</param>
        /// <param name="longAxisPx">产品长轴长度（原图像素，几何特征归一化用）。</param>
        /// <param name="codePresent">OBB 内是否命中候选条码（含 noread——码在场与码读出是两个信号）。</param>
        /// <param name="codeCenterX/Y">条码中心（原图坐标；codePresent=false 时忽略）。</param>
        /// <param name="deadband">相对差死区阈值（正/负半区相对差 |v| ≥ 此值才判定）。</param>
        public static HeadTailPoolDecision? Evaluate(
            double fallbackAngle,
            Mat? bgrImage,
            Segmentation edgeResult,
            float rectCenterX,
            float rectCenterY,
            float rectAngleDeg,
            float maskArea,
            double longAxisPx,
            bool codePresent,
            double codeCenterX,
            double codeCenterY,
            double deadband)
        {
            if (bgrImage is null || bgrImage.Empty() || maskArea <= 0)
            {
                return null;
            }

            var mask = edgeResult.Mask;
            if (mask is null || mask.Width <= 0 || mask.Height <= 0)
            {
                return null;
            }

            // 灰度与派生图（梯度幅值/局部标准差/边缘）。全部一次性预计算，主扫描只做 At 读取。
            using var gray = bgrImage.Channels() switch
            {
                3 => bgrImage.CvtColor(ColorConversionCodes.BGR2GRAY),
                1 => bgrImage.Clone(),
                _ => null,
            };
            if (gray is null)
            {
                return null;
            }

            using var gradMag = ComputeGradientMagnitude(gray);
            using var localStd = ComputeLocalStd(gray);
            using var edge = ComputeEdge(gray);
            if (gradMag is null || localStd is null || edge is null)
            {
                return null;
            }

            var bounds = edgeResult.Bounds;
            var rad = rectAngleDeg * Math.PI / 180.0;
            var ux = Math.Cos(rad);
            var uy = Math.Sin(rad);
            // 垂直方向 v（u 旋转 +90°），用于宽度剖面
            var vx = -uy;
            var vy = ux;

            // 码区剔除半径（仅光度/纹理特征跳过码邻域；几何特征用完整掩码保持两变体同口径）
            var codeRadius = codePresent && longAxisPx > 1.0 ? CodeExclusionRadiusRatio * longAxisPx : 0.0;
            var codeRadiusSq = codeRadius * codeRadius;

            long nPlus = 0, nMinus = 0;
            double sumAbsSPlus = 0, sumAbsSMinus = 0;
            long sampleCount = 0;
            double sumT = 0, sumT2 = 0, sumT3 = 0;
            long photoPlus = 0, photoMinus = 0;
            double sumGradPlus = 0, sumGradMinus = 0;
            double sumStdPlus = 0, sumStdMinus = 0;
            long edgePlus = 0, edgeMinus = 0;

            for (int my = 0; my < mask.Height; my++)
            {
                for (int mx = 0; mx < mask.Width; mx++)
                {
                    if (mask[my, mx] <= 0.5f)
                    {
                        continue;
                    }

                    var px = bounds.X + mx;
                    var py = bounds.Y + my;
                    if (px < 0 || py < 0 || px >= gray.Width || py >= gray.Height)
                    {
                        continue;
                    }

                    var dx = px - rectCenterX;
                    var dy = py - rectCenterY;
                    var t = dx * ux + dy * uy;
                    var s = dx * vx + dy * vy;

                    // 几何累加（含全部掩码像素）
                    sampleCount++;
                    sumT += t;
                    sumT2 += t * t;
                    sumT3 += t * t * t;
                    if (t >= 0)
                    {
                        nPlus++;
                        sumAbsSPlus += Math.Abs(s);
                    }
                    else
                    {
                        nMinus++;
                        sumAbsSMinus += Math.Abs(s);
                    }

                    // 光度/纹理累加（剔除码邻域，保证有码/无码变体同口径）
                    if (codeRadius > 0)
                    {
                        var cdx = px - codeCenterX;
                        var cdy = py - codeCenterY;
                        if (cdx * cdx + cdy * cdy < codeRadiusSq)
                        {
                            continue;
                        }
                    }

                    var g = gray.At<byte>(py, px);
                    var gm = gradMag.At<float>(py, px);
                    var ls = localStd.At<float>(py, px);
                    var isEdge = edge.At<byte>(py, px) > 0;

                    if (t >= 0)
                    {
                        photoPlus++;
                        sumGradPlus += gm;
                        sumStdPlus += ls;
                        if (isEdge) edgePlus++;
                    }
                    else
                    {
                        photoMinus++;
                        sumGradMinus += gm;
                        sumStdMinus += ls;
                        if (isEdge) edgeMinus++;
                    }
                }
            }

            var aggregates = new HeadTailAggregates(
                sampleCount, nPlus, nMinus, sumT, sumT2, sumT3,
                sumAbsSPlus, sumAbsSMinus,
                photoPlus, photoMinus, sumGradPlus, sumGradMinus,
                sumStdPlus, sumStdMinus, edgePlus, edgeMinus);
            var features = BuildFeatures(aggregates, longAxisPx, deadband);
            return BuildDecision(fallbackAngle, features);
        }

        /// <summary>级联决策（纯函数，internal 供单测）：第一个出死区的特征定头尾。</summary>
        internal static HeadTailPoolDecision BuildDecision(
            double fallbackAngle, IReadOnlyList<HeadTailFeatureSample> features)
        {
            var decisive = features.FirstOrDefault(f => f.Decisive);
            if (decisive is null)
            {
                // 级联全部死区：维持原角度（调用方回退灰度兜底）
                return new HeadTailPoolDecision(fallbackAngle, false, "None", false, features);
            }

            // "数值大的一侧是头部"：有符号值 > 0 → 头在正半区（u 正向），角度不翻转；
            // < 0 → 头在负半区 → +180°（归一化后等价 −180°），使头端与角度正向一致。
            var headInPositive = decisive.SignedValue > 0;
            var flipped = !headInPositive;
            var angle = fallbackAngle + (flipped ? 180.0 : 0.0);
            return new HeadTailPoolDecision(angle, flipped, decisive.Name, true, features);
        }

        /// <summary>判定轨迹序列化（落库 BarcodeData.HeadFeatures，诊断用）。</summary>
        public static string? SerializeTrace(HeadTailPoolDecision? decision)
        {
            if (decision is null)
            {
                return null;
            }

            var payload = decision.Features.Select(f => new
            {
                n = f.Name,
                v = Math.Round(f.SignedValue, 4),
                db = Math.Round(f.Deadband, 3),
                dec = f.Decisive,
            });
            return System.Text.Json.JsonSerializer.Serialize(
                new { src = decision.SourceFeature, dec = decision.Decisive, f = payload });
        }

        /// <summary>
        /// 从半区聚合构建特征池（纯函数，internal 供单测）。
        /// 特征值约定：v &gt; 0 = 头在 u 正向半区；全部为相对差/归一化形式。
        /// </summary>
        internal static IReadOnlyList<HeadTailFeatureSample> BuildFeatures(
            HeadTailAggregates a, double longAxisPx, double deadband)
        {
            var list = new List<HeadTailFeatureSample>(6);

            void Add(string name, double value, double db)
                => list.Add(new HeadTailFeatureSample(name, value, db, Math.Abs(value) >= db));

            // ① 轴向质心偏移：掩码像素质心相对 OBB 中心沿主轴的偏移（头重侧质心偏移）
            if (a.SampleCount > 0 && longAxisPx > 1.0)
            {
                var meanT = a.SumT / a.SampleCount;
                Add("CentroidOffset", meanT / (longAxisPx / 2.0), deadband);
            }

            // ② 两端宽度差：两半区平均 |垂直偏移| ≈ 各自半宽（头宽尾窄的锥度）
            if (a.CountPlus > 0 && a.CountMinus > 0)
            {
                var wPlus = a.SumAbsSPlus / a.CountPlus;
                var wMinus = a.SumAbsSMinus / a.CountMinus;
                if (wPlus + wMinus > 0)
                {
                    Add("WidthTaper", (wPlus - wMinus) / (wPlus + wMinus), deadband);
                }
            }

            // ③ 轴向偏度：掩码沿轴投影的三阶中心矩 / σ³——面积分布向哪端延伸（头重尾轻/反之）
            if (a.SampleCount >= 3)
            {
                var mu = a.SumT / a.SampleCount;
                var m2 = a.SumT2 / a.SampleCount - mu * mu;
                if (m2 > 1e-9)
                {
                    var m3 = a.SumT3 / a.SampleCount - 3 * mu * (m2 + mu * mu) + 2 * mu * mu * mu;
                    Add("AxialSkew", m3 / Math.Pow(m2, 1.5), deadband * SkewDeadbandMultiplier);
                }
            }

            if (a.PhotoCountPlus > 0 && a.PhotoCountMinus > 0)
            {
                // ④ 两半区梯度能量差（头端印刷/元件 → 边缘更密）
                var gPlus = a.SumGradPlus / a.PhotoCountPlus;
                var gMinus = a.SumGradMinus / a.PhotoCountMinus;
                if (gPlus + gMinus > 0)
                {
                    Add("GradientEnergyDiff", (gPlus - gMinus) / (gPlus + gMinus), deadband);
                }

                // ⑤ 两半区局部纹理差（3×3 局部标准差均值）
                var sPlus = a.SumStdPlus / a.PhotoCountPlus;
                var sMinus = a.SumStdMinus / a.PhotoCountMinus;
                if (sPlus + sMinus > 0)
                {
                    Add("TextureStdDiff", (sPlus - sMinus) / (sPlus + sMinus), deadband);
                }

                // ⑥ 两半区边缘密度差（Canny 边缘像素占比）
                if (a.EdgePlus + a.EdgeMinus > 0)
                {
                    var dPlus = (double)a.EdgePlus / a.PhotoCountPlus;
                    var dMinus = (double)a.EdgeMinus / a.PhotoCountMinus;
                    if (dPlus + dMinus > 0)
                    {
                        Add("EdgeDensityDiff", (dPlus - dMinus) / (dPlus + dMinus), deadband);
                    }
                }
            }

            return list;
        }

        /// <summary>梯度幅值图（Sobel X/Y 平方和开方，CV_32F）。</summary>
        private static Mat? ComputeGradientMagnitude(Mat gray)
        {
            try
            {
                using var gx = new Mat();
                Cv2.Sobel(gray, gx, MatType.CV_32F, 1, 0, 3);
                using var gy = new Mat();
                Cv2.Sobel(gray, gy, MatType.CV_32F, 0, 1, 3);
                using var gx2 = new Mat();
                Cv2.Multiply(gx, gx, gx2);
                using var gy2 = new Mat();
                Cv2.Multiply(gy, gy, gy2);
                using var sum = new Mat();
                Cv2.Add(gx2, gy2, sum);
                var mag = new Mat();
                Cv2.Sqrt(sum, mag);
                return mag;
            }
            catch (Exception)
            {
                return null;
            }
        }

        /// <summary>局部标准差图（E[g²] − μ² 的开方，5×5 高斯窗，CV_32F）——纹理强度代理。</summary>
        private static Mat? ComputeLocalStd(Mat gray)
        {
            try
            {
                using var g32 = new Mat();
                gray.ConvertTo(g32, MatType.CV_32F);
                using var mu = new Mat();
                Cv2.GaussianBlur(g32, mu, new Size(5, 5), 0);
                using var sq = new Mat();
                Cv2.Multiply(g32, g32, sq);
                using var muSq = new Mat();
                Cv2.GaussianBlur(sq, muSq, new Size(5, 5), 0);
                using var varMat = new Mat();
                Cv2.Subtract(muSq, mu, varMat);
                Cv2.Max(varMat, 0, varMat);
                var std = new Mat();
                Cv2.Sqrt(varMat, std);
                return std;
            }
            catch (Exception)
            {
                return null;
            }
        }

        /// <summary>Canny 边缘图（头端印刷/元件 → 边缘更密）。</summary>
        private static Mat? ComputeEdge(Mat gray)
        {
            try
            {
                var edge = new Mat();
                Cv2.Canny(gray, edge, 80, 160);
                return edge;
            }
            catch (Exception)
            {
                return null;
            }
        }
    }
}
