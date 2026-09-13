using System.Collections.Generic;
using System.Linq;
using MainAPP.Application;
using Xunit;

namespace MainAPP.Tests.Application
{
    /// <summary>
    /// 特征池自检统计测试（2026-09-13）。
    /// <para>锁住三个容易写错的口径：① 一致率的分母是"该特征出死区且有真值"的帧数，
    /// 未出死区的帧不参与（噪声符号不该被判错）；② 真值缺失时一致率为 NaN 而非 0；
    /// ③ 出死区率与裁决占比是两个不同分母的指标，不能混。</para>
    /// </summary>
    public class HeadTailFeatureAuditTests
    {
        /// <summary>构造一帧轨迹 JSON：src = 裁决者，features = (名称, 值, 是否出死区)。</summary>
        private static string Trace(string src, params (string Name, double Value, bool Decisive)[] features)
        {
            var f = string.Join(",", features.Select(x =>
                $"{{\"n\":\"{x.Name}\",\"v\":{x.Value.ToString(System.Globalization.CultureInfo.InvariantCulture)},\"db\":0.1,\"dec\":{(x.Decisive ? "true" : "false")}}}"));
            return $"{{\"src\":\"{src}\",\"dec\":{(src == "None" ? "false" : "true")},\"f\":[{f}]}}";
        }

        [Fact]
        public void AllDecisive_AgreesWithTruth_AgreeRateIsOne()
        {
            var traces = new List<string?>
            {
                Trace("CentroidOffset", ("CentroidOffset", 0.5, true), ("WidthTaper", 0.3, true)),
                Trace("CentroidOffset", ("CentroidOffset", 0.4, true), ("WidthTaper", 0.2, true)),
                Trace("CentroidOffset", ("CentroidOffset", 0.6, true), ("WidthTaper", 0.1, false)),
            };
            var truths = new List<bool?> { true, true, true };

            var audit = HeadTailFeatureAudit.Build(traces, truths);

            var centroid = audit.Stats.Single(s => s.Name == "CentroidOffset");
            Assert.Equal(3, centroid.TotalSamples);
            Assert.Equal(3, centroid.DecisiveCount);
            Assert.Equal(3, centroid.AdjudicatedCount);
            Assert.Equal(3, centroid.TruthComparedCount);
            Assert.Equal(3, centroid.AgreeCount);
            Assert.Equal(0, centroid.DisagreeCount);
            Assert.Equal(1.0, centroid.AgreeRate, 6);
        }

        [Fact]
        public void FeatureNeverDecisive_IsExcludedFromAgreeRateDenominator()
        {
            // WidthTaper 每帧都落死区 → 不该参与一致率（哪怕它的符号"看起来"和真值不同）
            var traces = new List<string?>
            {
                Trace("CentroidOffset", ("CentroidOffset", 0.5, true), ("WidthTaper", -0.9, false)),
                Trace("CentroidOffset", ("CentroidOffset", 0.5, true), ("WidthTaper", -0.9, false)),
            };
            var truths = new List<bool?> { true, true };

            var audit = HeadTailFeatureAudit.Build(traces, truths);

            var taper = audit.Stats.Single(s => s.Name == "WidthTaper");
            Assert.Equal(2, taper.TotalSamples);
            Assert.Equal(0, taper.DecisiveCount);
            Assert.Equal(0, taper.TruthComparedCount);
            Assert.Equal(0, taper.AgreeCount);
            Assert.Equal(0, taper.DisagreeCount);
            Assert.True(double.IsNaN(taper.AgreeRate), "无真值样本时必须返回 NaN，不能返回 0");
            Assert.Contains("从未出死区", taper.Diagnosis());
        }

        [Fact]
        public void NoTruth_AgreeRateIsNaN_NotZero()
        {
            var traces = new List<string?>
            {
                Trace("CentroidOffset", ("CentroidOffset", 0.5, true)),
            };
            var truths = new List<bool?> { null };

            var audit = HeadTailFeatureAudit.Build(traces, truths);

            var s = audit.Stats.Single();
            Assert.Equal(1, s.DecisiveCount);
            Assert.Equal(0, s.TruthComparedCount);
            Assert.True(double.IsNaN(s.AgreeRate));
            Assert.Equal(0, audit.FramesWithTruth);
            Assert.Equal(1, audit.TotalFrames);
        }

        [Fact]
        public void SignDisagreement_CountedAsDisagree()
        {
            // 第 1 帧：v > 0（头判在正向）但真值 false（头在负向）→ 判错
            // 第 2 帧：v < 0（头判在负向）且真值 false → 判对
            var traces = new List<string?>
            {
                Trace("CentroidOffset", ("CentroidOffset", 0.5, true)),
                Trace("CentroidOffset", ("CentroidOffset", -0.5, true)),
            };
            var truths = new List<bool?> { false, false };

            var audit = HeadTailFeatureAudit.Build(traces, truths);

            var s = audit.Stats.Single();
            Assert.Equal(1, s.AgreeCount);
            Assert.Equal(1, s.DisagreeCount);
            Assert.Equal(0.5, s.AgreeRate, 6);
            // 2 条样本不足以对一致率下结论（低于 MinTruthSamples）→ 走"待积累"而非"判别力可疑"
            Assert.Contains("待积累", s.Diagnosis());
        }

        [Fact]
        public void AllSignsOppositeTruth_AgreeRateIsZero()
        {
            // 符号系统性反向 → 一致率 0。这是最值得警惕的情形（"大=头"约定可能反了），
            // 需在样本量不足时也立即示警，不能被"待积累"压下去。
            var traces = new List<string?>
            {
                Trace("CentroidOffset", ("CentroidOffset", 0.5, true)),
                Trace("CentroidOffset", ("CentroidOffset", 0.6, true)),
            };
            var truths = new List<bool?> { false, false };

            var audit = HeadTailFeatureAudit.Build(traces, truths);

            var s = audit.Stats.Single();
            Assert.Equal(0, s.AgreeCount);
            Assert.Equal(2, s.DisagreeCount);
            Assert.Equal(0.0, s.AgreeRate, 6);
            Assert.Contains("符号系统性反向", s.Diagnosis());
        }

        [Fact]
        public void DecisiveButNeverAdjudicated_DistinguishedFromNoDecisive()
        {
            // CentroidOffset 每帧出死区，但 WidthTaper 排在前面且也出死区 → 它永远是裁决者；
            // CentroidOffset 的裁决占比应为 0，但出死区率应为 100%。两者必须区分。
            var traces = new List<string?>
            {
                Trace("WidthTaper", ("CentroidOffset", 0.5, true), ("WidthTaper", 0.6, true)),
                Trace("WidthTaper", ("CentroidOffset", 0.4, true), ("WidthTaper", 0.7, true)),
            };
            var truths = new List<bool?> { true, true };

            var audit = HeadTailFeatureAudit.Build(traces, truths);

            var centroid = audit.Stats.Single(s => s.Name == "CentroidOffset");
            Assert.Equal(1.0, centroid.DecisiveRate, 6);
            Assert.Equal(0, centroid.AdjudicatedCount);
            Assert.Equal(0.0, centroid.AdjudicatedRate, 6);
            Assert.Contains("被更高优先级特征压制", centroid.Diagnosis());

            var taper = audit.Stats.Single(s => s.Name == "WidthTaper");
            Assert.Equal(2, taper.AdjudicatedCount);
            Assert.Equal(1.0, taper.AdjudicatedRate, 6);
        }

        [Fact]
        public void AllDeadbandFrames_Counted()
        {
            var traces = new List<string?>
            {
                Trace("None", ("CentroidOffset", 0.05, false), ("WidthTaper", 0.02, false)),
                Trace("None", ("CentroidOffset", 0.01, false), ("WidthTaper", 0.03, false)),
                Trace("CentroidOffset", ("CentroidOffset", 0.5, true), ("WidthTaper", 0.02, false)),
            };
            var truths = new List<bool?> { true, true, true };

            var audit = HeadTailFeatureAudit.Build(traces, truths);

            Assert.Equal(3, audit.TotalFrames);
            Assert.Equal(2, audit.AllDeadbandFrames);
            Assert.Equal(2.0 / 3.0, audit.AllDeadbandRate, 6);
            // 全死区帧的特征样本仍要计入 total/decisive（它们是"无区分力"的证据）
            var centroid = audit.Stats.Single(s => s.Name == "CentroidOffset");
            Assert.Equal(3, centroid.TotalSamples);
            Assert.Equal(1, centroid.DecisiveCount);
        }

        [Fact]
        public void NullAndEmptyTraces_SkippedNotCounted()
        {
            var traces = new List<string?> { null, "", "   ", Trace("CentroidOffset", ("CentroidOffset", 0.5, true)) };
            var truths = new List<bool?> { null, null, null, true };

            var audit = HeadTailFeatureAudit.Build(traces, truths);

            Assert.Equal(1, audit.TotalFrames);
            Assert.Equal(0, audit.MalformedFrames);
        }

        [Fact]
        public void MalformedJson_CountedNotThrown()
        {
            var traces = new List<string?> { "{not json", Trace("CentroidOffset", ("CentroidOffset", 0.5, true)) };
            var truths = new List<bool?> { true, true };

            var audit = HeadTailFeatureAudit.Build(traces, truths);

            Assert.Equal(1, audit.MalformedFrames);
            Assert.Equal(1, audit.TotalFrames);
        }

        [Fact]
        public void MismatchedLengths_TruncatedToShorter()
        {
            var traces = new List<string?> { Trace("CentroidOffset", ("CentroidOffset", 0.5, true)) };
            var truths = new List<bool?> { true, true, true };

            var audit = HeadTailFeatureAudit.Build(traces, truths);

            Assert.Equal(1, audit.TotalFrames);
            Assert.Equal(1, audit.FramesWithTruth);
        }

        [Fact]
        public void FeatureOrder_PreservedAsFirstSeen()
        {
            var traces = new List<string?>
            {
                Trace("AxialSkew",
                    ("CentroidOffset", 0.5, true),
                    ("AxialSkew", 0.35, true),
                    ("GradientEnergyDiff", 0.2, false)),
            };
            var truths = new List<bool?> { true };

            var audit = HeadTailFeatureAudit.Build(traces, truths);

            Assert.Equal(
                new[] { "CentroidOffset", "AxialSkew", "GradientEnergyDiff" },
                audit.Stats.Select(s => s.Name).ToArray());
        }

        [Fact]
        public void LowTruthSamples_DiagnosisFlagsPending()
        {
            // 裁决多次但真值样本不足 → 不能给出一致率结论
            var traces = new List<string?>();
            var truths = new List<bool?>();
            for (int i = 0; i < HeadTailFeatureAudit.MinTruthSamples - 1; i++)
            {
                traces.Add(Trace("CentroidOffset", ("CentroidOffset", 0.5, true)));
                truths.Add(true);
            }

            var audit = HeadTailFeatureAudit.Build(traces, truths);

            var s = audit.Stats.Single();
            Assert.False(s.HasEnoughTruth);
            Assert.Contains("待积累", s.Diagnosis());
        }

        [Fact]
        public void HighAgreeRate_DiagnosisCallsItMainFeature()
        {
            var traces = new List<string?>();
            var truths = new List<bool?>();
            for (int i = 0; i < HeadTailFeatureAudit.MinTruthSamples; i++)
            {
                traces.Add(Trace("CentroidOffset", ("CentroidOffset", 0.5, true)));
                truths.Add(true);
            }

            var audit = HeadTailFeatureAudit.Build(traces, truths);

            var s = audit.Stats.Single();
            Assert.True(s.HasEnoughTruth);
            Assert.Equal(1.0, s.AgreeRate, 6);
            Assert.Contains("主力特征", s.Diagnosis());
        }

        /// <summary>
        /// 契约测试：统计器直接消费 <see cref="HeadTailFeaturePool.SerializeTrace"/> 的真实输出。
        /// <para>这是跨模块的隐式契约（JSON 字段名 n/v/dec/src），改任一侧字段名都会静默失效——
        /// 用一个真实序列化的轨迹往返验证，避免只靠手工拼 JSON 的测试漏掉格式漂移。</para>
        /// </summary>
        [Fact]
        public void RealSerializeTrace_RoundTripsThroughAudit()
        {
            var decision = new HeadTailPoolDecision(
                Angle: 45.0,
                Flipped: true,
                SourceFeature: "BrightnessDiff",
                Decisive: true,
                Features: new[]
                {
                    new HeadTailFeatureSample("CentroidOffset", -0.10, 0.10, true),
                    new HeadTailFeatureSample("BrightnessDiff", -62.3, 51.0, true),
                    new HeadTailFeatureSample("EdgeDensityDiff", 0.02, 0.10, false),
                });

            var json = HeadTailFeaturePool.SerializeTrace(decision);
            Assert.NotNull(json);

            // 真值 false = 头在负半区；BrightnessDiff 值为 −62.3（负）→ 一致
            var audit = HeadTailFeatureAudit.Build(new[] { json }, new bool?[] { false });

            Assert.Equal(1, audit.TotalFrames);
            Assert.Equal(1, audit.FramesWithTruth);
            Assert.Equal(0, audit.AllDeadbandFrames);
            Assert.Equal(0, audit.MalformedFrames);

            var names = audit.Stats.Select(s => s.Name).ToArray();
            Assert.Equal(new[] { "CentroidOffset", "BrightnessDiff", "EdgeDensityDiff" }, names);

            var brightness = audit.Stats.Single(s => s.Name == "BrightnessDiff");
            Assert.Equal(1, brightness.AdjudicatedCount);
            Assert.Equal(1, brightness.AgreeCount);
            Assert.Equal(0, brightness.DisagreeCount);

            // CentroidOffset 也出了死区（|−0.10| ≥ 0.10）且同符号 → 也计入一致
            var centroid = audit.Stats.Single(s => s.Name == "CentroidOffset");
            Assert.Equal(1, centroid.DecisiveCount);
            Assert.Equal(0, centroid.AdjudicatedCount);

            // EdgeDensityDiff 落死区 → 不参与一致率（分母为 0）
            var edge = audit.Stats.Single(s => s.Name == "EdgeDensityDiff");
            Assert.Equal(0, edge.TruthComparedCount);
            Assert.True(double.IsNaN(edge.AgreeRate));
        }

        // ─────────────────────── 弱信号让位统计（2026-09-13）───────────────────────
        // 判据：src ≠ 轨迹中首个出死区特征 ⇒ 该帧发生了接管（takeOverFrames++），
        // 接管者计 takeOver，被接管者计 yielded。全死区帧不算（没有让位语义）。

        [Fact]
        public void SourceIsFirstDecisive_NoTakeOver()
        {
            var traces = new List<string?>
            {
                Trace("CentroidOffset", ("CentroidOffset", 0.5, true), ("BrightnessDiff", 3.0, true)),
            };
            var truths = new List<bool?> { null };

            var audit = HeadTailFeatureAudit.Build(traces, truths);

            Assert.Equal(0, audit.TakeOverFrames);
            Assert.Equal(0, audit.TakeOverRate, 6);
            Assert.Equal(0, audit.Stats.Single(s => s.Name == "CentroidOffset").TakeOverCount);
            Assert.Equal(0, audit.Stats.Single(s => s.Name == "CentroidOffset").YieldedCount);
            Assert.Equal(0, audit.Stats.Single(s => s.Name == "BrightnessDiff").TakeOverCount);
        }

        [Fact]
        public void SourceIsLaterDecisive_CountedAsTakeOver()
        {
            // 首个出死区是 CentroidOffset（弱），最终裁决落在 BrightnessDiff（强）→ 一次接管
            var traces = new List<string?>
            {
                Trace("BrightnessDiff", ("CentroidOffset", 0.12, true), ("BrightnessDiff", 3.0, true)),
            };
            var truths = new List<bool?> { null };

            var audit = HeadTailFeatureAudit.Build(traces, truths);

            Assert.Equal(1, audit.TakeOverFrames);
            Assert.Equal(1.0, audit.TakeOverRate, 6);

            var weak = audit.Stats.Single(s => s.Name == "CentroidOffset");
            Assert.Equal(0, weak.TakeOverCount);
            Assert.Equal(1, weak.YieldedCount);   // 它让位了一次
            Assert.Equal(-1, weak.TakeOverNet);
            Assert.Equal(0, weak.AdjudicatedCount); // 让位后它不再裁决

            var strong = audit.Stats.Single(s => s.Name == "BrightnessDiff");
            Assert.Equal(1, strong.TakeOverCount);
            Assert.Equal(0, strong.YieldedCount);
            Assert.Equal(1, strong.TakeOverNet);
            Assert.Equal(1, strong.AdjudicatedCount);
        }

        [Fact]
        public void AllDeadband_NoTakeOverEvenIfSourceNotFirst()
        {
            // 全死区帧 src 为 None → 没有让位语义，所有让位计数保持 0
            var traces = new List<string?>
            {
                Trace("None", ("CentroidOffset", 0.01, false), ("BrightnessDiff", 0.02, false)),
            };
            var truths = new List<bool?> { null };

            var audit = HeadTailFeatureAudit.Build(traces, truths);

            Assert.Equal(1, audit.AllDeadbandFrames);
            Assert.Equal(0, audit.TakeOverFrames);
            Assert.All(audit.Stats, s =>
            {
                Assert.Equal(0, s.TakeOverCount);
                Assert.Equal(0, s.YieldedCount);
            });
        }

        [Fact]
        public void MultipleFrames_TakeOverAccumulates()
        {
            var traces = new List<string?>
            {
                Trace("BrightnessDiff", ("CentroidOffset", 0.12, true), ("BrightnessDiff", 3.0, true)),
                Trace("CentroidOffset", ("CentroidOffset", 0.50, true), ("BrightnessDiff", 0.30, false)),
                Trace("BrightnessDiff", ("CentroidOffset", 0.11, true), ("BrightnessDiff", -2.5, true)),
            };
            var truths = new List<bool?> { null, null, null };

            var audit = HeadTailFeatureAudit.Build(traces, truths);

            Assert.Equal(3, audit.TotalFrames);
            Assert.Equal(2, audit.TakeOverFrames);
            Assert.Equal(2.0 / 3.0, audit.TakeOverRate, 6);

            var centroid = audit.Stats.Single(s => s.Name == "CentroidOffset");
            Assert.Equal(1, centroid.AdjudicatedCount); // 只有第 2 帧由它裁决
            Assert.Equal(2, centroid.YieldedCount);      // 第 1、3 帧都让位给 BrightnessDiff
            Assert.Equal(0, centroid.TakeOverCount);
            Assert.Equal(-2, centroid.TakeOverNet);

            var brightness = audit.Stats.Single(s => s.Name == "BrightnessDiff");
            Assert.Equal(2, brightness.AdjudicatedCount); // 第 1、3 帧接管
            Assert.Equal(2, brightness.TakeOverCount);
            Assert.Equal(0, brightness.YieldedCount);
            Assert.Equal(2, brightness.TakeOverNet);
        }

        [Fact]
        public void SourceUnknown_NotInTrace_StillCountsTakeOver()
        {
            // src 指向一个不在特征表里的名字（理论上不该发生）→ 仍按"非首个出死区"计一次接管，
            // 不能让统计口径因脏数据而静默漏计。
            var traces = new List<string?>
            {
                Trace("GhostFeature", ("CentroidOffset", 0.5, true)),
            };
            var truths = new List<bool?> { null };

            var audit = HeadTailFeatureAudit.Build(traces, truths);

            Assert.Equal(1, audit.TakeOverFrames);
            Assert.Equal(1, audit.Stats.Single(s => s.Name == "CentroidOffset").YieldedCount);
        }

        [Fact]
        public void TakeOverMetric_DoesNotAffectAgreeRate()
        {
            // 让位统计是纯"谁裁决"的口径，不得污染一致率分母
            var traces = new List<string?>
            {
                Trace("BrightnessDiff", ("CentroidOffset", 0.12, true), ("BrightnessDiff", 3.0, true)),
                Trace("BrightnessDiff", ("CentroidOffset", 0.14, true), ("BrightnessDiff", 2.0, true)),
            };
            var truths = new List<bool?> { true, true };

            var audit = HeadTailFeatureAudit.Build(traces, truths);

            var centroid = audit.Stats.Single(s => s.Name == "CentroidOffset");
            Assert.Equal(2, centroid.DecisiveCount);
            Assert.Equal(2, centroid.TruthComparedCount);
            Assert.Equal(2, centroid.AgreeCount);   // 一致率只看它自己的符号，与是否让位无关

            var brightness = audit.Stats.Single(s => s.Name == "BrightnessDiff");
            Assert.Equal(2, brightness.TruthComparedCount);
            Assert.Equal(2, brightness.AgreeCount);
        }
    }
}
