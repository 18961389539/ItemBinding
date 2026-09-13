using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json;

namespace MainAPP.Application
{
    /// <summary>
    /// 单个特征的自检统计（2026-09-13）。
    /// <para><b>回答的问题</b>：这个特征对当前产品的头尾区分到底有没有用、判得对不对。</para>
    /// <para><b>为什么需要三个指标而非一个</b>：出死区率单独看会产生严重误判——
    /// "常出死区但每次都判对"与"常出死区但一半判错"出死区率完全相同，但价值天差地别。
    /// 只有把 QR 一致率并排看，才能分辨这两者。</para>
    /// </summary>
    /// <param name="Name">特征名（CentroidOffset / BrightnessDiff / ...）。</param>
    /// <param name="TotalSamples">该特征出现过的帧数（分母）。</param>
    /// <param name="DecisiveCount">该特征出死区（|v| ≥ db）的帧数——注意"出死区"是特征被级联采用的必要条件，
    /// 但只有排在前面特征都落死区时才会真正裁决，故本值 ≥ <paramref name="AdjudicatedCount"/>。</param>
    /// <param name="AdjudicatedCount">该特征真正成为裁决者（SourceFeature == Name）的帧数。</param>
    /// <param name="TruthComparedCount">该特征可与 QR 真值比对的帧数（该帧有真值且有该特征）。
    /// 它是 <paramref name="AgreeCount"/> / <paramref name="DisagreeCount"/> 的分母，也是判断一致率可信度的权重。</param>
    /// <param name="AgreeCount">符号与 QR 真值一致的帧数（判对）。</param>
    /// <param name="DisagreeCount">符号与 QR 真值相反的帧数（判错）。</param>
    /// <param name="TakeOverCount">该特征"抢走"裁决权的帧数（弱信号让位机制生效，2026-09-13）。
    /// 定义：该特征在级联中排在首个出死区特征之后，却因其置信更强而接管了裁决。
    /// 该值 &gt; 0 说明"让位"机制在为这类产品纠正优先级先验；长期为 0 说明机制未触发（可能是好事）。</param>
    /// <param name="YieldedCount">该特征"让位"给后出死区特征的帧数——仅在它是首个出死区特征、
    /// 且最终裁决者不是它时计入。这是 <paramref name="TakeOverCount"/> 的对偶面。</param>
    public sealed record HeadTailFeatureStat(
        string Name,
        int TotalSamples,
        int DecisiveCount,
        int AdjudicatedCount,
        int TruthComparedCount,
        int AgreeCount,
        int DisagreeCount,
        int TakeOverCount = 0,
        int YieldedCount = 0)
    {
        /// <summary>出死区率 = 出死区帧数 / 出现帧数。低 → 该特征对本产品几乎无区分力。</summary>
        public double DecisiveRate => TotalSamples > 0 ? (double)DecisiveCount / TotalSamples : 0;

        /// <summary>裁决占比 = 真正裁决帧数 / 出现帧数。直接回答"谁在实际干活"。</summary>
        public double AdjudicatedRate => TotalSamples > 0 ? (double)AdjudicatedCount / TotalSamples : 0;

        /// <summary>
        /// QR 一致率 = 判对帧数 / 可比对帧数。<b>这是三个指标里唯一衡量"对不对"的</b>。
        /// 无真值样本时返回 NaN（而非 0）——避免"没有数据"被误读成"全判错"。
        /// </summary>
        public double AgreeRate => TruthComparedCount > 0 ? (double)AgreeCount / TruthComparedCount : double.NaN;

        /// <summary>是否已有足够真值样本支撑一致率结论（低于此阈值时一致率仅供参考）。</summary>
        public bool HasEnoughTruth => TruthComparedCount >= HeadTailFeatureAudit.MinTruthSamples;

        /// <summary>
        /// 让位净收益 = 抢来裁决的次数 − 让出去的次数。正 → 该特征常作为"更强信号"接管；
        /// 负 → 该特征常作为"弱信号"被接管。绝对值为 0 表示它既不强也不弱。
        /// </summary>
        public int TakeOverNet => TakeOverCount - YieldedCount;

        /// <summary>
        /// 综合诊断：把三个指标压成一句人话，供 UI/AI 助手直接展示。
        /// </summary>
        public string Diagnosis()
        {
            if (TotalSamples == 0)
            {
                return "未出现";
            }

            if (AdjudicatedCount == 0)
            {
                return DecisiveCount == 0
                    ? $"从未出死区（出死区率 0%）——对本产品无区分力，可考虑删除"
                    : $"出死区 {DecisiveRate:P0} 但从未裁决——被更高优先级特征压制";
            }

            // 系统性反向优先于样本量检查：一致率为 0 意味着符号约定可能整体反了，
            // 这种错误哪怕只有几条样本也必须立刻示警，不能以"样本不足"为由压下去。
            if (TruthComparedCount > 0 && AgreeCount == 0)
            {
                return $"⚠ 符号系统性反向（{DisagreeCount} 条样本全部与真值相反）——" +
                       $"「大=头」约定或特征定义可能有误，需立即检查";
            }

            if (!HasEnoughTruth)
            {
                return $"裁决 {AdjudicatedCount} 次，真值样本仅 {TruthComparedCount} 条 —— 一致率待积累";
            }

            var rate = AgreeRate;
            return rate >= 0.95
                ? $"主力特征（裁决 {AdjudicatedRate:P0}，一致率 {rate:P1}）"
                : rate >= 0.80
                    ? $"可用但有噪声（裁决 {AdjudicatedRate:P0}，一致率 {rate:P1}）"
                    : $"⚠ 判别力可疑（裁决 {AdjudicatedRate:P0}，一致率仅 {rate:P1}）——需检查前提是否成立";
        }
    }

    /// <summary>
    /// 特征池自检报告（2026-09-13）：把落库的 HeadFeatures 轨迹 + HeadTruthPositive 真值
    /// 汇总成"哪个特征对区分头尾有决定性作用"的实测答案。
    /// <para><b>数据来源</b>：<c>DbModel.HeadFeatures</c>（每帧 JSON：来源特征 + 各特征 v/db/dec）
    /// 与 <c>DbModel.HeadTruthPositive</c>（QR 真值符号）。两者同约定（"数值大 = 头在 +u 侧"），
    /// 故一致率计算即符号相等比较。</para>
    /// </summary>
    public sealed class HeadTailFeatureAudit
    {
        /// <summary>一致率结论所需的最少真值样本数（低于此值标注"待积累"）。</summary>
        public const int MinTruthSamples = 30;

        /// <summary>逐特征统计（按级联优先级顺序，即 HeadTailFeaturePool 的固定顺序）。</summary>
        public IReadOnlyList<HeadTailFeatureStat> Stats { get; }

        /// <summary>参与统计的总帧数（HeadFeatures 非 null 的行数）。</summary>
        public int TotalFrames { get; }

        /// <summary>其中带 QR 真值的帧数（HeadTruthPositive 非 null）。</summary>
        public int FramesWithTruth { get; }

        /// <summary>级联全死区（SourceFeature == "None"）的帧数——越高说明特征整体越不适用。</summary>
        public int AllDeadbandFrames { get; }

        /// <summary>JSON 解析失败被跳过的帧数（数据损伤自检；正常应为 0）。</summary>
        public int MalformedFrames { get; }

        /// <summary>弱信号让位机制（2026-09-13）生效的帧数——即最终裁决者不是首个出死区特征。
        /// 用于回答"新机制到底改变了多少帧的裁决"，是评估该机制实际影响力的直接口径。</summary>
        public int TakeOverFrames { get; }

        private HeadTailFeatureAudit(
            IReadOnlyList<HeadTailFeatureStat> stats,
            int totalFrames, int framesWithTruth, int allDeadbandFrames, int malformedFrames,
            int takeOverFrames)
        {
            Stats = stats;
            TotalFrames = totalFrames;
            FramesWithTruth = framesWithTruth;
            AllDeadbandFrames = allDeadbandFrames;
            MalformedFrames = malformedFrames;
            TakeOverFrames = takeOverFrames;
        }

        /// <summary>级联全死区率 = 无人裁决的帧占比。</summary>
        public double AllDeadbandRate => TotalFrames > 0 ? (double)AllDeadbandFrames / TotalFrames : 0;

        /// <summary>让位机制触发率 = 被接管的帧占比。用于判断该机制是"常态修正"还是"偶发兜底"。</summary>
        public double TakeOverRate => TotalFrames > 0 ? (double)TakeOverFrames / TotalFrames : 0;

        /// <summary>
        /// 汇总统计（纯函数，internal 供单测）。
        /// </summary>
        /// <param name="traces">每帧的 HeadFeatures JSON（null/空串 = 特征池未运行，跳过）。</param>
        /// <param name="truths">与 <paramref name="traces"/> 逐帧对齐的真值符号
        /// （null = 该帧无真值）。长度不一致时按较短者截断。</param>
        public static HeadTailFeatureAudit Build(IReadOnlyList<string?> traces, IReadOnlyList<bool?> truths)
        {
            // 特征表的列集合与优先级顺序，取自首次出现的顺序（与 BuildFeatures 的插入顺序一致）
            var order = new List<string>();
            var decisive = new Dictionary<string, int>();
            var adjudicated = new Dictionary<string, int>();
            var total = new Dictionary<string, int>();
            var compared = new Dictionary<string, int>();
            var agree = new Dictionary<string, int>();
            var disagree = new Dictionary<string, int>();
            var takeOver = new Dictionary<string, int>();
            var yielded = new Dictionary<string, int>();

            int totalFrames = 0, framesWithTruth = 0, allDeadband = 0, malformed = 0, takeOverFrames = 0;
            int n = Math.Min(traces.Count, truths.Count);

            for (int i = 0; i < n; i++)
            {
                var json = traces[i];
                if (string.IsNullOrWhiteSpace(json))
                {
                    continue; // 特征池未运行
                }

                List<TraceFeature>? features;
                string? source;
                try
                {
                    using var doc = JsonDocument.Parse(json);
                    var root = doc.RootElement;
                    source = root.TryGetProperty("src", out var srcEl) ? srcEl.GetString() : null;
                    features = new List<TraceFeature>();
                    if (root.TryGetProperty("f", out var arr) && arr.ValueKind == JsonValueKind.Array)
                    {
                        foreach (var item in arr.EnumerateArray())
                        {
                            double v = item.TryGetProperty("v", out var vEl) ? vEl.GetDouble() : 0.0;
                            features.Add(new TraceFeature(
                                item.GetProperty("n").GetString() ?? string.Empty,
                                item.TryGetProperty("dec", out var decEl) && decEl.ValueKind == JsonValueKind.True,
                                v > 0));
                        }
                    }
                }
                catch (JsonException)
                {
                    // 解析失败不计入 totalFrames——它是数据损伤，不是"特征池跑了但没结果"
                    malformed++;
                    continue;
                }

                totalFrames++;

                if (string.Equals(source, "None", StringComparison.Ordinal))
                {
                    allDeadband++;
                }

                var truth = truths[i];
                if (truth.HasValue)
                {
                    framesWithTruth++;
                }

                foreach (var f in features)
                {
                    if (!total.ContainsKey(f.Name))
                    {
                        order.Add(f.Name);
                    }

                    total[f.Name] = total.GetValueOrDefault(f.Name) + 1;
                    if (f.Decisive)
                    {
                        decisive[f.Name] = decisive.GetValueOrDefault(f.Name) + 1;

                        // 一致率只在"该特征自身出死区"的帧上算——未出死区的特征符号无意义
                        // （噪声决定的符号不该被判为"判错"）。与"该特征能否定头尾"的语义一致。
                        if (truth.HasValue)
                        {
                            compared[f.Name] = compared.GetValueOrDefault(f.Name) + 1;
                            if (f.ValuePositive == truth.Value)
                            {
                                agree[f.Name] = agree.GetValueOrDefault(f.Name) + 1;
                            }
                            else
                            {
                                disagree[f.Name] = disagree.GetValueOrDefault(f.Name) + 1;
                            }
                        }
                    }
                }

                if (string.Equals(source, "None", StringComparison.Ordinal))
                {
                    continue;
                }

                adjudicated[source!] = adjudicated.GetValueOrDefault(source!) + 1;

                // 让位统计：首个出死区特征 ≠ 最终裁决者 ⇒ 该帧发生了接管。
                // 只在"有人裁决"的帧上判——全死区帧没有让位语义。
                var firstDecisive = features.FirstOrDefault(f => f.Decisive);
                if (firstDecisive.Name is not null &&
                    !string.Equals(firstDecisive.Name, source, StringComparison.Ordinal))
                {
                    takeOverFrames++;
                    takeOver[source!] = takeOver.GetValueOrDefault(source!) + 1;
                    yielded[firstDecisive.Name] = yielded.GetValueOrDefault(firstDecisive.Name) + 1;
                }
            }

            var stats = order.Select(name => new HeadTailFeatureStat(
                name,
                total.GetValueOrDefault(name),
                decisive.GetValueOrDefault(name),
                adjudicated.GetValueOrDefault(name),
                compared.GetValueOrDefault(name),
                agree.GetValueOrDefault(name),
                disagree.GetValueOrDefault(name),
                takeOver.GetValueOrDefault(name),
                yielded.GetValueOrDefault(name))).ToList();

            return new HeadTailFeatureAudit(stats, totalFrames, framesWithTruth, allDeadband, malformed, takeOverFrames);
        }

        /// <summary>
        /// 从检测记录列表构建自检报告（便捷入口——调用方拿 <c>BarcodeDataService</c> 的
        /// 查询结果直接喂进来即可，无需自行拆列）。
        /// <para>只消费 <c>HeadFeatures</c>（轨迹 JSON）与 <c>HeadTruthPositive</c>（QR 真值符号）两列，
        /// 其余字段忽略。</para>
        /// </summary>
        /// <param name="records">检测记录（可为任意时间范围/配方的子集）。</param>
        public static HeadTailFeatureAudit Build(IReadOnlyList<Models.DbModel> records)
            => Build(
                records.Select(r => r.HeadFeatures).ToList(),
                records.Select(r => r.HeadTruthPositive).ToList());

        /// <summary>轨迹里的单特征最小投影：只取一致率计算所需字段。
        /// <paramref name="ValuePositive"/> 是该特征有符号值是否 &gt; 0（"数值大的一侧是头部"的体现），
        /// 与 QR 真值符号同约定，故一致率 = 两个符号相等。
        /// </summary>
        private readonly record struct TraceFeature(string Name, bool Decisive, bool ValuePositive);
    }
}
