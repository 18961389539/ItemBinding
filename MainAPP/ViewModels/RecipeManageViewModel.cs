using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using MainAPP.Models;
using MainAPP.Services;
using System.Collections.ObjectModel;
using System.Text.Json;
using System.Windows;

// VSTHRD001: 使用 Dispatcher.BeginInvoke 切换到 UI 线程是 WPF 标准模式，无需 JoinableTaskFactory
#pragma warning disable VSTHRD001

namespace MainAPP.ViewModels
{
    /// <summary>
    /// 配方管理视图模型
    /// </summary>
    public partial class RecipeManageViewModel : ObservableObject, IDisposable
    {
        // L112: 搜索防抖延迟（毫秒）
        private const int SearchDebounceMs = 300;

        /// <summary>
        /// 对话框服务抽象。VM 通过此属性调用弹窗，避免直接依赖 View。
        /// 默认实例在 App.xaml.cs 的 DI 配置中被替换为 DI 解析的实例。
        /// </summary>
        public static IDialogService DialogService { get; set; } = new Services.DialogService();

        private readonly RecipesManage _recipesManage;
        private volatile bool _disposed;
        // M73: 搜索防抖 CTS
        // M278a: 声明为 volatile，确保多线程可见性，避免读取到陈旧值
        private volatile CancellationTokenSource? _searchDebounceCts;

        [ObservableProperty]
        private RecipeViewModel? _selectedRecipe;

        [ObservableProperty]
        private string _newRecipeName = string.Empty;

        [ObservableProperty]
        private string _newRecipeDescription = string.Empty;

        [ObservableProperty]
        private string _searchText = string.Empty;

        /// <summary>
        /// 配方视图模型列表
        /// </summary>
        public ObservableCollection<RecipeViewModel> Recipes { get; } = [];

        /// <summary>
        /// 过滤后的配方列表
        /// </summary>
        public ObservableCollection<RecipeViewModel> FilteredRecipes { get; } = [];

        public RecipeManageViewModel() : this(RecipesManage.Instance)
        {
        }

        public RecipeManageViewModel(RecipesManage recipesManage)
        {
            _recipesManage = recipesManage ?? throw new ArgumentNullException(nameof(recipesManage));
            _recipesManage.CurrentRecipeChanged += OnCurrentRecipeChanged;
            LoadRecipes();
        }

        /// <summary>
        /// 当前配方变更时刷新所有 RecipeViewModel 的 IsCurrent 标记
        /// </summary>
        private void OnCurrentRecipeChanged(Recipe? _)
        {
            foreach (var vm in Recipes)
            {
                vm.RefreshIsCurrent();
            }
        }

        /// <summary>
        /// 加载所有配方
        /// </summary>
        private void LoadRecipes()
        {
            // H39: 清理前显式 Dispose 所有旧 VM，避免资源泄漏
            foreach (var vm in Recipes)
            {
                vm.Dispose();
            }
            // L436: 集合修改统一调度到 UI 线程，防 WPF CollectionView 跨线程异常
            SafeModify(() =>
            {
                Recipes.Clear();
                FilteredRecipes.Clear();

                // 从配方管理器加载所有配方
                foreach (var recipe in _recipesManage.Recipes)
                {
                    var vm = new RecipeViewModel(recipe);
                    Recipes.Add(vm);
                    FilteredRecipes.Add(vm);
                }
            });

            // 设置当前选中配方
            if (_recipesManage.CurrentRecipe != null)
            {
                var match = Recipes.FirstOrDefault(r => r.Recipe == _recipesManage.CurrentRecipe);
                if (match != null) SelectedRecipe = match;
            }
        }

        /// <summary>
        /// L436: ObservableCollection 修改统一调度到 UI 线程，防止 CollectionView 跨线程异常
        /// </summary>
        private static void SafeModify(Action action)
        {
            var dispatcher = System.Windows.Application.Current?.Dispatcher;
            if (dispatcher == null || dispatcher.CheckAccess())
                action();
            else
                dispatcher.Invoke(action);
        }

        partial void OnSearchTextChanged(string value)
        {
            // M277a: 已 Dispose 时不再创建防抖任务，避免 Dispose 后仍触发后台操作
            if (_disposed) return;
            // M73: 防抖，避免每次按键都触发过滤
            // M100: 显式 Cancel + Dispose 旧 CTS，避免 CTS 未释放
            var oldCts = _searchDebounceCts;
            _searchDebounceCts = new CancellationTokenSource();
            oldCts?.Cancel();
            oldCts?.Dispose();
            var cts = _searchDebounceCts;
            _ = Task.Delay(SearchDebounceMs, cts.Token).ContinueWith(t =>
            {
                if (_disposed) return;
                if (!t.IsCanceled)
                {
                    // M101: 续体捕获异常，避免未观察异常导致应用崩溃
                    try
                    {
                        // 防抖续体不需要等待，使用 BeginInvoke 避免阻塞线程池线程
                        _ = System.Windows.Application.Current?.Dispatcher?.BeginInvoke(ApplyFilter);
                    }
                    catch (Exception ex)
                    {
                        LogService.Instance.Warning($"ApplyFilter 异常: {ex.Message}");
                    }
                }
            }, TaskScheduler.Default);
        }

        /// <summary>
        /// 应用搜索过滤
        /// </summary>
        private void ApplyFilter()
        {
            FilteredRecipes.Clear();

            var filtered = string.IsNullOrWhiteSpace(SearchText)
                ? Recipes
                : Recipes.Where(r =>
                    r.Name.Contains(SearchText, StringComparison.OrdinalIgnoreCase) ||
                    r.Description.Contains(SearchText, StringComparison.OrdinalIgnoreCase));

            foreach (var recipe in filtered)
            {
                FilteredRecipes.Add(recipe);
            }
        }

        [RelayCommand]
        private void SetAsCurrentRecipe()
        {
            try
            {
                if (SelectedRecipe != null)
                {
                    _recipesManage.SetAndSaveCurrentRecipe(SelectedRecipe.Recipe);
                }
            }
            catch (Exception ex)
            {
                LogService.Instance.Error($"设置当前配方失败: {ex}");
                NotificationService.Error($"设置当前配方失败: {ex.Message}");
            }
        }

        /// <summary>
        /// 创建新配方
        /// </summary>
        [RelayCommand]
        private void CreateRecipe()
        {
            try
            {
                if (string.IsNullOrWhiteSpace(NewRecipeName))
                {
                    NotificationService.Warning("配方名称不能为空，请输入名称后再创建。");
                    return;
                }

                if (_recipesManage.IsNameExists(NewRecipeName))
                {
                    NotificationService.Warning($"已存在名为 \"{NewRecipeName}\" 的配方，请更换名称后再创建。");
                    return;
                }

                var recipe = Recipe.CreateDefault();
                recipe.Name = NewRecipeName;
                recipe.Description = NewRecipeDescription;

                _recipesManage.AddRecipe(recipe);

                var vm = new RecipeViewModel(recipe);
                Recipes.Add(vm);
                FilteredRecipes.Add(vm);
                SelectedRecipe = vm;
                // 清空输入
                NewRecipeName = string.Empty;
                NewRecipeDescription = string.Empty;
            }
            catch (Exception ex)
            {
                LogService.Instance.Error($"创建配方失败: {ex}");
                NotificationService.Error($"创建配方失败: {ex.Message}");
            }
        }

        /// <summary>
        /// 删除选中配方
        /// </summary>
        [RelayCommand]
        private void DeleteRecipe()
        {
            try
            {
                if (SelectedRecipe == null)
                    return;

                var recipe = SelectedRecipe;
                // P0-FIX: 删除配方前加二次确认，避免误触丢失标定/AI 模型配置
                var confirm = NotificationService.Ask(
                    $"确定要删除配方 \"{recipe.Name}\" 吗？\n该操作不可恢复，相关的标定参数和 AI 模型配置将一并丢失。",
                    "确认删除配方",
                    MessageBoxButton.YesNo,
                    MessageBoxImage.Warning);
                if (confirm != MessageBoxResult.Yes)
                    return;

                _recipesManage.RemoveRecipe(recipe.Recipe);

                // H39: 移除前显式 Dispose，避免资源泄漏
                recipe.Dispose();
                Recipes.Remove(recipe);
                FilteredRecipes.Remove(recipe);

                SelectedRecipe = FilteredRecipes.FirstOrDefault();
            }
            catch (Exception ex)
            {
                LogService.Instance.Error($"删除配方失败: {ex}");
                NotificationService.Error($"删除配方失败: {ex.Message}");
            }
        }

        /// <summary>
        /// 外部（配方详情窗口“另存为新配方”）注册新配方实例到列表并选中。
        /// 集合变更统一走 SafeModify，防 WPF CollectionView 跨线程异常。
        /// </summary>
        public void AddRecipeFromExternal(RecipeViewModel vm)
        {
            SafeModify(() =>
            {
                Recipes.Add(vm);
                FilteredRecipes.Add(vm);
            });
            SelectedRecipe = vm;
        }

        /// <summary>
        /// 保存当前配方
        /// </summary>
        [RelayCommand]
        private void SaveRecipe()
        {
            try
            {
                if (SelectedRecipe == null)
                    return;

                _recipesManage.SaveRecipe(SelectedRecipe.Recipe);
            }
            catch (Exception ex)
            {
                LogService.Instance.Error($"保存配方失败: {ex}");
                NotificationService.Error($"保存配方失败: {ex.Message}");
            }
        }

        /// <summary>
        /// 刷新配方列表
        /// </summary>
        [RelayCommand]
        private void RefreshRecipes()
        {
            try
            {
                _recipesManage.LoadAllRecipes();
                LoadRecipes();
            }
            catch (Exception ex)
            {
                LogService.Instance.Error($"刷新配方列表失败: {ex}");
                NotificationService.Error($"刷新配方列表失败: {ex.Message}");
            }
        }

        /// <summary>
        /// 配方管理
        /// </summary>
        [RelayCommand]
        private async Task ManageRecipeAsync()
        {
            if (SelectedRecipe == null)
                return;

            try
            {
                // M317a: RecipeWindow 构造移入 try 块内，构造异常也能被捕获
                // 触发模式切换由 RecipeWindow.Loaded → ActivateAsync 处理：
                // 先 PauseLoop → 等当前帧完成 → 切软触发，避免主循环仍在取帧时设置 TriggerMode 触发 0x80020106
                // 通过 IDialogService 抽象调用弹窗，VM 不再直接依赖 View
                await DialogService.ShowRecipeEditorAsync(SelectedRecipe);
            }
            catch (Exception ex)
            {
                // L436: 补充日志记录，与同类 catch 块保持一致
                LogService.Instance.Error($"打开配方管理失败: {ex}");
                NotificationService.Error($"打开配方管理失败: {ex.Message}");
            }
        }

        /// <summary>
        /// 复制配方
        /// </summary>
        [RelayCommand]
        private async Task DuplicateRecipeAsync()
        {
            if (SelectedRecipe == null)
                return;

            try
            {
                var originalName = SelectedRecipe.Name;
                var newName = $"{originalName}_副本";
                var counter = 1;

                while (_recipesManage.IsNameExists(newName))
                {
                    newName = $"{originalName}_副本{counter++}";
                }

                // M295a: 捕获源数据快照，在后台线程执行 JSON 深拷贝，避免阻塞 UI 线程
                var sourceImageTool = SelectedRecipe.ImageTool;
                var sourceYoloTool = SelectedRecipe.YoloTool;
                var sourceCoordinateTool = SelectedRecipe.CoordinateTool;
                var description = SelectedRecipe.Description;

                var (imageTool, yoloTool, coordinateTool) = await Task.Run(() =>
                {
                    // M34/M72: 深拷贝引用类型配置，避免副本与原配方共享同一对象导致修改互相污染；
                    // 反序列化退化（返回 null）时记录 Warning 日志
                    return (
                        DeepCopyOrWarn(sourceImageTool, nameof(ImageTool)),
                        DeepCopyOrWarn(sourceYoloTool, nameof(YoloTool)),
                        DeepCopyOrWarn(sourceCoordinateTool, nameof(CoordinateTool)));
                }).ConfigureAwait(false);

                var newRecipe = Recipe.CreateDefault();
                newRecipe.Name = newName;
                newRecipe.Description = description;
                newRecipe.ImageTool = imageTool;
                newRecipe.YoloTool = yoloTool;
                newRecipe.CoordinateTool = coordinateTool;

                _recipesManage.AddRecipe(newRecipe);

                var vm = new RecipeViewModel(newRecipe);
                // L436: 通过 SafeModify 包装,防止 CollectionView 跨线程异常
                SafeModify(() =>
                {
                    Recipes.Add(vm);
                    FilteredRecipes.Add(vm);
                });

                SelectedRecipe = vm;
            }
            catch (Exception ex)
            {
                LogService.Instance.Error($"复制配方失败: {ex}");
                NotificationService.Error($"复制配方失败: {ex.Message}");
            }
        }

        /// <summary>
        /// M72: JSON 深拷贝辅助方法，反序列化返回 null 时记录 Warning 日志并返回默认实例
        /// </summary>
        private static T DeepCopyOrWarn<T>(T? source, string name) where T : new()
        {
            if (source is null)
            {
                LogService.Instance.Warning($"DuplicateRecipe: {name} 源对象为 null，使用默认值");
                return new T();
            }
            var copy = JsonSerializer.Deserialize<T>(JsonSerializer.Serialize(source));
            if (copy is null)
            {
                LogService.Instance.Warning($"DuplicateRecipe: {name} 深拷贝返回 null，使用默认值");
                return new T();
            }
            return copy;
        }

        public void Dispose()
        {
            if (_disposed) return;
            _disposed = true;

            _recipesManage.CurrentRecipeChanged -= OnCurrentRecipeChanged;

            // H39: 遍历所有 RecipeViewModel 调用 Dispose，释放扫描枪/Mat/预测器池等资源
            foreach (var vm in Recipes)
            {
                vm.Dispose();
            }
            Recipes.Clear();
            FilteredRecipes.Clear();

            // M73: 清理防抖 CTS
            _searchDebounceCts?.Cancel();
            _searchDebounceCts?.Dispose();
            _searchDebounceCts = null;
        }
    }
}
