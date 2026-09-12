using MainAPP.Models;
using System;
using System.Collections.Generic;
using System.Linq;

namespace MainAPP.Services
{
    /// <summary>
    /// 图表智能分析服务：提供统计摘要、异常点检测、分箱数推荐与图表类型推荐。
    /// 所有方法无状态，可安全并发调用。
    /// </summary>
    public static class ChartAnalyticsService
    {
        // 异常点检测阈值：|Z-Score| 超过此值视为异常（双尾约 1.24%）
        private const double OutlierZScoreThreshold = 2.5;
        // 分箱数下限与上限，避免数据量过小或过大时直方图失真
        private const int MinBinCount = 5;
        private const int MaxBinCount = 50;
        // 推荐趋势分析所需的最小数据点
        private const int MinPointsForTrend = 10;
        // 视为"近期数据"的最大天数
        private const int RecentDataDayWindow = 7;

        /// <summary>
        /// 计算数据集合的统计摘要：均值、标准差、最小值、最大值、中位数、计数。
        /// </summary>
        public static ChartStats ComputeStats(IEnumerable<double> values)
        {
            var arr = values as double[] ?? values.ToArray();
            if (arr.Length == 0)
                return ChartStats.Empty;

            var sorted = (double[])arr.Clone();
            Array.Sort(sorted);

            double sum = 0;
            double min = double.PositiveInfinity;
            double max = double.NegativeInfinity;
            for (int i = 0; i < arr.Length; i++)
            {
                double v = arr[i];
                sum += v;
                if (v < min) min = v;
                if (v > max) max = v;
            }
            double mean = sum / arr.Length;

            double sqSum = 0;
            for (int i = 0; i < arr.Length; i++)
            {
                double d = arr[i] - mean;
                sqSum += d * d;
            }
            // 总体标准差（分母 n），与工业 SPC 习惯一致
            double stdDev = arr.Length > 1 ? Math.Sqrt(sqSum / arr.Length) : 0;

            double median = sorted.Length % 2 == 0
                ? (sorted[sorted.Length / 2 - 1] + sorted[sorted.Length / 2]) / 2.0
                : sorted[sorted.Length / 2];

            return new ChartStats(mean, stdDev, min, max, median, arr.Length);
        }

        /// <summary>
        /// 基于 Z-Score 检测异常点索引。标准差为 0 或样本不足时返回空集合。
        /// </summary>
        public static IReadOnlyList<int> DetectOutlierIndices(IReadOnlyList<double> values)
        {
            if (values.Count < 3)
                return Array.Empty<int>();

            var stats = ComputeStats(values);
            if (stats.StdDev <= double.Epsilon || stats.Count == 0)
                return Array.Empty<int>();

            var indices = new List<int>();
            for (int i = 0; i < values.Count; i++)
            {
                double z = Math.Abs(values[i] - stats.Mean) / stats.StdDev;
                if (z > OutlierZScoreThreshold)
                    indices.Add(i);
            }
            return indices;
        }

        /// <summary>
        /// 根据数据量推荐直方图分箱数（Sturges 法则，限制在 [5, 50]）。
        /// 数据量小于 5 时返回 5，避免过少分箱无意义。
        /// </summary>
        public static int RecommendBinCount(int dataCount)
        {
            if (dataCount <= 0) return MinBinCount;
            // Sturges: k = ⌈log2(n) + 1⌉
            int k = (int)Math.Ceiling(Math.Log2(dataCount) + 1);
            return Math.Clamp(k, MinBinCount, MaxBinCount);
        }

        /// <summary>
        /// 根据数据特征推荐默认展示的图表标签页：
        /// - 数据稀少或无条码：趋势分析（Tab 0）
        /// - 数据点充足且包含条码：条码分析（Tab 2），可同时观察识别质量与位置分布
        /// - 数据点充足但无条码：分布分析（Tab 1）
        /// </summary>
        public static ChartRecommendation RecommendChartType(IReadOnlyList<DbModel> data)
        {
            if (data.Count == 0)
                return new ChartRecommendation(0, "No data loaded.");

            if (data.Count < MinPointsForTrend)
                return new ChartRecommendation(0, $"Only {data.Count} records — Trend tab helps inspect individual points.");

            // 含条码记录占比超过 50% 视为"有条码数据"
            int withBarcode = data.Count(m => !string.IsNullOrWhiteSpace(m.Barcode));
            bool hasTimeSpan = data.Max(m => m.DetectTime) - data.Min(m => m.DetectTime) > TimeSpan.FromDays(RecentDataDayWindow);

            if (withBarcode > data.Count / 2)
                return new ChartRecommendation(2, $"{withBarcode} records have barcodes — Barcode Analysis tab is recommended.");

            if (hasTimeSpan)
                return new ChartRecommendation(0, "Data spans multiple days — Trend Analysis tab is recommended.");

            return new ChartRecommendation(1, "Compact dataset — Distribution Analysis tab is recommended.");
        }

        /// <summary>
        /// 汇总数据集合的异常点情况：分数、速度、距离三个维度的异常点计数。
        /// </summary>
        public static AnomalySummary SummarizeAnomalies(IReadOnlyList<DbModel> data)
        {
            if (data.Count == 0)
                return AnomalySummary.Empty;

            var scores = data.Select(m => (double)m.Score).ToArray();
            var speeds = data.Select(m => (double)m.Speed).ToArray();
            var distances = data.Select(m =>
                Math.Sqrt(Math.Pow(m.ImageBarcodeX - m.ImageX, 2) + Math.Pow(m.ImageBarcodeY - m.ImageY, 2))).ToArray();

            int scoreOutliers = DetectOutlierIndices(scores).Count;
            int speedOutliers = DetectOutlierIndices(speeds).Count;
            int distanceOutliers = DetectOutlierIndices(distances).Count;

            return new AnomalySummary(scoreOutliers, speedOutliers, distanceOutliers, data.Count);
        }

        /// <summary>
        /// 判定数据集是否包含 Result 判定列数据（追溯列 2026-09-12 起才有，旧数据为空）。
        /// </summary>
        public static bool HasResultData(IReadOnlyList<DbModel> data)
            => data.Any(m => !string.IsNullOrWhiteSpace(m.Result));

        /// <summary>
        /// 计算 NG 率摘要：仅统计有 Result 判定的记录（OK/NG，忽略大小写）。
        /// </summary>
        public static NgSummary ComputeNgSummary(IReadOnlyList<DbModel> data)
        {
            var withResult = data.Where(m => !string.IsNullOrWhiteSpace(m.Result)).ToList();
            if (withResult.Count == 0)
                return NgSummary.Empty;

            int ng = withResult.Count(m => !string.Equals(m.Result, "OK", StringComparison.OrdinalIgnoreCase));
            return new NgSummary(withResult.Count - ng, ng, withResult.Count);
        }

        /// <summary>
        /// 按小时聚合 NG 率（仅统计有 Result 判定的记录），按小时升序返回。
        /// </summary>
        public static IReadOnlyList<NgRatePoint> ComputeNgRateByHour(IReadOnlyList<DbModel> data)
        {
            var withResult = data.Where(m => !string.IsNullOrWhiteSpace(m.Result)).ToList();
            if (withResult.Count == 0)
                return Array.Empty<NgRatePoint>();

            return withResult
                .GroupBy(m => new DateTime(m.DetectTime.Year, m.DetectTime.Month, m.DetectTime.Day, m.DetectTime.Hour, 0, 0))
                .OrderBy(g => g.Key)
                .Select(g => new NgRatePoint(
                    g.Key,
                    g.Count(),
                    g.Count(m => !string.Equals(m.Result, "OK", StringComparison.OrdinalIgnoreCase))))
                .ToList();
        }

        /// <summary>
        /// 检测异常点并返回明细记录（Score/Speed/Distance 三维度，与 SummarizeAnomalies 同口径）。
        /// 按最大 |Z| 降序，最多 maxRecords 条——供"质量复盘"标签页的异常明细表使用。
        /// </summary>
        public static IReadOnlyList<OutlierRecord> DetectOutlierRecords(IReadOnlyList<DbModel> data, int maxRecords = 200)
        {
            if (data.Count < 3)
                return Array.Empty<OutlierRecord>();

            var scores = data.Select(m => (double)m.Score).ToArray();
            var speeds = data.Select(m => (double)m.Speed).ToArray();
            var distances = data.Select(m =>
                Math.Sqrt(Math.Pow(m.ImageBarcodeX - m.ImageX, 2) + Math.Pow(m.ImageBarcodeY - m.ImageY, 2))).ToArray();

            var scoreStats = ComputeStats(scores);
            var speedStats = ComputeStats(speeds);
            var distanceStats = ComputeStats(distances);

            var result = new List<OutlierRecord>();
            for (int i = 0; i < data.Count; i++)
            {
                var reasons = new List<string>();
                double maxAbsZ = 0;

                if (scoreStats.StdDev > double.Epsilon)
                {
                    double z = (scores[i] - scoreStats.Mean) / scoreStats.StdDev;
                    if (Math.Abs(z) > OutlierZScoreThreshold)
                    {
                        reasons.Add($"Score Z={z:+0.0;-0.0}");
                        maxAbsZ = Math.Max(maxAbsZ, Math.Abs(z));
                    }
                }
                if (speedStats.StdDev > double.Epsilon)
                {
                    double z = (speeds[i] - speedStats.Mean) / speedStats.StdDev;
                    if (Math.Abs(z) > OutlierZScoreThreshold)
                    {
                        reasons.Add($"Interval Z={z:+0.0;-0.0}");
                        maxAbsZ = Math.Max(maxAbsZ, Math.Abs(z));
                    }
                }
                if (distanceStats.StdDev > double.Epsilon)
                {
                    double z = (distances[i] - distanceStats.Mean) / distanceStats.StdDev;
                    if (Math.Abs(z) > OutlierZScoreThreshold)
                    {
                        reasons.Add($"Distance Z={z:+0.0;-0.0}");
                        maxAbsZ = Math.Max(maxAbsZ, Math.Abs(z));
                    }
                }

                if (reasons.Count > 0)
                {
                    result.Add(new OutlierRecord(
                        data[i], data[i].DetectTime,
                        string.IsNullOrWhiteSpace(data[i].Barcode) ? "-" : data[i].Barcode,
                        scores[i], speeds[i], distances[i],
                        string.Join("; ", reasons), maxAbsZ));
                }
            }

            return result.OrderByDescending(o => o.MaxAbsZ).Take(maxRecords).ToList();
        }
    }

    /// <summary>
    /// 数据集合的统计摘要。空集合使用 <see cref="Empty"/>。
    /// </summary>
    public sealed record ChartStats(double Mean, double StdDev, double Min, double Max, double Median, int Count)
    {
        public static ChartStats Empty { get; } = new(0, 0, 0, 0, 0, 0);
    }

    /// <summary>
    /// 图表类型推荐结果：推荐的标签页索引与原因说明。
    /// </summary>
    public sealed record ChartRecommendation(int RecommendedTabIndex, string Reason);

    /// <summary>
    /// 异常点汇总：分数、速度、距离三个维度的异常计数。
    /// </summary>
    public sealed record AnomalySummary(int ScoreOutliers, int SpeedOutliers, int DistanceOutliers, int TotalRecords)
    {
        public static AnomalySummary Empty { get; } = new(0, 0, 0, 0);
        public int TotalOutliers => ScoreOutliers + SpeedOutliers + DistanceOutliers;
        public bool HasAnomalies => TotalOutliers > 0;
    }

    /// <summary>单小时 NG 率聚合点（仅统计有 Result 判定的记录）。</summary>
    public sealed record NgRatePoint(DateTime HourStart, int TotalCount, int NgCount)
    {
        public double NgRatePercent => TotalCount > 0 ? NgCount * 100.0 / TotalCount : 0;
    }

    /// <summary>NG 率摘要。Empty 的 HasResultData=false 表示数据集无判定列数据。</summary>
    public sealed record NgSummary(int OkCount, int NgCount, int TotalCount)
    {
        public static NgSummary Empty { get; } = new(0, 0, 0);
        public bool HasResultData => TotalCount > 0;
        public double NgRatePercent => TotalCount > 0 ? NgCount * 100.0 / TotalCount : 0;
    }

    /// <summary>异常点明细记录（三维度的原因标签 + 最大 |Z| 排序键）。</summary>
    public sealed record OutlierRecord(
        DbModel Record,
        DateTime Time,
        string Barcode,
        double Score,
        double Speed,
        double Distance,
        string Reason,
        double MaxAbsZ);
}
