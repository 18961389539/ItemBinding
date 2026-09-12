using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using MainAPP.Models;
using MainAPP.Services;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Globalization;
using System.Runtime.CompilerServices;
using System.Windows;
using System.Windows.Data;
using System.Windows.Threading;

namespace MainAPP.ViewModels
{
    /// <summary>
    /// LogView 的 ViewModel，承载日志检索/筛选/分页/清理等业务逻辑。
    /// L394b: 从 LogView.xaml.cs 抽离业务逻辑，View 仅保留 UI 事件转发。
    /// H85a: 实现 IDisposable 以释放 DispatcherTimer 资源，避免泄漏。
    /// 所有原 async void 业务方法改为 AsyncRelayCommand，消除 VSTHRD100 警告。
    /// 2026-09-12: 新增级别胶囊计数、最近 24 小时分布条（点击套用小时筛选）、
    /// 实时跟随自动刷新与选中日志派生状态（HasSelectedLog）。
    /// </summary>
    public partial class LogViewModel : ObservableObject, IDisposable
    {
        // 分页状态：PageSize 默认 500 条，_currentPage 从 1 开始
        private const int PageSize = 500;
        // M257: 搜索防抖延迟（毫秒），延迟刷新避免频繁输入时重复过滤
        private const int SearchDebounceMs = 300;
        // 筛选条件变化防抖延迟（毫秒），批量合并多个属性变化（如 SetToday 同时设置起止时间）
        private const int FilterDebounceMs = 200;
        // 分布条图表区像素高度（各级别柱段按比例折算到此高度）
        private const double DistributionChartHeight = 88.0;
        // 实时跟随刷新间隔（秒）
        private const double LiveFollowIntervalSeconds = 3;

        private bool _isDisposed;
        private readonly ObservableCollection<LogEntry> _logs = [];
        private readonly ICollectionView _logsView;
        private string _searchText = string.Empty;
        private LogEntry? _selectedLog;
        private int _totalLogCount;
        private int _visibleLogCount;
        private string _latestLogTimeText = "暂无日志";
        private bool _isLoading;
        // 保留策略可视化相关字段
        private int _currentLogCount;
        private DateTime? _oldestLogTime;
        // M257: 搜索防抖定时器
        private DispatcherTimer? _searchDebounceTimer;
        // 筛选条件变化防抖定时器
        private DispatcherTimer? _filterDebounceTimer;
        // 时间范围 + 级别筛选状态
        private DateTime? _startTimeFilter;
        private DateTime? _endTimeFilter;
        private string? _selectedLevel;
        // 防止筛选条件变化时递归触发重载
        private bool _isReloading;
        // 分页状态：是否还有更多日志可加载（上一页加载满页即可能有更多）
        private int _currentPage = 1;
        private bool _hasMoreLogs;

        // === 2026-09-12 新增：级别胶囊计数 ===
        private int _errorCount;
        private int _warningCount;
        private int _informationCount;
        private int _debugCount;

        // === 2026-09-12 新增：最近 24 小时分布条 ===
        private readonly ObservableCollection<LogHourBucket> _hourlyBuckets = [];
        private bool _hasDistribution;
        private string _distributionSummaryText = "最近 24 小时暂无日志";

        // === 2026-09-12 新增：小时精确筛选（点击分布条柱块启用）===
        private DateTime? _hourFilterEnd;
        private string? _selectedHourRangeText;
        // SelectHour 批量设置日期时的守卫：防止日期 setter 误清小时筛选
        private bool _isSettingHourRange;

        // === 2026-09-12 新增：实时跟随 ===
        private bool _isLiveFollow;
        private DispatcherTimer? _liveFollowTimer;
        // 标记当前重载来自实时跟随（跳过 Loading 覆盖层与分布条重查）
        private bool _isLiveRefreshInProgress;

        public LogViewModel()
        {
            _logsView = CollectionViewSource.GetDefaultView(_logs);
            _logsView.Filter = FilterLog;
            // 筛选条件变化时自动重新加载日志
            PropertyChanged += OnFilterPropertyChanged;
        }

        public ICollectionView LogsView => _logsView;

        /// <summary>底层日志集合，供 XAML 绑定 Logs.Count 触发空状态切换</summary>
        public ObservableCollection<LogEntry> Logs => _logs;

        public string SearchText
        {
            get => _searchText;
            set
            {
                if (SetProperty(ref _searchText, value))
                {
                    // M257: 使用防抖延迟刷新，避免每次输入都触发过滤
                    ScheduleSearchRefresh();
                    // 通知搜索状态相关派生属性
                    OnPropertyChanged(nameof(IsSearching));
                    OnPropertyChanged(nameof(IsNoSearchMatch));
                    OnPropertyChanged(nameof(HasActiveFilter));
                    OnPropertyChanged(nameof(EmptyStateTitle));
                    OnPropertyChanged(nameof(EmptyStateHint));
                }
            }
        }

        /// <summary>时间范围筛选起始日期（包含）。null 表示不限制。</summary>
        public DateTime? StartTimeFilter
        {
            get => _startTimeFilter;
            set
            {
                if (SetProperty(ref _startTimeFilter, value))
                {
                    OnPropertyChanged(nameof(HasActiveFilter));
                    OnPropertyChanged(nameof(EmptyStateHint));
                    OnPropertyChanged(nameof(EmptyStateTitle));
                    // 手工改日期（非分布条点击）时清除小时精确筛选
                    ClearHourRangeIfNeeded();
                }
            }
        }

        /// <summary>时间范围筛选结束日期（包含当天）。null 表示不限制。</summary>
        public DateTime? EndTimeFilter
        {
            get => _endTimeFilter;
            set
            {
                if (SetProperty(ref _endTimeFilter, value))
                {
                    OnPropertyChanged(nameof(HasActiveFilter));
                    OnPropertyChanged(nameof(EmptyStateHint));
                    OnPropertyChanged(nameof(EmptyStateTitle));
                    ClearHourRangeIfNeeded();
                }
            }
        }

        /// <summary>级别筛选（Serilog 原始级别名，如 "Information"）。null/空 表示全部。</summary>
        public string? SelectedLevel
        {
            get => _selectedLevel;
            set
            {
                if (SetProperty(ref _selectedLevel, value))
                {
                    OnPropertyChanged(nameof(HasActiveFilter));
                    OnPropertyChanged(nameof(EmptyStateHint));
                    OnPropertyChanged(nameof(EmptyStateTitle));
                    // 通知级别胶囊激活态
                    OnPropertyChanged(nameof(IsErrorChipActive));
                    OnPropertyChanged(nameof(IsWarningChipActive));
                    OnPropertyChanged(nameof(IsInfoChipActive));
                    OnPropertyChanged(nameof(IsDebugChipActive));
                }
            }
        }

        /// <summary>可选级别列表，空字符串表示全部。</summary>
        public List<string> AvailableLevels { get; } = new()
        {
            "", // 空 = 全部
            "Verbose",
            "Debug",
            "Information",
            "Warning",
            "Error",
            "Fatal"
        };

        // === 级别胶囊计数（当前时间范围内，不含级别筛选本身）===

        /// <summary>错误级（Error + Fatal）条数</summary>
        public int ErrorCount { get => _errorCount; private set => SetProperty(ref _errorCount, value); }

        /// <summary>警告级条数</summary>
        public int WarningCount { get => _warningCount; private set => SetProperty(ref _warningCount, value); }

        /// <summary>信息级条数</summary>
        public int InformationCount { get => _informationCount; private set => SetProperty(ref _informationCount, value); }

        /// <summary>调试级（Debug + Verbose）条数</summary>
        public int DebugCount { get => _debugCount; private set => SetProperty(ref _debugCount, value); }

        /// <summary>错误胶囊计数显示文本（万级缩写）</summary>
        public string ErrorCountText => FormatLevelCount(ErrorCount);

        /// <summary>警告胶囊计数显示文本</summary>
        public string WarningCountText => FormatLevelCount(WarningCount);

        /// <summary>信息胶囊计数显示文本</summary>
        public string InfoCountText => FormatLevelCount(InformationCount);

        /// <summary>调试胶囊计数显示文本</summary>
        public string DebugCountText => FormatLevelCount(DebugCount);

        /// <summary>错误胶囊是否激活（SelectedLevel == Error）</summary>
        public bool IsErrorChipActive => SelectedLevel == "Error";

        /// <summary>警告胶囊是否激活</summary>
        public bool IsWarningChipActive => SelectedLevel == "Warning";

        /// <summary>信息胶囊是否激活</summary>
        public bool IsInfoChipActive => SelectedLevel == "Information";

        /// <summary>调试胶囊是否激活</summary>
        public bool IsDebugChipActive => SelectedLevel == "Debug";

        // === 最近 24 小时分布条 ===

        /// <summary>小时分布柱数据集合（共 24 桶，最早 24 小时前 → 当前小时）</summary>
        public ObservableCollection<LogHourBucket> HourlyBuckets => _hourlyBuckets;

        /// <summary>是否有分布数据（控制分布条图表/空状态切换）</summary>
        public bool HasDistribution { get => _hasDistribution; private set => SetProperty(ref _hasDistribution, value); }

        /// <summary>分布条摘要文本（标题右侧）</summary>
        public string DistributionSummaryText
        {
            get => _distributionSummaryText;
            private set => SetProperty(ref _distributionSummaryText, value);
        }

        // === 小时精确筛选 ===

        /// <summary>当前激活的小时筛选范围文本（如 "15:00-16:00"），null 表示未激活</summary>
        public string? SelectedHourRangeText
        {
            get => _selectedHourRangeText;
            private set => SetProperty(ref _selectedHourRangeText, value);
        }

        /// <summary>是否激活了小时精确筛选（控制工具栏徽章显示）</summary>
        public bool HasHourFilter => _hourFilterEnd.HasValue;

        // === 实时跟随 ===

        /// <summary>
        /// 实时跟随开关。开启后每 LiveFollowIntervalSeconds 秒静默重载一次日志并滚动到最新一条。
        /// 由 XAML CheckBox 双向绑定直接驱动（不走 Command，避免 IsChecked 双重翻转）。
        /// </summary>
        public bool IsLiveFollow
        {
            get => _isLiveFollow;
            set
            {
                if (SetProperty(ref _isLiveFollow, value))
                {
                    if (value)
                    {
                        EnsureLiveFollowTimer()?.Start();
                    }
                    else
                    {
                        _liveFollowTimer?.Stop();
                    }
                }
            }
        }

        /// <summary>是否有选中的日志（控制右侧详情抽屉显隐）</summary>
        public bool HasSelectedLog => SelectedLog is not null;

        /// <summary>实时跟随刷新完成后请求视图滚动到最新一条（日志按 id 倒序 = 第一行）</summary>
        public event Action? ScrollToNewestRequested;

        /// <summary>请求切换到 AI 助手标签页（详情抽屉「问 AI 助手」按钮触发）</summary>
        public event Action? AiAssistRequested;

        /// <summary>是否还有更多日志可加载（由分页加载结果驱动）</summary>
        public bool HasMoreLogs
        {
            get => _hasMoreLogs;
            private set => SetProperty(ref _hasMoreLogs, value);
        }

        /// <summary>是否正在搜索（关键字非空）。控制搜索框旁的"筛选中..."状态提示显示。</summary>
        public bool IsSearching => !string.IsNullOrWhiteSpace(SearchText);

        /// <summary>搜索关键字非空但筛选结果为空。控制"无匹配结果"橙色提示显示。</summary>
        public bool IsNoSearchMatch => IsSearching && VisibleLogCount == 0;

        /// <summary>是否存在任何生效的筛选条件（关键字 / 时间范围 / 级别）</summary>
        public bool HasActiveFilter =>
            IsSearching || StartTimeFilter.HasValue || EndTimeFilter.HasValue || !string.IsNullOrEmpty(SelectedLevel);

        /// <summary>空状态主标题：根据是否有筛选条件切换显示</summary>
        public string EmptyStateTitle => HasActiveFilter
            ? "未找到匹配的日志，请尝试修改筛选条件"
            : "暂无日志记录";

        /// <summary>空状态提示文本，根据筛选条件显示不同提示</summary>
        public string EmptyStateHint => HasActiveFilter
            ? "可尝试清除筛选条件或修改关键字"
            : "数据库中暂无日志记录";

        public LogEntry? SelectedLog
        {
            get => _selectedLog;
            set
            {
                if (SetProperty(ref _selectedLog, value))
                {
                    OnPropertyChanged(nameof(HasSelectedLog));
                }
            }
        }

        public int TotalLogCount
        {
            get => _totalLogCount;
            private set => SetProperty(ref _totalLogCount, value);
        }

        public int VisibleLogCount
        {
            get => _visibleLogCount;
            private set
            {
                if (SetProperty(ref _visibleLogCount, value))
                {
                    OnPropertyChanged(nameof(IsNoSearchMatch));
                }
            }
        }

        public string LatestLogTimeText
        {
            get => _latestLogTimeText;
            private set => SetProperty(ref _latestLogTimeText, value);
        }

        /// <summary>是否正在加载日志（控制 Loading 覆盖层显示）</summary>
        public bool IsLoading
        {
            get => _isLoading;
            private set => SetProperty(ref _isLoading, value);
        }

        /// <summary>
        /// 当前日志数据库总数（用于保留策略可视化）。
        /// 变更时同步通知派生属性 LogCountPercent / LogCountDisplay / IsLogCountWarning。
        /// </summary>
        public int CurrentLogCount
        {
            get => _currentLogCount;
            set
            {
                if (SetProperty(ref _currentLogCount, value))
                {
                    OnPropertyChanged(nameof(LogCountPercent));
                    OnPropertyChanged(nameof(LogCountDisplay));
                    OnPropertyChanged(nameof(IsLogCountWarning));
                }
            }
        }

        /// <summary>最早日志记录时间（用于保留策略可视化）</summary>
        public DateTime? OldestLogTime
        {
            get => _oldestLogTime;
            set
            {
                if (SetProperty(ref _oldestLogTime, value))
                {
                    OnPropertyChanged(nameof(OldestLogTimeDisplay));
                }
            }
        }

        /// <summary>日志数据库最大保留条数（来自 Settings）</summary>
        public int MaxLogCount => Settings.Instance.LogMaxCount;

        /// <summary>日志保留天数（来自 Settings）</summary>
        public int LogRetentionDays => Settings.Instance.LogRetentionDays;

        /// <summary>数据库使用率百分比（0-100），超过上限截断为 100</summary>
        public double LogCountPercent => MaxLogCount > 0
            ? Math.Min(100, (double)CurrentLogCount / MaxLogCount * 100)
            : 0;

        /// <summary>日志总数显示文本（当前 / 上限）</summary>
        public string LogCountDisplay => $"{CurrentLogCount:N0} / {MaxLogCount:N0}";

        /// <summary>最早记录时间显示文本</summary>
        public string OldestLogTimeDisplay => OldestLogTime?.ToString("yyyy-MM-dd HH:mm") ?? "无记录";

        /// <summary>日志总数是否达到预警阈值（>=80% 上限）</summary>
        public bool IsLogCountWarning => MaxLogCount > 0 && CurrentLogCount >= MaxLogCount * 0.8;

        /// <summary>保留策略说明文本</summary>
        public string RetentionPolicyDisplay =>
            $"保留 {LogRetentionDays} 天 / 上限 {MaxLogCount:N0} 条";

        // === Commands ===
        // 使用 [RelayCommand] 特性自动生成 XxxCommand 属性，替代原 async void 方法，消除 VSTHRD100 警告。

        /// <summary>重新加载日志（替代原 async void Refresh_Click / ReloadLogsAsync 调用链）</summary>
        [RelayCommand]
        private async Task ReloadLogsAsync()
        {
            await ReloadLogsCoreAsync().ConfigureAwait(true);
        }

        /// <summary>
        /// 重载核心：按当前筛选条件并行加载日志/总数/最早时间/级别计数，普通刷新同时重建分布条。
        /// 实时跟随刷新（_isLiveRefreshInProgress）时跳过 Loading 覆盖层与分布条重查（每 3 秒一次太重）。
        /// </summary>
        private async Task ReloadLogsCoreAsync()
        {
            IsLoading = !_isLiveRefreshInProgress;
            _isReloading = true;
            try
            {
                // 重置到第一页，按当前筛选条件加载
                _currentPage = 1;

                // 并行加载：日志列表、数据库真实总数、最早记录时间、级别计数
                var logsTask = LogDatabaseService.Instance.LoadLogsFilteredAsync(
                    StartTimeFilter, EndTimeFilter, SelectedLevel, PageSize, 0, _hourFilterEnd);
                var countTask = LogDatabaseService.Instance.GetLogCountAsync();
                var oldestTask = LogDatabaseService.Instance.GetOldestLogTimeAsync();
                var levelCountsTask = LogDatabaseService.Instance.GetLevelCountsAsync(StartTimeFilter, EndTimeFilter);
                // 分布条仅普通刷新时重建；实时跟随跳过
                var distributionTask = _isLiveRefreshInProgress
                    ? Task.CompletedTask
                    : RefreshDistributionCoreAsync();

                await Task.WhenAll(logsTask, countTask, oldestTask, levelCountsTask).ConfigureAwait(true);

                var logs = await logsTask.ConfigureAwait(true);
                var count = await countTask.ConfigureAwait(true);
                var oldest = await oldestTask.ConfigureAwait(true);
                var levelCounts = await levelCountsTask.ConfigureAwait(true);

                _logs.Clear();
                // L415c: ObservableCollection 不支持 AddRange，只能逐条添加，存在 O(n) 性能开销。
                // 暂不改（需引入 RangeObservableCollection 等自定义集合，改动较大）。
                foreach (var log in logs)
                {
                    _logs.Add(log);
                }
                // 设置数据库真实总数（而非 _logs.Count，后者受 PageSize 截断）
                TotalLogCount = count;
                CurrentLogCount = count;
                OldestLogTime = oldest;
                ApplyLevelCounts(levelCounts);
                // 上一页满页即可能有更多日志可加载
                HasMoreLogs = logs.Count == PageSize;
                RefreshView();

                if (_isLiveRefreshInProgress)
                {
                    ScrollToNewestRequested?.Invoke();
                }
            }
            catch (Exception ex)
            {
                LogService.Instance.Error($"加载日志失败: {ex}");
            }
            finally
            {
                IsLoading = false;
                _isReloading = false;
                // 实时跟随轮次结束：重启定时器（自限速，重载耗时会计入间隔）
                if (_isLiveRefreshInProgress)
                {
                    _isLiveRefreshInProgress = false;
                    if (IsLiveFollow && !_isDisposed)
                    {
                        _liveFollowTimer?.Start();
                    }
                }
            }
        }

        /// <summary>加载下一页日志并追加到列表末尾（替代原 async void LoadMoreAsync 调用链）</summary>
        [RelayCommand]
        private async Task LoadMoreAsync()
        {
            if (IsLoading) return;
            try
            {
                _currentPage++;
                var additional = await LogDatabaseService.Instance.LoadLogsFilteredAsync(
                    StartTimeFilter, EndTimeFilter, SelectedLevel,
                    PageSize, (_currentPage - 1) * PageSize, _hourFilterEnd).ConfigureAwait(true);

                foreach (var log in additional)
                {
                    _logs.Add(log);
                }
                // 上一页满页即可能有更多，否则已到末尾
                HasMoreLogs = additional.Count == PageSize;
                RefreshView();
            }
            catch (Exception ex)
            {
                LogService.Instance.Error($"加载更多日志失败: {ex}");
                // 回退页码，避免下次加载跳过当前页
                _currentPage--;
            }
        }

        /// <summary>
        /// 根据保留策略立即清理过期日志（替代原 async void CleanupNowAsync）。
        /// 先按天数删除，再按数量裁剪，最后刷新列表。
        /// </summary>
        [RelayCommand]
        private async Task CleanupNowAsync()
        {
            try
            {
                var retentionDays = Settings.Instance.LogRetentionDays;
                var maxCount = Settings.Instance.LogMaxCount;
                var deletedCount = 0;

                if (retentionDays > 0)
                {
                    var cutoff = DateTime.Now.AddDays(-retentionDays);
                    deletedCount += await LogDatabaseService.Instance.DeleteOlderThanAsync(cutoff).ConfigureAwait(true);
                }
                if (maxCount > 0)
                {
                    deletedCount += await LogDatabaseService.Instance.TrimToMaxCountAsync(maxCount).ConfigureAwait(true);
                }

                NotificationService.Success($"日志清理完成，共删除 {deletedCount} 条");
                await ReloadLogsAsync().ConfigureAwait(true);
            }
            catch (Exception ex)
            {
                LogService.Instance.Error($"日志清理失败: {ex}");
                NotificationService.Error($"清理失败: {ex.Message}");
            }
        }

        /// <summary>清空所有日志（替代原 async void ClearLogs_Click）</summary>
        [RelayCommand]
        private async Task ClearLogsAsync()
        {
            try
            {
                if (_logs.Count == 0)
                {
                    return;
                }

                // L106: 确认对话框使用 Question 图标而非 Warning。
                // 命令由 WPF 按钮触发，必在 UI 线程执行，可直接调用 NotificationService.Ask（居中于主窗口）。
                if (NotificationService.Ask("确认清空所有日志吗？", "确认") != MessageBoxResult.Yes)
                {
                    return;
                }

                // 记录清空前的数量，用于成功反馈
                int deletedCount = TotalLogCount;

                // L362: 改用异步版本，避免 sync-over-async 阻塞 UI 线程
                await LogDatabaseService.Instance.ClearLogsAsync().ConfigureAwait(true);
                LogService.Instance.ClearLogs();
                _logs.Clear();
                SelectedLog = null;
                // 数据库已清空，真实总数归零（UpdateStatistics 不再设置 TotalLogCount）
                TotalLogCount = 0;
                // 重置分页状态
                _currentPage = 1;
                HasMoreLogs = false;
                RefreshView();

                NotificationService.Success($"日志已清空，共删除 {deletedCount} 条");
            }
            catch (Exception ex)
            {
                LogService.Instance.Error($"清空日志失败: {ex}");
                NotificationService.Error($"清空日志失败: {ex.Message}");
            }
        }

        /// <summary>清除搜索关键字</summary>
        [RelayCommand]
        private void ClearSearch()
        {
            SearchText = string.Empty;
        }

        /// <summary>复制当前选中日志到剪贴板（替代原 CopySelected_Click）</summary>
        [RelayCommand]
        private void CopySelected()
        {
            if (SelectedLog is null)
            {
                NotificationService.Info("请先选择一条日志。");
                return;
            }

            CopyLogToClipboard(SelectedLog);
        }

        /// <summary>关闭详情抽屉（清除选中日志）</summary>
        [RelayCommand]
        private void ClearSelectedLog()
        {
            SelectedLog = null;
        }

        /// <summary>
        /// 问 AI 助手：复制选中日志全文到剪贴板并请求切换到 AI 助手标签页。
        /// 上下文经剪贴板传递（AI 面板为 WebView2 内嵌网页，直接预填消息需额外的协议桥，暂不引入）。
        /// </summary>
        [RelayCommand]
        private void AskAiAboutSelected()
        {
            if (SelectedLog is null)
            {
                return;
            }

            CopyLogToClipboard(SelectedLog);
            AiAssistRequested?.Invoke();
        }

        /// <summary>级别胶囊点击：同级别再点取消，否则切换到该级别</summary>
        [RelayCommand]
        private void SetLevelChip(string? level)
        {
            SelectedLevel = SelectedLevel == level ? null : level;
        }

        // 时间范围快捷筛选命令
        [RelayCommand]
        private void SetTodayFilter()
        {
            StartTimeFilter = DateTime.Today;
            EndTimeFilter = DateTime.Today;
            ScheduleFilterReload();
        }

        [RelayCommand]
        private void SetLast7DaysFilter()
        {
            StartTimeFilter = DateTime.Today.AddDays(-7);
            EndTimeFilter = DateTime.Today;
            ScheduleFilterReload();
        }

        [RelayCommand]
        private void SetLast30DaysFilter()
        {
            StartTimeFilter = DateTime.Today.AddDays(-30);
            EndTimeFilter = DateTime.Today;
            ScheduleFilterReload();
        }

        [RelayCommand]
        private void ClearFilter()
        {
            ClearHourRangeInternal();
            StartTimeFilter = null;
            EndTimeFilter = null;
            SelectedLevel = null;
            // 小时筛选被清除而日期本就为 null 时无属性变化，需显式触发重载（防抖会合并重复调用）
            ScheduleFilterReload();
        }

        /// <summary>分布条柱块点击：套用该小时的精确时间筛选；再次点击同柱块取消</summary>
        [RelayCommand]
        private void SelectHour(LogHourBucket? bucket)
        {
            if (bucket is null)
            {
                return;
            }

            var hourStart = bucket.HourStart;
            var hourEnd = hourStart.AddHours(1);
            var isReselect = _hourFilterEnd.HasValue && _hourFilterEnd.Value == hourEnd;

            // 日期筛选同步到该小时所在日期（守卫防止 setter 清掉小时筛选）
            _isSettingHourRange = true;
            StartTimeFilter = hourStart.Date;
            EndTimeFilter = hourStart.Date;
            _isSettingHourRange = false;

            if (isReselect)
            {
                ClearHourRangeInternal();
            }
            else
            {
                _hourFilterEnd = hourEnd;
                SelectedHourRangeText = $"{hourStart:HH:00}-{hourEnd:HH:00}";
            }

            RefreshBucketSelection();
            ScheduleFilterReload();
        }

        /// <summary>仅清除小时精确筛选（保留日期/级别筛选）</summary>
        [RelayCommand]
        private void ClearHourFilter()
        {
            ClearHourRangeInternal();
            RefreshBucketSelection();
            ScheduleFilterReload();
        }

        // === 内部业务逻辑 ===

        private void RefreshView()
        {
            _logsView.Refresh();
            UpdateStatistics();

            if (SelectedLog is not null && !_logsView.Contains(SelectedLog))
            {
                SelectedLog = null;
            }
        }

        // M257: 搜索防抖，延迟 300ms 后刷新视图，避免每次按键都触发过滤
        private void ScheduleSearchRefresh()
        {
            // H93: Dispose 后不再重建 DispatcherTimer，避免 Unloaded→Dispose 后切换回标签页时
            // 因 _searchDebounceTimer 为 null 而绕过守卫重新创建定时器，导致资源泄漏
            if (_isDisposed) return;
            if (_searchDebounceTimer is null)
            {
                _searchDebounceTimer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(SearchDebounceMs) };
                _searchDebounceTimer.Tick += SearchDebounceTimer_Tick;
            }
            _searchDebounceTimer.Stop();
            _searchDebounceTimer.Start();
        }

        // VSTHRD100: 保留为同步 void 事件处理器（DispatcherTimer.Tick 签名要求），不使用 async void。
        // 仅刷新视图（同步操作），无需 await。
        private void SearchDebounceTimer_Tick(object? sender, EventArgs e)
        {
            _searchDebounceTimer?.Stop();
            RefreshView();
        }

        private void UpdateStatistics()
        {
            // TotalLogCount 由 ReloadLogsAsync 从数据库真实计数获取，此处不再用 _logs.Count 覆盖
            VisibleLogCount = _logsView.Cast<object>().Count();

            var latestLog = _logs.Count > 0 ? _logs[0] : null;
            // M122/L104: 时间戳转换为本地时间，统一格式为 yyyy-MM-dd HH:mm:ss.fff
            LatestLogTimeText = latestLog is null
                ? "暂无日志"
                : latestLog.Timestamp.ToString("yyyy-MM-dd HH:mm:ss.fff", CultureInfo.InvariantCulture);
        }

        private bool FilterLog(object item)
        {
            if (item is not LogEntry logEntry)
            {
                return false;
            }

            // 级别筛选已下推至数据库查询（LoadLogsFilteredAsync），此处仅做关键字过滤
            if (string.IsNullOrWhiteSpace(SearchText))
            {
                return true;
            }

            return logEntry.Level.Contains(SearchText, StringComparison.OrdinalIgnoreCase)
                || logEntry.Message.Contains(SearchText, StringComparison.OrdinalIgnoreCase)
                || logEntry.RenderedMessage.Contains(SearchText, StringComparison.OrdinalIgnoreCase)
                // M288b: Exception/Properties 为 nullable，使用 ?.Contains(...) == true 兼容 null
                || logEntry.Exception?.Contains(SearchText, StringComparison.OrdinalIgnoreCase) == true
                || logEntry.Properties?.Contains(SearchText, StringComparison.OrdinalIgnoreCase) == true
                // L282: 使用预缓存的 TimestampText 避免每次过滤重复计算
                || logEntry.TimestampText.Contains(SearchText, StringComparison.OrdinalIgnoreCase);
        }

        /// <summary>
        /// 筛选条件变化时自动重新加载日志。
        /// 监听 StartTimeFilter/EndTimeFilter/SelectedLevel 三个属性。
        /// 使用防抖合并批量属性变化（如 SetTodayFilterCommand 同时设置起止时间）。
        /// </summary>
        private void OnFilterPropertyChanged(object? sender, PropertyChangedEventArgs e)
        {
            // 重载过程中属性变化由重载自身触发，忽略避免递归
            if (_isReloading || _isDisposed) return;
            // 分布条点击批量设置日期时不触发（SelectHour 末尾统一调度重载）
            if (_isSettingHourRange) return;
            if (e.PropertyName is not (nameof(StartTimeFilter) or nameof(EndTimeFilter) or nameof(SelectedLevel)))
                return;

            ScheduleFilterReload();
        }

        // 筛选条件变化防抖：延迟 200ms 后重载，合并连续的属性变化
        private void ScheduleFilterReload()
        {
            if (_isDisposed) return;
            if (_filterDebounceTimer is null)
            {
                _filterDebounceTimer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(FilterDebounceMs) };
                _filterDebounceTimer.Tick += FilterDebounceTimer_Tick;
            }
            _filterDebounceTimer.Stop();
            _filterDebounceTimer.Start();
        }

        // VSTHRD100: 保留为同步 void 事件处理器（DispatcherTimer.Tick 签名要求），不使用 async void。
        // 通过 ICommand.Execute 触发异步重载命令，命令内部已有 try-catch 处理异常。
        private void FilterDebounceTimer_Tick(object? sender, EventArgs e)
        {
            _filterDebounceTimer?.Stop();
            // 触发 ReloadLogsCommand 异步执行；命令委托内含 try-catch，无需在此 await
            ReloadLogsCommand.Execute(null);
        }

        // === 实时跟随 ===

        private DispatcherTimer? EnsureLiveFollowTimer()
        {
            if (_isDisposed) return null;
            if (_liveFollowTimer is null)
            {
                _liveFollowTimer = new DispatcherTimer
                {
                    Interval = TimeSpan.FromSeconds(LiveFollowIntervalSeconds),
                };
                _liveFollowTimer.Tick += LiveFollowTimer_Tick;
            }

            return _liveFollowTimer;
        }

        private void LiveFollowTimer_Tick(object? sender, EventArgs e)
        {
            _liveFollowTimer?.Stop();

            // 上一轮重载未结束则本轮跳过（待重载完成后的 finally 重启定时器）
            if (IsLoading || _isReloading || _isDisposed)
            {
                if (!_isDisposed && IsLiveFollow && !_isLiveRefreshInProgress)
                {
                    _liveFollowTimer?.Start();
                }
                return;
            }

            _isLiveRefreshInProgress = true;
            ReloadLogsCommand.Execute(null);
        }

        // === 分布条 ===

        /// <summary>查询最近 24 小时日志投影并重建小时分布柱（普通刷新路径调用）</summary>
        private async Task RefreshDistributionCoreAsync()
        {
            try
            {
                var cutoff = DateTime.Now.AddHours(-24);
                var rows = await LogDatabaseService.Instance.GetRecentTimestampsAsync(cutoff).ConfigureAwait(true);
                BuildHourlyBuckets(cutoff, rows);
            }
            catch (Exception ex)
            {
                LogService.Instance.Error($"刷新日志分布失败: {ex}");
            }
        }

        private void BuildHourlyBuckets(DateTime cutoff, IReadOnlyList<(DateTime Timestamp, string Level)> rows)
        {
            // 24 个小时桶：从 cutoff 所在整点起，到当前小时
            var startHour = new DateTime(cutoff.Year, cutoff.Month, cutoff.Day, cutoff.Hour, 0, 0);
            var buckets = new List<LogHourBucket>(24);
            for (int i = 0; i < 24; i++)
            {
                buckets.Add(new LogHourBucket { HourStart = startHour.AddHours(i) });
            }

            foreach (var (timestamp, level) in rows)
            {
                var index = (int)(timestamp - startHour).TotalHours;
                if (index < 0 || index > 23)
                {
                    continue;
                }

                var bucket = buckets[index];
                switch (level)
                {
                    case "ERROR":
                    case "FATAL":
                        bucket.Error++;
                        break;
                    case "WARNING":
                        bucket.Warning++;
                        break;
                    case "DEBUG":
                        bucket.Debug++;
                        break;
                    default:
                        // INFO 及其他未识别级别归入信息
                        bucket.Info++;
                        break;
                }
            }

            // 按最大桶总数归一化柱高，非零段给最小可见高度
            var maxTotal = buckets.Max(b => b.Total);
            foreach (var b in buckets)
            {
                b.ErrorHeight = ScaleBar(b.Error, maxTotal);
                b.WarningHeight = ScaleBar(b.Warning, maxTotal);
                b.InfoHeight = ScaleBar(b.Info, maxTotal);
                b.DebugHeight = ScaleBar(b.Debug, maxTotal);
            }

            _hourlyBuckets.Clear();
            foreach (var b in buckets)
            {
                _hourlyBuckets.Add(b);
            }
            RefreshBucketSelection();

            var errorTotal = buckets.Sum(b => b.Error);
            var warningTotal = buckets.Sum(b => b.Warning);
            HasDistribution = rows.Count > 0;
            DistributionSummaryText = rows.Count == 0
                ? "最近 24 小时暂无日志"
                : $"共 {rows.Count:N0} 条 · 错误 {errorTotal:N0} · 警告 {warningTotal:N0}";
        }

        private static double ScaleBar(int count, int maxTotal)
        {
            if (count <= 0 || maxTotal <= 0)
            {
                return 0;
            }

            return Math.Max(2.0, count * DistributionChartHeight / maxTotal);
        }

        /// <summary>按当前小时筛选状态刷新柱块选中标记</summary>
        private void RefreshBucketSelection()
        {
            foreach (var b in _hourlyBuckets)
            {
                b.IsSelected = _hourFilterEnd.HasValue && _hourFilterEnd.Value == b.HourStart.AddHours(1);
            }
        }

        // === 级别计数 ===

        private void ApplyLevelCounts(IReadOnlyDictionary<string, int> counts)
        {
            counts.TryGetValue("ERROR", out var error);
            counts.TryGetValue("FATAL", out var fatal);
            counts.TryGetValue("WARNING", out var warning);
            counts.TryGetValue("INFO", out var info);
            counts.TryGetValue("DEBUG", out var debug);
            counts.TryGetValue("VERBOSE", out var verbose);

            ErrorCount = error + fatal;
            WarningCount = warning;
            InformationCount = info;
            DebugCount = debug + verbose;

            OnPropertyChanged(nameof(ErrorCountText));
            OnPropertyChanged(nameof(WarningCountText));
            OnPropertyChanged(nameof(InfoCountText));
            OnPropertyChanged(nameof(DebugCountText));
        }

        private static string FormatLevelCount(int count)
        {
            return count < 10000
                ? count.ToString("N0", CultureInfo.InvariantCulture)
                : $"{count / 1000.0:0.0k}";
        }

        // === 小时筛选辅助 ===

        /// <summary>清除小时精确筛选状态（不触发重载）</summary>
        private void ClearHourRangeInternal()
        {
            _hourFilterEnd = null;
            SelectedHourRangeText = null;
        }

        /// <summary>日期筛选被手工修改（非分布条点击）时，联动清除小时精确筛选</summary>
        private void ClearHourRangeIfNeeded()
        {
            if (_isSettingHourRange || !_hourFilterEnd.HasValue)
            {
                return;
            }

            ClearHourRangeInternal();
        }

        private static void CopyLogToClipboard(LogEntry logEntry)
        {
            var logInfo = BuildLogText(logEntry);
            // M240: 剪贴板可能被其他进程占用，包裹 try-catch 避免异常崩溃
            try
            {
                Clipboard.SetDataObject(logInfo);
            }
            catch (Exception ex)
            {
                LogService.Instance.Error($"复制日志到剪贴板失败: {ex}");
                NotificationService.Error("复制日志到剪贴板失败，请稍后重试。");
                return;
            }
            NotificationService.Success($"日志信息已复制到剪贴板:\n{logInfo}");
        }

        private static string BuildLogText(LogEntry logEntry)
        {
            var message = string.IsNullOrWhiteSpace(logEntry.RenderedMessage) ? logEntry.Message : logEntry.RenderedMessage;
            // M122: 时间戳转换为本地时间
            var logInfo = $"[{logEntry.Timestamp:yyyy-MM-dd HH:mm:ss.fff}] [{logEntry.Level}] {message}";

            if (!string.IsNullOrWhiteSpace(logEntry.Exception))
            {
                logInfo += $"\n异常:\n{logEntry.Exception}";
            }

            if (!string.IsNullOrWhiteSpace(logEntry.Properties))
            {
                logInfo += $"\n属性:\n{logEntry.Properties}";
            }

            return logInfo;
        }

        // H85a: 释放 DispatcherTimer，避免资源泄漏
        public void Dispose()
        {
            if (_isDisposed) return;
            _isDisposed = true;

            // 取消自身 PropertyChanged 订阅，避免 Dispose 后仍被触发
            PropertyChanged -= OnFilterPropertyChanged;

            if (_searchDebounceTimer != null)
            {
                _searchDebounceTimer.Stop();
                _searchDebounceTimer.Tick -= SearchDebounceTimer_Tick;
                _searchDebounceTimer = null;
            }

            if (_filterDebounceTimer != null)
            {
                _filterDebounceTimer.Stop();
                _filterDebounceTimer.Tick -= FilterDebounceTimer_Tick;
                _filterDebounceTimer = null;
            }

            if (_liveFollowTimer != null)
            {
                _liveFollowTimer.Stop();
                _liveFollowTimer.Tick -= LiveFollowTimer_Tick;
                _liveFollowTimer = null;
            }
        }
    }

    /// <summary>
    /// 日志小时分布柱数据（最近 24 小时分布条的展示模型）。
    /// 计数字段由 ViewModel 构建时填充；IsSelected 需要在集合已绑定后切换，故实现 INotifyPropertyChanged。
    /// </summary>
    public sealed class LogHourBucket : INotifyPropertyChanged
    {
        /// <summary>该桶的起始整点（本地时间）</summary>
        public DateTime HourStart { get; init; }

        /// <summary>小时标签（如 "15"）</summary>
        public string HourLabel => HourStart.ToString("HH", CultureInfo.InvariantCulture);

        public int Error { get; set; }
        public int Warning { get; set; }
        public int Info { get; set; }
        public int Debug { get; set; }

        /// <summary>该小时总条数</summary>
        public int Total => Error + Warning + Info + Debug;

        public double ErrorHeight { get; set; }
        public double WarningHeight { get; set; }
        public double InfoHeight { get; set; }
        public double DebugHeight { get; set; }

        private bool _isSelected;

        /// <summary>是否为当前激活的小时筛选桶（分布条上描边高亮）</summary>
        public bool IsSelected
        {
            get => _isSelected;
            set
            {
                if (_isSelected != value)
                {
                    _isSelected = value;
                    OnPropertyChanged();
                }
            }
        }

        /// <summary>悬停提示文本</summary>
        public string Tooltip =>
            $"{HourStart:HH:00}-{HourStart.AddHours(1):HH:00}\n错误 {Error} · 警告 {Warning} · 信息 {Info} · 调试 {Debug}\n点击筛选该小时，再次点击取消";

        public event PropertyChangedEventHandler? PropertyChanged;

        private void OnPropertyChanged([CallerMemberName] string? propertyName = null)
        {
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
        }
    }
}
