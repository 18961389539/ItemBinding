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
            MainTabControl.SelectionChanged += MainTabControl_SelectionChanged;

            _viewModel = new ChartsViewModel(dbModels);
            // View 向 ViewModel 注册需要访问 WpfPlot 控件的回调（渲染与导出）
            _viewModel.RenderAllChartsCallback = RenderAllCharts;
            _viewModel.GetExportItemsCallback = GetExportItems;
            _viewModel.DataChanged += ViewModel_DataChanged;
            _viewModel.PropertyChanged += ViewModel_PropertyChanged;
            // 2026-09-12 调试闭环改进：问 AI 助手（复制摘要 + 跳转 AI 标签页）
            _viewModel.AiAssistRequested += ViewModel_AiAssistRequested;
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
    }
}
