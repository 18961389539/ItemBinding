using JinlongYolo.YoloSharp.Data;
using OpenCvSharp;
using System;
using System.Collections.Generic;
using System.Linq;

namespace MainAPP.Application
{
    /// <summary>单个候选特征的判定样本：有符号值 + 死区 + 是否 decisive（级联在此特征定头尾）。</summary>
    /// <summary>
    /// 单个头尾特征样本。
    /// <para><b>为什么要把 <paramref name="Scale"/> 单独记下来</b>（2026-09-13）：各特征量纲不同
    /// （③偏度天然比相对差大、④亮度是 0~255 灰度差），故 <paramref name="Deadband"/>
    /// 是"基准死区 × 本特征缩放系数"的积。而弱信号让位的置信度
    /// <c>conf = |v| / 基准</c> 必须<b>跨产品可比</b> —— 基准死区一旦按产品自适应，
    /// 就无法再从 <paramref name="Deadband"/> 反推出缩放系数（基准×系数与基准耦合了）。
    /// 记下 <paramref name="Scale"/> 后用固定的 <c>ConfReference</c> 归一化，即得跨产品稳定的置信度。</para>
    /// <para>语义分离：<paramref name="Deadband"/> 只决定"<b>是否发言</b>"（可自适应），
    /// <c>ConfReference × Scale</c> 只决定"<b>发言有多强</b>"（恒定，保证让位阈值跨产品通用）。</para>
    /// </summary>
    /// <param name="Name">特征名。</param>
    /// <param name="SignedValue">有符号特征值（&gt;0 = 头在 +u 侧）。</param>
    /// <param name="Deadband">实际生效死区 = 基准死区 × <paramref name="Scale"/>（自适应时基准会变）。</param>
    /// <param name="Decisive">是否出死区（|值| ≥ <paramref name="Deadband"/>）。</param>
    /// <param name="Scale">本特征相对"基准死区"的缩放系数（量纲换算用；默认 1.0 = 与基准同量纲）。</param>
    public sealed record HeadTailFeatureSample(
        string Name, double SignedValue, double Deadband, bool Decisive, double Scale = 1.0);

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
    /// <param name="SumT">主轴投影 t 的一阶和（质心偏移用）。</param>
    /// <param name="SumT2">t 的二阶和（方差/偏度用）。</param>
    /// <param name="SumT3">t 的三阶和（偏度用）。</param>
    /// <param name="SumAbsSPlus">正半区 |垂直偏移 s| 之和（≈ 正半区半宽 × 像素数）。</param>
    /// <param name="SumAbsSMinus">负半区 |垂直偏移 s| 之和。</param>
    /// <param name="PhotoCountPlus">正半区参与光度/纹理统计的像素数（已剔除码邻域）。</param>
    /// <param name="PhotoCountMinus">负半区参与光度/纹理统计的像素数。</param>
    /// <param name="SumGradPlus">正半区梯度幅值之和。</param>
    /// <param name="SumGradMinus">负半区梯度幅值之和。</param>
    /// <param name="SumStdPlus">正半区局部标准差之和。</param>
    /// <param name="SumStdMinus">负半区局部标准差之和。</param>
    /// <param name="EdgePlus">正半区 Canny 边缘像素数。</param>
    /// <param name="EdgeMinus">负半区 Canny 边缘像素数。</param>
    /// <param name="HistPlus">正半区 256 桶灰度直方图（亮度特征用；仅灰度图可用时非 null）。</param>
    /// <param name="HistMinus">负半区 256 桶灰度直方图。</param>
    /// <param name="HistAll">全域 256 桶灰度直方图（取拉伸分位窗口用）。</param>
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
        long EdgeMinus,
        int[]? HistPlus = null,
        int[]? HistMinus = null,
        int[]? HistAll = null)
    {
        /// <summary>是否有可用的灰度直方图（三张图齐全才谈得上亮度特征）。</summary>
        public bool HasHistograms => HistPlus is not null && HistMinus is not null && HistAll is not null;
    }

    /// <summary>
    /// 亮度判向落库统计快照（2026-09-13 自 DetectionRecordService.BrightnessDirectionStats 平移而来）。
    /// <para><b>量纲</b>：<paramref name="MeanPlus"/>/<paramref name="MeanMinus"/>/<paramref name="Diff"/> 均为
    /// "掩码内对比度拉伸之后"的值（拉伸关闭或窗口过窄时即原始绝对灰度 0~255）；
    /// <b>不变式 <c>Diff = MeanPlus − MeanMinus</c></b> 恒成立，落库列
    /// <c>DbModel.BrightMean/DarkMean/BrightnessDiff</c> 同口径。<b>符号约定</b>：
    /// <c>Diff &gt; 0</c> = 正半区更亮（"数值大的一侧是头部"的亮度体现）。</para>
    /// </summary>
    /// <param name="MeanPlus">正向半区（+u 侧）拉伸后平均灰度 0~255。</param>
    /// <param name="MeanMinus">负向半区（−u 侧）拉伸后平均灰度 0~255。</param>
    /// <param name="Diff">两侧拉伸后平均灰度差 = MeanPlus − MeanMinus。</param>
    /// <param name="Low">本次使用的拉伸窗口低分位灰度；未拉伸时为 NaN。</param>
    /// <param name="High">本次使用的拉伸窗口高分位灰度；未拉伸时为 NaN。</param>
    public readonly record struct BrightnessDirectionStats(
        double MeanPlus,
        double MeanMinus,
        double Diff,
        double Low,
        double High);

    /// <summary>
    /// 头尾特征池（2026-09-13）：掩码回退路径的"零定标"头尾判定级联。
    /// <para><b>核心约定："数值大的一侧是头部"</b>——每个候选特征按主轴方向 u 把掩码分成
    /// 正/负两个半区，计算有符号相对值；数值大（正号）的半区即头部。该规则利用"产品的
    /// 物理不对称性随产品一起旋转"这一事实自定向，无需任何符号定标/约定冻结。</para>
    /// <para>级联按固定优先级排序（几何 → 亮度 → 结构 → 纹理）：几何特征对光照完全免疫故最优先；
    /// <b>亮度特征</b>（2026-09-13 由灰度判向并入）是产品固有属性、信噪比高于梯度/纹理，紧随几何之后。
    /// 默认取第一个出死区的特征定头尾；<b>但先到者仅勉强过线时，允许后方明显更强的特征接管</b>
    /// （弱信号让位，见 <c>WeakConfidence</c>/<c>StrongConfidence</c>），避免「早到的弱者压制后到的强者」。
    /// 全部死区则返回非 decisive 结果。</para>
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
        /// 亮度特征死区换算系数：亮度用"拉伸后绝对灰度差"（0~255 量纲），而级联死区是相对差量纲（≈0~1）。
        /// <para>换算依据：相对差 <c>(P−M)/(P+M)</c> 在两侧均值接近满量程时约等于 <c>Diff/(2×255)</c>，
        /// 故 <c>DeadbandBrightness = Deadband × 2 × 255</c> 使两者在"同量纲可比"的意义上对齐——
        /// 灰度判向的死区默认 5.0（≈ 满量程 2%），特征池相对差死区默认 0.10（≈ 满量程 40% 的 1/4），
        /// 两者本就不同源，此处以特征池死区为准做统一换算，避免出现两套并行阈值。</para>
        /// <para>注意：<b>不能</b>把亮度改成相对差形式来对齐量纲——相对差对线性缩放免疫，
        /// 而掩码内对比度拉伸恰恰是线性映射，改形式等于让拉伸完全失效（判别力归零）。</para>
        /// </summary>
        private const double BrightnessDeadbandScale = 2.0 * 255.0;

        /// <summary>对比度拉伸的最小有效窗口跨度（灰度级）。窗口窄于此值视为近单色掩码，跳过拉伸。</summary>
        private const double MinStretchSpan = 8.0;

        /// <summary>灰度直方图桶数（掩码内灰度是 byte，天然 256 桶）。</summary>
        private const int HistogramBuckets = 256;

        /// <summary>
        /// 派生图 ROI 相对掩码外接框的外扩余量（像素）。取最大卷积核半径：
        /// 局部标准差的 5×5 高斯 → 半径 2；Sobel 3×3 → 1；Canny 内部 Sobel → 1。
        /// 余量保证外接框内像素的核邻域完整落在 ROI 内，框内结果与整幅图计算一致
        /// （差异只出现在余量环上，而余量环不被掩码读取）。
        /// </summary>
        private const int RoiMargin = 2;

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
        /// <param name="brightnessEnabled">是否启用亮度特征（配方级覆盖 ?? 全局设置）。关闭时不累加直方图，
        /// 级联退化为纯几何/结构/纹理——历史行为对齐"灰度判向关闭"。</param>
        /// <param name="stretchEnabled">是否启用掩码内对比度拉伸（全局设置）。</param>
        /// <param name="stretchLowPercentile">拉伸窗口低分位（0~100）。</param>
        /// <param name="stretchHighPercentile">拉伸窗口高分位（0~100）。</param>
        /// <param name="brightnessStats">输出：亮度统计快照（供落库/配方页显示）；未执行或不可统计时为 null。
        /// 注意"统计完成"与"判为 decisive"是两回事——死区样本同样输出统计（正是标定所需数据）。</param>
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
            double deadband,
            bool brightnessEnabled,
            bool stretchEnabled,
            double stretchLowPercentile,
            double stretchHighPercentile,
            out BrightnessDirectionStats? brightnessStats,
            Func<string, double>? adaptiveBaseDeadband = null)
        {
            brightnessStats = null;
            if (bgrImage is null || bgrImage.Empty() || maskArea <= 0)
            {
                return null;
            }

            var mask = edgeResult.Mask;
            if (mask is null || mask.Width <= 0 || mask.Height <= 0)
            {
                return null;
            }

            var bounds = edgeResult.Bounds;

            // 2026-09-13: 派生图裁到掩码外接框（含核半径余量）。三张派生图只在掩码内被读取，
            // 却按整幅图计算——5MP 实测占单帧耗时约一半（Sobel 29ms + 局部标准差 31ms + Canny 6ms）。
            // 裁剪后这部分成本随【掩码面积】而非【画幅】缩放（掩码约占画幅 45% 时省约 35ms/帧）。
            // 余量 RoiMargin 保证框内像素的卷积核邻域完整落在 ROI 内：Sobel/高斯在框内与
            // 整幅图计算逐位一致；Canny 的非极大值抑制/滞后可能使框缘约 2px 内有细微差异
            // （远小于 0.10 死区，对相对差特征可忽略）。
            // ROI 视图零拷贝：new Mat(parent, rect) 只调整数据指针与尺寸，不复制像素。
            var extentWidth = Math.Max(bounds.Width, mask.Width);
            var extentHeight = Math.Max(bounds.Height, mask.Height);
            if (!TryComputeMaskRoi(bounds.X, bounds.Y, extentWidth, extentHeight,
                    bgrImage.Width, bgrImage.Height, out var roi))
            {
                return null; // 掩码外接框与图像无交集（退化，防御）
            }

            using var grayRoiView = new Mat(bgrImage, roi);
            using var gray = bgrImage.Channels() switch
            {
                3 => grayRoiView.CvtColor(ColorConversionCodes.BGR2GRAY),
                1 => grayRoiView.Clone(),
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

            // 2026-09-13: 扫描改用平铺 Span 替代 Mat.At<T>()。
            // 实测（连续 Mat、行主序遍历）：At 5.8 ns/次、GetGenericIndexer 20.5 ns/次、
            // AsSpan 1.1 ns/次——5 倍于 At，且 GetGenericIndexer 反而比 At 慢 3.5 倍（勿用）。
            // 每像素 4 次读图是 ROI 扫描的主要成本，此项是剩余优化中收益最大的一处。
            // 四张图都是 OpenCV 函数新分配的输出（或 Clone），恒为连续内存；
            // AsSpan 对非连续 Mat 会按错误的行主序布局索引（静默数据损坏），
            // 故显式校验并快速失败——这是编程错误而非运行时条件，响亮失败优于静默出错。
            if (!gray.IsContinuous() || !gradMag.IsContinuous() ||
                !localStd.IsContinuous() || !edge.IsContinuous())
            {
                throw new InvalidOperationException(
                    "头尾特征池的派生图预期为连续内存（应为 OpenCV 函数新分配的输出或 Clone），" +
                    "实际不连续——AsSpan 的行主序索引布局将不成立。" +
                    "请检查派生图的构造方式是否被改动（例如对 ROI 视图做了二次裁剪）。");
            }

            var graySpan = gray.AsSpan<byte>();
            var gradSpan = gradMag.AsSpan<float>();
            var stdSpan = localStd.AsSpan<float>();
            var edgeSpan = edge.AsSpan<byte>();
            var maskSpan = mask.AsReadOnlySpan();
            // 四张图同尺寸（同源于 ROI），行主序索引共用列数；ROI ≤ 5MP 时 rows*cols < int.MaxValue
            var spanCols = roi.Width;

            // 扫描按图像坐标寻址（掩码外接框可能越出图像），派生图按 ROI 局部坐标读取
            var imageWidth = bgrImage.Width;
            var imageHeight = bgrImage.Height;
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

            // 亮度特征（2026-09-13 由灰度判向并入）：仅在启用时累积直方图，关闭时零开销。
            // 用直方图而非"累加和"是为了支撑掩码内对比度拉伸——拉伸需要先拿到分位窗口，
            // 而分位数无法从累加和反推；代价仅 3×256 个 int，且与主扫描同遍完成。
            int[]? histPlus = brightnessEnabled ? new int[HistogramBuckets] : null;
            int[]? histMinus = brightnessEnabled ? new int[HistogramBuckets] : null;
            int[]? histAll = brightnessEnabled ? new int[HistogramBuckets] : null;

            for (int my = 0; my < mask.Height; my++)
            {
                for (int mx = 0; mx < mask.Width; mx++)
                {
                    if (maskSpan[(my * mask.Width) + mx] <= 0.5f)
                    {
                        continue;
                    }

                    var px = bounds.X + mx;
                    var py = bounds.Y + my;
                    if (px < 0 || py < 0 || px >= imageWidth || py >= imageHeight)
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

                    // ROI 局部坐标 + 平铺索引：四张图同尺寸，(ly, lx) → ly * spanCols + lx
                    var lx = px - roi.X;
                    var ly = py - roi.Y;
                    var idx = ly * spanCols + lx;
                    var g = graySpan[idx];
                    var gm = gradSpan[idx];
                    var ls = stdSpan[idx];
                    var isEdge = edgeSpan[idx] > 0;

                    // 亮度累加（同样剔除码邻域——码是高对比度区域，不剔除会把它误判成头端特征）
                    if (histAll is not null)
                    {
                        histAll[g]++;
                        if (t >= 0)
                        {
                            histPlus![g]++;
                        }
                        else
                        {
                            histMinus![g]++;
                        }
                    }

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
                sumStdPlus, sumStdMinus, edgePlus, edgeMinus,
                histPlus, histMinus, histAll);
            var features = BuildFeatures(
                aggregates, longAxisPx, deadband,
                stretchEnabled, stretchLowPercentile, stretchHighPercentile,
                out brightnessStats, adaptiveBaseDeadband);
            return BuildDecision(fallbackAngle, features);
        }

        /// <summary>
        /// 弱信号让位门槛：首个出死区者的置信倍数（|v|/基准）低于此值时，
        /// 允许后续更强特征接管裁决（2026-09-13 修「早到的弱者压制后到的强者」）。
        /// </summary>
        /// <remarks>
        /// 纯 <c>FirstOrDefault</c> 的缺陷：① 只要 <c>|v| = 0.101 &gt; db = 0.10</c> 就裁决，
        /// 哪怕后面 ⑦ 的 <c>|v|/db = 8</c>（信号强 80 倍）也永远没机会说话——用微弱信号覆盖强信号。
        /// 本机制给"先到"一个<b>有条件</b>的优先权：先到者信号足够强（≥ <see cref="StrongConfidence"/>）
        /// 则维持级联语义不变；只有先到者勉强过线（&lt; <see cref="WeakConfidence"/>）时，
        /// 才检查后方是否存在明显更强的特征。
        /// </remarks>
        private const double WeakConfidence = 1.5;

        /// <summary>
        /// 强信号门槛：后续特征置信倍数达此值才允许接管弱信号先到者。
        /// 与 <see cref="WeakConfidence"/> 拉开 2 倍差值，避免两特征置信接近时反复横跳。
        /// </summary>
        private const double StrongConfidence = 3.0;

        /// <summary>
        /// 置信度归一化基准（2026-09-13 引入，为自适应死区铺路）。
        /// <para><b>为什么需要它</b>：<see cref="WeakConfidence"/>/<see cref="StrongConfidence"/>
        /// 是<b>跨产品通用</b>的阈值——"1.5 倍"在产品 A 与产品 B 上必须代表同等信号强度。
        /// 若用实际死区（即将自适应）做归一化，同一帧图的"1.5 倍"会随产品漂移，
        /// 这两个阈值的物理含义就消失了。</para>
        /// <para><b>取值</b>：等于相对差量纲下的传统死区默认值 0.10，故在"死区未自适应"
        /// 的现状下 <c>conf</c> 与旧口径<b>完全等价</b>（回归零影响）；自适应上线后
        /// 它保持不变，从而 <c>conf</c> 仍是跨产品可比的绝对量。</para>
        /// </summary>
        private const double ConfReference = 0.10;

        /// <summary>
        /// 置信倍数比较的浮点容差。必要性：<c>0.15 / 0.10</c> 在 IEEE754 下得 1.4999999999999998
        /// 而非 1.5，<c>0.30 / 0.10</c> 得 2.9999999999999996 而非 3.0 → 恰好落在门槛上的样本
        /// 会因精度误差被判到错误一侧。加入 1e-9 相对容差使边界行为符合直觉（阈值本身是启发式值，
        /// 这点容差不会改变任何实质判断）。
        /// </summary>
        private const double ConfidenceEpsilon = 1e-9;

        /// <summary>
        /// 级联决策（纯函数，internal 供单测）。
        /// <para><b>裁决规则</b>：默认取第一个出死区的特征（级联语义，体现"几何对光照免疫故优先"的先验）；
        /// 但当该特征只是<b>勉强过线</b>（|v|/db &lt; <see cref="WeakConfidence"/>）而后方存在
        /// <b>明显更强</b>的特征（|v|/db ≥ <see cref="StrongConfidence"/>）时，由后者接管——
        /// 避免微弱信号压制强信号。接管者取后方置信度最大者，同等置信度时保持原顺序（稳定性）。</para>
        /// <para>设计取舍：用"刚性区间"而非连续加权，是为了保住级联的<b>可解释性</b>——
        /// 落库的 SourceFeature 依旧明确指向单一裁决者，现场工程师能复查"这帧是谁判的"；
        /// 贝叶斯/加权融合会把多特征混在一起，出问题无法归因到具体一项。</para>
        /// </summary>
        internal static HeadTailPoolDecision BuildDecision(
            double fallbackAngle, IReadOnlyList<HeadTailFeatureSample> features)
        {
            var first = features.FirstOrDefault(f => f.Decisive);
            if (first is null)
            {
                // 级联全部死区：维持原角度（调用方回退灰度兜底）
                return new HeadTailPoolDecision(fallbackAngle, false, "None", false, features);
            }

            var decisive = ResolveWeakSignalYield(first, features);

            // "数值大的一侧是头部"：有符号值 > 0 → 头在正半区（u 正向），角度不翻转；
            // < 0 → 头在负半区 → +180°（归一化后等价 −180°），使头端与角度正向一致。
            var headInPositive = decisive.SignedValue > 0;
            var flipped = !headInPositive;
            var angle = fallbackAngle + (flipped ? 180.0 : 0.0);
            return new HeadTailPoolDecision(angle, flipped, decisive.Name, true, features);
        }

        /// <summary>
        /// 弱信号让位裁决（纯函数，internal 供单测）：返回最终定头尾的特征。
        /// <para>先到者置信 ≥ <see cref="StrongConfidence"/> → 直接采用（不折腾，保持级联稳定）；
        /// 先到者置信 ≥ <see cref="WeakConfidence"/> → 也采用（不算勉强，视为有效判断）；
        /// 先到者置信 &lt; <see cref="WeakConfidence"/> → 在后出死区且<b>符号相同</b>的特征中找置信最大者，
        /// 若该值 ≥ <see cref="StrongConfidence"/> 则由它接管，否则仍由先到者裁决。</para>
        /// <para><b>为什么要求符号相同（让位只确认、不翻案）</b>：让位要修的是"弱者压制强者"
        /// （同一结论本该由更强证据支持），而不是"弱者判错了方向"。若允许异符号接管，
        /// 伪信号会因数值极端而 conf 虚高并劫持裁决——实测案例：全屏掩码下
        /// <c>EdgeDensityDiff = −1.0</c>（相对差理论极值，Canny 边界响应所致）conf 高达 10，
        /// 会推翻亮度特征的 conf 1.18 从而给出错误方向。<b>方向性错误应交给一致性统计去发现和修正</b>
        /// （headtail_audit 里某特征一致率长期偏低 → 降优先级或删除），
        /// 而不是靠启发式在这里翻案——职责分离更干净，也不会引入新的拍脑袋参数。</para>
        /// </summary>
        /// <param name="first">第一个出死区的特征（调用方保证非 null）。</param>
        /// <param name="features">完整特征表（含未出死区项，本方法自行筛选）。</param>
        internal static HeadTailFeatureSample ResolveWeakSignalYield(
            HeadTailFeatureSample first, IReadOnlyList<HeadTailFeatureSample> features)
        {
            var firstConfidence = ConfidenceOf(first);
            if (firstConfidence + ConfidenceEpsilon >= WeakConfidence)
            {
                return first; // 先到者信号不算勉强，维持级联语义
            }

            var firstPositive = first.SignedValue > 0;

            // 用索引定位而非引用比较：引用相等依赖"调用方传同一实例"这一隐式契约，
            // 将来若改成值语义或复制列表会静默失效；record 的 Equals 是值语义，
            // 故按字段匹配首个命中项即可（特征名在单帧内唯一）。
            var startIndex = -1;
            for (int i = 0; i < features.Count; i++)
            {
                if (ReferenceEquals(features[i], first) || features[i] == first)
                {
                    startIndex = i;
                    break;
                }
            }

            if (startIndex < 0)
            {
                return first; // 理论上不可达；保守起见不做让位
            }

            HeadTailFeatureSample? strongest = null;
            var strongestConfidence = 0.0;
            for (int i = startIndex + 1; i < features.Count; i++)
            {
                var f = features[i];
                if (!f.Decisive)
                {
                    continue;
                }

                // 只接受与先到者同符号的接管者（让位 = 用更强证据确认同一结论）
                if ((f.SignedValue > 0) != firstPositive)
                {
                    continue;
                }

                var conf = ConfidenceOf(f);
                // 严格大于：同等置信度时保留先出现者，避免级联顺序被噪声扰动
                if (conf > strongestConfidence)
                {
                    strongestConfidence = conf;
                    strongest = f;
                }
            }

            return strongest is not null && strongestConfidence + ConfidenceEpsilon >= StrongConfidence
                ? strongest
                : first;
        }

        /// <summary>
        /// 置信倍数 = |有符号值| / (<see cref="ConfReference"/> × 本特征缩放系数)。
        /// <para><b>为什么不用实际死区归一化</b>：实际死区即将按产品自适应，
        /// 用它做分母会让"1.5 倍"在不同产品上代表不同信号强度，让位阈值失去意义。
        /// 用固定基准后，<c>conf</c> 是<b>跨产品可比</b>的绝对信号强度。</para>
        /// <para>缩放系数非正时返回 0（不可比，视作无信号）。现状下死区恒为 0.10 × 系数，
        /// 故本式与旧口径 <c>|v|/Deadband</c> 完全等价。</para>
        /// </summary>
        private static double ConfidenceOf(HeadTailFeatureSample f)
            => f.Scale > 0 ? Math.Abs(f.SignedValue) / (ConfReference * f.Scale) : 0.0;

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
                // 缩放系数（2026-09-13 增）：使轨迹自描述——基准死区自适应后，
                // 仅凭 db 无法还原该特征由何种量纲换算而来，也就无法离线复算跨产品可比的 conf。
                sc = Math.Round(f.Scale, 4),
            });
            return System.Text.Json.JsonSerializer.Serialize(
                new { src = decision.SourceFeature, dec = decision.Decisive, f = payload });
        }

        /// <summary>
        /// 从半区聚合构建特征池（纯函数，internal 供单测）。
        /// 特征值约定：v &gt; 0 = 头在 u 正向半区；除亮度外均为相对差/归一化形式。
        /// </summary>
        /// <param name="a">半区聚合（含可选灰度直方图）。</param>
        /// <param name="longAxisPx">产品长轴长度（原图像素）。</param>
        /// <param name="deadband">相对差死区（亮度特征按 <see cref="BrightnessDeadbandScale"/> 换算）。</param>
        /// <param name="stretchEnabled">是否启用掩码内对比度拉伸。</param>
        /// <param name="stretchLowPercentile">拉伸窗口低分位（0~100）。</param>
        /// <param name="stretchHighPercentile">拉伸窗口高分位（0~100）。</param>
        /// <param name="brightnessStats">输出：亮度统计快照（供落库/配方页显示）；不可统计时 null。</param>
        /// <param name="adaptiveBaseDeadband">
        /// 可选的逐特征自适应基准死区（2026-09-13，按特征名查询；null = 统一用 <paramref name="deadband"/>）。
        /// 由 <c>HeadTailAdaptiveDeadband</c> 提供（k × median(|v|)，按配方分桶）。
        /// <b>只替换"基准"，量纲缩放系数仍由本方法乘上</b>（偏度 ×2、亮度 ×510）——
        /// 让位机制的 conf 按固定 ConfReference 归一化，不受此处影响。
        /// 置于 out 参数之后是为了不搅动二十余处既有调用点（生产路径之外均传 null）。</param>
        internal static IReadOnlyList<HeadTailFeatureSample> BuildFeatures(
            HeadTailAggregates a, double longAxisPx, double deadband,
            bool stretchEnabled,
            double stretchLowPercentile,
            double stretchHighPercentile,
            out BrightnessDirectionStats? brightnessStats,
            Func<string, double>? adaptiveBaseDeadband = null)
        {
            brightnessStats = null;
            var list = new List<HeadTailFeatureSample>(7);

            // 统一入口：调用方只给"相对基准死区的缩放系数"（量纲换算），
            // 实际死区 = 基准死区 × 系数。基准死区可来自自适应（按特征名查询），
            // 也可回退统一固定值；系数恒定，故 conf 语义不受自适应影响。
            void Add(string name, double value, double scale = 1.0)
            {
                var baseDb = adaptiveBaseDeadband?.Invoke(name) ?? deadband;
                var db = baseDb * scale;
                list.Add(new HeadTailFeatureSample(name, value, db, Math.Abs(value) >= db, scale));
            }

            // ① 轴向质心偏移：掩码像素质心相对 OBB 中心沿主轴的偏移（头重侧质心偏移）
            if (a.SampleCount > 0 && longAxisPx > 1.0)
            {
                var meanT = a.SumT / a.SampleCount;
                Add("CentroidOffset", meanT / (longAxisPx / 2.0));
            }

            // ② 两端宽度差：两半区平均 |垂直偏移| ≈ 各自半宽（头宽尾窄的锥度）
            if (a.CountPlus > 0 && a.CountMinus > 0)
            {
                var wPlus = a.SumAbsSPlus / a.CountPlus;
                var wMinus = a.SumAbsSMinus / a.CountMinus;
                if (wPlus + wMinus > 0)
                {
                    Add("WidthTaper", (wPlus - wMinus) / (wPlus + wMinus));
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
                    Add("AxialSkew", m3 / Math.Pow(m2, 1.5), SkewDeadbandMultiplier);
                }
            }

            // ④ 亮度差（2026-09-13 由灰度判向并入）：排在几何之后、梯度/纹理之前。
            //    亮度是产品固有属性，信噪比高于梯度/纹理；且沿用原位次可保证"灰度判向原本能判出的样本，
            //    并入后在级联中的相对优先级不降级"。量纲用"拉伸后绝对灰度差"，死区乘 BrightnessDeadbandScale
            //    换算到相对差量纲，使级联内部可比。
            if (a.HasHistograms)
            {
                AddBrightnessFeature(a, list, deadband, stretchEnabled, stretchLowPercentile, stretchHighPercentile,
                    out brightnessStats, adaptiveBaseDeadband);
            }

            if (a.PhotoCountPlus > 0 && a.PhotoCountMinus > 0)
            {
                // ⑤ 两半区梯度能量差（头端印刷/元件 → 边缘更密）
                var gPlus = a.SumGradPlus / a.PhotoCountPlus;
                var gMinus = a.SumGradMinus / a.PhotoCountMinus;
                if (gPlus + gMinus > 0)
                {
                    Add("GradientEnergyDiff", (gPlus - gMinus) / (gPlus + gMinus));
                }

                // ⑥ 两半区局部纹理差（5×5 局部标准差均值）
                var sPlus = a.SumStdPlus / a.PhotoCountPlus;
                var sMinus = a.SumStdMinus / a.PhotoCountMinus;
                if (sPlus + sMinus > 0)
                {
                    Add("TextureStdDiff", (sPlus - sMinus) / (sPlus + sMinus));
                }

                // ⑦ 两半区边缘密度差（Canny 边缘像素占比）
                if (a.EdgePlus + a.EdgeMinus > 0)
                {
                    var dPlus = (double)a.EdgePlus / a.PhotoCountPlus;
                    var dMinus = (double)a.EdgeMinus / a.PhotoCountMinus;
                    if (dPlus + dMinus > 0)
                    {
                        Add("EdgeDensityDiff", (dPlus - dMinus) / (dPlus + dMinus));
                    }
                }
            }

            return list;
        }

        /// <summary>
        /// 计算亮度特征并追加到特征表（自 <c>DetectionRecordService.ApplyBrightnessHeadDirectionIfEnabled</c>
        /// 平移而来，2026-09-13）。
        /// <para><b>为什么保留绝对差而非改成相对差</b>：相对差 <c>(P−M)/(P+M)</c> 对线性缩放免疫，
        /// 而掩码内对比度拉伸恰恰是线性映射 <c>v=(i−lo)·scale</c> ——改成相对差会让拉伸完全失效，
        /// 而拉伸正是灰度判向现在唯一的判别力来源（实测把 |diff| 中位数放大 2.3 倍、出死区率 44.6%→76%）。
        /// 故保留绝对差，改用 <see cref="BrightnessDeadbandScale"/> 把死区换算到同量纲。</para>
        /// <para><b>符号约定</b>：<c>Diff &gt; 0</c> = 正半区更亮 → 与特征池"数值大的一侧是头部"
        /// 天然一致，无需任何外部明暗约定（原 BrightnessHeadEndIsBright 开关已废弃）。</para>
        /// </summary>
        private static void AddBrightnessFeature(
            HeadTailAggregates a,
            List<HeadTailFeatureSample> list,
            double deadband,
            bool stretchEnabled,
            double lowPercentile,
            double highPercentile,
            out BrightnessDirectionStats? stats,
            Func<string, double>? adaptiveBaseDeadband = null)
        {
            stats = null;
            var histPlus = a.HistPlus!;
            var histMinus = a.HistMinus!;
            var histAll = a.HistAll!;

            long countPlus = 0, countMinus = 0;
            for (int i = 0; i < HistogramBuckets; i++)
            {
                countPlus += histPlus[i];
                countMinus += histMinus[i];
            }

            // 某侧无像素（极端掩码）→ 无法判向，也不产出统计
            if (countPlus == 0 || countMinus == 0)
            {
                return;
            }

            // ---- 掩码内对比度拉伸 ----
            // 把掩码内灰度的 [lo, hi] 分位窗口线性映射到 [0,255]，让两端本来就小的反差在满量程下被放大。
            // 窗口过窄（近单色掩码）时跳过，避免把噪声放大成信号。
            double lo = 0, hi = 255;
            bool stretch = stretchEnabled;
            if (stretch)
            {
                long total = countPlus + countMinus;
                lo = PercentileFromHistogram(histAll, total, lowPercentile);
                hi = PercentileFromHistogram(histAll, total, highPercentile);
                if (hi - lo < MinStretchSpan)
                {
                    stretch = false;
                    lo = 0;
                    hi = 255;
                }
            }

            double scale = stretch ? 255.0 / (hi - lo) : 1.0;
            double sumPlus = 0, sumMinus = 0;
            for (int i = 0; i < HistogramBuckets; i++)
            {
                if (histPlus[i] == 0 && histMinus[i] == 0)
                {
                    continue;
                }

                double v = stretch ? (i - lo) * scale : i;
                if (v < 0)
                {
                    v = 0;
                }
                else if (v > 255)
                {
                    v = 255;
                }

                sumPlus += v * histPlus[i];
                sumMinus += v * histMinus[i];
            }

            var meanPlus = sumPlus / countPlus;
            var meanMinus = sumMinus / countMinus;
            var diff = meanPlus - meanMinus;

            // 统计已完成即回填（死区样本同样回填——正是标定死区阈值所需的分析数据）
            stats = new BrightnessDirectionStats(
                meanPlus, meanMinus, diff,
                stretch ? lo : double.NaN,
                stretch ? hi : double.NaN);

            // 量纲换算：绝对灰度差 → 相对差量纲，与其它特征在级联内可比。
            // 基准死区同样走自适应（按特征名查询），缩放系数恒定——
            // 使 ConfidenceOf 能用固定 ConfReference 算出跨产品可比的置信度。
            var brightnessBase = adaptiveBaseDeadband?.Invoke("BrightnessDiff") ?? deadband;
            var brightnessScale = BrightnessDeadbandScale;
            var brightnessDb = brightnessBase * brightnessScale;
            list.Add(new HeadTailFeatureSample(
                "BrightnessDiff",
                diff,
                brightnessDb,
                Math.Abs(diff) >= brightnessDb,
                brightnessScale));
        }

        /// <summary>
        /// 按"最近秩"（nearest-rank）从 256 桶直方图取分位数（0~100）。
        /// 用分位而非极值取窗口，可避开孤立噪点与掩码边缘毛刺对拉伸窗口的干扰。
        /// </summary>
        private static double PercentileFromHistogram(int[] histogram, long total, double percentile)
        {
            if (total <= 0)
            {
                return 0;
            }

            if (percentile <= 0)
            {
                percentile = 0;
            }
            else if (percentile >= 100)
            {
                percentile = 100;
            }

            long target = (long)Math.Ceiling(total * percentile / 100.0);
            if (target < 1)
            {
                target = 1;
            }

            long acc = 0;
            for (int i = 0; i < histogram.Length; i++)
            {
                acc += histogram[i];
                if (acc >= target)
                {
                    return i;
                }
            }

            return histogram.Length - 1;
        }

        /// <summary>
        /// 计算派生图的 ROI = 掩码外接框外扩 <see cref="RoiMargin"/> 后与图像求交。
        /// 返回 false 表示外接框与图像无交集（掩码完全越界，退化场景）。
        /// <para><b>为什么用 extent 而不是 bounds</b>：扫描按 (bounds.X + mx, bounds.Y + my) 寻址、
        /// mx 遍历 [0, mask.Width)——若 mask 尺寸大于 bounds（异常数据），按 bounds 算的 ROI
        /// 会漏掉部分被读取的像素。取两者较大值是防御性的，代价只是 ROI 略大。</para>
        /// </summary>
        private static bool TryComputeMaskRoi(
            int originX, int originY, int extentWidth, int extentHeight,
            int imageWidth, int imageHeight, out Rect roi)
        {
            var x0 = Math.Max(0, originX - RoiMargin);
            var y0 = Math.Max(0, originY - RoiMargin);
            var x1 = Math.Min(imageWidth, originX + extentWidth + RoiMargin);
            var y1 = Math.Min(imageHeight, originY + extentHeight + RoiMargin);
            if (x1 <= x0 || y1 <= y0)
            {
                roi = default;
                return false;
            }

            roi = new Rect(x0, y0, x1 - x0, y1 - y0);
            return true;
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
