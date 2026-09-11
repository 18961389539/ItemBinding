using MainAPP.Models;
using ScottPlot;
using ScottPlot.Statistics;
using System;
using System.Collections.Generic;
using System.Linq;

namespace MainAPP.Services
{
    /// <summary>
    /// 图表渲染服务：将 ScottPlot 图表渲染逻辑从 View 中分离，
    /// 提供无状态的静态渲染方法。每个方法接收 Plot 与数据，完成图表配置（不含 Refresh，由调用方负责）。
    /// </summary>
    public static class ChartRenderingService
    {
        // 热力图范围扩展阈值与扩展量
        private const double HeatmapRangeThreshold = 0.001;
        private const double HeatmapRangeExtension = 5;
        // 热力图网格尺寸
        private const int HeatmapGridWidth = 50;
        private const int HeatmapGridHeight = 50;
        // TechChartTheme 标准配色（与项目 UI 配色一致：Cyan/Blue/Orange/Yellow/Teal/Green）
        // REVIEW-FIX(亮色主题): 数据系列色改用亮背景下的深色系（与 DarkTheme.xaml 亮色 token 对应）
        private static readonly string[] TechChartPalette =
        {
            "#00838F", // Cyan
            "#0277BD", // Blue
            "#EF6C00", // Orange
            "#B8860B", // Yellow (REVIEW-FIX 对比度: 原 #F9A825 白底仅 2.0:1，深黄 #B8860B 为 3.3:1)
            "#00897B", // Teal
            "#2E7D32"  // Green
        };

        // TechChartTheme 颜色常量（亮色背景 + Cyan 强调色，与 DarkTheme.xaml 亮色 token 一致）
        // 背景色：白色 #FFFFFF
        private static readonly ScottPlot.Color s_techBgColor = ScottPlot.Color.FromHex("#FFFFFF");
        // 网格线色：半透明深色 #12000000（约 7% 不透明度）
        private static readonly ScottPlot.Color s_techGridColor = ScottPlot.Color.FromHex("#000000").WithAlpha(0x12);
        // 坐标轴标签色（刻度数字/刻度线/轴框）：#5C6B7A
        private static readonly ScottPlot.Color s_techAxisLabelColor = ScottPlot.Color.FromHex("#5C6B7A");
        // 坐标轴标题色（XLabel/YLabel 文本）：#37474F
        private static readonly ScottPlot.Color s_techAxisTitleColor = ScottPlot.Color.FromHex("#37474F");
        // 图表标题色：#00838F（Cyan 强调色）
        private static readonly ScottPlot.Color s_techTitleColor = ScottPlot.Color.FromHex("#00838F");

        // 智能分析叠加层颜色：均值线（深灰）、±1σ 带（蓝色半透明）、异常点（深红）
        private static readonly ScottPlot.Color s_meanLineColor = ScottPlot.Color.FromHex("#37474F");
        private static readonly ScottPlot.Color s_stdBandColor = ScottPlot.Color.FromHex("#1565C0").WithAlpha(0.15);
        private static readonly ScottPlot.Color s_outlierColor = ScottPlot.Color.FromHex("#C62828");

        // REVIEW-FIX: 安全的 Y 轴上限。全 0 数据时 Max()*1.1 == 0，ScottPlot 5 对上下限相等
        // 的轴抛 "axis limits are equal" 异常导致图表渲染失败；空/非正数据统一回退到 1。
        private static double SafeMaxYScale(double maxValue, double factor = 1.1) =>
            maxValue > 0 ? maxValue * factor : 1;

        /// <summary>
        /// 应用 TechChartTheme 浅色科技风主题到 ScottPlot Plot。
        /// 统一背景色、网格线、坐标轴标签、坐标轴标题、图表标题与数据系列配色。
        /// 注意：ScottPlot 5.x API 与 4.x 不同，本方法基于 5.x（plot.Axes.Color、plot.FigureBackground 等）。
        /// </summary>
        private static void ApplyTechChartTheme(Plot plot)
        {
            // 背景：图与数据区统一白色 #FFFFFF
            plot.FigureBackground.Color = s_techBgColor;
            plot.DataBackground.Color = s_techBgColor;

            // 网格线：半透明深色 #12000000，主/次线宽保持原默认
            plot.Grid.MajorLineColor = s_techGridColor;
            plot.Grid.MinorLineColor = s_techGridColor;
            plot.Grid.MajorLineWidth = 1;
            plot.Grid.MinorLineWidth = 0.5f;

            // 数据系列配色：使用 Accent 系列（Cyan/Blue/Orange/Yellow/Teal/Green）
            plot.Add.Palette = ScottPlot.Palette.FromColors(TechChartPalette);

            // 坐标轴标签色（刻度数字、刻度线、轴框）：#5C6B7A
            // Axes.Color 同时会把 Title 标签色设为同色，下面会覆盖
            plot.Axes.Color(s_techAxisLabelColor);

            // 坐标轴标题色（XLabel/YLabel 文本）：#37474F
            plot.Axes.Bottom.Label.ForeColor = s_techAxisTitleColor;
            plot.Axes.Left.Label.ForeColor = s_techAxisTitleColor;
            plot.Axes.Right.Label.ForeColor = s_techAxisTitleColor;
            plot.Axes.Top.Label.ForeColor = s_techAxisTitleColor;

            // 图表标题色：#00838F（Cyan 强调色，覆盖 Axes.Color 设的色）
            plot.Axes.Title.Label.ForeColor = s_techTitleColor;
        }

        // ===== 趋势分析 =====

        public static void RenderScoreOverTime(Plot plot, IEnumerable<DbModel> data)
        {
            var dates = data.Select(m => m.DetectTime.ToOADate()).ToArray();
            var scores = data.Select(m => m.Score).ToArray();
            // M186: 空数据集时直接返回，避免 SetLimitsY(0, 0)
            if (scores.Length == 0)
                return;

            plot.Clear();
            var scatter = plot.Add.Scatter(dates, scores);
            scatter.LineWidth = 2;
            scatter.LineColor = ScottPlot.Color.FromHex(TechChartPalette[0]); // Cyan
            scatter.MarkerSize = 6;
            scatter.MarkerColor = ScottPlot.Color.FromHex(TechChartPalette[0]);
            scatter.MarkerShape = MarkerShape.FilledCircle;

            plot.Title("Score Trend Analysis", size: 16);
            plot.XLabel("Detection Time", size: 13);
            plot.YLabel("Score", size: 13);

            plot.Axes.AutoScale();
            plot.Axes.SetLimitsY(0, SafeMaxYScale(scores.DefaultIfEmpty(0).Max()));

            AddTrendAnalytics(plot, dates, scores);

            ApplyTechChartTheme(plot);
            plot.Axes.Bottom.TickLabelStyle.Rotation = 45;
        }

        public static void RenderSpeedOverTime(Plot plot, IEnumerable<DbModel> data)
        {
            var dates = data.Select(m => m.DetectTime.ToOADate()).ToArray();
            var speeds = data.Select(m => (double)m.Speed).ToArray();
            if (speeds.Length == 0)
                return;

            plot.Clear();
            var scatter = plot.Add.Scatter(dates, speeds);
            scatter.LineWidth = 2;
            scatter.LineColor = ScottPlot.Color.FromHex(TechChartPalette[2]); // Orange
            scatter.MarkerSize = 6;
            scatter.MarkerColor = ScottPlot.Color.FromHex(TechChartPalette[2]);
            scatter.MarkerShape = MarkerShape.FilledCircle;

            plot.Title("Speed Trend Analysis", size: 16);
            plot.XLabel("Detection Time", size: 13);
            plot.YLabel("Speed", size: 13);

            plot.Axes.AutoScale();
            plot.Axes.SetLimitsY(0, SafeMaxYScale(speeds.DefaultIfEmpty(0).Max()));

            AddTrendAnalytics(plot, dates, speeds);

            ApplyTechChartTheme(plot);
            plot.Axes.Bottom.TickLabelStyle.Rotation = 45;
        }

        public static void RenderCostTime(Plot plot, IEnumerable<DbModel> data)
        {
            var dates = data.Select(m => m.DetectTime.ToOADate()).ToArray();
            var costs = data.Select(m => (double)m.CostTime).ToArray();
            if (costs.Length == 0)
                return;

            plot.Clear();
            var scatter = plot.Add.Scatter(dates, costs);
            scatter.LineWidth = 2;
            scatter.LineColor = ScottPlot.Color.FromHex(TechChartPalette[1]); // Blue
            scatter.MarkerSize = 6;
            scatter.MarkerColor = ScottPlot.Color.FromHex(TechChartPalette[1]);
            scatter.MarkerShape = MarkerShape.FilledCircle;

            plot.XLabel("Detection Time", size: 13);
            plot.YLabel("Duration (ms)", size: 13);

            plot.Axes.AutoScale();
            plot.Axes.SetLimitsY(0, SafeMaxYScale(costs.DefaultIfEmpty(0).Max()));

            AddTrendAnalytics(plot, dates, costs);

            ApplyTechChartTheme(plot);
            plot.Axes.Bottom.TickLabelStyle.Rotation = 45;
        }

        public static void RenderDistanceOverTime(Plot plot, IEnumerable<DbModel> data)
        {
            var dates = data.Select(m => m.DetectTime.ToOADate()).ToArray();
            var distances = data.Select(m =>
                Math.Sqrt(Math.Pow(m.ImageBarcodeX - m.ImageX, 2) + Math.Pow(m.ImageBarcodeY - m.ImageY, 2))
            ).ToArray();
            if (distances.Length == 0)
                return;

            plot.Clear();
            var scatter = plot.Add.Scatter(dates, distances);
            scatter.LineWidth = 2;
            scatter.LineColor = ScottPlot.Color.FromHex(TechChartPalette[3]); // Yellow
            scatter.MarkerSize = 6;
            scatter.MarkerColor = ScottPlot.Color.FromHex(TechChartPalette[3]);
            scatter.MarkerShape = MarkerShape.FilledCircle;

            plot.Title("Distance Trend Analysis", size: 16);
            plot.XLabel("Detection Time", size: 13);
            plot.YLabel("Distance", size: 13);

            plot.Axes.AutoScale();
            plot.Axes.SetLimitsY(0, SafeMaxYScale(distances.DefaultIfEmpty(0).Max()));

            AddTrendAnalytics(plot, dates, distances);

            ApplyTechChartTheme(plot);
            plot.Axes.Bottom.TickLabelStyle.Rotation = 45;
        }

        // ===== 分布分析 =====

        public static void RenderScoreDistribution(Plot plot, IEnumerable<DbModel> data)
        {
            var scores = data.Select(m => m.Score).ToArray();
            if (scores.Length == 0)
                return;

            plot.Clear();
            var histogram = Histogram.WithBinCount(ChartAnalyticsService.RecommendBinCount(scores.Length), scores);
            plot.Add.Histogram(histogram, color: ScottPlot.Color.FromHex(TechChartPalette[0])); // Cyan

            plot.Title("Score Distribution Histogram", size: 16);
            plot.XLabel("Score", size: 13);
            plot.YLabel("Frequency", size: 13);

            plot.Axes.AutoScale();

            AddHistogramAnalytics(plot, scores);

            ApplyTechChartTheme(plot);
            plot.Axes.Bottom.TickLabelStyle.Rotation = 45;
        }

        public static void RenderSpeedDistribution(Plot plot, IEnumerable<DbModel> data)
        {
            var speeds = data.Select(m => (double)m.Speed).ToArray();
            if (speeds.Length == 0)
                return;

            plot.Clear();
            var histogram = Histogram.WithBinCount(ChartAnalyticsService.RecommendBinCount(speeds.Length), speeds);
            plot.Add.Histogram(histogram, color: ScottPlot.Color.FromHex(TechChartPalette[2])); // Orange

            plot.Title("Speed Distribution Histogram", size: 16);
            plot.XLabel("Speed", size: 13);
            plot.YLabel("Frequency", size: 13);

            plot.Axes.AutoScale();

            AddHistogramAnalytics(plot, speeds);

            ApplyTechChartTheme(plot);
            plot.Axes.Bottom.TickLabelStyle.Rotation = 45;
        }

        public static void RenderAreaHistogram(Plot plot, IEnumerable<DbModel> data)
        {
            var areas = data.Select(m => m.Area).ToArray();
            if (areas.Length == 0)
                return;

            plot.Clear();
            var histogram = Histogram.WithBinCount(ChartAnalyticsService.RecommendBinCount(areas.Length), areas);
            plot.Add.Histogram(histogram, color: ScottPlot.Color.FromHex(TechChartPalette[1])); // Blue
            plot.XLabel("Area", size: 13);
            plot.YLabel("Frequency", size: 13);

            plot.Axes.AutoScale();

            AddHistogramAnalytics(plot, areas);

            ApplyTechChartTheme(plot);
            plot.Axes.Bottom.TickLabelStyle.Rotation = 45;
        }

        public static void RenderAngleHistogram(Plot plot, IEnumerable<DbModel> data)
        {
            var angles = data.Select(m => m.Angle).ToArray();
            if (angles.Length == 0)
                return;

            plot.Clear();
            var histogram = Histogram.WithBinCount(ChartAnalyticsService.RecommendBinCount(angles.Length), angles);
            plot.Add.Histogram(histogram, color: ScottPlot.Color.FromHex(TechChartPalette[4])); // Teal

            plot.Title("Angle Distribution Histogram", size: 16);
            plot.XLabel("Angle", size: 13);
            plot.YLabel("Frequency", size: 13);

            plot.Axes.AutoScale();

            AddHistogramAnalytics(plot, angles);

            ApplyTechChartTheme(plot);
            plot.Axes.Bottom.TickLabelStyle.Rotation = 45;
        }

        // ===== 条码分析 =====

        public static void RenderBarcodeScoreDistribution(Plot plot, IEnumerable<DbModel> data)
        {
            var scores = data.Select(m => m.BarcodeScore).ToArray();
            if (scores.Length == 0)
                return;

            plot.Clear();
            var histogram = Histogram.WithBinCount(ChartAnalyticsService.RecommendBinCount(scores.Length), scores);
            plot.Add.Histogram(histogram, color: ScottPlot.Color.FromHex(TechChartPalette[0])); // Cyan

            plot.Title("Barcode Score Distribution Histogram", size: 16);
            plot.XLabel("Barcode Score", size: 13);
            plot.YLabel("Frequency", size: 13);

            plot.Axes.AutoScale();

            AddHistogramAnalytics(plot, scores);

            ApplyTechChartTheme(plot);
            plot.Axes.Bottom.TickLabelStyle.Rotation = 45;
        }

        public static void RenderBarcodeScoreOverTime(Plot plot, IEnumerable<DbModel> data)
        {
            var dates = data.Select(m => m.DetectTime.ToOADate()).ToArray();
            var barcodeScores = data.Select(m => m.BarcodeScore).ToArray();
            if (barcodeScores.Length == 0)
                return;

            plot.Clear();
            var scatter = plot.Add.Scatter(dates, barcodeScores);
            scatter.LineWidth = 2;
            scatter.LineColor = ScottPlot.Color.FromHex(TechChartPalette[4]); // Teal
            scatter.MarkerSize = 6;
            scatter.MarkerColor = ScottPlot.Color.FromHex(TechChartPalette[4]);
            scatter.MarkerShape = MarkerShape.FilledCircle;

            plot.Title("Barcode Score Trend Analysis", size: 16);
            plot.XLabel("Detection Time", size: 13);
            plot.YLabel("Barcode Score", size: 13);

            plot.Axes.AutoScale();
            plot.Axes.SetLimitsY(0, SafeMaxYScale(barcodeScores.DefaultIfEmpty(0).Max()));

            AddTrendAnalytics(plot, dates, barcodeScores);

            ApplyTechChartTheme(plot);
            plot.Axes.Bottom.TickLabelStyle.Rotation = 45;
        }

        public static void RenderDistanceHistogram(Plot plot, IEnumerable<DbModel> data)
        {
            var distances = data.Select(m =>
                Math.Sqrt(Math.Pow(m.ImageBarcodeX - m.ImageX, 2) + Math.Pow(m.ImageBarcodeY - m.ImageY, 2))
            ).ToArray();

            if (distances.Length == 0)
                return;

            plot.Clear();
            var histogram = Histogram.WithBinCount(ChartAnalyticsService.RecommendBinCount(distances.Length), distances);
            plot.Add.Histogram(histogram, color: ScottPlot.Color.FromHex(TechChartPalette[3])); // Yellow

            plot.Title("Distance Distribution Histogram", size: 16);
            plot.XLabel("Distance", size: 13);
            plot.YLabel("Frequency", size: 13);

            plot.Axes.AutoScale();

            AddHistogramAnalytics(plot, distances);

            ApplyTechChartTheme(plot);
            plot.Axes.Bottom.TickLabelStyle.Rotation = 45;
        }

        public static void RenderBarcodePositionScoreHeatmap(Plot plot, IEnumerable<DbModel> data)
        {
            // L259: data 在调用方已初始化，不会为 null，简化为 Count 检查
            var models = data as IList<DbModel> ?? data.ToList();
            if (models.Count == 0)
                return;

            // 使用条码位置作为热力图坐标范围（不要混入 ImageX/ImageY）
            var minX = models.Min(m => m.ImageBarcodeX);
            var maxX = models.Max(m => m.ImageBarcodeX);
            var minY = models.Min(m => m.ImageBarcodeY);
            var maxY = models.Max(m => m.ImageBarcodeY);

            // 如果数据点范围太小，稍微扩展范围
            if (Math.Abs(maxX - minX) < HeatmapRangeThreshold) { minX -= HeatmapRangeExtension; maxX += HeatmapRangeExtension; }
            if (Math.Abs(maxY - minY) < HeatmapRangeThreshold) { minY -= HeatmapRangeExtension; maxY += HeatmapRangeExtension; }

            int gridWidth = HeatmapGridWidth;
            int gridHeight = HeatmapGridHeight;

            // 使用累加和与计数数组来正确计算每个单元格的平均值
            double[,] sum = new double[gridHeight, gridWidth];
            int[,] count = new int[gridHeight, gridWidth];

            // 计算每个网格单元的宽度和高度
            double cellWidth = (maxX - minX) / gridWidth;
            double cellHeight = (maxY - minY) / gridHeight;

            // 遍历数据点，将它们按条码坐标分配到网格
            foreach (var model in models)
            {
                int xIndex = (int)((model.ImageBarcodeX - minX) / cellWidth);
                int yIndex = (int)((model.ImageBarcodeY - minY) / cellHeight);

                // 保证索引在范围内
                if (xIndex < 0) xIndex = 0;
                if (xIndex >= gridWidth) xIndex = gridWidth - 1;
                if (yIndex < 0) yIndex = 0;
                if (yIndex >= gridHeight) yIndex = gridHeight - 1;

                sum[yIndex, xIndex] += model.BarcodeScore;
                count[yIndex, xIndex] += 1;
            }

            // 计算平均值。没有数据的单元设为 NaN
            double[,] heatmapData = new double[gridHeight, gridWidth];
            for (int r = 0; r < gridHeight; r++)
            {
                for (int c = 0; c < gridWidth; c++)
                {
                    heatmapData[r, c] = count[r, c] == 0 ? double.NaN : (sum[r, c] / count[r, c]);
                }
            }

            plot.Clear();

            var heatmap = plot.Add.Heatmap(heatmapData);
            heatmap.Extent = new ScottPlot.CoordinateRect(minX, maxX, minY, maxY);
            heatmap.Smooth = true;

            plot.Add.ColorBar(heatmap);

            plot.Title("Barcode Position-Score Heatmap", size: 16);
            plot.XLabel("X Position", size: 13);
            plot.YLabel("Y Position", size: 13);

            ApplyTechChartTheme(plot);
            plot.Axes.Bottom.TickLabelStyle.Rotation = 45;

            // 如果希望热力图坐标与图像像素坐标方向一致（图像 Y 向下），反转 Y 轴：
            plot.Axes.SetLimitsY(maxY, minY);
        }

        // ===== 散点分析 =====

        public static void RenderScatter(Plot plot, IEnumerable<DbModel> data)
        {
            var xs = data.Select(m => m.ImageX).ToArray();
            var ys = data.Select(m => m.ImageY).ToArray();

            plot.Clear();
            var scatter = plot.Add.Scatter(xs, ys);
            scatter.LineWidth = 0;
            scatter.MarkerSize = 6;
            scatter.MarkerColor = ScottPlot.Color.FromHex(TechChartPalette[0]); // Cyan
            scatter.MarkerShape = MarkerShape.FilledCircle;
            scatter.MarkerLineColor = ScottPlot.Color.FromHex(TechChartPalette[0]);
            scatter.MarkerLineWidth = 0.5f;

            plot.Title("Coordinate Scatter Plot", size: 16);
            plot.XLabel("X Coordinate", size: 13);
            plot.YLabel("Y Coordinate", size: 13);

            plot.Axes.AutoScale();

            AddScatterOutliers(plot, xs, ys);

            ApplyTechChartTheme(plot);
            plot.Axes.Bottom.TickLabelStyle.Rotation = 45;
        }

        public static void RenderBarcodeScatter(Plot plot, IEnumerable<DbModel> data)
        {
            var xs = data.Select(m => m.ImageBarcodeX).ToArray();
            var ys = data.Select(m => m.ImageBarcodeY).ToArray();

            plot.Clear();
            var scatter = plot.Add.Scatter(xs, ys);
            scatter.LineWidth = 0;
            scatter.MarkerSize = 6;
            scatter.MarkerColor = ScottPlot.Color.FromHex(TechChartPalette[2]); // Orange
            scatter.MarkerShape = MarkerShape.FilledCircle;
            scatter.MarkerLineColor = ScottPlot.Color.FromHex(TechChartPalette[2]);
            scatter.MarkerLineWidth = 0.5f;

            plot.Title("Barcode Coordinate Scatter", size: 16);
            plot.XLabel("Barcode X Coordinate", size: 13);
            plot.YLabel("Barcode Y Coordinate", size: 13);

            plot.Axes.AutoScale();

            AddScatterOutliers(plot, xs, ys);

            ApplyTechChartTheme(plot);
            plot.Axes.Bottom.TickLabelStyle.Rotation = 45;
        }

        public static void RenderDistanceScoreScatter(Plot plot, IEnumerable<DbModel> data)
        {
            var distances = data.Select(m =>
                Math.Sqrt(Math.Pow(m.ImageBarcodeX - m.ImageX, 2) + Math.Pow(m.ImageBarcodeY - m.ImageY, 2))
            ).ToArray();

            var scores = data.Select(m => m.BarcodeScore).ToArray();

            plot.Clear();
            var scatter = plot.Add.Scatter(distances, scores);
            scatter.LineWidth = 0;
            scatter.MarkerSize = 6;
            scatter.MarkerColor = ScottPlot.Color.FromHex(TechChartPalette[5]); // Green
            scatter.MarkerShape = MarkerShape.FilledCircle;
            scatter.MarkerLineColor = ScottPlot.Color.FromHex(TechChartPalette[5]);
            scatter.MarkerLineWidth = 0.5f;

            plot.Title("Distance vs Barcode Score Scatter", size: 16);
            plot.XLabel("Distance", size: 13);
            plot.YLabel("Barcode Score", size: 13);

            plot.Axes.AutoScale();

            AddScatterOutliers(plot, distances, scores);

            ApplyTechChartTheme(plot);
            plot.Axes.Bottom.TickLabelStyle.Rotation = 45;
        }

        // ===== 智能分析叠加层 =====

        /// <summary>
        /// 在趋势图上叠加智能分析：均值线（深灰虚线）、±1σ 带（蓝色半透明）、异常点（红色高亮）。
        /// 异常点检测基于 Y 值的 Z-Score。统计数值显示在 Banner，此处仅保留视觉叠加层。
        /// </summary>
        private static void AddTrendAnalytics(Plot plot, double[] xs, double[] ys)
        {
            if (ys.Length < 3)
                return;

            var stats = ChartAnalyticsService.ComputeStats(ys);
            if (stats.StdDev <= double.Epsilon)
                return;

            // ±1σ 带（先画，避免遮挡均值线与散点）
            var span = plot.Add.HorizontalSpan(stats.Mean - stats.StdDev, stats.Mean + stats.StdDev);
            span.FillColor = s_stdBandColor;

            // 均值线（深灰虚线）
            var meanLine = plot.Add.HorizontalLine(stats.Mean);
            meanLine.Color = s_meanLineColor;
            meanLine.LineWidth = 1.5f;
            meanLine.LinePattern = LinePattern.Dashed;

            // 异常点高亮（红色，更大标记）
            var outlierIndices = ChartAnalyticsService.DetectOutlierIndices(ys);
            if (outlierIndices.Count > 0)
            {
                var ox = new double[outlierIndices.Count];
                var oy = new double[outlierIndices.Count];
                int i = 0;
                foreach (int idx in outlierIndices)
                {
                    ox[i] = xs[idx];
                    oy[i] = ys[idx];
                    i++;
                }
                var outlierScatter = plot.Add.Scatter(ox, oy);
                outlierScatter.LineWidth = 0;
                outlierScatter.MarkerSize = 10;
                outlierScatter.MarkerColor = s_outlierColor;
                outlierScatter.MarkerShape = MarkerShape.OpenCircle;
                outlierScatter.MarkerLineWidth = 2f;
                outlierScatter.MarkerLineColor = s_outlierColor;
            }
        }

        /// <summary>
        /// 在二维散点图上叠加异常点高亮（基于 Y 轴 Z-Score）。
        /// </summary>
        private static void AddScatterOutliers(Plot plot, double[] xs, double[] ys)
        {
            if (ys.Length < 3)
                return;

            var outlierIndices = ChartAnalyticsService.DetectOutlierIndices(ys);
            if (outlierIndices.Count == 0)
                return;

            var ox = new double[outlierIndices.Count];
            var oy = new double[outlierIndices.Count];
            int i = 0;
            foreach (int idx in outlierIndices)
            {
                ox[i] = xs[idx];
                oy[i] = ys[idx];
                i++;
            }
            var outlierScatter = plot.Add.Scatter(ox, oy);
            outlierScatter.LineWidth = 0;
            outlierScatter.MarkerSize = 10;
            outlierScatter.MarkerColor = s_outlierColor;
            outlierScatter.MarkerShape = MarkerShape.OpenCircle;
            outlierScatter.MarkerLineWidth = 2f;
            outlierScatter.MarkerLineColor = s_outlierColor;
        }

        /// <summary>
        /// 在直方图上叠加均值线（深灰虚线）。统计数值显示在 Banner，此处仅保留视觉叠加层。
        /// </summary>
        private static void AddHistogramAnalytics(Plot plot, double[] values)
        {
            if (values.Length < 3)
                return;

            var stats = ChartAnalyticsService.ComputeStats(values);
            if (stats.StdDev <= double.Epsilon)
                return;

            var meanLine = plot.Add.VerticalLine(stats.Mean);
            meanLine.Color = s_meanLineColor;
            meanLine.LineWidth = 1.5f;
            meanLine.LinePattern = LinePattern.Dashed;
        }
    }
}
