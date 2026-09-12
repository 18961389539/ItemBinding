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
            long eP = 0, long eM = 0)
            => new(n, nP, nM, sumT, sumT2, sumT3, sAP, sAM, pP, pM, gP, gM, stP, stM, eP, eM);

        [Fact]
        public void StrongPositiveCentroidOffset_Decides_NoFlip()
        {
            // μ=+20（半轴 50 → v=+0.4 出死区）；二阶矩 σ=10、三阶矩构造为零偏度，避免干扰级联
            var agg = Agg(100, 50, 50, sumT: 2000, sumT2: 50_000, sumT3: 1_400_000);
            var features = HeadTailFeaturePool.BuildFeatures(agg, LongAxis, Deadband);
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
                HeadTailFeaturePool.BuildFeatures(agg, LongAxis, Deadband));

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
            var features = HeadTailFeaturePool.BuildFeatures(agg, LongAxis, Deadband);
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
                HeadTailFeaturePool.BuildFeatures(agg, LongAxis, Deadband));

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
            var features = HeadTailFeaturePool.BuildFeatures(agg, LongAxis, Deadband);
            var decision = HeadTailFeaturePool.BuildDecision(30.0, features);

            Assert.True(decision.Decisive);
            Assert.Equal("GradientEnergyDiff", decision.SourceFeature);
        }
    }
}
