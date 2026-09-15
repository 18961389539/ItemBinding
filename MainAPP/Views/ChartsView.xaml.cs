using MainAPP.Models;
using MainAPP.Services;
using MainAPP.ViewModels;
using ScottPlot.WPF;
using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Windows;
using System.Windows.Controls;

namespace MainAPP.Views
{
    /// <summary>
    /// Interaction logic for ChartsView.xaml
    /// M328c: 实现 IDisposable 以在窗口关闭时清理数据集合与事件订阅，避免内存泄漏
    /// </summary>
    public partial class ChartsView : Window, IDisposable
    {
        private readonly ChartsViewModel _viewModel;
        private readonly HashSet<int> _renderedTabIndexes = [];
        private bool _isDataReady;
        // H94: Dispose 守卫，防止 Closed 与显式 Dispose 重复调用导致状态不一致
        private bool _isDisposed;

        public ChartsView(IEnumerable<DbModel> dbModels)
        {
            InitializeComponent();
            // 2026-09-15 分辨率适配：XAML 声明的 1600×1000 是按大屏设计的，
            // 在 1024×768 上会超出屏幕且原 MinWidth=1200 让用户无法拖拽缩小。
            // 这里按当前工作区夹取（同时下调 MinWidth/MinHeight），细节见 WindowSizing。
            WindowSizing.Apply(this, Width, Height);
            MainTabControl.SelectionChanged += MainTabControl_SelectionChanged;

            _viewModel = new ChartsViewModel(dbModels);
            // View 向 ViewModel 注册需要访问 WpfPlot 控件的回调（渲染与导出）
            _viewModel.RenderAllChartsCallback = RenderAllCharts;
            _viewModel.GetExportItemsCallback = GetExportItems;
            _viewModel.DataChanged += ViewModel_DataChanged;
            _viewModel.PropertyChanged += ViewModel_PropertyChanged;
            // 2026-09-12 调试闭环改进：问 AI 助手（复制摘要 + 跳转 AI 标签页）
            _viewModel.AiAssistRequested += ViewModel_AiAssistRequested;
            // 2026-09-13: SPC 控制图渲染 / 日报生成落盘提示
            _viewModel.SpcRenderer = RenderSpc;
            _viewModel.ReportDone = OnReportDone;
            DataContext = _viewModel;

            Loaded += ChartsView_Loaded;
            // M55: 窗口关闭时取消订阅，避免内存泄漏
            Closed += ChartsView_Closed;
        }

        // M320c: 命名方法替代 lambda 订阅 Closed 事件
        // M328c/L412c: 关闭时清理数据集合，释放内存
        private void ChartsView_Closed(object? sender, EventArgs e)
        {
            MainTabControl.SelectionChanged -= MainTabControl_SelectionChanged;
            // H94: 清理逻辑统一收敛到 Dispose，避免重复 Clear 调用
            Dispose();
        }

        // M185: Loaded 事件处理器加 try-catch，避免渲染异常终止进程
        private void ChartsView_Loaded(object sender, RoutedEventArgs e)
        {
            // M319c: 首行取消 Loaded 订阅，避免重复触发
            Loaded -= ChartsView_Loaded;
            try
            {
                _isDataReady = true;
                // 智能推荐：切换到推荐标签页（仅初始化时，不覆盖用户后续操作）
                if (_viewModel.RecommendedTabIndex >= 0
                    && _viewModel.RecommendedTabIndex < MainTabControl.Items.Count)
                {
                    MainTabControl.SelectedIndex = _viewModel.RecommendedTabIndex;
                }
                RenderTab(MainTabControl.SelectedIndex >= 0 ? MainTabControl.SelectedIndex : 0);
                // 智能分析：弹出异常预警通知（仅一次）
                _viewModel.NotifyAnomaliesIfNeeded();
            }
            catch (Exception ex)
            {
                LogService.Instance.Error($"Chart load failed: {ex}");
            }
        }

        // 响应 ViewModel 状态变化：加载期间切换等待光标
        private void ViewModel_PropertyChanged(object? sender, PropertyChangedEventArgs e)
        {
            if (e.PropertyName == nameof(ChartsViewModel.IsLoading))
            {
                Cursor = _viewModel.IsLoading ? System.Windows.Input.Cursors.Wait : null;
            }
        }

        // 响应 ViewModel 数据重新加载：清除已渲染缓存并重绘当前标签页
        private void ViewModel_DataChanged()
        {
            _renderedTabIndexes.Clear();
            RenderTab(MainTabControl.SelectedIndex >= 0 ? MainTabControl.SelectedIndex : 0);
        }

        private void MainTabControl_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            if (e.Source != MainTabControl)
            {
                return;
            }

            if (!_isDataReady)
            {
                return;
            }

            RenderTab(MainTabControl.SelectedIndex);
        }

        private void RenderTab(int tabIndex)
        {
            if (!_renderedTabIndexes.Add(tabIndex))
            {
                return;
            }

            switch (tabIndex)
            {
                case 0:
                    RenderTrendCharts();
                    break;
                case 1:
                    RenderDistributionCharts();
                    break;
                case 2:
                    RenderBarcodeCharts();
                    break;
                case 3:
                    RenderScatterCharts();
                    break;
                case 4:
                    RenderQualityCharts();
                    break;
            }
        }

        private void RenderAllCharts()
        {
            RenderTab(0);
            RenderTab(1);
            RenderTab(2);
            RenderTab(3);
            RenderTab(4);
        }

        private void RenderTrendCharts()
        {
            var data = _viewModel.DbModels;
            ChartRenderingService.RenderScoreOverTime(ScoreOverTimePlot.Plot, data);
            ScoreOverTimePlot.Refresh();
            ChartRenderingService.RenderSpeedOverTime(SpeedOverTimePlot.Plot, data);
            SpeedOverTimePlot.Refresh();
            ChartRenderingService.RenderCostTime(CostTimePlot.Plot, data);
            CostTimePlot.Refresh();
            ChartRenderingService.RenderDistanceOverTime(DistanceOverTimePlot.Plot, data);
            DistanceOverTimePlot.Refresh();
        }

        private void RenderDistributionCharts()
        {
            var data = _viewModel.DbModels;
            ChartRenderingService.RenderScoreDistribution(ScorePlot.Plot, data);
            ScorePlot.Refresh();
            ChartRenderingService.RenderSpeedDistribution(SpeedPlot.Plot, data);
            SpeedPlot.Refresh();
            ChartRenderingService.RenderAreaHistogram(AreaPlot.Plot, data);
            AreaPlot.Refresh();
            ChartRenderingService.RenderAngleHistogram(AnglePlot.Plot, data);
            AnglePlot.Refresh();
        }

        private void RenderBarcodeCharts()
        {
            var data = _viewModel.DbModels;
            ChartRenderingService.RenderBarcodeScoreDistribution(BarcodeScorePlot.Plot, data);
            BarcodeScorePlot.Refresh();
            ChartRenderingService.RenderBarcodeScoreOverTime(BarcodeScoreOverTimePlot.Plot, data);
            BarcodeScoreOverTimePlot.Refresh();
            ChartRenderingService.RenderDistanceHistogram(DistanceHistogramPlot.Plot, data);
            DistanceHistogramPlot.Refresh();
            ChartRenderingService.RenderBarcodePositionScoreHeatmap(BarcodePositionScoreHeatmapPlot.Plot, data);
            BarcodePositionScoreHeatmapPlot.Refresh();
        }

        private void RenderScatterCharts()
        {
            var data = _viewModel.DbModels;
            ChartRenderingService.RenderScatter(ScatterPlot.Plot, data);
            ScatterPlot.Refresh();
            ChartRenderingService.RenderBarcodeScatter(BarcodeScatterPlot.Plot, data);
            BarcodeScatterPlot.Refresh();
            ChartRenderingService.RenderDistanceScoreScatter(DistanceScoreScatterPlot.Plot, data);
            DistanceScoreScatterPlot.Refresh();
        }

        // 2026-09-12 调试闭环改进：质量复盘标签页（NG 率小时趋势）
        private void RenderQualityCharts()
        {
            ChartRenderingService.RenderNgRateOverTime(NgRatePlot.Plot, _viewModel.DbModels);
            NgRatePlot.Refresh();
        }

        // 供 ViewModel 导出回调使用：返回 (WpfPlot, 文件名) 列表
        private IEnumerable<(WpfPlot Plot, string FileName)> GetExportItems()
        {
            return new (WpfPlot Plot, string FileName)[]
            {
                (CostTimePlot, "CostTimeChart.svg"),
                (AreaPlot, "AreaDistribution.svg"),
                (ScatterPlot, "XYScatter.svg"),
                (AnglePlot, "AngleDistribution.svg"),
                (BarcodeScorePlot, "BarcodeScoreDistribution.svg"),
                (ScorePlot, "ScoreDistribution.svg"),
                (SpeedPlot, "SpeedDistribution.svg"),
                (BarcodeScatterPlot, "BarcodeScatter.svg"),
                (ScoreOverTimePlot, "ScoreOverTime.svg"),
                (SpeedOverTimePlot, "SpeedOverTime.svg"),
                (BarcodeScoreOverTimePlot, "BarcodeScoreOverTime.svg"),
                (DistanceOverTimePlot, "DistanceOverTime.svg"),
                (DistanceHistogramPlot, "DistanceHistogram.svg"),
                // M136: 补充之前缺失的两个图表导出
                (BarcodePositionScoreHeatmapPlot, "BarcodePositionScoreHeatmap.svg"),
                (DistanceScoreScatterPlot, "DistanceScoreScatter.svg"),
                (NgRatePlot, "NgRateTrend.svg"),
            };
        }

        // M328c: IDisposable 实现，清理事件订阅与渲染缓存
        // H94: 添加 _isDisposed 守卫确保幂等，防止 Closed 与显式 Dispose 重复调用
        public void Dispose()
        {
            if (_isDisposed) return;
            _isDisposed = true;
            _viewModel.DataChanged -= ViewModel_DataChanged;
            _viewModel.PropertyChanged -= ViewModel_PropertyChanged;
            _viewModel.AiAssistRequested -= ViewModel_AiAssistRequested;
            _renderedTabIndexes.Clear();
        }

        // 问 AI 助手：摘要已在 VM 复制到剪贴板，此处切换主窗口 AI 标签页
        private void ViewModel_AiAssistRequested()
        {
            if (System.Windows.Application.Current?.MainWindow is MainWindow mainWindow)
            {
                mainWindow.ActivateAiChatTab();
                NotificationService.Info("分析摘要已复制到剪贴板，可在 AI 助手中粘贴提问。");
            }
        }

        // ─────────────────────────────────────────────────────────────
        // 2026-09-13: SPC 控制图渲染 / 日报生成提示
        // ─────────────────────────────────────────────────────────────

        private void RenderSpc(MainAPP.Services.SpcResult? r)
        {
            var plot = SpcPlot.Plot;
            plot.Clear();
            if (r is null || r.MeanValues.Length == 0)
            {
                plot.Title("SPC 控制图（无数据）");
                SpcPlot.Refresh();
                return;
            }

            var xs = Enumerable.Range(1, r.MeanValues.Length).Select(i => (double)i).ToArray();
            plot.Add.Scatter(xs, r.MeanValues, color: ScottPlot.Color.FromHex("#22A5F7"));
            var cl = plot.Add.HorizontalLine(r.CenterLine, color: ScottPlot.Color.FromHex("#3ddc84"));
            var ucl = plot.Add.HorizontalLine(r.Ucl, color: ScottPlot.Color.FromHex("#ff6b6b"));
            var lcl = plot.Add.HorizontalLine(r.Lcl, color: ScottPlot.Color.FromHex("#ff6b6b"));
            // 判异点标红
            if (r.AlarmIndices.Count > 0)
            {
                var ax = r.AlarmIndices.Select(i => (double)i).ToArray();
                var ay = r.AlarmIndices.Select(i => r.MeanValues[i - 1]).ToArray();
                plot.Add.Scatter(ax, ay, color: ScottPlot.Color.FromHex("#e05252"));
            }

            plot.Title($"X̄ 控制图（CL={r.CenterLine:F3} UCL={r.Ucl:F3} LCL={r.Lcl:F3}" + (r.Cpk is { } cpk ? $" CPK={cpk:F2}" : "") + "）");
            plot.Axes.AutoScale();
            SpcPlot.Refresh();
        }

        private void OnReportDone(string? path)
        {
            if (path is null)
            {
                MessageBox.Show("日报生成失败，详见日志。", "日报", MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }

            var result = MessageBox.Show($"日报已生成：\n{path}\n\n打开所在文件夹？", "日报",
                MessageBoxButton.YesNo, MessageBoxImage.Information);
            if (result == MessageBoxResult.Yes)
            {
                _ = System.Diagnostics.Process.Start("explorer.exe", $"/select,\"{path}\"");
            }
        }
    }
}
