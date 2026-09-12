using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using MainAPP.Models;
using MainAPP.Services;
using Microsoft.Win32;
using ScottPlot.WPF;
using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
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
        // 2026-09-12 调试闭环改进：切片筛选 / NG 率摘要 / 异常点明细
        private List<DbModel> _filteredModels = [];
        private string? _selectedRecipe;
        private string? _selectedStation;
        private string? _selectedResult;
        private string _ngRateSummaryText = "-";
        private bool _hasOutliers;
        private ObservableCollection<OutlierRowVm> _outlierRecords = new();

        public ChartsViewModel(IEnumerable<DbModel> initialData)
        {
            _dbModels = initialData?.ToList() ?? new List<DbModel>();
            _filteredModels = _dbModels;
            RefreshSliceOptions();

            QueryCommand = new AsyncRelayCommand(LoadDataAsync, () => !IsLoading);
            TodayFilterCommand = new AsyncRelayCommand(TodayFilterAsync, () => !IsLoading);
            Last7DaysFilterCommand = new AsyncRelayCommand(Last7DaysFilterAsync, () => !IsLoading);
            Last30DaysFilterCommand = new AsyncRelayCommand(Last30DaysFilterAsync, () => !IsLoading);
            ClearFilterCommand = new AsyncRelayCommand(ClearFilterAsync, () => !IsLoading);
            ExportImagesCommand = new AsyncRelayCommand(ExportImagesAsync, () => !IsExporting);
            CopyOutliersCommand = new RelayCommand(CopyOutliers, () => HasOutliers);
            AskAiCommand = new RelayCommand(AskAi, () => HasOutliers);

            UpdateStatus();
            RunAnalytics();
        }

        /// <summary>
        /// 当前用于渲染/统计的数据集合（只读视图），供 View 渲染图表使用。
        /// 2026-09-12: 语义变更为"切片筛选后"的集合——配方/工位/结果筛选即时生效于所有图表。
        /// </summary>
        public IReadOnlyList<DbModel> DbModels => _filteredModels;

        /// <summary>切片前的完整数据集合（切片选项的数据源）。</summary>
        public IReadOnlyList<DbModel> AllModels => _dbModels;

        /// <summary>配方切片选项（数据集内 distinct，"全部" 表示不过滤）。</summary>
        public ObservableCollection<string> RecipeOptions { get; } = new();

        /// <summary>工位切片选项（数据集内 distinct，"全部" 表示不过滤）。</summary>
        public ObservableCollection<string> StationOptions { get; } = new();

        /// <summary>结果切片选项。</summary>
        public List<string> ResultOptions { get; } = ["全部", "OK", "NG"];

        /// <summary>当前配方切片（"全部" = 不过滤）。</summary>
        public string? SelectedRecipe
        {
            get => _selectedRecipe;
            set { if (SetProperty(ref _selectedRecipe, value)) ApplySlice(); }
        }

        /// <summary>当前工位切片（"全部" = 不过滤）。</summary>
        public string? SelectedStation
        {
            get => _selectedStation;
            set { if (SetProperty(ref _selectedStation, value)) ApplySlice(); }
        }

        /// <summary>当前结果切片（"全部" = 不过滤）。</summary>
        public string? SelectedResult
        {
            get => _selectedResult;
            set { if (SetProperty(ref _selectedResult, value)) ApplySlice(); }
        }

        /// <summary>NG 率摘要文本（无判定列数据时给出明确说明）。</summary>
        public string NgRateSummaryText
        {
            get => _ngRateSummaryText;
            private set => SetProperty(ref _ngRateSummaryText, value);
        }

        /// <summary>异常点明细集合（Z-Score > 2.5，按偏离度降序，最多 200 条）。</summary>
        public ObservableCollection<OutlierRowVm> OutlierRecords
        {
            get => _outlierRecords;
            private set => SetProperty(ref _outlierRecords, value);
        }

        /// <summary>是否存在异常点明细（控制复制/问 AI 按钮可用性）。</summary>
        public bool HasOutliers
        {
            get => _hasOutliers;
            private set
            {
                if (SetProperty(ref _hasOutliers, value))
                {
                    CopyOutliersCommand.NotifyCanExecuteChanged();
                    AskAiCommand.NotifyCanExecuteChanged();
                }
            }
        }

        /// <summary>请求跳转到 AI 助手标签页（View 订阅：复制摘要 + 切换主窗口标签）。</summary>
        public event Action? AiAssistRequested;

        /// <summary>复制异常点明细到剪贴板（含切片条件与 NG 率上下文）。</summary>
        public IRelayCommand CopyOutliersCommand { get; }

        /// <summary>复制分析摘要到剪贴板并请求跳转 AI 助手标签页。</summary>
        public IRelayCommand AskAiCommand { get; }

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
                _filteredModels = _dbModels;
                RefreshSliceOptions();
                // ApplySlice 内部完成统计/分析刷新；此处不重复触发 DataChanged（下方统一触发）
                ApplySlice(notifyView: false);
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
            // 2026-09-12: 统计口径改为切片筛选后的集合（与图表一致）
            if (_filteredModels.Count == 0)
            {
                RecordCount = 0;
                TimeRangeText = "-";
                AvgScoreText = "-";
                AvgSpeedText = "-";
                return;
            }

            RecordCount = _filteredModels.Count;
            var minTime = _filteredModels.Min(r => r.DetectTime);
            var maxTime = _filteredModels.Max(r => r.DetectTime);
            // M120: 时间范围显示转换为本地时间
            TimeRangeText = $"{minTime:yyyy-MM-dd HH:mm:ss}  ~  {maxTime:yyyy-MM-dd HH:mm:ss}";
            AvgScoreText = $"Barcode Score:{Math.Round(_filteredModels.Average(p => p.BarcodeScore), 3)},AI Score:{Math.Round(_filteredModels.Average(p => p.Score), 3)}";
            // P0-3: Speed 字段实际语义为"编码器报文间隔（ms）"而非"速度"，与传送带速度成反比
            AvgSpeedText = $"Interval:{Math.Round(_filteredModels.Average(p => p.Speed))}ms";
        }

        /// <summary>
        /// 执行智能分析：图表类型推荐与异常点汇总。
        /// 在构造函数与每次数据重新加载后调用，结果驱动 Banner UI。
        /// </summary>
        private void RunAnalytics()
        {
            // 图表类型推荐
            var recommendation = ChartAnalyticsService.RecommendChartType(_filteredModels);
            RecommendedTabIndex = recommendation.RecommendedTabIndex;
            RecommendationText = recommendation.Reason;

            // 异常点汇总
            var anomalies = ChartAnalyticsService.SummarizeAnomalies(_filteredModels);
            HasAnomalies = anomalies.HasAnomalies;
            AnomalySummaryText = anomalies.TotalRecords == 0
                ? "No data"
                : $"Score:{anomalies.ScoreOutliers}  Speed:{anomalies.SpeedOutliers}  Distance:{anomalies.DistanceOutliers}  (Total {anomalies.TotalOutliers}/{anomalies.TotalRecords})";

            // 2026-09-12 调试闭环改进：异常点明细 + NG 率摘要
            var outliers = ChartAnalyticsService.DetectOutlierRecords(_filteredModels);
            OutlierRecords = new ObservableCollection<OutlierRowVm>(
                outliers.Select(o => new OutlierRowVm
                {
                    Time = o.Time,
                    Barcode = o.Barcode,
                    Score = o.Score,
                    Speed = o.Speed,
                    Distance = o.Distance,
                    Reason = o.Reason,
                }));
            HasOutliers = OutlierRecords.Count > 0;

            var ng = ChartAnalyticsService.ComputeNgSummary(_filteredModels);
            NgRateSummaryText = !ng.HasResultData
                ? "本数据集无 Result 判定（追溯列上线前的旧数据不支持 NG 率分析）"
                : $"NG 率 {ng.NgRatePercent:0.0}%（NG {ng.NgCount} / 有判定 {ng.TotalCount}）";
        }

        /// <summary>
        /// 应用切片筛选并刷新统计/分析与图表。
        /// notifyView=false 时不触发重绘，由调用方（LoadDataAsync）统一触发。
        /// </summary>
        private void ApplySlice(bool notifyView = true)
        {
            IEnumerable<DbModel> query = _dbModels;
            if (!string.IsNullOrEmpty(_selectedRecipe) && _selectedRecipe != "全部")
                query = query.Where(m => m.RecipeName == _selectedRecipe);
            if (!string.IsNullOrEmpty(_selectedStation) && _selectedStation != "全部")
                query = query.Where(m => m.Station == _selectedStation);
            if (!string.IsNullOrEmpty(_selectedResult) && _selectedResult != "全部")
                query = query.Where(m => string.Equals(m.Result, _selectedResult, StringComparison.OrdinalIgnoreCase));

            _filteredModels = query.ToList();
            UpdateStatus();
            RunAnalytics();
            if (notifyView)
            {
                DataChanged?.Invoke();
            }
        }

        /// <summary>从完整数据集重建切片下拉选项；保持仍存在的当前选中项。</summary>
        private void RefreshSliceOptions()
        {
            // Select(n => n!): IsWhitespace 过滤已排除 null，null 容忍标记消除 CS8604
            var recipes = _dbModels
                .Select(m => m.RecipeName)
                .Where(n => !string.IsNullOrWhiteSpace(n))
                .Select(n => n!)
                .Distinct().OrderBy(n => n).ToList();
            var stations = _dbModels
                .Select(m => m.Station)
                .Where(n => !string.IsNullOrWhiteSpace(n))
                .Select(n => n!)
                .Distinct().OrderBy(n => n).ToList();

            RecipeOptions.Clear();
            RecipeOptions.Add("全部");
            foreach (var r in recipes) RecipeOptions.Add(r);
            StationOptions.Clear();
            StationOptions.Add("全部");
            foreach (var st in stations) StationOptions.Add(st);

            // 当前选中项已不在数据集中（新查询后）→ 回退"全部"
            if (!string.IsNullOrEmpty(_selectedRecipe) && _selectedRecipe != "全部" && !recipes.Contains(_selectedRecipe))
            {
                _selectedRecipe = "全部";
                OnPropertyChanged(nameof(SelectedRecipe));
            }
            if (!string.IsNullOrEmpty(_selectedStation) && _selectedStation != "全部" && !stations.Contains(_selectedStation))
            {
                _selectedStation = "全部";
                OnPropertyChanged(nameof(SelectedStation));
            }
        }

        /// <summary>切片条件摘要（复制/问 AI 的上下文前缀）。</summary>
        private string SliceSummaryText =>
            $"配方={_selectedRecipe ?? "全部"}, 工位={_selectedStation ?? "全部"}, 结果={_selectedResult ?? "全部"}";

        private void CopyOutliers()
        {
            var sb = new System.Text.StringBuilder();
            sb.AppendLine($"图表分析异常明细（{SliceSummaryText}）");
            sb.AppendLine($"时间范围: {TimeRangeText}    NG 率: {NgRateSummaryText}");
            sb.AppendLine($"共 {OutlierRecords.Count} 条异常点（Z-Score > 2.5，按偏离度降序，最多 200 条）:");
            foreach (var o in OutlierRecords)
            {
                sb.AppendLine($"[{o.Time:yyyy-MM-dd HH:mm:ss}] 条码={o.Barcode}  Score={o.Score:0.000}  Interval={o.Speed:0}ms  Distance={o.Distance:0.0}px  异常: {o.Reason}");
            }

            try
            {
                System.Windows.Clipboard.SetText(sb.ToString());
                NotificationService.Success("异常明细已复制到剪贴板");
            }
            catch (Exception ex)
            {
                LogService.Instance.Error($"复制异常明细失败: {ex}");
                NotificationService.Error("复制失败，请稍后重试。");
            }
        }

        private void AskAi()
        {
            var sb = new System.Text.StringBuilder();
            sb.AppendLine($"图表分析摘要（{SliceSummaryText}，共 {RecordCount} 条）:");
            sb.AppendLine($"- 时间范围: {TimeRangeText}");
            sb.AppendLine($"- {AnomalySummaryText}");
            sb.AppendLine($"- {NgRateSummaryText}");
            sb.AppendLine("- 异常点 Top（Z-Score > 2.5）:");
            foreach (var o in OutlierRecords.Take(8))
            {
                sb.AppendLine($"  [{o.Time:MM-dd HH:mm:ss}] 条码={o.Barcode} Score={o.Score:0.000} 异常: {o.Reason}");
            }

            try
            {
                System.Windows.Clipboard.SetText(sb.ToString());
                AiAssistRequested?.Invoke();
            }
            catch (Exception ex)
            {
                LogService.Instance.Error($"复制分析摘要失败: {ex}");
            }
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

    /// <summary>
    /// 异常点明细行（"质量复盘"标签页 DataGrid 绑定模型）。
    /// 集合整体重建式刷新，无需 INPC。
    /// </summary>
    public sealed class OutlierRowVm
    {
        public DateTime Time { get; init; }
        public string Barcode { get; init; } = "-";
        public double Score { get; init; }
        public double Speed { get; init; }
        public double Distance { get; init; }
        public string Reason { get; init; } = "-";
    }
}
