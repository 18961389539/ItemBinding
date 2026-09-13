using MainAPP.Application;
using Xunit;

namespace MainAPP.Tests.Application
{
    /// <summary>
    /// 头尾特征池纯函数测试：级联顺序、死区行为、"数值大的一侧是头部"翻转约定。
    /// </summary>
    public class HeadTailFeaturePoolTests
    {
        private const double Deadband = 0.10;
        private const double LongAxis = 100.0;

        private static HeadTailAggregates Agg(
            long n, long nP, long nM, double sumT, double sumT2, double sumT3,
            double sAP = 0, double sAM = 0,
            long pP = 0, long pM = 0,
            double gP = 0, double gM = 0,
            double stP = 0, double stM = 0,
            long eP = 0, long eM = 0,
            int[]? histP = null, int[]? histM = null, int[]? histAll = null)
            => new(n, nP, nM, sumT, sumT2, sumT3, sAP, sAM, pP, pM, gP, gM, stP, stM, eP, eM,
                   histP, histM, histAll);

        /// <summary>构建对称灰度直方图（两半区各 count 个像素、灰度分别为 minus/plus）——亮度特征用。</summary>
        private static (int[] Plus, int[] Minus, int[] All) Histograms(
            byte plusGray, byte minusGray, int countPlus, int countMinus)
        {
            var hp = new int[256];
            var hm = new int[256];
            var ha = new int[256];
            hp[plusGray] = countPlus;
            hm[minusGray] = countMinus;
            ha[plusGray] = countPlus;
            ha[minusGray] = countMinus;
            return (hp, hm, ha);
        }

        [Fact]
        public void StrongPositiveCentroidOffset_Decides_NoFlip()
        {
            // μ=+20（半轴 50 → v=+0.4 出死区）；二阶矩 σ=10、三阶矩构造为零偏度，避免干扰级联
            var agg = Agg(100, 50, 50, sumT: 2000, sumT2: 50_000, sumT3: 1_400_000);
            var features = HeadTailFeaturePool.BuildFeatures(agg, LongAxis, Deadband, false, 1.0, 99.0, out _);
            var decision = HeadTailFeaturePool.BuildDecision(30.0, features);

            Assert.True(decision.Decisive);
            Assert.Equal("CentroidOffset", decision.SourceFeature);
            Assert.False(decision.Flipped);
            Assert.Equal(30.0, decision.Angle);
        }

        [Fact]
        public void StrongNegativeCentroidOffset_Flips_180()
        {
            var agg = Agg(100, 50, 50, sumT: -2000, sumT2: 50_000, sumT3: -1_400_000);
            var decision = HeadTailFeaturePool.BuildDecision(30.0,
                HeadTailFeaturePool.BuildFeatures(agg, LongAxis, Deadband, false, 1.0, 99.0, out _));

            Assert.True(decision.Decisive);
            Assert.Equal("CentroidOffset", decision.SourceFeature);
            Assert.True(decision.Flipped);
            Assert.Equal(210.0, decision.Angle, 6);
        }

        [Fact]
        public void WeakCentroid_SkipsToSkewness_NegativeSkewFlips()
        {
            // μ=0（质心特征死区）、宽度对称（锥度死区）、正偏度 5.0 出死区
            var agg = Agg(100, 50, 50, sumT: 0, sumT2: 10_000, sumT3: 500_000);
            var features = HeadTailFeaturePool.BuildFeatures(agg, LongAxis, Deadband, false, 1.0, 99.0, out _);
            var decision = HeadTailFeaturePool.BuildDecision(30.0, features);

            Assert.True(decision.Decisive);
            Assert.Equal("AxialSkew", decision.SourceFeature);
            Assert.False(decision.Flipped);
            Assert.Equal(30.0, decision.Angle, 6);
        }

        [Fact]
        public void AllFeaturesInDeadband_NotDecisive_KeepsFallback()
        {
            // 全部特征值远小于死区
            var agg = Agg(100, 50, 50, sumT: 1, sumT2: 3, sumT3: 0,
                sAP: 51, sAM: 50, pP: 10, pM: 10, gP: 101, gM: 100, stP: 11, stM: 10, eP: 1, eM: 1);
            var decision = HeadTailFeaturePool.BuildDecision(30.0,
                HeadTailFeaturePool.BuildFeatures(agg, LongAxis, Deadband, false, 1.0, 99.0, out _));

            Assert.False(decision.Decisive);
            Assert.Equal("None", decision.SourceFeature);
            Assert.Equal(30.0, decision.Angle);
        }

        [Fact]
        public void GradientEnergy_OutOfDeadband_Decides_AsFirstStructuralFeature()
        {
            // 质心/锥度/偏度全部死区，梯度能量相对差 = (200−100)/(200+100) ≈ 0.33 出死区
            var agg = Agg(100, 50, 50, sumT: 0, sumT2: 10_000, sumT3: 0,
                pP: 50, pM: 50, gP: 200, gM: 100, stP: 11, stM: 10, eP: 1, eM: 1);
            var features = HeadTailFeaturePool.BuildFeatures(agg, LongAxis, Deadband, false, 1.0, 99.0, out _);
            var decision = HeadTailFeaturePool.BuildDecision(30.0, features);

            Assert.True(decision.Decisive);
            Assert.Equal("GradientEnergyDiff", decision.SourceFeature);
        }

        // ───────── 亮度特征（2026-09-13 由灰度判向并入）─────────

        /// <summary>
        /// 亮度特征位于级联第 4 位（几何之后、梯度之前）：几何全死区时由亮度定头尾，
        /// 且必须优先于梯度/纹理——这是"亮度信噪比高于梯度/纹理"的排序意图。
        /// </summary>
        [Fact]
        public void Brightness_OutOfDeadband_DecidesBeforeGradient()
        {
            // 几何全死区；梯度/纹理也给出信号，但亮度差值更大 → 应由亮度胜出
            var (hp, hm, ha) = Histograms(plusGray: 200, minusGray: 50, countPlus: 50, countMinus: 50);
            var agg = Agg(100, 50, 50, sumT: 0, sumT2: 10_000, sumT3: 0,
                sAP: 50, sAM: 50, pP: 50, pM: 50, gP: 200, gM: 100, stP: 11, stM: 10, eP: 1, eM: 1,
                histP: hp, histM: hm, histAll: ha);

            var features = HeadTailFeaturePool.BuildFeatures(
                agg, LongAxis, Deadband,
                stretchEnabled: false, stretchLowPercentile: 1.0, stretchHighPercentile: 99.0,
                out var stats);
            var decision = HeadTailFeaturePool.BuildDecision(30.0, features);

            Assert.True(decision.Decisive);
            Assert.Equal("BrightnessDiff", decision.SourceFeature);

            // 正半区更亮（200 vs 50）→ diff=+150 > 0 → 符合"数值大的一侧是头部" → 不翻转
            Assert.False(decision.Flipped);
            Assert.Equal(30.0, decision.Angle);

            // 统计快照必须同时产出（供落库），且不变式 Diff = MeanPlus − MeanMinus 成立
            Assert.NotNull(stats);
            Assert.Equal(150.0, stats!.Value.Diff, 6);
            Assert.Equal(stats.Value.Diff, stats.Value.MeanPlus - stats.Value.MeanMinus, 6);
        }

        /// <summary>亮度较暗的一侧被判为尾 → 正半区更亮时不翻转；负半区更亮时翻转 180°。</summary>
        [Fact]
        public void Brightness_HeadOnMinusHalf_Flips180()
        {
            var (hp, hm, ha) = Histograms(plusGray: 50, minusGray: 200, countPlus: 50, countMinus: 50);
            var agg = Agg(100, 50, 50, sumT: 0, sumT2: 10_000, sumT3: 0,
                sAP: 50, sAM: 50, pP: 50, pM: 50, histP: hp, histM: hm, histAll: ha);

            var features = HeadTailFeaturePool.BuildFeatures(
                agg, LongAxis, Deadband, false, 1.0, 99.0, out var stats);
            var decision = HeadTailFeaturePool.BuildDecision(30.0, features);

            Assert.Equal("BrightnessDiff", decision.SourceFeature);
            Assert.True(decision.Flipped);
            Assert.Equal(210.0, decision.Angle, 6);
            Assert.NotNull(stats);
            Assert.Equal(-150.0, stats!.Value.Diff, 6);
        }

        /// <summary>
        /// 量纲换算验证：亮度用绝对灰度差，死区须换算到相对差量纲（× 2×255）。
        /// 差值 30 在相对差量纲下等价 30/(2×255)≈0.0588 &lt; 0.10，应判为死区（不可判）。
        /// </summary>
        [Fact]
        public void Brightness_UsesScaledDeadband_NotRawRelDiff()
        {
            var (hp, hm, ha) = Histograms(plusGray: 100, minusGray: 70, countPlus: 50, countMinus: 50);
            var agg = Agg(100, 50, 50, sumT: 0, sumT2: 10_000, sumT3: 0,
                sAP: 50, sAM: 50, pP: 50, pM: 50, histP: hp, histM: hm, histAll: ha);

            var features = HeadTailFeaturePool.BuildFeatures(
                agg, LongAxis, Deadband, false, 1.0, 99.0, out var stats);

            var brightness = Assert.Single(features, f => f.Name == "BrightnessDiff");
            Assert.Equal(30.0, brightness.SignedValue, 6);
            Assert.Equal(Deadband * 2.0 * 255.0, brightness.Deadband, 6);
            Assert.False(brightness.Decisive);   // 30 < 51（=0.10×2×255）
            Assert.NotNull(stats);               // 但统计仍产出（死区样本正是标定所需数据）
        }

        /// <summary>未提供直方图（brightnessEnabled=false）时不应产生亮度特征。</summary>
        [Fact]
        public void NoHistograms_NoBrightnessFeature()
        {
            var agg = Agg(100, 50, 50, sumT: 0, sumT2: 10_000, sumT3: 0, sAP: 50, sAM: 50);
            var features = HeadTailFeaturePool.BuildFeatures(agg, LongAxis, Deadband, false, 1.0, 99.0, out var stats);

            Assert.DoesNotContain(features, f => f.Name == "BrightnessDiff");
            Assert.Null(stats);
        }

        /// <summary>单侧无像素（极端掩码）时亮度特征缺失且无统计。</summary>
        [Fact]
        public void OneSidedHistogram_NoBrightnessFeature()
        {
            var (hp, hm, ha) = Histograms(plusGray: 200, minusGray: 0, countPlus: 10, countMinus: 0);
            var agg = Agg(10, 10, 0, sumT: 0, sumT2: 0, sumT3: 0,
                sAP: 50, sAM: 0, pP: 10, pM: 0, histP: hp, histM: hm, histAll: ha);

            var features = HeadTailFeaturePool.BuildFeatures(agg, LongAxis, Deadband, false, 1.0, 99.0, out var stats);

            Assert.DoesNotContain(features, f => f.Name == "BrightnessDiff");
            Assert.Null(stats);
        }

        /// <summary>
        /// 掩码内对比度拉伸在特征池内仍然生效：灰度落在 60/120（窗口跨度 60 ≥ 最小跨度）时
        /// 应被放大到 0/255，使差值从 60 提升到 255——即拉抶不在合并过程中丢失。
        /// </summary>
        [Fact]
        public void Brightness_ContrastStretchStillApplies()
        {
            var (hp, hm, ha) = Histograms(plusGray: 120, minusGray: 60, countPlus: 50, countMinus: 50);
            var agg = Agg(100, 50, 50, sumT: 0, sumT2: 10_000, sumT3: 0,
                sAP: 50, sAM: 50, pP: 50, pM: 50, histP: hp, histM: hm, histAll: ha);

            var features = HeadTailFeaturePool.BuildFeatures(
                agg, LongAxis, Deadband,
                stretchEnabled: true, stretchLowPercentile: 1.0, stretchHighPercentile: 99.0,
                out var stats);

            Assert.NotNull(stats);
            Assert.Equal(255.0, stats!.Value.MeanPlus, 1);
            Assert.Equal(0.0, stats.Value.MeanMinus, 1);
            Assert.Equal(255.0, stats.Value.Diff, 1);
            Assert.Equal(60.0, stats.Value.Low, 1);
            Assert.Equal(120.0, stats.Value.High, 1);

            var brightness = Assert.Single(features, f => f.Name == "BrightnessDiff");
            Assert.Equal(255.0, brightness.SignedValue, 1);
            Assert.True(brightness.Decisive);
        }

        /// <summary>拉伸窗口过窄（近单色掩码）时应跳过拉伸，避免把噪声放大成信号。</summary>
        [Fact]
        public void Brightness_StretchSkippedWhenWindowTooNarrow()
        {
            var (hp, hm, ha) = Histograms(plusGray: 104, minusGray: 100, countPlus: 50, countMinus: 50);
            var agg = Agg(100, 50, 50, sumT: 0, sumT2: 10_000, sumT3: 0,
                sAP: 50, sAM: 50, pP: 50, pM: 50, histP: hp, histM: hm, histAll: ha);

            var features = HeadTailFeaturePool.BuildFeatures(
                agg, LongAxis, Deadband, stretchEnabled: true, 1.0, 99.0, out var stats);

            Assert.NotNull(stats);
            Assert.True(double.IsNaN(stats!.Value.Low), "窗口过窄应跳过拉伸，Low 为 NaN");
            Assert.Equal(4.0, stats.Value.Diff, 1);   // 原始差值，未被放大
        }

        // ------------------------------------------------------------------
        // 弱信号让位（2026-09-13）：修「早到的弱者压制后到的强者」
        // ------------------------------------------------------------------

        /// <summary>直接构造特征表，精确控制 v/db/出死区，用于让位逻辑的单点验证。</summary>
        private static HeadTailFeatureSample[] Feats(params (string Name, double V, double Db)[] items)
            => items.Select(x => new HeadTailFeatureSample(
                   x.Name, x.V, x.Db, Math.Abs(x.V) >= x.Db)).ToArray();

        /// <summary>先到者勉强过线（conf 1.2 &lt; 1.5）、后面有同向强信号（conf 8）→ 强信号接管。</summary>
        [Fact]
        public void WeakFirst_StrongLaterSameSign_LaterTakesOver()
        {
            var features = Feats(
                ("CentroidOffset", 0.12, 0.10),   // conf = 1.2，勉强过线
                ("BrightnessDiff", 0.00, 51.0),   // 未出死区
                ("EdgeDensityDiff", 0.80, 0.10)); // conf = 8.0，强信号、同向

            var decision = HeadTailFeaturePool.BuildDecision(30.0, features);

            Assert.True(decision.Decisive);
            Assert.Equal("EdgeDensityDiff", decision.SourceFeature);
            Assert.False(decision.Flipped);          // v > 0 → 头在正向
            Assert.Equal(30.0, decision.Angle);
        }

        /// <summary>
        /// 接管者符号相反 → <b>不让位</b>（让位只确认、不翻案）。
        /// <para>理由：让位要修的是"弱者压制强者"（同一结论本该由更强证据支持），
        /// 而非"弱者判错方向"。允许异符号接管会让伪信号劫持裁决——实测全屏掩码下
        /// EdgeDensityDiff 的 ±1.0 极值（Canny 边界响应）conf 高达 10，会推翻正确的亮度判断。
        /// 方向性错误交给 headtail_audit 的一致率统计去发现。</para>
        /// </summary>
        [Fact]
        public void WeakFirst_StrongLaterOppositeSign_NoTakeOver()
        {
            var features = Feats(
                ("CentroidOffset", 0.12, 0.10),    // 弱，指向正向
                ("EdgeDensityDiff", -0.80, 0.10)); // 强但指向负向 → 不允许接管

            var decision = HeadTailFeaturePool.BuildDecision(30.0, features);

            Assert.Equal("CentroidOffset", decision.SourceFeature);
            Assert.False(decision.Flipped);
            Assert.Equal(30.0, decision.Angle);
        }

        /// <summary>先到者信号足够强（conf 4 ≥ 3）→ 维持级联语义，后面再强也不接管。</summary>
        [Fact]
        public void StrongFirst_LaterStronger_KeepsFirst()
        {
            var features = Feats(
                ("CentroidOffset", 0.40, 0.10),    // conf = 4.0，不勉强
                ("EdgeDensityDiff", 0.90, 0.10));  // conf = 9.0，更强

            var decision = HeadTailFeaturePool.BuildDecision(30.0, features);

            Assert.Equal("CentroidOffset", decision.SourceFeature);
        }

        /// <summary>先到者勉强、但后面只是中等强（conf 2.5 &lt; 3）→ 不让位（阈值留滞回）。</summary>
        [Fact]
        public void WeakFirst_MediumLater_NoTakeOver()
        {
            var features = Feats(
                ("CentroidOffset", 0.12, 0.10),    // conf = 1.2，勉强
                ("EdgeDensityDiff", 0.25, 0.10));  // conf = 2.5，不够格接管

            var decision = HeadTailFeaturePool.BuildDecision(30.0, features);

            Assert.Equal("CentroidOffset", decision.SourceFeature);
        }

        /// <summary>先到者勉强、后方有多个强信号 → 取置信度最大者（不是第一个够格的）。</summary>
        [Fact]
        public void WeakFirst_MultipleStrong_TakesHighestConfidence()
        {
            var features = Feats(
                ("CentroidOffset", 0.11, 0.10),    // conf = 1.1
                ("WidthTaper", 0.35, 0.10),        // conf = 3.5，够格
                ("EdgeDensityDiff", 0.90, 0.10));  // conf = 9.0，更强 → 应选它

            var decision = HeadTailFeaturePool.BuildDecision(30.0, features);

            Assert.Equal("EdgeDensityDiff", decision.SourceFeature);
        }

        /// <summary>接管者与先到者同向但符号为负 → 翻转方向由该符号决定（+180°）。</summary>
        [Fact]
        public void TakeOver_SameNegativeSign_Flips180()
        {
            var features = Feats(
                ("CentroidOffset", -0.12, 0.10),   // 弱，指向负向
                ("EdgeDensityDiff", -0.80, 0.10)); // 强且同向 → 接管

            var decision = HeadTailFeaturePool.BuildDecision(30.0, features);

            Assert.Equal("EdgeDensityDiff", decision.SourceFeature);
            Assert.True(decision.Flipped);
            Assert.Equal(210.0, decision.Angle);
        }

        /// <summary>全部落死区 → 不裁决（让位机制不改变此行为）。</summary>
        [Fact]
        public void AllDeadband_StillNotDecisive()
        {
            var features = Feats(
                ("CentroidOffset", 0.05, 0.10),
                ("EdgeDensityDiff", 0.02, 0.10));

            var decision = HeadTailFeaturePool.BuildDecision(30.0, features);

            Assert.False(decision.Decisive);
            Assert.Equal("None", decision.SourceFeature);
            Assert.Equal(30.0, decision.Angle);   // 维持回退角
        }

        /// <summary>先到者的 v 恰在弱门槛边界（conf = 1.5）→ 视为不勉强，不让位（边界含等号）。</summary>
        [Fact]
        public void ExactlyAtWeakThreshold_KeepsFirst()
        {
            var features = Feats(
                ("CentroidOffset", 0.15, 0.10),    // conf = 1.5，恰在边界
                ("EdgeDensityDiff", 0.90, 0.10));  // conf = 9.0

            var decision = HeadTailFeaturePool.BuildDecision(30.0, features);

            Assert.Equal("CentroidOffset", decision.SourceFeature);
        }

        /// <summary>接管者置信恰在强门槛边界（conf = 3.0）→ 允许接管（边界含等号）。</summary>
        [Fact]
        public void ExactlyAtStrongThreshold_TakesOver()
        {
            var features = Feats(
                ("CentroidOffset", 0.11, 0.10),    // conf = 1.1，弱
                ("EdgeDensityDiff", 0.30, 0.10));  // conf = 3.0，恰在边界 → 接管

            var decision = HeadTailFeaturePool.BuildDecision(30.0, features);

            Assert.Equal("EdgeDensityDiff", decision.SourceFeature);
        }

        /// <summary>死区为 0（不可比）→ 置信倍数为 0，不参与让位也不被接管。</summary>
        [Fact]
        public void ZeroDeadband_ConfidenceIsZero_SafeFallback()
        {
            var features = Feats(
                ("CentroidOffset", 0.5, 0.0),      // db=0 → 但 Decisive 由 |v|>=0 决定 = true，conf=0
                ("EdgeDensityDiff", 0.80, 0.10));  // conf = 8.0

            var decision = HeadTailFeaturePool.BuildDecision(30.0, features);

            // 先到者 conf=0 < 1.5 → 检查后方；后方 conf 8 ≥ 3 → 接管
            Assert.Equal("EdgeDensityDiff", decision.SourceFeature);
        }

        /// <summary>
        /// 回归保护：现有产品数据里几何特征通常给出 conf ≥ 3 的强信号，
        /// 让位机制不应改变这类帧的裁决结果（级联稳定性优先）。
        /// </summary>
        [Fact]
        public void StrongGeometrySignal_BehaviorUnchanged()
        {
            // 复刻 StrongPositiveCentroidOffset 的真实 BuildFeatures 输出：
            // v = 0.4、db = 0.10 → conf = 4.0 ≥ 3.0
            var agg = Agg(100, 50, 50, sumT: 2000, sumT2: 50_000, sumT3: 1_400_000);
            var features = HeadTailFeaturePool.BuildFeatures(
                agg, LongAxis, Deadband, false, 1.0, 99.0, out _);

            var decision = HeadTailFeaturePool.BuildDecision(30.0, features);

            Assert.Equal("CentroidOffset", decision.SourceFeature);
            Assert.False(decision.Flipped);
        }
    }
}
