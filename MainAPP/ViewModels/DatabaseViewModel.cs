using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using MainAPP.Models;
using MainAPP.Services;
using MainAPP.ViewModels.Base;
using MainAPP.ViewModels.Common;
using ScottPlot;
using ScottPlot.Palettes;
using System.Collections.ObjectModel;
using System.IO;
using System.Windows;

// VSTHRD001: 使用 Dispatcher.Invoke 切换到 UI 线程是 WPF 标准模式（MessageBox.Show/集合修改必须在 UI 线程），无需 JoinableTaskFactory
#pragma warning disable VSTHRD001

namespace MainAPP.ViewModels
{
    public partial class DatabaseViewModel : BasePageViewModel
    {
        private const int ChartLoadLimit = 5000;

        /// <summary>
        /// 对话框服务抽象。VM 通过此属性调用弹窗，避免直接依赖 View。
        /// 默认实例在 App.xaml.cs 的 DI 配置中被替换为 DI 解析的实例。
        /// </summary>
        public static IDialogService DialogService { get; set; } = new Services.DialogService();

        private readonly BarcodeDataService _dataService;
        // H71: ObservableCollection 跨线程修改保护锁
        private string _searchBarcode = string.Empty;
        // L397a: StartDate/EndDate 在构造函数中一次性赋默认值，避免惰性默认值掩盖显式 null
        private DateTime? _startDate;
        private DateTime? _endDate;
        private long? _minEncode;
        private long? _maxEncode;
        private double? _minScore;
        private double? _maxScore;
        private DbModel? _selectedItem;
        private const int DefaultPageSize = 20;
        private int _pageNumber = 1;
        private int _pageSize = DefaultPageSize;
        // L393a: 移除冗余初始化器 = 0
        private int _totalCount;
        private int _targetPageNumber = 1;

        public DatabaseViewModel()
        {
            _dataService = BarcodeDataService.Instance;
            _startDate = DateTime.Now - TimeSpan.FromDays(1);
            _endDate = DateTime.Now + TimeSpan.FromDays(1);
            LoadDataCommand = new AsyncRelayCommand(LoadDataPagedAsync);
            SearchCommand = new AsyncRelayCommand(SearchPagedAsync);
            ExportCsvCommand = new AsyncRelayCommand(ExportCsvAsync);
            ImportCsvCommand = new AsyncRelayCommand(ImportCsvAsync);
            DeleteSelectedCommand = new AsyncRelayCommand(DeleteSelectedAsync);
            ClearAllCommand = new AsyncRelayCommand(ClearAllAsync);
            // M319b: SaveBuffer 改为手动创建 AsyncRelayCommand，与其他命令保持一致
            SaveBufferCommand = new AsyncRelayCommand(SaveBuffer);
            RefreshCommand = new AsyncRelayCommand(LoadCurrentPageAsync);
            GoToFirstPageCommand = new AsyncRelayCommand(GoToFirstPageAsync);
            GoToPreviousPageCommand = new AsyncRelayCommand(GoToPreviousPageAsync);
            GoToNextPageCommand = new AsyncRelayCommand(GoToNextPageAsync);
            GoToLastPageCommand = new AsyncRelayCommand(GoToLastPageAsync);
            GoToPageCommand = new AsyncRelayCommand(GoToPageAsync);
            ChangePageSizeCommand = new AsyncRelayCommand(ChangePageSizeAsync);
            OpenImageCommand = new AsyncRelayCommand<DbModel>(OpenImageAsync);
            OpenChartCommand = new AsyncRelayCommand(OpenChartAsync);
            // P0-6: 选中数变化时同步 SelectedCount，驱动"删除选中(N)"文案更新
            SelectedItems.CollectionChanged += (_, _) => SelectedCount = SelectedItems.Count;
        }

        // L394a: 使用目标类型 new
        public ObservableCollection<DbModel> Items { get; } = [];

        public DbModel? SelectedItem
        {
            get => _selectedItem;
            set => SetProperty(ref _selectedItem, value);
        }

        // P0-6: 多选支持。由 DatabaseView.xaml.cs 的 SelectionChanged 同步 DataGrid.SelectedItems 到此集合。
        public ObservableCollection<DbModel> SelectedItems { get; } = [];

        private int _selectedCount;
        /// <summary>P0-6: 当前选中行数，用于"删除选中(N)"按钮文案与删除确认。</summary>
        public int SelectedCount
        {
            get => _selectedCount;
            set => SetProperty(ref _selectedCount, value);
        }

        public string SearchBarcode
        {
            get => _searchBarcode;
            set
            {
                if (SetProperty(ref _searchBarcode, value))
                    OnPropertyChanged(nameof(HasActiveFilters));
            }
        }

        public DateTime? StartDate
        {
            get => _startDate;
            set
            {
                if (SetProperty(ref _startDate, value))
                {
                    // P2-22: 手动改日期时回退到"自定义"预设，避免预设值与实际日期不一致
                    // 预设应用中除外，避免 ApplyTimePreset 触发的 setter 反向回退
                    if (!_isApplyingPreset && _timePreset != "自定义")
                        SetProperty(ref _timePreset, "自定义", nameof(TimePreset));
                    OnPropertyChanged(nameof(HasActiveFilters));
                }
            }
        }

        public DateTime? EndDate
        {
            get => _endDate;
            set
            {
                if (SetProperty(ref _endDate, value))
                {
                    if (!_isApplyingPreset && _timePreset != "自定义")
                        SetProperty(ref _timePreset, "自定义", nameof(TimePreset));
                    OnPropertyChanged(nameof(HasActiveFilters));
                }
            }
        }

        public long? MinEncode
        {
            get => _minEncode;
            set
            {
                if (SetProperty(ref _minEncode, value))
                    OnPropertyChanged(nameof(HasActiveFilters));
            }
        }

        public long? MaxEncode
        {
            get => _maxEncode;
            set
            {
                if (SetProperty(ref _maxEncode, value))
                    OnPropertyChanged(nameof(HasActiveFilters));
            }
        }

        public double? MinScore
        {
            get => _minScore;
            set
            {
                if (SetProperty(ref _minScore, value))
                    OnPropertyChanged(nameof(HasActiveFilters));
            }
        }

        public double? MaxScore
        {
            get => _maxScore;
            set
            {
                if (SetProperty(ref _maxScore, value))
                    OnPropertyChanged(nameof(HasActiveFilters));
            }
        }

        public int PageNumber
        {
            get => _pageNumber;
            set
            {
                var validValue = value;
                if (TotalPages > 0)
                {
                    if (validValue < 1) validValue = 1;
                    if (validValue > TotalPages) validValue = TotalPages;
                }
                else
                {
                    // 当 TotalPages 为 0 时（例如 PageSize 为 0），将页码固定为 1
                    validValue = 1;
                }
                if (SetProperty(ref _pageNumber, validValue))
                {
                    OnPropertyChanged(nameof(CanGoToPreviousPage));
                    OnPropertyChanged(nameof(CanGoToNextPage));
                }
            }
        }

        public int PageSize
        {
            get => _pageSize;
            set
            {
                // L363a: 校验 PageSize 有效性，避免 <= 0 导致除零或分页异常
                if (value <= 0) return;
                if (SetProperty(ref _pageSize, value))
                {
                    OnPropertyChanged(nameof(TotalPages));
                    OnPropertyChanged(nameof(CanGoToPreviousPage));
                    OnPropertyChanged(nameof(CanGoToNextPage));
                }
            }
        }

        public int TotalCount
        {
            get => _totalCount;
            set
            {
                if (SetProperty(ref _totalCount, value))
                {
                    OnPropertyChanged(nameof(TotalPages));
                    OnPropertyChanged(nameof(CanGoToPreviousPage));
                    OnPropertyChanged(nameof(CanGoToNextPage));
                }
            }
        }

        // L401a: TotalPages 始终 Math.Max(1, ...)，与 PageNumber 状态一致
        public int TotalPages => PageSize > 0 ? Math.Max(1, (int)Math.Ceiling((double)TotalCount / PageSize)) : 1;

        public bool CanGoToPreviousPage => PageNumber > 1;

        public bool CanGoToNextPage => PageNumber < TotalPages;

        public int TargetPageNumber
        {
            get => _targetPageNumber;
            set => SetProperty(ref _targetPageNumber, value);
        }

        // P2-22: 时间筛选预设
        // 预设值："自定义"（不修改 StartDate/EndDate），"今天"，"最近3天"，"最近7天"，"最近30天"，"全部"
        private string _timePreset = "自定义";
        // 标志位：预设应用中，避免 StartDate/EndDate setter 将 TimePreset 回退到"自定义"
        private bool _isApplyingPreset;

        /// <summary>可用时间预设项</summary>
        public string[] AvailableTimePresets { get; } =
            { "自定义", "今天", "最近3天", "最近7天", "最近30天", "全部" };

        /// <summary>
        /// 当前时间预设。切换为非"自定义"值时会自动更新 StartDate/EndDate。
        /// 用户手动改 DatePicker 时应回退为"自定义"。
        /// </summary>
        public string TimePreset
        {
            get => _timePreset;
            set
            {
                if (SetProperty(ref _timePreset, value))
                {
                    ApplyTimePreset(value);
                }
            }
        }

        private void ApplyTimePreset(string preset)
        {
            _isApplyingPreset = true;
            try
            {
                var now = DateTime.Now;
                switch (preset)
                {
                    case "今天":
                        StartDate = now.Date;
                        EndDate = now.Date.AddDays(1).AddSeconds(-1);
                        break;
                    case "最近3天":
                        StartDate = now.Date.AddDays(-2);
                        EndDate = now.Date.AddDays(1).AddSeconds(-1);
                        break;
                    case "最近7天":
                        StartDate = now.Date.AddDays(-6);
                        EndDate = now.Date.AddDays(1).AddSeconds(-1);
                        break;
                    case "最近30天":
                        StartDate = now.Date.AddDays(-29);
                        EndDate = now.Date.AddDays(1).AddSeconds(-1);
                        break;
                    case "全部":
                        StartDate = null;
                        EndDate = null;
                        break;
                    // "自定义": 不修改现有 StartDate/EndDate
                }
            }
            finally
            {
                _isApplyingPreset = false;
            }
        }

        public IAsyncRelayCommand LoadDataCommand { get; }
        public IAsyncRelayCommand SearchCommand { get; }
        public IAsyncRelayCommand ExportCsvCommand { get; }
        public IAsyncRelayCommand ImportCsvCommand { get; }
        public IAsyncRelayCommand DeleteSelectedCommand { get; }
        public IAsyncRelayCommand ClearAllCommand { get; }
        // M319b: SaveBufferCommand 改为手动创建，与其他命令一致
        public IAsyncRelayCommand SaveBufferCommand { get; }
        public IAsyncRelayCommand RefreshCommand { get; }
        public IAsyncRelayCommand GoToFirstPageCommand { get; }
        public IAsyncRelayCommand GoToPreviousPageCommand { get; }
        public IAsyncRelayCommand GoToNextPageCommand { get; }
        public IAsyncRelayCommand GoToLastPageCommand { get; }
        public IAsyncRelayCommand GoToPageCommand { get; }
        public IAsyncRelayCommand ChangePageSizeCommand { get; }
        public IAsyncRelayCommand OpenImageCommand { get; }
        public IAsyncRelayCommand OpenChartCommand { get; }

        private bool HasFilters()
        {
            return !string.IsNullOrWhiteSpace(SearchBarcode)
                || StartDate.HasValue
                || EndDate.HasValue
                || MinEncode.HasValue
                || MaxEncode.HasValue
                || MinScore.HasValue
                || MaxScore.HasValue;
        }

        /// <summary>D6: 当前是否设置筛选条件，用于区分"无数据"与"搜索无结果"提示。</summary>
        public bool HasActiveFilters => HasFilters();

        private async Task LoadCurrentPageAsync()
        {
            LoadState = LoadState.Loading;
            BusyMessage = "加载数据中...";
            try
            {
                // M69: 完全用 HasFilters() 判断，删除 _isSearching 字段
                if (HasFilters())
                {
                    // H79a: DB 存储本地时间，查询时直接传本地时间
                    // M321a: 传递 CancellationToken
                    var (items, totalCount) = await _dataService.SearchPagedAsync(
                        pageNumber: PageNumber,
                        pageSize: PageSize,
                        barcode: string.IsNullOrWhiteSpace(SearchBarcode) ? null : SearchBarcode,
                        minEncode: MinEncode,
                        maxEncode: MaxEncode,
                        minScore: MinScore,
                        maxScore: MaxScore,
                        startTime: StartDate,
                        endTime: EndDate,
                        cancellationToken: Cts.Token).ConfigureAwait(false);
                    // M110: 提取重复的页面结果应用逻辑
                    ApplyPageResult(items, totalCount);
                }
                else
                {
                    // M321a: 传递 CancellationToken
                    var (items, totalCount) = await _dataService.GetPagedAsync(PageNumber, PageSize, Cts.Token).ConfigureAwait(false);
                    ApplyPageResult(items, totalCount);
                }
            }
            catch (OperationCanceledException)
            {
                // M340: Dispose 取消操作时静默退出，不弹错误提示
            }
            catch (Exception ex)
            {
                LogService.Instance.Error($"加载当前页数据失败: {ex}");
                NotificationService.Error(ex.Message);
                ErrorMessage = ex.Message;
                LoadState = LoadState.Error;
            }
        }

        // M110: 提取重复的页面结果应用逻辑
        private void ApplyPageResult(List<DbModel> items, int totalCount)
        {
            // H81: Items 为 ObservableCollection，跨线程修改必须切换到 UI 线程
            System.Windows.Application.Current?.Dispatcher?.Invoke(() =>
            {
                Items.Clear();
                foreach (var item in items)
                {
                    Items.Add(item);
                }
                // D5: 根据结果数量设置加载状态
                LoadState = items.Count == 0 ? LoadState.Empty : LoadState.Loaded;
            });
            TotalCount = totalCount;
        }

        private async Task LoadDataPagedAsync()
        {
            await LoadCurrentPageAsync().ConfigureAwait(false);
        }

        private async Task SearchPagedAsync()
        {
            PageNumber = 1; // 搜索时重置到第一页
            await LoadCurrentPageAsync().ConfigureAwait(false);
        }

        private async Task GoToFirstPageAsync()
        {
            PageNumber = 1;
            await LoadCurrentPageAsync().ConfigureAwait(false);
        }

        private async Task GoToPreviousPageAsync()
        {
            if (CanGoToPreviousPage)
            {
                PageNumber--;
                await LoadCurrentPageAsync().ConfigureAwait(false);
            }
        }

        private async Task GoToNextPageAsync()
        {
            if (CanGoToNextPage)
            {
                PageNumber++;
                await LoadCurrentPageAsync().ConfigureAwait(false);
            }
        }

        private async Task GoToLastPageAsync()
        {
            // L437: TotalPages 始终 >= 1（见 TotalPages 属性定义 Math.Max(1, ...)），移除冗余的 > 0 检查
            PageNumber = TotalPages;
            await LoadCurrentPageAsync().ConfigureAwait(false);
        }

        private async Task GoToPageAsync()
        {
            if (TotalPages <= 0)
            {
                // 没有数据，跳转到第一页
                PageNumber = 1;
                await LoadCurrentPageAsync().ConfigureAwait(false);
                return;
            }

            if (TargetPageNumber >= 1 && TargetPageNumber <= TotalPages)
            {
                PageNumber = TargetPageNumber;
                await LoadCurrentPageAsync().ConfigureAwait(false);
            }
            else
            {
                // H82/M322c: 统一使用 Dispatcher.Invoke 并补充 MessageBoxButton/MessageBoxImage
                NotificationService.Warning($"请输入有效的页码 (1-{TotalPages})");
            }
        }

        private async Task ChangePageSizeAsync()
        {
            PageNumber = 1; // 重置到第一页
            await LoadCurrentPageAsync().ConfigureAwait(false);
        }

        private async Task ExportCsvAsync()
        {
            var dialog = new Microsoft.Win32.SaveFileDialog
            {
                Filter = "CSV文件 (*.csv)|*.csv|所有文件 (*.*)|*.*",
                DefaultExt = ".csv",
                FileName = $"barcode_data_{DateTime.Now:yyyyMMdd_HHmmss}.csv"
            };
            if (dialog.ShowDialog() == true)
            {
                // P2-9: 长操作期间显示 Loading 指示
                IsBusy = true;
                BusyMessage = "正在导出 CSV...";
                try
                {
                    // P0-FIX: 导出当前筛选结果而非全表，与用户预期一致
                    // 无筛选时退化为全表导出
                    if (HasFilters())
                    {
                        await _dataService.ExportToCsvAsync(
                            dialog.FileName,
                            barcode: string.IsNullOrWhiteSpace(SearchBarcode) ? null : SearchBarcode,
                            minEncode: MinEncode,
                            maxEncode: MaxEncode,
                            minScore: MinScore,
                            maxScore: MaxScore,
                            startTime: StartDate,
                            endTime: EndDate,
                            cancellationToken: Cts.Token).ConfigureAwait(false);
                    }
                    else
                    {
                        await _dataService.ExportToCsvAsync(dialog.FileName, Cts.Token).ConfigureAwait(false);
                    }
                    // H82/M322c: 统一使用 Dispatcher.Invoke 并补充 MessageBoxButton/MessageBoxImage
                    NotificationService.Success($"数据已导出到: {dialog.FileName}");
                }
                catch (OperationCanceledException)
                {
                    // M340: Dispose 取消操作时静默退出，不弹错误提示
                }
                catch (Exception ex)
                {
                    LogService.Instance.Error($"导出 CSV 失败: {ex}");
                NotificationService.Error(ex.Message);
                }
                finally
                {
                    IsBusy = false;
                }
            }
        }

        private async Task ImportCsvAsync()
        {
            var dialog = new Microsoft.Win32.OpenFileDialog
            {
                Filter = "CSV文件 (*.csv)|*.csv|所有文件 (*.*)|*.*",
                DefaultExt = ".csv"
            };
            if (dialog.ShowDialog() == true)
            {
                // P2-9: 长操作期间显示 Loading 指示
                IsBusy = true;
                BusyMessage = "正在导入 CSV...";
                try
                {
                    // M321a: 传递 CancellationToken
                    // #7: ImportFromCsvAsync 返回分批事务导入结果摘要
                    var result = await _dataService.ImportFromCsvAsync(dialog.FileName, Cts.Token).ConfigureAwait(false);
                    // H82/M322c: 统一使用 Dispatcher.Invoke 并补充 MessageBoxButton/MessageBoxImage
                    if (result.IsAllSuccess)
                    {
                        NotificationService.Success($"数据已从 {dialog.FileName} 导入（{result.ImportedCount} 条）");
                    }
                    else
                    {
                        // 部分批次失败时提示成功与失败数
                        NotificationService.Warning($"导入完成：成功 {result.ImportedCount} 条，失败 {result.FailedCount} 条（{result.FailedBatchCount} 批次失败）");
                    }
                    await LoadDataPagedAsync().ConfigureAwait(false); // 刷新列表
                }
                catch (OperationCanceledException)
                {
                    // M340: Dispose 取消操作时静默退出，不弹错误提示
                }
                catch (Exception ex)
                {
                    LogService.Instance.Error($"导入 CSV 失败: {ex}");
                NotificationService.Error(ex.Message);
                }
                finally
                {
                    IsBusy = false;
                }
            }
        }

        private async Task DeleteSelectedAsync()
        {
            // P0-6: 基于多选 SelectedItems 批量删除
            if (SelectedItems.Count == 0)
            {
                // H82/M322c: 统一使用 Dispatcher.Invoke 并补充 MessageBoxButton/MessageBoxImage
                NotificationService.Info("请先选择要删除的记录");
                return;
            }

            var count = SelectedItems.Count;
            // H82/M322c: 统一使用 Dispatcher.Invoke
            var result = System.Windows.Application.Current?.Dispatcher?.Invoke(() =>
                NotificationService.Ask($"确定要删除选中的 {count} 条记录吗？此操作不可恢复！", "确认删除")) ?? MessageBoxResult.None;
            if (result == MessageBoxResult.Yes)
            {
                // P2-9: 批量删除是长操作，期间显示 Loading 指示并禁用按钮
                BusyMessage = $"正在删除 {count} 条记录...";
                IsBusy = true;
                try
                {
                    var ids = SelectedItems.Select(x => x.Id).ToList();
                    // M321a: 传递 CancellationToken
                    await _dataService.BulkDeleteAsync(ids, Cts.Token).ConfigureAwait(false);
                    // H73/H81: Items 集合修改需在 UI 线程执行，避免后台线程竞态
                    var idSet = new HashSet<int>(ids);
                    System.Windows.Application.Current?.Dispatcher?.Invoke(() =>
                    {
                        for (int i = Items.Count - 1; i >= 0; i--)
                        {
                            if (idSet.Contains(Items[i].Id))
                                Items.RemoveAt(i);
                        }
                        SelectedItems.Clear();
                    });
                    // M297a: 删除成功后递减 TotalCount
                    TotalCount = Math.Max(0, TotalCount - count);
                    if (Items.Count == 0)
                        LoadState = LoadState.Empty;
                    // H82/M322c: 统一使用 Dispatcher.Invoke 并补充 MessageBoxButton/MessageBoxImage
                    NotificationService.Success($"已删除 {count} 条记录");
                }
                catch (OperationCanceledException)
                {
                    // M340: Dispose 取消操作时静默退出，不弹错误提示
                }
                catch (Exception ex)
                {
                    LogService.Instance.Error($"批量删除记录失败: {ex}");
                    NotificationService.Error(ex.Message);
                }
                finally
                {
                    IsBusy = false;
                }
            }
        }
        // M319b: 移除 [RelayCommand]，改为构造函数中手动创建 AsyncRelayCommand
        private async Task SaveBuffer()
        {
            // M159: 补齐 try-catch，与其它命令方法保持一致，避免 SavePendingAsync 抛出未观察异常导致应用崩溃
            try
            {
                // M321a: 传递 CancellationToken
                await _dataService.SavePendingAsync(Cts.Token).ConfigureAwait(false);
            }
            catch (OperationCanceledException)
            {
                // M340: Dispose 取消操作时静默退出，不弹错误提示
            }
            catch (Exception ex)
            {
                LogService.Instance.Error($"保存缓冲数据失败: {ex}");
                NotificationService.Error(ex.Message);
            }
        }
        private async Task ClearAllAsync()
        {
            // H82/M322c: 统一使用 Dispatcher.Invoke
            var result = System.Windows.Application.Current?.Dispatcher?.Invoke(() =>
                NotificationService.Ask("确定要清空所有数据吗？此操作不可恢复！", "确认清空")) ?? MessageBoxResult.None;
            if (result == MessageBoxResult.Yes)
            {
                // P2-9: 长操作期间显示 Loading 指示
                BusyMessage = "正在清空数据...";
                IsBusy = true;
                try
                {
                    // M321a: 传递 CancellationToken
                    await _dataService.ClearAllAsync(Cts.Token).ConfigureAwait(false);
                    // H81: Items 为 ObservableCollection，跨线程修改必须切换到 UI 线程
                    System.Windows.Application.Current?.Dispatcher?.Invoke(() => Items.Clear());
                    // M296a: 清空后重置分页状态
                    TotalCount = 0;
                    PageNumber = 1;
                    TargetPageNumber = 1;
                    LoadState = LoadState.Empty;
                    // H82/M322c: 统一使用 Dispatcher.Invoke 并补充 MessageBoxButton/MessageBoxImage
                    NotificationService.Success("已清空所有数据");
                }
                catch (OperationCanceledException)
                {
                    // M340: Dispose 取消操作时静默退出，不弹错误提示
                }
                catch (Exception ex)
                {
                    LogService.Instance.Error($"清空数据失败: {ex}");
                NotificationService.Error(ex.Message);
                }
                finally
                {
                    IsBusy = false;
                }
            }
        }

        // M70: 改为非 async（无实际 await），返回 Task.CompletedTask
        private Task OpenImageAsync(DbModel? model)
        {
            if (model == null)
            {
                // H82/M322c: 统一使用 Dispatcher.Invoke 并补充 MessageBoxButton/MessageBoxImage
                NotificationService.Info("未选择记录");
                return Task.CompletedTask;
            }

            if (string.IsNullOrWhiteSpace(model.ImageFullName))
            {
                // H82/M322c: 统一使用 Dispatcher.Invoke 并补充 MessageBoxButton/MessageBoxImage
                NotificationService.Info("该记录没有关联的图片");
                return Task.CompletedTask;
            }

            if (!File.Exists(model.ImageFullName))
            {
                // H82/M322c: 统一使用 Dispatcher.Invoke 并补充 MessageBoxButton/MessageBoxImage
                NotificationService.Warning($"图片文件不存在:\n{model.ImageFullName}");
                return Task.CompletedTask;
            }

            try
            {
                // M68: 用 using 包裹 Process 对象，避免泄漏非托管资源
                using var process = System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo
                {
                    FileName = model.ImageFullName,
                    UseShellExecute = true
                });
            }
            catch (Exception ex)
            {
                // H82/M322c: 补充 MessageBoxButton/MessageBoxImage
                NotificationService.Warning($"打开图片失败: {ex.Message}");
            }

            return Task.CompletedTask;
        }

        private async Task OpenChartAsync()
        {
            // P2-9: 加载图表数据期间显示 Loading 指示
            IsBusy = true;
            try
            {
                var (items, totalCount) = await LoadChartItemsAsync().ConfigureAwait(false);

                if (items.Count == 0)
                {
                    // H82/M322c: 统一使用 Dispatcher.Invoke 并补充 MessageBoxButton/MessageBoxImage
                NotificationService.Info("No data to analyze");
                    return;
                }

                if (totalCount > items.Count)
                {
                    LogService.Instance.Warning($"Charts loaded only the most recent {items.Count} records out of {totalCount} total, to avoid loading all data at once.");
                }

                // H73: 窗口创建和 Show 必须在 UI 线程执行，避免从线程池线程创建 WPF 窗口
                // 通过 IDialogService 抽象调用弹窗，VM 不再直接依赖 View
                System.Windows.Application.Current?.Dispatcher?.Invoke(() =>
                {
                    DialogService.ShowCharts(items);
                });
            }
            catch (OperationCanceledException)
            {
                // M340: Dispose 取消操作时静默退出，不弹错误提示
            }
            catch (Exception ex)
            {
                LogService.Instance.Error($"Failed to open charts: {ex}");
                NotificationService.Error(ex.Message);
            }
            finally
            {
                IsBusy = false;
            }
        }

        private async Task<(List<DbModel> Items, int TotalCount)> LoadChartItemsAsync()
        {
            // M69: 完全用 HasFilters() 判断
            if (HasFilters())
            {
                // H79a: DB 存储本地时间，查询时直接传本地时间
                // M321a: 传递 CancellationToken
                return await _dataService.SearchPagedAsync(
                    pageNumber: 1,
                    pageSize: ChartLoadLimit,
                    barcode: string.IsNullOrWhiteSpace(SearchBarcode) ? null : SearchBarcode,
                    minEncode: MinEncode,
                    maxEncode: MaxEncode,
                    minScore: MinScore,
                    maxScore: MaxScore,
                    startTime: StartDate,
                    endTime: EndDate,
                    cancellationToken: Cts.Token).ConfigureAwait(false);
            }

            // M321a: 传递 CancellationToken
            return await _dataService.GetPagedAsync(1, ChartLoadLimit, Cts.Token).ConfigureAwait(false);
        }

    }
}
