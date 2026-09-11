using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using MainAPP.Models;
using MainAPP.Services;
using Microsoft.Win32;
using ScottPlot.WPF;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading.Tasks;

namespace MainAPP.ViewModels
{
    /// <summary>
    /// 图表分析窗口的 ViewModel：负责数据加载、时间范围筛选、SVG 导出等业务逻辑。
    /// 替代原 ChartsView 中的 async void 方法（VSTHRD100），使用 AsyncRelayCommand 暴露异步操作。
    /// </summary>
    public partial class ChartsViewModel : ObservableObject
    {
        // L100: 图表导出默认尺寸
        private const int DefaultExportWidth = 800;
        private const int DefaultExportHeight = 600;
        // REVIEW-FIX: 无时间筛选时的加载上限，与 DatabaseViewModel.ChartLoadLimit 保持一致，
        // 防止全表加载 OOM（与 DatabaseViewModel 同名同值常量，保持图表内存有界）
        private const int ChartLoadLimit = 5000;

        private List<DbModel> _dbModels;
        private DateTime? _startTime;
        private DateTime? _endTime;
        private bool _isLoading;
        private bool _isExporting;
        private int _recordCount;
        private string _timeRangeText = "-";
        private string _avgScoreText = "-";
        private string _avgSpeedText = "-";
        // 智能分析摘要字段
        private string _recommendationText = "-";
        private int _recommendedTabIndex;
        private string _anomalySummaryText = "No anomalies";
        private bool _hasAnomalies;

        public ChartsViewModel(IEnumerable<DbModel> initialData)
        {
            _dbModels = initialData?.ToList() ?? new List<DbModel>();

            QueryCommand = new AsyncRelayCommand(LoadDataAsync, () => !IsLoading);
            TodayFilterCommand = new AsyncRelayCommand(TodayFilterAsync, () => !IsLoading);
            Last7DaysFilterCommand = new AsyncRelayCommand(Last7DaysFilterAsync, () => !IsLoading);
            Last30DaysFilterCommand = new AsyncRelayCommand(Last30DaysFilterAsync, () => !IsLoading);
            ClearFilterCommand = new AsyncRelayCommand(ClearFilterAsync, () => !IsLoading);
            ExportImagesCommand = new AsyncRelayCommand(ExportImagesAsync, () => !IsExporting);

            UpdateStatus();
            RunAnalytics();
        }

        /// <summary>
        /// 当前已加载的数据集合（只读视图），供 View 渲染图表使用。
        /// </summary>
        public IReadOnlyList<DbModel> DbModels => _dbModels;

        /// <summary>
        /// 起始日期（包含），null 表示不限制下限
        /// </summary>
        public DateTime? StartTime
        {
            get => _startTime;
            set => SetProperty(ref _startTime, value);
        }

        /// <summary>
        /// 结束日期（包含），null 表示不限制上限
        /// </summary>
        public DateTime? EndTime
        {
            get => _endTime;
            set => SetProperty(ref _endTime, value);
        }

        /// <summary>
        /// 数据加载中标志，用于禁用筛选按钮并设置等待光标
        /// </summary>
        public bool IsLoading
        {
            get => _isLoading;
            set
            {
                if (SetProperty(ref _isLoading, value))
                {
                    QueryCommand.NotifyCanExecuteChanged();
                    TodayFilterCommand.NotifyCanExecuteChanged();
                    Last7DaysFilterCommand.NotifyCanExecuteChanged();
                    Last30DaysFilterCommand.NotifyCanExecuteChanged();
                    ClearFilterCommand.NotifyCanExecuteChanged();
                }
            }
        }

        /// <summary>
        /// 导出进行中标志，用于禁用导出按钮并显示"导出中..."反馈
        /// </summary>
        public bool IsExporting
        {
            get => _isExporting;
            set
            {
                if (SetProperty(ref _isExporting, value))
                {
                    ExportImagesCommand.NotifyCanExecuteChanged();
                    OnPropertyChanged(nameof(ExportButtonText));
                }
            }
        }

        /// <summary>
        /// 导出按钮文案，随 IsExporting 状态切换
        /// </summary>
        public string ExportButtonText => _isExporting ? "Exporting..." : "Export as SVG";

        /// <summary>记录总数</summary>
        public int RecordCount
        {
            get => _recordCount;
            set => SetProperty(ref _recordCount, value);
        }

        /// <summary>时间范围显示文本</summary>
        public string TimeRangeText
        {
            get => _timeRangeText;
            set => SetProperty(ref _timeRangeText, value);
        }

        /// <summary>平均分数显示文本</summary>
        public string AvgScoreText
        {
            get => _avgScoreText;
            set => SetProperty(ref _avgScoreText, value);
        }

        /// <summary>平均编码器报文间隔显示文本（毫秒，与传送带速度成反比）</summary>
        public string AvgSpeedText
        {
            get => _avgSpeedText;
            set => SetProperty(ref _avgSpeedText, value);
        }

        /// <summary>智能图表类型推荐说明（含推荐标签页索引与原因）</summary>
        public string RecommendationText
        {
            get => _recommendationText;
            set => SetProperty(ref _recommendationText, value);
        }

        /// <summary>推荐的默认标签页索引，View 可据此切换 Tab</summary>
        public int RecommendedTabIndex
        {
            get => _recommendedTabIndex;
            set => SetProperty(ref _recommendedTabIndex, value);
        }

        /// <summary>异常点汇总显示文本（分数/速度/距离三维度异常计数）</summary>
        public string AnomalySummaryText
        {
            get => _anomalySummaryText;
            set => SetProperty(ref _anomalySummaryText, value);
        }

        /// <summary>是否检测到异常点，用于驱动 Banner 颜色与图标</summary>
        public bool HasAnomalies
        {
            get => _hasAnomalies;
            set => SetProperty(ref _hasAnomalies, value);
        }

        public IAsyncRelayCommand QueryCommand { get; }
        public IAsyncRelayCommand TodayFilterCommand { get; }
        public IAsyncRelayCommand Last7DaysFilterCommand { get; }
        public IAsyncRelayCommand Last30DaysFilterCommand { get; }
        public IAsyncRelayCommand ClearFilterCommand { get; }
        public IAsyncRelayCommand ExportImagesCommand { get; }

        /// <summary>
        /// View 注册的"渲染所有图表"回调，导出前调用以确保所有 Plot 包含最新内容。
        /// </summary>
        public Action? RenderAllChartsCallback { get; set; }

        /// <summary>
        /// View 注册的"获取导出项"回调，返回 (WpfPlot, 文件名) 元组集合。
        /// </summary>
        public Func<IEnumerable<(WpfPlot Plot, string FileName)>>? GetExportItemsCallback { get; set; }

        /// <summary>
        /// 数据重新加载后触发，View 订阅以重新渲染当前标签页。
        /// </summary>
        public event Action? DataChanged;

        /// <summary>
        /// 按当前时间范围异步重新加载数据并通知 View 重绘。
        /// 替代原 async void ReloadDataAsync（VSTHRD100）。
        /// </summary>
        private async Task LoadDataAsync()
        {
            if (_isLoading) return;
            IsLoading = true;
            try
            {
                // DatePicker 仅选择日期，将起始日期当作 00:00:00、结束日期当作 23:59:59
                DateTime? startTime = StartTime?.Date;
                DateTime? endTime = EndTime?.Date.AddDays(1).AddSeconds(-1);

                List<DbModel> items;
                if (startTime.HasValue || endTime.HasValue)
                {
                    // 复用 SearchAsync 的可空时间范围筛选（其余筛选参数留空）
                    items = await BarcodeDataService.Instance
                        .SearchAsync(startTime: startTime, endTime: endTime)
                        .ConfigureAwait(true);
                }
                else
                {
                    // REVIEW-FIX: 无时间筛选时不再 GetAllAsync 全表加载（本系统每帧入库，累计可达
                    // 百万级，全量加载有 OOM 风险）。改为取最近 ChartLoadLimit 条——图表展示的
                    // 是近期趋势，语义一致且内存有界。需要全量统计时请显式指定时间范围。
                    items = await BarcodeDataService.Instance
                        .GetRecentAsync(ChartLoadLimit)
                        .ConfigureAwait(true);
                }

                _dbModels = items ?? new List<DbModel>();
                UpdateStatus();
                RunAnalytics();
                // 通知 View 清除已渲染缓存并重绘当前标签页
                DataChanged?.Invoke();
            }
            catch (Exception ex)
            {
                LogService.Instance.Error($"Chart data load failed: {ex}");
                NotificationService.Error($"Data load failed: {ex.Message}");
            }
            finally
            {
                IsLoading = false;
            }
        }

        // 时间范围快捷命令：今日
        private async Task TodayFilterAsync()
        {
            var today = DateTime.Today;
            StartTime = today;
            EndTime = today;
            await LoadDataAsync().ConfigureAwait(true);
        }

        // 时间范围快捷命令：最近7天
        private async Task Last7DaysFilterAsync()
        {
            var end = DateTime.Today;
            var start = end.AddDays(-6);
            StartTime = start;
            EndTime = end;
            await LoadDataAsync().ConfigureAwait(true);
        }

        // 时间范围快捷命令：最近30天
        private async Task Last30DaysFilterAsync()
        {
            var end = DateTime.Today;
            var start = end.AddDays(-29);
            StartTime = start;
            EndTime = end;
            await LoadDataAsync().ConfigureAwait(true);
        }

        // 清除时间筛选并加载全部数据
        private async Task ClearFilterAsync()
        {
            StartTime = null;
            EndTime = null;
            await LoadDataAsync().ConfigureAwait(true);
        }

        /// <summary>
        /// 导出所有图表为 SVG 文件。
        /// 替代原 async void ExportImages_Click（VSTHRD100）。
        /// </summary>
        private async Task ExportImagesAsync()
        {
            try
            {
                var dialog = new OpenFolderDialog();
                dialog.Title = "Select Folder";
                dialog.Multiselect = false;
                if (dialog.ShowDialog() != true)
                    return;

                string folderPath = dialog.FolderName;

                IsExporting = true;
                // 导出前确保所有标签页的图表都已渲染
                RenderAllChartsCallback?.Invoke();

                var exportItems = GetExportItemsCallback?.Invoke();
                if (exportItems == null)
                    return;

                // M256: 并行导出图表。Plot 非线程安全，SVG 字符串在 UI 线程生成，文件写入在后台并行执行。
                var exportTasks = exportItems
                    .Select(item => ExportPlotAsImage(item.Plot, Path.Combine(folderPath, item.FileName)))
                    .ToArray();

                await Task.WhenAll(exportTasks).ConfigureAwait(true);
                NotificationService.Success($"Charts exported as SVG files: {folderPath}");
            }
            catch (Exception ex)
            {
                LogService.Instance.Error($"Chart export failed: {ex}");
                NotificationService.Error($"Export failed: {ex.Message}");
            }
            finally
            {
                IsExporting = false;
            }
        }

        // M256: Plot 非线程安全，在 UI 线程生成 SVG 字符串，后台线程仅负责写入文件
        private static async Task ExportPlotAsImage(WpfPlot plot, string filePath)
        {
            var width = (int)plot.ActualWidth;
            var height = (int)plot.ActualHeight;
            // L100: 使用提取的默认尺寸常量
            if (width <= 0) width = DefaultExportWidth;
            if (height <= 0) height = DefaultExportHeight;
            var svg = plot.Plot.GetSvgHtml(width, height);
            await Task.Run(() => File.WriteAllText(filePath, svg)).ConfigureAwait(true);
        }

        // 根据当前数据集合更新状态栏统计信息
        private void UpdateStatus()
        {
            if (_dbModels.Count == 0)
            {
                RecordCount = 0;
                TimeRangeText = "-";
                AvgScoreText = "-";
                AvgSpeedText = "-";
                return;
            }

            RecordCount = _dbModels.Count;
            var minTime = _dbModels.Min(r => r.DetectTime);
            var maxTime = _dbModels.Max(r => r.DetectTime);
            // M120: 时间范围显示转换为本地时间
            TimeRangeText = $"{minTime:yyyy-MM-dd HH:mm:ss}  ~  {maxTime:yyyy-MM-dd HH:mm:ss}";
            AvgScoreText = $"Barcode Score:{Math.Round(_dbModels.Average(p => p.BarcodeScore), 3)},AI Score:{Math.Round(_dbModels.Average(p => p.Score), 3)}";
            // P0-3: Speed 字段实际语义为"编码器报文间隔（ms）"而非"速度"，与传送带速度成反比
            AvgSpeedText = $"Interval:{Math.Round(_dbModels.Average(p => p.Speed))}ms";
        }

        /// <summary>
        /// 执行智能分析：图表类型推荐与异常点汇总。
        /// 在构造函数与每次数据重新加载后调用，结果驱动 Banner UI。
        /// </summary>
        private void RunAnalytics()
        {
            // 图表类型推荐
            var recommendation = ChartAnalyticsService.RecommendChartType(_dbModels);
            RecommendedTabIndex = recommendation.RecommendedTabIndex;
            RecommendationText = recommendation.Reason;

            // 异常点汇总
            var anomalies = ChartAnalyticsService.SummarizeAnomalies(_dbModels);
            HasAnomalies = anomalies.HasAnomalies;
            AnomalySummaryText = anomalies.TotalRecords == 0
                ? "No data"
                : $"Score:{anomalies.ScoreOutliers}  Speed:{anomalies.SpeedOutliers}  Distance:{anomalies.DistanceOutliers}  (Total {anomalies.TotalOutliers}/{anomalies.TotalRecords})";
        }

        /// <summary>
        /// 在 View 完全加载后调用：弹出异常预警通知（仅一次）。
        /// 避免在构造函数中过早调用 Growl，此时 GrowlParent 容器尚未就绪。
        /// </summary>
        public void NotifyAnomaliesIfNeeded()
        {
            if (_hasAnomalies && !_analyticsNotified)
            {
                _analyticsNotified = true;
                NotificationService.Warning($"Detected potential outliers (Z-Score > 2.5). Highlighted in red on charts. See analytics banner for details.");
            }
        }

        // 标记是否已弹出异常预警通知，避免每次重新加载重复打扰
        private bool _analyticsNotified;
    }
}
