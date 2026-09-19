using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using MainAPP.Models;
using MainAPP.Services;
using MainAPP.Views;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Windows;
using System.Windows.Threading;

namespace MainAPP.ViewModels
{
    /// <summary>
    /// 配方页顶部状态区：未保存脏标记、配方健康度检查、上次保存时间、恢复已保存版本、另存为新配方、模型路径校验。
    /// 脏标记依赖周期对比“当前配方 JSON ⇄ 最近一次保存快照”，可捕获任意字段（含子对象深层改动）的修改，
    /// 无需为每个子模型接入 INotifyPropertyChanged。
    /// </summary>
    public partial class RecipeViewModel
    {
        // 与 Recipe.s_jsonOpts 同理由：CoordinateTool 的 Point2f 为公共字段，序列化必须 IncludeFields
        private static readonly JsonSerializerOptions s_snapshotOpts = new() { IncludeFields = true };

        /// <summary>配方 JSON 快照对比间隔：周期扫一次，避免每个按键都整份序列化</summary>
        private const int StatusRefreshMs = 500;

        /// <summary>最近一次成功保存时的配方 JSON 快照（用于脏判断与恢复）</summary>
        private string _savedSnapshotJson = string.Empty;

        private DispatcherTimer? _statusTimer;

        [ObservableProperty]
        private bool _isDirty;

        /// <summary>上次保存时间显示文本（保存后/周期刷新时更新）</summary>
        public string SavedTimeText => _recipe.ModifiedTime == default ? "—" : _recipe.ModifiedTime.ToString("MM-dd HH:mm");

        /// <summary>健康度检查摘要文本（如 “待处理 2 项” / “健康”）</summary>
        public string RecipeHealthText { get; private set; } = "检查中";

        /// <summary>健康度检查问题逐条列表（换行分隔，供弹层展示）</summary>
        public string RecipeIssuesText { get; private set; } = string.Empty;

        public bool HasRecipeIssues => RecipeIssuesText.Length > 0;

        // ───────── 状态周期刷新（窗口激活时启动，停用/销毁时停止） ─────────

        private void StartStatusTimer()
        {
            if (_statusTimer is not null)
            {
                return;
            }

            _statusTimer = new DispatcherTimer(DispatcherPriority.Background)
            {
                Interval = TimeSpan.FromMilliseconds(StatusRefreshMs)
            };
            _statusTimer.Tick += StatusTimer_Tick;
            _statusTimer.Start();
            RefreshStatusState();
        }

        private void StopStatusTimer()
        {
            if (_statusTimer is null)
            {
                return;
            }

            _statusTimer.Stop();
            _statusTimer.Tick -= StatusTimer_Tick;
            _statusTimer = null;
        }

        private void StatusTimer_Tick(object? sender, EventArgs e) => RefreshStatusState();

        /// <summary>
        /// 周期对比快照刷新脏标记、健康度、保存时间。
        /// 脏判断用“整份 JSON 是否一致”而非逐属性监听：配方多数子对象（ImageTool/YoloTool 等）
        /// 不实现 INotifyPropertyChanged，深改字段无法靠属性通知捕获，对比快照是唯一可靠的统一口径。
        /// </summary>
        private void RefreshStatusState()
        {
            var dirty = !string.Equals(SnapshotRecipeJson(), _savedSnapshotJson, StringComparison.Ordinal);
            if (IsDirty != dirty)
            {
                IsDirty = dirty;
            }
            RefreshHealth();
            OnPropertyChanged(nameof(SavedTimeText));
            OnPropertyChanged(nameof(CalibrationSummaryText));
        }

        private string SnapshotRecipeJson()
        {
            try
            {
                return JsonSerializer.Serialize(_recipe, s_snapshotOpts);
            }
            catch (Exception ex)
            {
                // 序列化失败沿用旧快照，脏标记保守置为不脏，避免异常刷屏
                LogService.Instance.Warning($"[配方状态] 快照序列化失败: {ex.Message}");
                return _savedSnapshotJson;
            }
        }

        /// <summary>保存成功后调用：刷新快照并清除脏标记</summary>
        public void MarkSaved()
        {
            _savedSnapshotJson = SnapshotRecipeJson();
            if (IsDirty)
            {
                IsDirty = false;
            }
            OnPropertyChanged(nameof(SavedTimeText));
            RefreshHealth();
        }

        // ───────── 配方健康度检查 ─────────

        /// <summary>
        /// 汇总配方“可用于产线”的前置检查项，任意一项缺失都应先补齐再下发。
        /// 与头部图像/标定/扫码枪徽章互补：这里给出的是“为什么还不健康”的完整清单。
        /// </summary>
        private void RefreshHealth()
        {
            var issues = new System.Collections.Generic.List<string>();

            if (ImageReadFromScanner)
            {
                if (!HasScanner)
                {
                    issues.Add("扫码枪未连接，请先在「图像采集」页连接设备。");
                }
            }
            else if (!HasFolderImages)
            {
                issues.Add("图像目录无可用图片，请检查目录路径。");
            }

            if (!IsCalibrated)
            {
                issues.Add("坐标系未标定（三点缺失、重合或共线），请先采图并执行「查找基准点」。");
            }

            var edge = YoloTool?.EdgeDetection;
            if (edge is null)
            {
                issues.Add("边缘检测配置缺失。");
            }
            else if (string.IsNullOrWhiteSpace(edge.ModelPath))
            {
                issues.Add("边缘检测模型路径为空。");
            }
            else if (!File.Exists(edge.ModelPath))
            {
                issues.Add($"边缘检测模型文件不存在：{edge.ModelPath}");
            }

            if ((YoloTool?.IsAngleDetectionEnabled ?? false) && YoloTool?.AngleDetection is { ModelPath: var anglePath })
            {
                if (string.IsNullOrWhiteSpace(anglePath))
                {
                    issues.Add("角度检测已启用但模型路径为空。");
                }
                else if (!File.Exists(anglePath))
                {
                    issues.Add($"角度检测模型文件不存在：{anglePath}");
                }
            }

            if (edge?.MinMaskAreaPixels is > 0 && edge.MaxMaskAreaPixels is > 0 && edge.MinMaskAreaPixels > edge.MaxMaskAreaPixels)
            {
                issues.Add("掩码面积下限大于上限，目标会被全部过滤。");
            }

            RecipeIssuesText = string.Join(Environment.NewLine, issues);
            RecipeHealthText = issues.Count == 0 ? "健康" : $"待处理 {issues.Count} 项";
            OnPropertyChanged(nameof(RecipeIssuesText));
            OnPropertyChanged(nameof(RecipeHealthText));
            OnPropertyChanged(nameof(HasRecipeIssues));
        }

        // ───────── 恢复上次保存版本 ─────────

        /// <summary>放弃当前未保存修改，从快照恢复最近一次保存的内容</summary>
        [RelayCommand]
        private void RevertToSaved()
        {
            if (!IsDirty)
            {
                ShowInfo("当前没有未保存的修改。");
                return;
            }

            var ok = NotificationService.Ask(
                "放弃当前所有未保存修改，恢复上次保存的版本？",
                "恢复已保存版本",
                MessageBoxButton.YesNo,
                MessageBoxImage.Warning);
            if (ok != MessageBoxResult.Yes)
            {
                return;
            }

            Recipe? restored;
            try
            {
                restored = JsonSerializer.Deserialize<Recipe>(_savedSnapshotJson, s_snapshotOpts);
            }
            catch (Exception ex)
            {
                LogService.Instance.Warning($"[配方状态] 恢复快照解析失败: {ex.Message}");
                ShowError("恢复失败：已保存版本解析异常，请从配方列表刷新后重试。");
                return;
            }

            if (restored is null)
            {
                ShowError("恢复失败：无法解析已保存的版本。");
                return;
            }

            ApplyRestored(restored);
            LogService.Instance.Info($"配方「{_recipe.Name}」已恢复上次保存版本");
            ShowInfo("已恢复上次保存的版本。");
        }

        /// <summary>把恢复结果整体写回配方模型并刷新所有绑定属性</summary>
        private void ApplyRestored(Recipe restored)
        {
            _recipe.Name = restored.Name;
            _recipe.Description = restored.Description;
            _recipe.OffsetAngle = restored.OffsetAngle;
            _recipe.GrabOffsetLongMm = restored.GrabOffsetLongMm;
            _recipe.GrabOffsetShortMm = restored.GrabOffsetShortMm;
            _recipe.ImageTool = restored.ImageTool;
            _recipe.YoloTool = restored.YoloTool;
            _recipe.CoordinateTool = restored.CoordinateTool;
            _recipe.CreatedTime = restored.CreatedTime;
            _recipe.ModifiedTime = restored.ModifiedTime;

            OnPropertyChanged(nameof(Name));
            OnPropertyChanged(nameof(Description));
            OnPropertyChanged(nameof(OffsetAngle));
            OnPropertyChanged(nameof(GrabOffsetLongMm));
            OnPropertyChanged(nameof(GrabOffsetShortMm));
            OnPropertyChanged(nameof(ImageTool));
            OnPropertyChanged(nameof(YoloTool));
            OnPropertyChanged(nameof(CoordinateTool));
            OnPropertyChanged(nameof(ImageReadFromScanner));
            OnPropertyChanged(nameof(ImageDirectoryPath));

            RefreshCaptureAvailability();
            OnPropertyChanged(nameof(IsCalibrated));

            _savedSnapshotJson = SnapshotRecipeJson();
            if (IsDirty)
            {
                IsDirty = false;
            }
            RefreshStatusState();
        }

        // ───────── 另存为新配方 ─────────

        /// <summary>
        /// 基于当前配方生成独立副本并加入配方库（JSON 深拷贝，与原配方无共享引用）。
        /// 调好的参数作为同类产品的起点，避免旧配方被改崩后无法回退。
        /// </summary>
        [RelayCommand]
        private async Task SaveAsNewRecipeAsync()
        {
            var newName = await RecipeManageViewModel.DialogService
                .ShowInputDialogAsync("另存为新配方", "新配方名称：", $"{_recipe.Name}_复制").ConfigureAwait(true);
            if (string.IsNullOrWhiteSpace(newName))
            {
                return;
            }

            if (RecipesManage.Instance.IsNameExists(newName))
            {
                ShowWarning($"已存在同名配方：{newName}");
                return;
            }

            Recipe copy = await Task.Run(() =>
            {
                var json = JsonSerializer.Serialize(_recipe, s_snapshotOpts);
                var r = JsonSerializer.Deserialize<Recipe>(json, s_snapshotOpts) ?? Recipe.CreateDefault();
                r.Name = newName;
                r.CreatedTime = DateTime.Now;
                r.ModifiedTime = DateTime.Now;
                return r;
            }).ConfigureAwait(true);

            RecipesManage.Instance.AddRecipe(copy);
            var newVm = new RecipeViewModel(copy);

            // 注册到已打开的配方列表页（若未找到列表页则仅落盘，用户可手动刷新）
            var manageVm = System.Windows.Application.Current?.Windows
                .OfType<RecipesManageView>()
                .Select(w => w.DataContext)
                .OfType<RecipeManageViewModel>()
                .FirstOrDefault();
            if (manageVm is null)
            {
                LogService.Instance.Info($"已另存配方「{newName}」，但未找到配方列表页，可在列表页刷新后查看。");
            }
            else
            {
                manageVm.AddRecipeFromExternal(newVm);
            }

            ShowInfo($"已另存为新配方：{newName}");
        }

        // ───────── 模型路径即时校验 ─────────

        /// <summary>边缘检测模型路径（直通配方，变更时即时刷新校验状态与健康度）</summary>
        public string EdgeModelPath
        {
            get => YoloTool?.EdgeDetection?.ModelPath ?? string.Empty;
            set
            {
                if (YoloTool?.EdgeDetection is not { } edge || edge.ModelPath == value)
                {
                    return;
                }
                edge.ModelPath = value;
                OnPropertyChanged();
                OnPropertyChanged(nameof(EdgeModelPathExists));
                RefreshHealth();
            }
        }

        public bool EdgeModelPathExists => File.Exists(EdgeModelPath);

        /// <summary>角度检测模型路径（直通配方，变更时即时刷新校验状态与健康度）</summary>
        public string AngleModelPath
        {
            get => YoloTool?.AngleDetection?.ModelPath ?? string.Empty;
            set
            {
                if (YoloTool?.AngleDetection is not { } angle || angle.ModelPath == value)
                {
                    return;
                }
                angle.ModelPath = value;
                OnPropertyChanged();
                OnPropertyChanged(nameof(AngleModelPathExists));
                RefreshHealth();
            }
        }

        public bool AngleModelPathExists => File.Exists(AngleModelPath);
    }
}