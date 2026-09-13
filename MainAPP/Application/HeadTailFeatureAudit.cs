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
    /// 三个自洽性指标（2026-09-13）：<b>完全不依赖 QR 真值</b>，是无码场景下唯一可用的反馈信号。
    /// <para><b>为什么需要它</b>：<see cref="HeadTailFeatureStat.AgreeRate"/> 需要真值才能算，
    /// 而无码帧（特征池的主战场）永远没有真值。本组指标回答的不是"判得对不对"，
    /// 而是"<b>判得稳不稳</b>"——这是无监督能做到的极限，也是驱动在线标定的唯一闭环。</para>
    /// <para><b>三个指标的分工</b>：分歧度低说明特征互相印证；抖动率低说明输出连续稳定；
    /// 位置相关性接近零说明没有被成像链（镜头渐晕、传感器响应不均）污染。
    /// 三者都异常时说明系统在"瞎猜"，但无论哪一个都<b>不能判定方向对错</b>。</para>
    /// </summary>
    /// <param name="MultiDecisiveFrames">≥2 个特征出死区的帧数——分歧度/位置相关性的有效分母。
    /// 单特征帧的"分歧度"恒为 1（无分歧可言），计入会稀释指标，故单独统计。</param>
    /// <param name="SingleDecisiveFrames">恰好 1 个特征出死区的帧数。该值偏高说明特征池退化为单特征级联。</param>
    /// <param name="DivergentFrames">出死区特征符号不统一的帧数（分歧度指标）。</param>
    /// <param name="DirectionFlips">相邻裁决帧之间方向发生翻转的次数（抖动率指标的分子）。</param>
    /// <param name="ComparablePairs">可比较的相邻裁决帧对数（抖动率的分母）。</param>
    /// <param name="PositionSamples">参与位置相关性计算的帧数（要求有裁决 + 位置有效）。</param>
    /// <param name="PositionStdX">产品画面 X 坐标的标准差——相关性计算的可用性门槛。</param>
    /// <param name="PositionStdY">产品画面 Y 坐标的标准差。</param>
    /// <param name="PositionCorrelationX">头向（+1/−1）与产品 X 坐标的皮尔逊相关系数；样本不足或位置无变化时为 NaN。</param>
    /// <param name="PositionCorrelationY">头向与产品 Y 坐标的皮尔逊相关系数；同上。</param>
    public sealed record HeadTailConsistency(
        int MultiDecisiveFrames,
        int SingleDecisiveFrames,
        int DivergentFrames,
        int DirectionFlips,
        int ComparablePairs,
        int PositionSamples,
        double PositionStdX,
        double PositionStdY,
        double PositionCorrelationX,
        double PositionCorrelationY)
    {
        /// <summary>特征分歧度 = 符号不统一的帧 / 多特征帧。0 = 特征们总是互相印证；接近 1 = 总是互相矛盾。</summary>
        public double DivergenceRate => MultiDecisiveFrames > 0 ? (double)DivergentFrames / MultiDecisiveFrames : 0;

        /// <summary>时序抖动率 = 方向翻转次数 / 相邻帧对数。
        /// <b>仅在产品同向摆放时才有"抖动"语义</b>——混向摆放时翻转是真实行为，该值无意义。</summary>
        public double FlipRate => ComparablePairs > 0 ? (double)DirectionFlips / ComparablePairs : 0;

        /// <summary>单特征帧占比——偏高说明级联退化为单特征决策，多特征的价值未兑现。</summary>
        public double SingleDecisiveRate
        {
            get
            {
                var d = MultiDecisiveFrames + SingleDecisiveFrames;
                return d > 0 ? (double)SingleDecisiveFrames / d : 0;
            }
        }

        /// <summary>位置相关性是否可评估——位置必须有足够变化，否则相关系数分母趋零、结果随机跳动。</summary>
        public bool PositionUsable => PositionSamples >= HeadTailFeatureAudit.MinPositionSamples
            && PositionStdX > HeadTailFeatureAudit.MinPositionStd;

        /// <summary>综合诊断：把三个指标压成一句人话。</summary>
        public string Diagnosis()
        {
            var parts = new List<string>();

            if (MultiDecisiveFrames + SingleDecisiveFrames == 0)
            {
                return "无可裁决帧——特征池未产出结论";
            }

            parts.Add(SingleDecisiveRate > 0.8
                ? $"⚠ 单特征帧占 {SingleDecisiveRate:P0}（级联退化为单特征决策）"
                : $"分歧度 {DivergenceRate:P0}");

            parts.Add(ComparablePairs > 0
                ? $"抖动率 {FlipRate:P0}"
                : "抖动率不可评估（无相邻裁决帧对）");

            if (!PositionUsable)
            {
                parts.Add("位置相关性不可用（产品位置无足够变化）");
            }
            else
            {
                var maxCorr = Math.Max(Math.Abs(PositionCorrelationX), Math.Abs(PositionCorrelationY));
                parts.Add(maxCorr > 0.3
                    ? $"⚠ 位置相关性 {maxCorr:F2}（疑被镜头渐晕/传感器不均污染）"
                    : $"位置相关性 {maxCorr:F2}（未见污染）");
            }

            return string.Join("；", parts);
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

        /// <summary>位置相关性所需的最少样本数（低于此值不评估）。</summary>
        public const int MinPositionSamples = 30;

        /// <summary>位置相关性所需的最小位置标准差（像素）。低于此值说明产品总在同一位置，
        /// 相关系数分母趋零会随机跳动，必须如实报"不可用"而非给一个噪声值。</summary>
        public const double MinPositionStd = 1.0;

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

        /// <summary>自洽性指标（2026-09-13，<b>不依赖真值</b>）——无码场景下唯一可用的反馈信号。</summary>
        public HeadTailConsistency Consistency { get; }

        private HeadTailFeatureAudit(
            IReadOnlyList<HeadTailFeatureStat> stats,
            int totalFrames, int framesWithTruth, int allDeadbandFrames, int malformedFrames,
            int takeOverFrames, HeadTailConsistency consistency)
        {
            Stats = stats;
            TotalFrames = totalFrames;
            FramesWithTruth = framesWithTruth;
            AllDeadbandFrames = allDeadbandFrames;
            MalformedFrames = malformedFrames;
            TakeOverFrames = takeOverFrames;
            Consistency = consistency;
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
            => Build(traces, truths, null, null);

        /// <summary>
        /// 汇总统计（含自洽性指标，纯函数，internal 供单测）。
        /// </summary>
        /// <param name="traces">每帧的 HeadFeatures JSON（null/空串 = 特征池未运行，跳过）。</param>
        /// <param name="truths">与 <paramref name="traces"/> 逐帧对齐的真值符号（null = 无真值）。</param>
        /// <param name="posXs">与 <paramref name="traces"/> 逐帧对齐的产品画面 X 坐标（null = 不评估位置相关性，
        /// 或长度不足）。用于"头向-位置相关性"自检。</param>
        /// <param name="posYs">产品画面 Y 坐标，同 <paramref name="posXs"/>。</param>
        public static HeadTailFeatureAudit Build(
            IReadOnlyList<string?> traces, IReadOnlyList<bool?> truths,
            IReadOnlyList<double>? posXs, IReadOnlyList<double>? posYs)
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

            // 自洽性累加器
            int multiDecisive = 0, singleDecisive = 0, divergent = 0;
            int flips = 0, comparablePairs = 0;
            bool hasPrevDirection = false;
            bool prevDirection = false;
            var corrXs = new List<double>();
            var corrYs = new List<double>();
            var corrDirs = new List<int>();

            // 位置序列必须与 trace 严格对齐才可用（长度不足则整体放弃，避免错位相关）
            var usePositions = posXs is not null && posYs is not null
                && posXs.Count >= traces.Count && posYs.Count >= traces.Count;

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

                // ── 自洽性指标（不依赖真值）──
                // ① 分歧度：只看多特征帧——单特征帧"分歧度"恒为 1，计入会稀释指标。
                var decisiveFeatures = features.Where(f => f.Decisive).ToList();
                if (decisiveFeatures.Count >= 2)
                {
                    multiDecisive++;
                    var posCount = decisiveFeatures.Count(f => f.ValuePositive);
                    if (posCount != 0 && posCount != decisiveFeatures.Count)
                    {
                        divergent++;
                    }
                }
                else if (decisiveFeatures.Count == 1)
                {
                    singleDecisive++;
                }

                // ② 抖动率：相邻"有裁决帧"之间的方向翻转。方向 = 裁决者（source）的符号。
                var adjudicator = features.FirstOrDefault(f => string.Equals(f.Name, source, StringComparison.Ordinal));
                var direction = adjudicator.Name is not null ? adjudicator.ValuePositive : firstDecisive.ValuePositive;
                // 正常情况 adjudicator 必然命中（source 就是本帧裁决者名）；未命中仅见于脏数据
                // （src 指向不在特征表里的名字），此时退用首个出死区特征的符号，仍计入翻转对比以如实反映不稳定。
                if (hasPrevDirection)
                {
                    comparablePairs++;
                    if (direction != prevDirection)
                    {
                        flips++;
                    }
                }

                prevDirection = direction;
                hasPrevDirection = true;

                // ③ 位置相关性：收集 (位置X, 位置Y, 方向±1)，阈值判定放在收尾处做。
                if (usePositions)
                {
                    corrXs.Add(posXs![i]);
                    corrYs.Add(posYs![i]);
                    corrDirs.Add(direction ? 1 : -1);
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

            var consistency = BuildConsistency(
                multiDecisive, singleDecisive, divergent, flips, comparablePairs, corrXs, corrYs, corrDirs);

            return new HeadTailFeatureAudit(
                stats, totalFrames, framesWithTruth, allDeadband, malformed, takeOverFrames, consistency);
        }

        /// <summary>
        /// 收尾计算位置相关性（样本不足/位置无变化时如实返回 NaN，而非给一个随机数）。
        /// </summary>
        private static HeadTailConsistency BuildConsistency(
            int multiDecisive, int singleDecisive, int divergent, int flips, int comparablePairs,
            List<double> xs, List<double> ys, List<int> dirs)
        {
            var samples = xs.Count;
            var stdX = StdDev(xs);
            var stdY = StdDev(ys);

            double corrX = double.NaN, corrY = double.NaN;
            // 双门槛：样本够 + 位置有足够变化。缺任一都不能计算——否则分母趋零，结果随机跳动。
            if (samples >= MinPositionSamples && stdX > MinPositionStd && stdY > MinPositionStd)
            {
                corrX = Pearson(dirs, xs);
                corrY = Pearson(dirs, ys);
            }

            return new HeadTailConsistency(
                multiDecisive, singleDecisive, divergent, flips, comparablePairs,
                samples, stdX, stdY, corrX, corrY);
        }

        /// <summary>总体标准差（n 为分母，非样本标准差——这里描述的是观测数据集本身离散程度）。</summary>
        private static double StdDev(IReadOnlyList<double> values)
        {
            if (values.Count == 0)
            {
                return 0.0;
            }

            var mean = values.Average();
            var sumSq = values.Sum(v => (v - mean) * (v - mean));
            return Math.Sqrt(sumSq / values.Count);
        }

        /// <summary>
        /// 皮尔逊相关系数。<paramref name="dirs"/> 是 ±1 的方向序列。
        /// 任一序列方差为零时返回 NaN（相关无定义）——调用方已用 std 门槛预先排除，此处仅防御。
        /// </summary>
        private static double Pearson(IReadOnlyList<int> dirs, IReadOnlyList<double> values)
        {
            var n = Math.Min(dirs.Count, values.Count);
            if (n < 2)
            {
                return double.NaN;
            }

            double meanD = 0, meanV = 0;
            for (int i = 0; i < n; i++)
            {
                meanD += dirs[i];
                meanV += values[i];
            }

            meanD /= n;
            meanV /= n;

            double cov = 0, varD = 0, varV = 0;
            for (int i = 0; i < n; i++)
            {
                var dd = dirs[i] - meanD;
                var dv = values[i] - meanV;
                cov += dd * dv;
                varD += dd * dd;
                varV += dv * dv;
            }

            var denom = Math.Sqrt(varD * varV);
            return denom > 0 ? cov / denom : double.NaN;
        }

        /// <summary>
        /// 从检测记录列表构建自检报告（便捷入口——调用方拿 <c>BarcodeDataService</c> 的
        /// 查询结果直接喂进来即可，无需自行拆列）。
        /// <para>消费 <c>HeadFeatures</c>（轨迹 JSON）、<c>HeadTruthPositive</c>（QR 真值符号）、
        /// <c>ImageX</c>/<c>ImageY</c>（产品画面位置，供位置相关性自检）三组字段，其余忽略。</para>
        /// <para><b>调用方须按 DetectTime 升序传入</b>——抖动率依赖"相邻帧"语义，乱序会得出错误翻转率。</para>
        /// </summary>
        /// <param name="records">检测记录（可为任意时间范围/配方的子集，需按时间升序）。</param>
        public static HeadTailFeatureAudit Build(IReadOnlyList<Models.DbModel> records)
            => Build(
                records.Select(r => r.HeadFeatures).ToList(),
                records.Select(r => r.HeadTruthPositive).ToList(),
                records.Select(r => r.ImageX).ToList(),
                records.Select(r => r.ImageY).ToList());

        /// <summary>轨迹里的单特征最小投影：只取一致率计算所需字段。
        /// <paramref name="ValuePositive"/> 是该特征有符号值是否 &gt; 0（"数值大的一侧是头部"的体现），
        /// 与 QR 真值符号同约定，故一致率 = 两个符号相等。
        /// </summary>
        private readonly record struct TraceFeature(string Name, bool Decisive, bool ValuePositive);
    }
}
