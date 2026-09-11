using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using CoordinateSystemMapping;
using Extensions;
using HikScanner;
using JinlongYolo.YoloSharp;
using JinlongYolo.YoloSharp.Data;
using JinlongYolo.YoloSharp.Extensions;
using JinlongYolo.YoloSharp.Plotting;
using MainAPP.Application;
using MainAPP.Models;
using MainAPP.Services;
using MainAPP.Views;
using OpenCvSharp;
using OpenCvSharp.WpfExtensions;
using Image = SixLabors.ImageSharp.Image;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Diagnostics;
using System.IO;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using Serilog;

// VSTHRD001: 使用 Dispatcher.InvokeAsync 切换到 UI 线程是 WPF 标准模式，无需 JoinableTaskFactory
#pragma warning disable VSTHRD001

namespace MainAPP.ViewModels
{
    /// <summary>
    /// 识别图模版视图模型
    /// </summary>
    public partial class RecipeViewModel : ObservableObject, IDisposable
    {
        #region 模版信息
        private readonly Recipe _recipe;
        // M77: 改为 static，所有 RecipeViewModel 实例共享同一个扫描枪服务实例（设备单例）
        private static readonly RecipeScannerService _scannerService = new();

        /// <summary>扫码枪是否存在（用于 UI 绑定控制按钮可用状态）</summary>
        public bool HasScanner => _scannerService.HasScanner;

        /// <summary>
        /// 图像来源是否从扫码枪读取（双向联动 <see cref="ImageTool.ReadFromScanner"/>，供 XAML 绑定；
        /// 用包装属性而不是直绑模型，是为了切换来源时能同步刷新「拍照」按钮可用性）。
        /// </summary>
        public bool ImageReadFromScanner
        {
            get => ImageTool?.ReadFromScanner ?? true;
            set
            {
                if (ImageTool is null || ImageTool.ReadFromScanner == value)
                {
                    return;
                }

                ImageTool.ReadFromScanner = value;
                OnPropertyChanged();
                // 来源切换会影响右侧扫码枪操作区可见性与目录输入框可见性，一并刷新
                OnPropertyChanged(nameof(ImageDirectoryPath));
                RefreshCaptureAvailability();
            }
        }

        /// <summary>
        /// 文件夹取图路径（双向联动 <see cref="ImageTool.DirectoryPath"/>）。
        /// </summary>
        public string ImageDirectoryPath
        {
            get => ImageTool?.DirectoryPath ?? string.Empty;
            set
            {
                if (ImageTool is null || ImageTool.DirectoryPath == value)
                {
                    return;
                }

                ImageTool.DirectoryPath = value;
                OnPropertyChanged();
                RefreshCaptureAvailability();
            }
        }

        /// <summary>
        /// 「拍照/获取图像」可用性，按来源模式联动：
        /// 扫码枪模式 = 有扫码枪；文件夹模式 = 目录内存在可读图片（不依赖扫码枪）。
        /// </summary>
        public bool CanCapture => ImageReadFromScanner ? HasScanner : HasFolderImages;

        /// <summary>文件夹取图模式下目录内是否有可用图片（短路枚举，命中即停）</summary>
        public bool HasFolderImages => !ImageReadFromScanner && ExistsFolderImages(ImageTool?.DirectoryPath);

        /// <summary>文件夹取图模式下目录内图片数量（不含 RemoveCount 已跳过的开头帧；未绑定时不计算）</summary>
        public int FolderImageCount
        {
            get
            {
                if (ImageTool is { ReadFromScanner: false } tool && !string.IsNullOrWhiteSpace(tool.DirectoryPath))
                {
                    return CountFolderImages(tool.DirectoryPath);
                }
                return 0;
            }
        }

        /// <summary>
        /// 刷新取图按钮/文件夹状态相关属性。在来源切换、目录路径变化、Browse 选择目录、
        /// 配方 ImageTool 更换、以及每次取图后调用，保证 UI 与数据一致。
        /// </summary>
        private void RefreshCaptureAvailability()
        {
            OnPropertyChanged(nameof(CanCapture));
            OnPropertyChanged(nameof(HasFolderImages));
            OnPropertyChanged(nameof(FolderImageCount));
        }

        private static readonly string[] s_folderImageExtensions = [".jpg", ".jpeg", ".png", ".bmp", ".tif", ".tiff"];

        /// <summary>目录内是否存在受支持图像文件（短路枚举，命中即停，避免大目录卡 UI）</summary>
        private static bool ExistsFolderImages(string? directory)
        {
            try
            {
                if (string.IsNullOrWhiteSpace(directory) || !Directory.Exists(directory))
                {
                    return false;
                }
                return Directory.EnumerateFiles(directory)
                                .Any(f => s_folderImageExtensions.Contains(Path.GetExtension(f), StringComparer.OrdinalIgnoreCase));
            }
            catch (Exception ex)
            {
                LogService.Instance.Warning($"[文件夹取图] 探测目录失败: {ex.Message}");
                return false;
            }
        }

        /// <summary>统计目录内受支持图像文件数量（目录不存在/为空返回 0）</summary>
        private static int CountFolderImages(string directory)
        {
            try
            {
                if (!Directory.Exists(directory))
                {
                    return 0;
                }
                return Directory.EnumerateFiles(directory)
                                .Count(f => s_folderImageExtensions.Contains(Path.GetExtension(f), StringComparer.OrdinalIgnoreCase));
            }
            catch (Exception ex)
            {
                LogService.Instance.Warning($"[文件夹取图] 统计目录失败: {ex.Message}");
                return 0;
            }
        }

        /// <summary>枚举目录内图像文件并按名称排序（每次重扫，及时反映新增/删除）</summary>
        private static List<string> ListFolderImages(string directory)
        {
            if (!Directory.Exists(directory))
            {
                return [];
            }
            return Directory.EnumerateFiles(directory)
                            .Where(f => s_folderImageExtensions.Contains(Path.GetExtension(f), StringComparer.OrdinalIgnoreCase))
                            .OrderBy(f => f, StringComparer.OrdinalIgnoreCase)
                            .ToList();
        }

        /// <summary>
        /// P2-19: 当前是否已加载图像（用于标定向导步骤状态指示）
        /// </summary>
        public bool HasImage => _originalMat is not null && !_originalMat.Empty();

        /// <summary>
        /// P2-19: 坐标系是否已完成标定（三个标定点不重合且不共线）
        /// </summary>
        public bool IsCalibrated => CoordinateTool is not null && IsCoordinateToolCalibrated(CoordinateTool);

        /// <summary>
        /// P2-19: 判断三点标定是否完成：三个点不共点且不共线。
        /// 与 HomeViewModel.IsCoordinateToolCalibrated 逻辑保持一致。
        /// </summary>
        private static bool IsCoordinateToolCalibrated(CoordinateTool tool)
        {
            if (IsSamePoint(tool.Origin, tool.XPoint)) return false;
            if (IsSamePoint(tool.Origin, tool.YPoint)) return false;
            if (IsSamePoint(tool.XPoint, tool.YPoint)) return false;
            var cross = (tool.XPoint.X - tool.Origin.X) * (tool.YPoint.Y - tool.Origin.Y)
                      - (tool.XPoint.Y - tool.Origin.Y) * (tool.YPoint.X - tool.Origin.X);
            return System.Math.Abs(cross) > 0.5f;
        }

        private static bool IsSamePoint(Point2f a, Point2f b, float tolerance = 0.5f)
            => System.Math.Abs(a.X - b.X) < tolerance && System.Math.Abs(a.Y - b.Y) < tolerance;
        private ImageSource? _imageForShow;
        private Mat? _originalMat;
        private bool _isLiveDisplayEnabled;
        // L332: 声明 volatile，确保跨线程可见性
        private volatile CancellationTokenSource? _liveDisplayCts;
        private volatile CancellationTokenSource? _optimizeCts;
        // H37: 保存优化任务引用，便于 Dispose 时等待
        private Task? _optimizeTask;
        // M220: 跟踪推理任务，便于 Dispose 时带超时等待
        private Task? _inferenceTask;
        // M275a: 跟踪实时显示 fire-and-forget 任务，便于 Dispose 中带超时等待
        private Task? _liveDisplayTask;
        // M284a: 推理取消标记源，用于 Acquire 等可取消操作
        private readonly CancellationTokenSource _cts = new();
        private YoloPredictorPool? _edgeModelPool;
        private string? _edgeModelPath;
        // 2026-09-05: 测试推理勾选角度检测时，懒加载的角度模型池（CPU）与其路径缓存
        private YoloPredictorPool? _angleModelPool;
        private string? _angleModelPath;
        private const int RecipeInferenceCpuCount = 1;
        // M313c: Dispose 等待任务超时（秒）
        private const int DisposeTaskTimeoutSec = 2;
        // M314b: 优化完成后应用参数的延迟（毫秒）
        private const int OptimizePostApplyDelayMs = 500;
        // L402a: 优化过程中 UI 更新间隔（毫秒）
        private const int OptimizeUiUpdateIntervalMs = 100;
        // L: GetImageAsync 取图自动重试参数——瞬态故障（如相机忙、超时）时自动重试一次
        private const int GetImageMaxAttempts = 2;
        private const int GetImageRetryDelayMs = 150;
        // 2026-09-05: 文件夹取图（图像来源=本地目录）浏览游标。RemoveCount 语义="跳过开头不稳定帧"，
        // 即从第 RemoveCount 张开始逐张读取、到末尾回绕。目录变化/RemoveCount 变化时重置起点。
        private int _folderImageIndex;
        private int _folderImageStartIndex = -1;
        private string? _folderImageListPath;

        [ObservableProperty]
        private bool _isOptimizing;

        // L: 标定进行中标志，用于 UI 显示进度并禁用查找基准点按钮
        [ObservableProperty]
        private bool _isCalibrating;

        public string Name
        {
            get => _recipe.Name;
            set
            {
                if (_recipe.Name != value)
                {
                    _recipe.Name = value;
                    OnPropertyChanged();
                }
            }
        }

        /// <summary>
        /// 角度偏移（度，允许负值，建议范围 [-360, 360)，由 UI 输入控件约束）。
        /// 手动标定：放置已知朝向的产品，期望角度 − 系统显示角度 即为该值（可为负，如逆时针偏置）。
        /// 计算层天然兼容负偏移：最终角度 = 原始角 + OffsetAngle，随后统一归一化到 (-180,180]
        /// （2026-09-05 全系统规范域；角度模型经 AngleTracker.Normalize / 显示出口 ToRobotAngle），
        /// 故不必在此强制取模，保留用户输入的原始语义。
        /// </summary>
        public double OffsetAngle
        {
            get => _recipe.OffsetAngle;
            set
            {
                var v = (float)value;
                if (Math.Abs(_recipe.OffsetAngle - v) > 0.001)
                {
                    _recipe.OffsetAngle = v;
                    OnPropertyChanged();
                }
            }
        }

        /// <summary>
        /// REVIEW(2026-08-05): 平移补偿 X（mm）。图像→世界坐标转换后加在最终 X 上，
        /// 校准相机/机械坐标系安装偏差（正负均可）。
        /// </summary>
        public double OffsetX
        {
            get => _recipe.OffsetX;
            set
            {
                var v = (float)value;
                if (Math.Abs(_recipe.OffsetX - v) > 0.001)
                {
                    _recipe.OffsetX = v;
                    OnPropertyChanged();
                }
            }
        }

        /// <summary>
        /// REVIEW(2026-08-05): 平移补偿 Y（mm）。图像→世界坐标转换后加在最终 Y 上。
        /// </summary>
        public double OffsetY
        {
            get => _recipe.OffsetY;
            set
            {
                var v = (float)value;
                if (Math.Abs(_recipe.OffsetY - v) > 0.001)
                {
                    _recipe.OffsetY = v;
                    OnPropertyChanged();
                }
            }
        }

        public Recipe Recipe => _recipe;

        /// <summary>
        /// 当前是否为激活配方（用于 UI 显示视觉标记）
        /// </summary>
        public bool IsCurrent => ReferenceEquals(_recipe, RecipesManage.Instance.CurrentRecipe);

        /// <summary>
        /// 通知 IsCurrent 属性可能已变更（由 RecipeManageViewModel 在当前配方切换时调用）
        /// </summary>
        public void RefreshIsCurrent() => OnPropertyChanged(nameof(IsCurrent));

        public string Description
        {
            get => _recipe.Description;
            set
            {
                if (_recipe.Description != value)
                {
                    _recipe.Description = value;
                    OnPropertyChanged();
                }
            }
        }

        public ImageSource? ImageForShow
        {
            get => _imageForShow;
            set
            {
                // M273a: 先 Freeze 新值，再 Dispose 旧值，最后赋值，避免异常路径状态不一致
                // 冻结 BitmapSource 使其脱离 Dispatcher 依赖，WPF 可立即释放非托管内存
                if (value is System.Windows.Freezable freezable && !freezable.IsFrozen)
                {
                    freezable.Freeze();
                }

                // 释放旧图像源
                if (_imageForShow is IDisposable disposable)
                {
                    disposable.Dispose();
                }

                _imageForShow = value;
                OnPropertyChanged();
                // P2-19: 图像加载状态变化时通知 HasImage 属性，刷新标定向导状态
                OnPropertyChanged(nameof(HasImage));
            }
        }

        public ImageTool? ImageTool
        {
            get => _recipe.ImageTool;
            set
            {
                if (_recipe.ImageTool != value)
                {
                    _recipe.ImageTool = value;
                    OnPropertyChanged();
                    RefreshCaptureAvailability();
                }
            }
        }

        public YoloTools? YoloTool
        {
            get => _recipe.YoloTool;
            set
            {
                if (_recipe.YoloTool != value)
                {
                    _recipe.YoloTool = value;
                    OnPropertyChanged();
                }
            }
        }

        public CoordinateTool? CoordinateTool
        {
            get => _recipe.CoordinateTool;
            set
            {
                if (_recipe.CoordinateTool != value)
                {
                    _recipe.CoordinateTool = value;
                    OnPropertyChanged();
                }
            }
        }

        public DateTime CreatedTime => _recipe.CreatedTime;

        public DateTime ModifiedTime => _recipe.ModifiedTime;

        public bool IsLiveDisplayEnabled
        {
            get => _isLiveDisplayEnabled;
            set
            {
                if (SetProperty(ref _isLiveDisplayEnabled, value))
                {
                    if (value)
                    {
                        // M275a: 跟踪 fire-and-forget 任务，便于 Dispose 中带超时等待
                        _liveDisplayTask = StartLiveDisplayAsync();
                    }
                    else
                    {
                        StopLiveDisplay();
                    }
                }
            }
        }

        public RecipeViewModel(Recipe model)
        {
            _recipe = model ?? throw new ArgumentNullException(nameof(model));
            // 初始化取图可用性（ImageTool 默认可能是文件夹模式且目录已配置）
            RefreshCaptureAvailability();
        }

        /// <summary>
        /// 配方页激活：暂停主循环 → 等当前帧完成 → 切软触发。
        /// 软触发模式下实时显示和"获取图像"都能正常出图。
        /// </summary>
        public async Task ActivateAsync()
        {
            HomeViewModel.PauseLoop();
            // 等主循环当前帧释放锁（10s 取图超时 + 余量）
            await Task.Delay(TimeSpan.FromSeconds(12)).ConfigureAwait(false);
            // 主循环已暂停，切到软触发使实时显示和获取图像可用
            try
            {
                await _scannerService.SwitchToSoftTriggerAsync(CancellationToken.None).ConfigureAwait(false);
            }
            catch (Exception ex)
            {
                LogService.Instance.Warning($"切换软触发失败: {ex.Message}");
            }
        }

        /// <summary>
        /// 配方页停用：切回硬触发 → 恢复主循环。
        /// </summary>
        private async Task DeactivateAsync()
        {
            try
            {
                await _scannerService.SwitchToHardTriggerAsync(CancellationToken.None).ConfigureAwait(false);
            }
            catch (Exception ex)
            {
                LogService.Instance.Warning($"恢复硬触发失败: {ex.Message}");
            }
            HomeViewModel.ResumeLoop();
        }

        /// <summary>
        /// 配方窗口关闭时调用：可选保存 → 等推理结束 → 恢复硬触发 → 恢复主循环 → 清理会话资源。
        /// 注意：本方法不 Dispose 视图模型。RecipeViewModel 由配方列表（RecipeManageViewModel.Recipes）
        /// 长期持有并可再次打开同一实例，若在此销毁 _cts，二次打开后所有 _cts.Token 操作（推理 Acquire、
        /// 取图失败重试等）都会抛 ObjectDisposedException。真正销毁（释放 _cts/预测池等）由
        /// RecipeManageViewModel（删除配方/列表卸载/退出）调用 Dispose() 完成。
        /// </summary>
        /// <param name="skipSave">true 时跳过保存（调用方已自行处理保存）</param>
        public async Task DeactivateAndClearSessionAsync(bool skipSave = false)
        {
            if (!skipSave)
            {
                try { SaveSilently(); }
                catch (Exception ex) { LogService.Instance.Error($"保存配方失败: {ex}"); }
            }

            // 若测试推理仍在执行，先等它结束再清理，避免其访问即将被释放的预测池/Mat
            try
            {
                if (_inferenceTask is { IsCompleted: false })
                {
                    await _inferenceTask.WaitAsync(TimeSpan.FromSeconds(DisposeTaskTimeoutSec)).ConfigureAwait(false);
                }
            }
            catch (Exception ex)
            {
                LogService.Instance.Warning($"等待推理任务结束超时: {ex.Message}");
            }

            await DeactivateAsync().ConfigureAwait(false);
            StopAuxiliaryTasks();
            ReleaseCoreResources();
        }

        /// <summary>
        /// 停止编辑器会话期的后台任务：实时显示与参数优化，并释放其 CTS。
        /// 供窗口关闭复用场景（DeactivateAndClearSessionAsync）与最终销毁（Dispose）共用。
        /// </summary>
        private void StopAuxiliaryTasks()
        {
            // 停止实时显示（只 Cancel 不立即 Dispose 的逻辑在 StopLiveDisplay/StartLiveDisplayAsync 内闭环）
            StopLiveDisplay();
            // VSTHRD002: 此处为同步等待（async 方法也经 sync-over-async），已用 WaitAsync 超时避免无限阻塞
#pragma warning disable VSTHRD002
            try { _liveDisplayTask?.WaitAsync(TimeSpan.FromSeconds(DisposeTaskTimeoutSec)).GetAwaiter().GetResult(); }
            catch (Exception ex) { LogService.Instance.Warning($"等待实时显示任务结束超时: {ex.Message}"); }
#pragma warning restore VSTHRD002
            _liveDisplayCts?.Dispose();
            _liveDisplayCts = null;
            _liveDisplayTask = null;
            _isLiveDisplayEnabled = false;

            // 取消参数优化并等待其结束
            _optimizeCts?.Cancel();
#pragma warning disable VSTHRD002
            try { _optimizeTask?.WaitAsync(TimeSpan.FromSeconds(DisposeTaskTimeoutSec)).GetAwaiter().GetResult(); }
            catch (Exception ex) { LogService.Instance.Warning($"等待优化任务结束超时: {ex.Message}"); }
#pragma warning restore VSTHRD002
            _optimizeCts?.Dispose();
            _optimizeCts = null;
            _optimizeTask = null;
        }

        /// <summary>
        /// 释放编辑器会话期资源：原图 Mat、显示图像、边缘/角度预测池（下次使用按路径懒加载重建）。
        /// 不触碰 _cts——它属于 VM 生命周期。供窗口关闭复用场景与 Dispose 共用。
        /// </summary>
        private void ReleaseCoreResources()
        {
            if (_originalMat is not null)
            {
                var matSize = (long)_originalMat.Width * _originalMat.Height * _originalMat.Channels();
                _originalMat.Dispose();
                _originalMat = null;
                MemoryDiagnostics.LogDeallocation("RecopeVM(OriginalMat-Dispose)", matSize);
            }

            if (_imageForShow is IDisposable imgDisposable)
            {
                try { imgDisposable.Dispose(); } catch { /* 尽力清理 */ }
            }
            _imageForShow = null;

            _edgeModelPool?.Dispose();
            _edgeModelPool = null;
            _edgeModelPath = null;
            _angleModelPool?.Dispose();
            _angleModelPool = null;
            _angleModelPath = null;
        }

        /// <summary>
        /// 构建配方三点标定的坐标变换器（图像像素 → 世界 mm）。
        /// 口径与 HomeViewModel/DetectionRecordService 完全一致：X 方向物理距离 = SquareSize×4 格、Y 方向 = SquareSize×2 格。
        /// 未标定或方格尺寸无效时返回 null——调用方需回退到图像像素坐标系（此时坐标/角度非真实世界值）。
        /// </summary>
        private CoordinateTransformer? BuildCalibratedTransformer()
        {
            var calib = CoordinateTool;
            if (calib is null || !IsCoordinateToolCalibrated(calib) || calib.SquareSize <= 0)
            {
                return null;
            }

            var tf = new CoordinateTransformer();
            tf.Initialize(calib.Origin, calib.XPoint, calib.YPoint,
                calib.SquareSize * 4d, calib.SquareSize * 2d);
            return tf;
        }

        // M27: 改为 async 以配合 RecipeScannerService 的 async 方法
        private async Task<bool> EnsureScannerReadyAsync()
        {
            if (!_scannerService.HasScanner)
            {
                ShowWarning("未连接到设备");
                return false;
            }

            if (!await _scannerService.EnsureOpenAsync().ConfigureAwait(false))
            {
                ShowWarning("扫码枪未就绪，请检查设备连接后重试。");
                return false;
            }

            return true;
        }

        private static void ShowInfo(string message, string title = "提示") =>
            NotificationService.Info(message);

        private static void ShowWarning(string message, string title = "提示") =>
            NotificationService.Warning(message);

        private static void ShowError(string message, string title = "错误") =>
            NotificationService.Error(message);

        private void UpdateImageForShow(BitmapSource bitmapSource)
        {
            bitmapSource.Freeze();
            // M156: Application.Current 在应用关闭期间可能为 null，使用 ?. 避免NullReferenceException
            _ = System.Windows.Application.Current?.Dispatcher?.InvokeAsync(() => ImageForShow = bitmapSource);
        }

        /// <summary>
        /// 浏览边缘检测模型文件
        /// </summary>
        [RelayCommand]
        private void BrowseEdgeModel()
        {
            var dialog = new Microsoft.Win32.OpenFileDialog
            {
                Title = "选择ONNX模型文件",
                Filter = "ONNX模型|*.onnx|所有文件|*.*",
                CheckFileExists = true
            };

            if (dialog.ShowDialog() == true)
            {
                // 如果模型对象为空，尝试初始化
                if (YoloTool?.EdgeDetection == null)
                {
                    YoloTool ??= new YoloTools();
                    YoloTool.EdgeDetection = new YoloTool();
                }

                YoloTool.EdgeDetection.ModelPath = dialog.FileName;
                OnPropertyChanged(nameof(YoloTool));
            }
        }

        /// <summary>
        /// 浏览角度检测模型文件
        /// </summary>
        [RelayCommand]
        private void BrowseAngleModel()
        {
            var dialog = new Microsoft.Win32.OpenFileDialog
            {
                Title = "选择ONNX模型文件",
                Filter = "ONNX模型|*.onnx|所有文件|*.*",
                CheckFileExists = true
            };

            if (dialog.ShowDialog() == true)
            {
                // 如果模型对象为空，尝试初始化
                if (YoloTool?.AngleDetection == null)
                {
                    if (YoloTool == null)
                    {
                        YoloTool = new YoloTools();
                    }
                    YoloTool.AngleDetection = new YoloTool();
                }

                YoloTool.AngleDetection.ModelPath = dialog.FileName;
                OnPropertyChanged(nameof(YoloTool));
            }
        }

        #endregion

        #region 命令

        /// <summary>
        /// 保存识别模版到文件（显示提示）
        /// </summary>
        [RelayCommand]
        private void SaveToFile()
        {
            RecipesManage.Instance.SaveRecipe(_recipe);
            ShowInfo("保存成功");
        }

        /// <summary>
        /// 关闭配方窗口（Esc 快捷键）。触发 Window.Closing 事件，会弹出保存提示。
        /// </summary>
        [RelayCommand]
        private void Close()
        {
            // 注意：MainAPP.Application 命名空间与 System.Windows.Application 冲突，使用全限定名
            System.Windows.Application.Current.Windows
                .OfType<RecipeWindow>()
                .FirstOrDefault()?.Close();
        }

        /// <summary>
        /// 连接相机（Ctrl+D 快捷键）——确保扫码设备已连接。
        /// 设备连接由 RecipeScannerService 统一管理，无独立断开操作（窗口关闭时自动恢复硬触发）。
        /// </summary>
        [RelayCommand]
        private async Task ConnectCameraAsync()
        {
            if (await EnsureScannerReadyAsync().ConfigureAwait(false))
            {
                ShowInfo("相机已连接");
            }
        }

        #region 推理结果
        /// <summary>
        /// 「测试推理」历史结果集合（供配方窗口「推理结果」页展示，最新一条插入在头部）
        /// </summary>
        public ObservableCollection<RecipeTestResultItem> TestResults { get; } = [];

        [ObservableProperty]
        private RecipeTestResultItem? _selectedResult;

        /// <summary>
        /// 清空测试推理历史（集合操作必须回到 UI 线程，列表绑定依赖 Dispatcher 序列化）
        /// </summary>
        [RelayCommand]
        private void ClearTestResults()
        {
            if (System.Windows.Application.Current?.Dispatcher is { } d && !d.CheckAccess())
            {
                // VSTHRD110: fire-and-forget 封送回 UI 线程，显式丢弃 DispatcherOperation 以观察结果
                _ = d.InvokeAsync(ClearTestResults);
                return;
            }
            TestResults.Clear();
            SelectedResult = null;
        }

        /// <summary>
        /// 在头部插入一次测试推理结果并选中最新条目（推理结果页卡片列表最新在前，免倒序视图）。
        /// 推理任务在线程池线程完成（ConfigureAwait(false)），而 ObservableCollection 一旦被 UI
        /// CollectionView 订阅，跨线程变更会被拒绝或丢失——表现为“计数更新但卡片不显示”，
        /// 因此必须切换回 UI 线程再修改集合。
        /// </summary>
        private void AddTestResult(RecipeTestResultItem item)
        {
            if (System.Windows.Application.Current?.Dispatcher is { } d && !d.CheckAccess())
            {
                // VSTHRD110: fire-and-forget 封送回 UI 线程，显式丢弃 DispatcherOperation 以观察结果
                _ = d.InvokeAsync(() => AddTestResult(item));
                return;
            }
            TestResults.Insert(0, item);
            SelectedResult = item;
        }
        #endregion

        #region Inference
        /// <summary>
        /// AI模型推理
        /// </summary>
        [RelayCommand]
        private async Task Inference()
        {
            // 防御：VM 已被列表销毁（删除配方/卸载）时禁止继续操作，避免访问已释放 _cts
            if (_disposed)
            {
                LogService.Instance.Warning("测试推理：配方视图模型已释放，请关闭并重新打开配方后再试。");
                ShowWarning("配方视图已失效，请关闭后重新打开配方。");
                return;
            }
            // L372a: 防御性检查，避免覆盖正在执行的推理任务
            // L398a: 推理任务并发时给出提示，而非静默返回
            if (_inferenceTask is { IsCompleted: false })
            {
                LogService.Instance.Warning("测试推理：已有推理任务进行中，忽略本次点击。");
                ShowWarning("正在执行推理，请等待...");
                return;
            }
            LogService.Instance.Info("测试推理：开始执行");
            // M220: 跟踪推理任务，便于 Dispose 时等待
            _inferenceTask = InferenceCoreAsync();
            await _inferenceTask.ConfigureAwait(false);
        }

        private async Task InferenceCoreAsync()
        {
            try
            {
                if (this.YoloTool is null)
                {
                    // L357a: 错误消息统一使用中文
                    LogService.Instance.Warning("测试推理：YoloTool 配置为空。");
                    ShowError("AI 工具配置为空");
                    return;
                }
                if (this.YoloTool.EdgeDetection is null)
                {
                    LogService.Instance.Warning("测试推理：边缘检测配置为空。");
                    ShowError("边缘检测配置为空");
                    return;
                }
                if (_originalMat is null || _originalMat.Empty())
                {
                    LogService.Instance.Warning("测试推理：未加载图片(_originalMat 为空)。");
                    ShowError("请先加载图片");
                    return;
                }
                string fullname = this.YoloTool.EdgeDetection.ModelPath;
                if (!File.Exists(fullname))
                {
                    LogService.Instance.Warning($"测试推理：边缘检测模型文件不存在: {fullname}");
                    ShowError("边缘检测模型文件不存在，请检查路径。");
                    return;
                }
                if (_edgeModelPool == null || !string.Equals(_edgeModelPath, fullname, StringComparison.OrdinalIgnoreCase))
                {
                    LogService.Instance.Info($"测试推理：加载边缘模型 {fullname}");
                    _edgeModelPool?.Dispose();
                    // M97: 模型加载放在后台线程，避免阻塞 UI 线程
                    _edgeModelPool = await Task.Run(() => YoloPredictorPool.Create(fullname, new YoloPredictorPoolLayout
                    {
                        // L70: 使用提取的常量
                        CpuOnlyCount = RecipeInferenceCpuCount,
                    })).ConfigureAwait(false);
                    _edgeModelPath = fullname;
                }
                Stopwatch stopwatch = Stopwatch.StartNew();
                var edgeTool = this.YoloTool.EdgeDetection;
                YoloConfiguration configuration = new YoloConfiguration()
                {
                    IoU = edgeTool.IoU,
                    ApplyAutoOrient = edgeTool.ApplyAutoOrient,
                    Confidence = edgeTool.Confidence,
                    KeepAspectRatio = edgeTool.KeepAspectRatio,
                    SuppressParallelInference = edgeTool.SuppressParallelInference,
                };
                if (edgeTool.IsResize &&
                    (edgeTool.ResizeScale <= 0 || edgeTool.ResizeScaleY <= 0))
                {
                    LogService.Instance.Warning($"测试推理：缩放比例必须大于0 (X={edgeTool.ResizeScale}, Y={edgeTool.ResizeScaleY})");
                    ShowError("缩放比例必须大于0");
                    return;
                }

                using var inferenceMat = edgeTool.IsResize
                    ? _originalMat.Resize(new Size(0, 0), 1.0 / edgeTool.ResizeScale, 1.0 / edgeTool.ResizeScaleY)
                    : _originalMat.Clone();

                var matSize = (long)inferenceMat.Width * inferenceMat.Height * inferenceMat.Channels();
                MemoryDiagnostics.LogAllocation("RecopeVM(InferenceMat)", matSize,
                    $"WxH={inferenceMat.Width}x{inferenceMat.Height}, IsResize={edgeTool.IsResize}");

                // M284a: 传入 CancellationToken，使 Acquire 阶段可响应取消
                using var lease = _edgeModelPool!.Acquire(cancellationToken: _cts.Token);
                var segmentation = await lease.Predictor.SegmentAsync(inferenceMat.ToBytes(), configuration).ConfigureAwait(false);
                LogService.Instance.Info($"测试推理：边缘推理完成，目标数={segmentation?.Count ?? 0}，推理图={inferenceMat.Width}x{inferenceMat.Height}，IsResize={edgeTool.IsResize}，缩放比={edgeTool.ResizeScale}/{edgeTool.ResizeScaleY}");

                // 取分割结果中面积（OBB）最大的目标作为"产品"（单产品调试语义：角度推理与坐标显示共用同一目标）
                Segmentation? product = null;
                float bestArea = 0f;
                if (segmentation is not null)
                {
                    foreach (var seg in segmentation)
                    {
                        var rect = seg.GetMaskMinAreaRect();
                        if (rect.Area > bestArea)
                        {
                            bestArea = rect.Area;
                            product = seg;
                        }
                    }
                }
                LogService.Instance.Info($"测试推理：边缘目标数={segmentation?.Count ?? 0}，选定产品 OBB面积={bestArea:F0}");

                // ==== 角度模型推理（仅当配方勾选"角度检测"时执行）====
                // 2026-09-05 扩展：与主检测管线一致——先跑边缘分割，再对面积最大的目标
                // 走 摆正裁剪→角度模型分割→质心→(默认标定) 求出角度，用于配方页联调验证。
                var angleConfig = this.YoloTool?.AngleDetection;
                var angleEnabledFlag = this.YoloTool?.IsAngleDetectionEnabled ?? false;
                // 2026-09-08: 勾选角度检测但模型文件缺失/路径为空 → 视同"单个分割模型"状态。
                // 生产主链路在角度模型池加载失败（_anglePredictorPool=null → angleEnabled=false）时
                // 即走掩码回退+灰度判向；配方页同步回退，保证测试推理显示值 = 生产实发值。
                string? angleModelPath = angleConfig?.ModelPath;
                bool angleFileMissing = angleEnabledFlag && angleConfig is not null
                    && (string.IsNullOrWhiteSpace(angleModelPath) || !File.Exists(angleModelPath));
                var wantAngle = angleEnabledFlag && angleConfig is not null && !angleFileMissing;
                if (angleFileMissing)
                {
                    LogService.Instance.Warning($"测试推理：已勾选角度检测，但角度模型文件不存在: {angleModelPath}，本次按单个分割模型（掩码回退角度+灰度判向）执行。");
                    ShowWarning("已勾选角度检测，但角度模型文件不存在，本次按单个分割模型（掩码回退角度+灰度判向）执行。");
                }
                string angleEnterDesc = wantAngle ? "是" : angleEnabledFlag ? "否(模型文件缺失→分割回退)" : "否(跳过)";
                LogService.Instance.Info($"测试推理：角度检测开关={angleEnabledFlag}，AngleDetection配置={(angleConfig is null ? "null" : "存在")}，进入角度模型={angleEnterDesc}");
                AngleDetectionResult? angleResult = null;
                // 2026-09-11: 角度模型方向退化标记（特征质心偏移比低于配方阈值）。置位后不采信模型角度，
                // 改走与生产主链路相同的"掩码主轴角 + 灰度判向"兜底，保证配方页所见即所发。
                bool angleDirectionDegenerate = false;
                if (wantAngle)
                {
                    // 模型文件存在性已在上方判定（angleFileMissing=false 才进入），此处直接使用外层路径变量
                    var angleTool = angleConfig!;
                    LogService.Instance.Info($"测试推理：角度模型路径有效: {angleModelPath}");
                    try
                    {
                        // 懒加载角度模型池（CPU，与边缘检测同规格），路径变化时重建
                        if (_angleModelPool == null || !string.Equals(_angleModelPath, angleModelPath, StringComparison.OrdinalIgnoreCase))
                        {
                            LogService.Instance.Info($"测试推理：加载角度模型 {angleModelPath}");
                                _angleModelPool?.Dispose();
                                // wantAngle 成立 ⇒ angleFileMissing=false ⇒ 路径非空且文件存在，此处安全断言
                                _angleModelPool = await Task.Run(() => YoloPredictorPool.Create(angleModelPath!, new YoloPredictorPoolLayout
                            {
                                CpuOnlyCount = RecipeInferenceCpuCount,
                            })).ConfigureAwait(false);
                            _angleModelPath = angleModelPath;
                        }
                        // 产品选择已在上层（分割完成后）完成，此处直接使用面积最大的目标做角度推理
                        if (product is null)
                        {
                            LogService.Instance.Warning("测试推理：边缘检测未输出目标，无法执行角度推理。");
                            ShowWarning("边缘检测未输出目标，无法执行角度推理。");
                        }
                        else
                        {
                            using var angleLease = _angleModelPool!.Acquire(cancellationToken: _cts.Token);
                            angleResult = await AngleDetectionProcessor.ComputeAngleAsync(
                                inferenceMat,
                                product,
                                // 已标定→世界坐标变换器（角度为真实世界角度，与主流程一致）；
                                // 未标定→默认(未初始化)变换器，处理器自动退化为图像像素方向角并提示非真实值
                                BuildCalibratedTransformer() ?? new CoordinateTransformer(),
                                edgeTool.IsResize,
                                edgeTool.ResizeScale,
                                edgeTool.ResizeScaleY,
                                angleLease.Predictor,
                                angleTool,
                                (float)OffsetAngle,
                                _cts.Token,
                                // 诊断回调：仅配方页调试使用，把质量门拒绝原因落盘
                                reason => LogService.Instance.Warning($"测试推理：角度质量门拒绝，原因={reason}")).ConfigureAwait(false);
                            if (angleResult is null)
                            {
                                LogService.Instance.Warning("测试推理：角度模型已推理但未输出有效结果（具体原因见角度质量门日志）。");
                                ShowWarning("角度模型已推理但未输出有效结果（质量门不过/无特征），详见日志。");
                            }
                            else if (angleResult.DirectionOffsetRatio < angleTool.MinCentroidOffsetRatio)
                            {
                                // 2026-09-11: 模型方向退化——特征质心与产品质心几乎重合，方向向量趋近 0，
                                // 角度由噪声决定却能通过原有三道质量门。此处不采信模型角度，回落掩码兜底
                                // （与生产主链路 DetectionRecordService 的判定同口径、同阈值）。
                                double degenerateRatio = angleResult.DirectionOffsetRatio;
                                angleDirectionDegenerate = true;
                                angleResult = null;
                                string degenerateMsg =
                                    $"测试推理：角度模型方向退化（特征质心偏移比 {degenerateRatio:F4} < " +
                                    $"{angleTool.MinCentroidOffsetRatio:F4}），改用掩码主轴角 + 灰度判向兜底。";
                                LogService.Instance.Warning(degenerateMsg);
                                ShowWarning(degenerateMsg);
                            }
                            else
                            {
                                // 2026-09-05: AngleDetectionProcessor 返回"世界角+Offset"的原始值（可能越界，
                                // 如 328° 或负值），此处 ToRobotAngle 归一化到全系统规范域 (-180,180]，
                                // 与生产 DbModel.Angle 落库/机器人实收 RZ 同域，测试推理显示所见即所发。
                                angleResult = angleResult with { Angle = ToVGT.ToRobotAngle(angleResult.Angle) };
                                LogService.Instance.Info($"测试推理角度: {angleResult.Angle:F1}°");
                            }
                        }
                    }
                    catch (OperationCanceledException)
                    {
                        throw; // 取消向上传播
                    }
                    catch (Exception ex)
                    {
                        LogService.Instance.Error($"角度模型推理失败: {ex}");
                        ShowWarning($"角度模型推理失败: {ex.Message}");
                    }
                }

                // H54: ToImageSharp() 返回的 Image 与 PlotImageAsync 返回的 colorImage 均为 IDisposable，需用 using 释放
                // 2026-09-05: SegmentAsync 可能返回 null（无目标/失败）。CreateDisplayImageAsync 统一所有权：
                // 有分割结果 → 画分割可视化（输入 sharpImg 用后即弃）；无结果 → 直接借用 sharpImg 作为显示图。
                // 二者都只由外层 colorImage 这一个 using 负责释放，避免双重释放；非泛型 Image 亦无 Clone()。
                var sharpImg = inferenceMat.ToImageSharp();
                using var colorImage = await CreateDisplayImageAsync(segmentation, sharpImg).ConfigureAwait(false);
                stopwatch.Stop();

                // 无分割可视化时的显示图：直接使用原推理图；分割成功则释放输入图（PlotImageAsync 已生成新图）
                async Task<Image> CreateDisplayImageAsync(YoloResult<Segmentation>? seg, Image source)
                {
                    if (seg is null)
                    {
                        return source; // 所有权移交给调用方 using（colorImage）
                    }

                    using (source)
                    {
                        return await seg.PlotImageAsync(source).ConfigureAwait(false);
                    }
                }

                // H54: colorImage.ToMat() 返回的中间 Mat 也需释放，避免 OpenCV 非托管内存泄漏
                using var rawPlottedMat = colorImage.ToMat();
                if (angleResult is not null)
                {
                    // 角度质心坐标为"原图像素"；rawPlottedMat 为推理图坐标，需除以缩放系数还原后再绘制
                    double sx = edgeTool.IsResize ? edgeTool.ResizeScale : 1.0;
                    double sy = edgeTool.IsResize ? edgeTool.ResizeScaleY : 1.0;
                    var productPoint = new Point(angleResult.ProductCentroidImage.X / sx, angleResult.ProductCentroidImage.Y / sy);
                    var featurePoint = new Point(angleResult.FeatureCentroidImage.X / sx, angleResult.FeatureCentroidImage.Y / sy);
                    // 黄:产品质心 → 特征质心方向线(即角度方向)；品红:特征质心
                    Cv2.Circle(rawPlottedMat, productPoint, 6, new Scalar(0, 255, 255), 2, LineTypes.AntiAlias);
                    Cv2.Circle(rawPlottedMat, featurePoint, 6, new Scalar(255, 0, 255), 2, LineTypes.AntiAlias);
                    Cv2.Line(rawPlottedMat, productPoint, featurePoint, new Scalar(0, 255, 255), 2, LineTypes.AntiAlias);
                }
                using var plottedMat = rawPlottedMat.Resize(new Size(), edgeTool.ResizeScale, edgeTool.ResizeScaleY);
                UpdateImageForShow(plottedMat.ToBitmapSource());

                string angleOutDesc = angleResult is not null ? $"{angleResult.Angle:F1}°" : "无";

                // ==== 测试结果汇总：产品坐标 X/Y + 角度 ====
                // 产品参考点（原图像素）：2026-09-05 起与生产发送口径一致——固定取面积最大目标的
                // 最小外接旋转矩形中心（minAreaRect.Center），不再使用角度处理器产品质心或掩码加权质心；
                // 保证配方页"测试推理显示值 = 实际发送给机器人的坐标"。缩放语义与原掩码质心路径相同。
                Point2f productPixel;
                // 掩码主轴角（度）：角度未启用时生产会以该角(+OffsetAngle)兜底发机器人，配方页同步展示
                double maskFallbackAngleDeg = 0;
                // 2026-09-08: 灰度判向所需掩码矩形字段（推理图坐标系，与 inferenceMat/分割掩码一致），
                // 供回退分支复用生产主链路同款判向实现；仅存在有效掩码像素（MaskArea>0）时赋值。
                float maskRectCenterX = 0, maskRectCenterY = 0, maskRectAngleDeg = 0, maskArea = 0;
                // 2026-09-08: 掩码矩形宽度（推理图像素=长轴长），掩码回退角度走三点标定换算世界角时
                // 需沿长轴取端点（ComputeMaskAngleCalibrated），与生产 BuildAndSaveAsync 口径一致。
                float maskRectWidth = 0;
                if (product is not null)
                {
                    var (_, rect) = product.GetMaskStats();
                    productPixel = new Point2f(
                        rect.Center.X * (edgeTool.IsResize ? edgeTool.ResizeScale : 1f),
                        rect.Center.Y * (edgeTool.IsResize ? edgeTool.ResizeScaleY : 1f));
                    if (rect.MaskArea > 0)
                    {
                        maskFallbackAngleDeg = rect.Angle;
                        maskRectCenterX = rect.Center.X;
                        maskRectCenterY = rect.Center.Y;
                        maskRectAngleDeg = rect.Angle;
                        maskRectWidth = rect.Width;
                        maskArea = rect.MaskArea;
                    }
                }
                else
                {
                    productPixel = default;
                }

                // 世界坐标换算：三点标定完成→按主流程口径转 mm 并叠加配方平移补偿（真实值）；
                // 未标定→图像像素坐标并明确提示（非真实坐标，仅供联调比对）
                string coordText = "无产品";
                string remark;
                double? worldX = null, worldY = null;
                if (segmentation is { Count: > 0 })
                {
                    var calibTf = BuildCalibratedTransformer();
                    if (calibTf is not null)
                    {
                        var phys = calibTf.ImageToPhysical(productPixel);
                        double wx = phys.X + OffsetX;
                        double wy = phys.Y + OffsetY;
                        worldX = wx;
                        worldY = wy;
                        coordText = $"X={wx:F2} mm, Y={wy:F2} mm";
                    }
                    else
                    {
                        coordText = $"X={productPixel.X:F1} px, Y={productPixel.Y:F1} px（未标定，非真实坐标）";
                    }
                }

                // 2026-09-05: 最终角度统一为"发送给机器人的域值"(-180..180]，与生产链路一致：
                // - 角度模型成功 → angleResult.Angle（已在上面换算为 ToRobotAngle 域）；
                // - 角度未启用/勾选但模型文件缺失 → 回退掩码最小外接矩形主轴角 + OffsetAngle，
                //   并应用灰度判向消除 180° 歧义（生产在模型池加载失败时同走此回退）；
                // - 启用但模型无输出 → 视为无角度（对应生产锁定缺失帧，不作为真实角度）。
                double? sendAngle = angleResult?.Angle;
                bool angleFromMaskFallback = false;
                // 2026-09-08: 与生产主链路（DetectionRecordService.BuildAndSaveAsync 回退分支）行为一致——
                // 掩码回退角度同样应用灰度判向（配方级 IsBrightnessDirectionEnabled ?? 全局 Algorithm
                // .BrightnessDirectionEnabled），消除 180° 方向歧义，测试推理显示值 = 生产实发值（所见即所发）。
                bool brightnessDirectionTried = false;
                // 2026-09-08: 判向灰度统计随测试推理展示（与生产落库同一 out 数据），供标定 OffsetAngle/
                // 死区阈值时核对"亮/暗半区灰度差"是否稳定；声明在外层便于汇总文案引用。
                DetectionRecordService.BrightnessDirectionStats? brightnessStats = null;
                if (sendAngle is null && (!wantAngle || angleDirectionDegenerate))
                {
                    // 2026-09-08: 掩码回退角度经三点标定换算为世界坐标系角度（与生产 BuildAndSaveAsync
                    // 回退分支 ComputeMaskAngleCalibrated 同口径）——图像系主轴角直接当世界角用在
                    // 图像Y/机器人Y镜像时会整体反号，此处先取主轴两端点 ImageToPhysical 后 atan2，
                    // 未标定时退回图像角（同旧行为，仅供联调）。再叠加 OffsetAngle 与生产一致。
                    var calibTf = BuildCalibratedTransformer();
                    double maskWorldAngle = calibTf is not null && maskArea > 0
                        ? DetectionRecordService.ComputeMaskAngleCalibrated(
                            maskRectCenterX, maskRectCenterY, maskRectAngleDeg, maskRectWidth, maskArea,
                            calibTf, edgeTool.IsResize, edgeTool.ResizeScale, edgeTool.ResizeScaleY)
                        : maskFallbackAngleDeg;
                    var fallbackAngle = maskWorldAngle + (double)OffsetAngle;
                    bool brightnessEnabled = this.YoloTool?.IsBrightnessDirectionEnabled
                        ?? Models.Settings.Instance.Algorithm.BrightnessDirectionEnabled;
                    if (brightnessEnabled && product is not null && maskArea > 0)
                    {
                        // 复用生产同一判向实现：inferenceMat 与 product 掩码/Bounds 同处推理图坐标系，
                        // 与方法内部灰度统计的坐标系约定一致；头端明暗约定与死区仍由该方法读全局设置。
                        sendAngle = ToVGT.ToRobotAngle(DetectionRecordService.ApplyBrightnessHeadDirectionIfEnabled(
                            fallbackAngle, inferenceMat, product,
                            maskRectCenterX, maskRectCenterY, maskRectAngleDeg, maskArea,
                            brightnessEnabled: true,
                            out brightnessStats));
                        brightnessDirectionTried = true;
                    }
                    else
                    {
                        sendAngle = ToVGT.ToRobotAngle(fallbackAngle);
                    }
                    angleFromMaskFallback = true;
                }

                // 判向完成（两侧均有像素）时附上灰度统计，页面上直接核对头端明暗与死区是否合适。
                // 2026-09-11: 半区命名改为"正向/负向"（原名"亮/暗半区"与事实相反，两值大小关系不固定）；
                // 并显示掩码内对比度拉伸窗口，便于现场判断绝对灰度水平（窗口整体偏低说明成像偏暗）。
                string brightnessDetail = brightnessStats is { } bStats
                    ? $" 正向半区={bStats.MeanPlus:F0} 负向半区={bStats.MeanMinus:F0} 差值={bStats.Diff:F1}"
                      + (double.IsNaN(bStats.Low)
                          ? "（未拉伸）"
                          : $"（拉伸窗口 {bStats.Low:F0}~{bStats.High:F0}）")
                    : string.Empty;
                string fallbackAngleDesc = angleFromMaskFallback
                    ? (angleDirectionDegenerate ? "模型方向退化→" : string.Empty)
                      + (brightnessDirectionTried ? $"掩码主轴角+灰度判向{brightnessDetail}" : "掩码主轴角")
                    : string.Empty;
                string angleText = sendAngle is not null
                    ? $"\n角度: {sendAngle:F1}°" + (angleFromMaskFallback ? $"（{fallbackAngleDesc}）" : string.Empty)
                    : wantAngle
                        ? "\n角度: 无（角度模型未输出有效结果，详见日志质量门原因）"
                        : string.Empty;
                if (!wantAngle)
                {
                    // 2026-09-08: 勾选但模型文件缺失已回退单分割模型，备注区分原因（与生产行为一致）
                    string cause = angleFileMissing ? "角度模型文件缺失" : "角度未启用";
                    remark = (worldX is null ? "未标定 · " : string.Empty)
                             + (angleFromMaskFallback ? $"{cause}({fallbackAngleDesc})" : cause);
                }
                else if (angleResult is null)
                {
                    remark = worldX is null ? "未标定 · 角度无输出" : "角度无输出";
                }
                else
                {
                    remark = worldX is null ? "未标定 · 推理成功" : "OK（标定真实值）";
                }
                var summary = $"推理完成，耗时: {stopwatch.Elapsed.TotalMilliseconds:F0} ms\n"
                            + $"目标数: {segmentation?.Count ?? 0}\n"
                            + $"产品坐标: {coordText}"
                            + angleText;
                string angleOutLog = sendAngle is not null ? $"{sendAngle:F1}°" : angleOutDesc;
                LogService.Instance.Info($"测试推理完成：耗时={stopwatch.Elapsed.TotalMilliseconds:F0}ms，目标数={segmentation?.Count ?? 0}，产品坐标={coordText}，角度输出={angleOutLog}");
                ShowInfo(summary);

                // 记录到「推理结果」页并选中最新一条（角度=最终发送域值，与机器人实收 RZ 一致）
                AddTestResult(new RecipeTestResultItem(
                    DateTime.Now,
                    worldX,
                    worldY,
                    productPixel.X,
                    productPixel.Y,
                    sendAngle,
                    segmentation?.Count ?? 0,
                    stopwatch.Elapsed.TotalMilliseconds,
                    remark));
            }
            catch (Exception ex)
            {
                LogService.Instance.Error($"推理失败: {ex}");
                AddTestResult(new RecipeTestResultItem(
                    DateTime.Now, null, null, 0, 0, null, 0, 0, $"推理失败: {ex.Message}"));
                ShowError($"推理失败: {ex.Message}");
            }
        }
        #endregion
        /// <summary>
        /// 保存识别模版到文件，不显示消息
        /// </summary>
        public void SaveSilently()
        {
            RecipesManage.Instance.SaveRecipe(_recipe);
        }

        /// <summary>
        /// 浏览图像目录
        /// </summary>
        [RelayCommand]
        private void BrowseImageDirectory()
        {
            var dialog = new Microsoft.Win32.OpenFolderDialog
            {
                Title = "选择图像目录"
            };

            if (dialog.ShowDialog() == true && ImageTool != null)
            {
                ImageTool.DirectoryPath = dialog.FolderName;
                OnPropertyChanged(nameof(ImageTool));
                OnPropertyChanged(nameof(ImageDirectoryPath));
                RefreshCaptureAvailability();
            }
        }

        #region 设备参数
        /// <summary>
        /// 设置识别设备参数
        /// </summary>
        [RelayCommand]
        private async Task SetParameterAsync()
        {
            try
            {
                if (!await EnsureScannerReadyAsync().ConfigureAwait(false))
                {
                    return;
                }

                if (ImageTool != null)
                {
                    // H84: 传递 CancellationToken
                    await _scannerService.SetExposureTimeAsync(ImageTool.ExposureTime, _cts.Token).ConfigureAwait(false);
                    await _scannerService.SetGainAsync(ImageTool.Gain, _cts.Token).ConfigureAwait(false);
                }

                ShowInfo("设置参数成功");
            }
            catch (Exception ex)
            {
                LogService.Instance.Error($"设置参数失败: {ex}");
                ShowError($"设置参数失败: {ex.Message}");
            }
        }

        /// <summary>
        /// 获取识别设备参数
        /// </summary>
        [RelayCommand]
        private async Task GetParameterAsync()
        {
            try
            {
                if (!await EnsureScannerReadyAsync().ConfigureAwait(false))
                {
                    return;
                }

                if (ImageTool != null)
                {
                    // H84: 传递 CancellationToken
                    ImageTool.ExposureTime = await _scannerService.GetExposureTimeAsync(_cts.Token).ConfigureAwait(false);
                    ImageTool.Gain = await _scannerService.GetGainAsync(_cts.Token).ConfigureAwait(false);
                    OnPropertyChanged(nameof(ImageTool));
                }

                ShowInfo("获取参数成功");
            }
            catch (Exception ex)
            {
                LogService.Instance.Error($"获取参数失败: {ex}");
                ShowError($"获取参数失败: {ex.Message}");
            }
        }
        #endregion


        #region 获取图像
        /// <summary>
        /// 获取图像——按「图像来源」分流：
        /// <list type="bullet">
        /// <item>从扫码枪读取（ReadFromScanner=true）：相机软触发取单帧（ActivateAsync 已预先设软触发），失败自动重试。</item>
        /// <item>本地目录（ReadFromScanner=false）：逐个读取 DirectoryPath 下图片，跳过开头 RemoveCount 张，
        /// 到末尾自动回绕；不依赖扫码枪设备。</item>
        /// </list>
        /// </summary>
        [RelayCommand]
        private async Task GetImageAsync()
        {
            try
            {
                // 防御：VM 已被列表销毁（删除配方/卸载）时禁止继续操作，避免访问已释放 _cts
                if (_disposed)
                {
                    LogService.Instance.Warning("获取图像：配方视图模型已释放，请关闭并重新打开配方后再试。");
                    ShowWarning("配方视图已失效，请关闭后重新打开配方。");
                    return;
                }

                // 文件夹取图分支：不依赖扫码枪
                if (ImageTool is { ReadFromScanner: false } folderTool)
                {
                    await LoadNextFolderImageAsync(folderTool).ConfigureAwait(false);
                    return;
                }

                if (!await EnsureScannerReadyAsync().ConfigureAwait(false))
                {
                    return;
                }

                // 1. SDK 取图（带自动重试）
                var swGrab = Stopwatch.StartNew();
                FrameResult? imageResult = null;
                Exception? grabException = null;
                for (int attempt = 1; attempt <= GetImageMaxAttempts; attempt++)
                {
                    try
                    {
                        imageResult = await _scannerService.GetImageAsync().ConfigureAwait(false);
                        break; // 成功则退出重试循环
                    }
                    catch (OperationCanceledException)
                    {
                        throw; // 取消不重试，向上传播
                    }
                    catch (Exception ex)
                    {
                        grabException = ex;
                        LogService.Instance.Warning($"获取图像失败(尝试 {attempt}/{GetImageMaxAttempts}): {ex.Message}");
                        if (attempt < GetImageMaxAttempts)
                        {
                            await Task.Delay(GetImageRetryDelayMs, _cts.Token).ConfigureAwait(false);
                        }
                    }
                }
                swGrab.Stop();

                if (imageResult is null)
                {
                    // L: 所有重试均已失败，记录错误并提示用户可手动重试
                    LogService.Instance.Error($"获取图像失败(已重试 {GetImageMaxAttempts} 次): {grabException}");
                    ShowError($"获取图像失败（已重试 {GetImageMaxAttempts} 次），可点击\"获取图像\"按钮手动重试。");
                    return;
                }

                // P0-FIX: 用 using 模式包装 imageResult，确保 ImDecode 抛异常或提前 return 时也能归还 ArrayPool 缓冲区
                using (imageResult)
                {
                    if (imageResult.ImageData is null)
                    {
                        LogService.Instance.Warning("拍照返回空图像数据，跳过处理");
                        return;
                    }
                    var imageDataSize = imageResult.ImageData.Length;
                    MemoryDiagnostics.LogAllocation("RecopeVM(GetImage)", imageDataSize,
                        $"WxH={imageResult.Width}x{imageResult.Height}, Barcodes={imageResult.BarcodeResults?.Length ?? 0}");

                    // 2. 解码
                    var swDecode = Stopwatch.StartNew();
                    using var grayMat = Mat.ImDecode(imageResult.ImageData, ImreadModes.Grayscale);
                    // M700: ImageData 已解码，归还 ArrayPool 缓冲区（Dispose 幂等，重复调用安全）
                    imageResult.ReleaseImageData();
                    using var colorMat = grayMat.CvtColor(ColorConversionCodes.GRAY2BGR);
                    _originalMat?.Dispose();
                    _originalMat = grayMat.Clone();
                    var matSize = (long)_originalMat.Width * _originalMat.Height * _originalMat.Channels();
                    swDecode.Stop();

                    MemoryDiagnostics.LogAllocation("RecopeVM(OriginalMat)", matSize,
                        $"WxH={_originalMat.Width}x{_originalMat.Height}, Channels={_originalMat.Channels()}");

                    // 3. 条码绘制
                    var swDraw = Stopwatch.StartNew();
                    if (imageResult.HasBarcodeResults)
                    {
                        Tools.DrawBarcodeResults(colorMat, imageResult.BarcodeResults!);
                    }
                    swDraw.Stop();

                    UpdateImageForShow(colorMat.ToBitmapSource());

                    ShowInfo($"图像采集完成\n"
                        + $" SDK取图: {swGrab.Elapsed.TotalMilliseconds:F0} ms\n"
                        + $" 解码:     {swDecode.Elapsed.TotalMilliseconds:F0} ms\n"
                        + $" 条码绘制: {swDraw.Elapsed.TotalMilliseconds:F0} ms");
                }
            }
            catch (OperationCanceledException)
            {
                // L: 取消时静默退出
            }
            catch (Exception ex)
            {
                LogService.Instance.Error($"获取图像失败: {ex}");
                ShowError($"获取图像失败: {ex.Message}");
            }
        }

        /// <summary>
        /// 文件夹取图：从 DirectoryPath 逐个读取图片。
        /// 起点 = RemoveCount（跳过开头不稳定帧）；每次读取后游标 +1，到末尾回绕到起点。
        /// 读取结果与相机路径对齐：_originalMat 存灰度克隆（供测试推理/标定复用），画面按 BGR 显示。
        /// </summary>
        private async Task LoadNextFolderImageAsync(ImageTool tool)
        {
            if (string.IsNullOrWhiteSpace(tool.DirectoryPath) || !Directory.Exists(tool.DirectoryPath))
            {
                LogService.Instance.Warning($"[文件夹取图] 目录无效: {tool.DirectoryPath}");
                ShowWarning($"图像目录不存在或为空：\n{tool.DirectoryPath}");
                RefreshCaptureAvailability();
                return;
            }

            List<string> files;
            try
            {
                files = await Task.Run(() => ListFolderImages(tool.DirectoryPath)).ConfigureAwait(false);
            }
            catch (Exception ex)
            {
                LogService.Instance.Error($"[文件夹取图] 枚举目录失败: {ex}");
                ShowError($"读取图像目录失败: {ex.Message}");
                return;
            }

            if (files.Count == 0)
            {
                LogService.Instance.Warning($"[文件夹取图] 目录内没有图片: {tool.DirectoryPath}");
                ShowWarning("目录内没有可用的图片文件。");
                RefreshCaptureAvailability();
                return;
            }

            // 起点=RemoveCount（跳过开头帧）；越界保护到最后一个可用下标；RemoveCount/目录变化时重置
            int start = Math.Min(Math.Max(tool.RemoveCount, 0), Math.Max(files.Count - 1, 0));
            if (_folderImageStartIndex != start || !string.Equals(_folderImageListPath, tool.DirectoryPath, StringComparison.OrdinalIgnoreCase))
            {
                _folderImageStartIndex = start;
                _folderImageListPath = tool.DirectoryPath;
                _folderImageIndex = start;
            }

            // 回绕区间 [start, files.Count)，游标按自然递增取模
            int span = files.Count - start;
            int index = start + ((_folderImageIndex - start) % span + span) % span;
            _folderImageIndex = index + 1;
            string filePath = files[index];

            try
            {
                using var grayMat = await Task.Run(() =>
                {
                    var mat = Cv2.ImRead(filePath, ImreadModes.Grayscale);
                    return mat is null || mat.Empty() ? null : mat;
                }).ConfigureAwait(false);

                if (grayMat is null)
                {
                    LogService.Instance.Warning($"[文件夹取图] 解码失败，已跳过: {filePath}");
                    ShowWarning($"图片解码失败：{Path.GetFileName(filePath)}");
                    return;
                }

                _originalMat?.Dispose();
                _originalMat = grayMat.Clone();
                using var colorMat = grayMat.CvtColor(ColorConversionCodes.GRAY2BGR);
                UpdateImageForShow(colorMat.ToBitmapSource());

                MemoryDiagnostics.LogAllocation("RecipeVM(FolderGetImage)",
                    (long)_originalMat.Width * _originalMat.Height * _originalMat.Channels(),
                    $"File={Path.GetFileName(filePath)} WxH={_originalMat.Width}x{_originalMat.Height}");
                ShowInfo($"已从文件夹读取（{index + 1}/{files.Count}）：{Path.GetFileName(filePath)}");
            }
            catch (Exception ex)
            {
                LogService.Instance.Error($"[文件夹取图] 读取失败: {ex}");
                ShowError($"读取图片失败: {ex.Message}");
            }
        }
        #endregion

        #region 保存当前显示的图像
        /// <summary>
        /// 保存当前显示的图像
        /// </summary>
        [RelayCommand]
        private void SaveImage()
        {
            if (ImageForShow == null)
            {
                ShowWarning("未获取到图像");
                return;
            }

            var dialog = new Microsoft.Win32.SaveFileDialog
            {
                Title = "保存图像",
                Filter = "PNG图像|*.png|JPEG图像|*.jpg|BMP图像|*.bmp|所有文件|*.*",
                DefaultExt = ".png",
                FileName = $"Image_{DateTime.Now:yyyyMMdd_HHmmss}"
            };

            if (dialog.ShowDialog() == true)
            {
                // L71: 强制转换前做类型检查，避免 InvalidCastException
                if (ImageForShow is not BitmapSource bmp)
                {
                    ShowWarning("当前图像格式不支持保存");
                    return;
                }
                using var mat = BitmapSourceConverter.ToMat(bmp);
                mat.SaveImage(dialog.FileName);
                ShowInfo($"图像已保存至:\n{dialog.FileName}");
            }
        }
        #endregion
        #endregion

        #region 实时显示

        private async Task StartLiveDisplayAsync()
        {
            // H51b/L395a: 只 Cancel 不 Dispose，避免后台任务访问已 Dispose 的 token 抛 ObjectDisposedException；
            // 旧 _liveDisplayCts 有意不在此处 Dispose（由 GC 回收），Dispose 中显式释放
            // VSTHRD103: CancellationTokenSource.Cancel() 本身是同步方法，分析器误报
#pragma warning disable VSTHRD103
            _liveDisplayCts?.Cancel();
#pragma warning restore VSTHRD103
            _liveDisplayCts = new CancellationTokenSource();
            var token = _liveDisplayCts.Token;
            // M67: 捕获 token 后使用前检查是否已取消（StopLiveDisplay 可能在 await 期间被调用）
            if (token.IsCancellationRequested)
            {
                return;
            }
            if (!await EnsureScannerReadyAsync().ConfigureAwait(false))
            {
                // M14: 启动失败时回滚 IsLiveDisplayEnabled 状态
                SetProperty(ref _isLiveDisplayEnabled, false);
                return;
            }
            try
            {
                await _scannerService.StartLiveDisplayAsync(UpdateImageForShow, token).ConfigureAwait(false);
            }
            catch (OperationCanceledException)
            {
            }
            catch (Exception ex)
            {
                LogService.Instance.Error($"实时显示异常: {ex}");
                // M274a: catch 块中先调用 StopLiveDisplay 取消 CTS，再回滚状态
                StopLiveDisplay();
                SetProperty(ref _isLiveDisplayEnabled, false);
            }
        }

        private void StopLiveDisplay()
        {
            // M67: 只 Cancel，不立即 Dispose CTS，避免后台任务访问已 Dispose 的 token 抛 ObjectDisposedException；
            // CTS 将在 StartLiveDisplayAsync 重新赋值前或 Dispose 中显式释放
            _liveDisplayCts?.Cancel();
        }

        #endregion

        #region 调试信息
        // L331: 改为带 INotifyPropertyChanged 的属性
        private bool _isShowDebug;
        public bool IsShowDebug { get => _isShowDebug; set => SetProperty(ref _isShowDebug, value); }

        private void DebugCallback(string message, Mat mat)
        {
            if (!IsShowDebug)
            {
                return;
            }
            // REVIEW(2026-08-05): 此回调在后台线程（GetThreePoints 的 Task.Run）执行，
            // OpenCV HighGUI（NamedWindow/ImShow）在 WPF 非 UI 线程调用易触发 SEH/AccessViolation，
            // 且 App 对这类异常不 Handle 直接崩退。这里 try-catch 兜底，防止标定流程崩溃。
            try
            {
                Cv2.NamedWindow(message, WindowFlags.Normal);
                Cv2.ImShow(message, mat);
                if (message.Contains("调试图像", StringComparison.Ordinal))
                {
                    Cv2.WaitKey(1);
                    Cv2.DestroyWindow(message);
                }
            }
            catch (Exception ex)
            {
                LogService.Instance.Warning($"调试图像显示失败(已跳过): {ex.Message}");
            }
        }

        #region 查找参考点
        /// <summary>
        /// 查找参考点（三个圆点和原点）
        /// </summary>
        [RelayCommand]
        private async Task FindReferencePointsAsync()
        {
            if (_originalMat == null || _originalMat.Empty())
            {
                ShowWarning("尚未获取图像");
                return;
            }

            if (CoordinateTool == null)
            {
                ShowWarning("坐标工具未初始化");
                return;
            }

            // L: 标定进行中标志置位，UI 切换为"标定中..."并禁用按钮
            IsCalibrating = true;
            try
            {
                // 使用原始图像进行检测
                using var grayMat = _originalMat.Channels() == 1 ? _originalMat.Clone() : _originalMat.CvtColor(ColorConversionCodes.BGR2GRAY);
                // M298a: Chessboard.GetThreePoints 签名不接受 enableDebug 参数，只能通过 static 属性控制调试输出
                Chessboard.EnableDebugOutput = IsShowDebug;
                // L: 将 CPU 密集型圆点检测放到后台线程执行，避免阻塞 UI；支持取消
                var token = _cts.Token;
                var detectedRects = await Task.Run(() => Chessboard.GetThreePoints(grayMat,
                    patternSize: CoordinateTool.PatternSize,
                    roiPadding: CoordinateTool.RoiPadding,
                    minArea: CoordinateTool.MinArea,
                    maxArea: CoordinateTool.MaxArea,
                    minCircularity: CoordinateTool.MinCircularity,
                    bilateralD: CoordinateTool.BilateralD,
                    bilateralSigmaColor: CoordinateTool.BilateralSigmaColor,
                    bilateralSigmaSpace: CoordinateTool.BilateralSigmaSpace,
                    cannyThreshold1: CoordinateTool.CannyThreshold1,
                    cannyThreshold2: CoordinateTool.CannyThreshold2,
                    kernelSize: CoordinateTool.KernelSize,
                    enableCLAHE: CoordinateTool.EnableCLAHE,
                    enableMultiStrategy: CoordinateTool.EnableMultiStrategy,
                    debugCallback: DebugCallback), token).ConfigureAwait(true);

                // 将 RotatedRect 转换为 Point2f 用于坐标系
                var circles = detectedRects.Select(r => r.Center).ToList();

                if (circles.Count < 3)
                {
                    ShowWarning($"仅检测到 {circles.Count} 个圆点，需要3个圆点来确定坐标系");
                    return;
                }
                if (CoordinateTool.SquareSize <= 0)
                {
                    ShowWarning("方格尺寸必须大于0");
                    return;
                }

                // L358a: _coordinateSystem 改为局部变量
                var coordinateSystem = Chessboard.GetCoordinateSystem(circles);
                var origin = coordinateSystem.Origin;
                var xPoint = coordinateSystem.XPoint;
                var yPoint = coordinateSystem.YPoint;

                var pixelDistX = Math.Sqrt(Math.Pow(xPoint.X - origin.X, 2) + Math.Pow(xPoint.Y - origin.Y, 2));
                var pixelDistY = Math.Sqrt(Math.Pow(yPoint.X - origin.X, 2) + Math.Pow(yPoint.Y - origin.Y, 2));
                var pixelPerSquareX = pixelDistX / 2d;
                var pixelPerSquareY = pixelDistY;

                // 更新坐标工具的参考点
                CoordinateTool.Origin = origin;
                CoordinateTool.XPoint = xPoint;
                CoordinateTool.YPoint = yPoint;
                OnPropertyChanged(nameof(CoordinateTool));

                // 在原始图像上绘制参考点
                using var colorMat = _originalMat.Channels() == 1 ? _originalMat.CvtColor(ColorConversionCodes.GRAY2BGR) : _originalMat.Clone();
                DrawReferencePoints(colorMat, coordinateSystem, circles);
                UpdateImageForShow(colorMat.ToBitmapSource());

                ShowInfo($"成功找到参考点\n原点: ({origin.X:F2}, {origin.Y:F2})\nX轴: ({xPoint.X:F2}, {xPoint.Y:F2})\nY轴: ({yPoint.X:F2}, {yPoint.Y:F2})\n方格尺寸: {CoordinateTool.SquareSize}\nX方向每格像素: {pixelPerSquareX:F2}\nY方向每格像素: {pixelPerSquareY:F2}");
                // P2-19: 标定完成后通知 IsCalibrated 属性，刷新向导状态
                OnPropertyChanged(nameof(IsCalibrated));
            }
            catch (OperationCanceledException)
            {
                // L: 标定被取消（通常由 Dispose 触发），静默退出
            }
            catch (Exception ex)
            {
                // REVIEW(2026-08-05): 兜底捕获所有异常——此方法由 RelayCommand 直接调用，
                // 未捕获异常会经 Dispatcher/AppDomain 通道处理，重则直接崩退（SEH 等不 Handle）。
                LogService.Instance.Error($"查找基准点失败: {ex}");
                ShowWarning($"查找基准点失败: {ex.Message}");
            }
            finally
            {
                IsCalibrating = false;
            }
        }
        #endregion
        #region 在图像上绘制参考点
        /// <summary>
        /// 在图像上绘制参考点
        /// </summary>
        private void DrawReferencePoints(Mat mat, ThreePointCoordinateSystem coordinateSystem, List<Point2f> allCircles)
        {
            // 绘制检测到的圆点（灰色）
            foreach (var circle in allCircles)
            {
                Cv2.Circle(mat, new OpenCvSharp.Point((int)circle.X, (int)circle.Y), 10, Scalar.Gray, 2);
            }
            coordinateSystem.DrawOnMat(mat);
        }
        #endregion
        #endregion

        #region 参数优化

        /// <summary>
        /// 自动优化参数
        /// </summary>
        [RelayCommand]
        private void OptimizeParameter()
        {
            // 取消正在优化的任务
            if (IsOptimizing)
            {
                _optimizeCts?.Cancel();
                return;
            }

            _optimizeCts?.Cancel();
            _optimizeCts?.Dispose();
            _optimizeCts = new CancellationTokenSource();
            // H55: 在启动任务前设置 IsOptimizing=true，避免任务排队期间（首个 await 之前）IsOptimizing 仍为 false，
            // 导致用户再次点击时 Dispose 仍被排队任务引用的旧 CTS，或 finally 块误取消新 CTS
            IsOptimizing = true;
            // H37: 保存任务引用，便于 Dispose 时等待，避免 fire-and-forget 导致未观察异常或资源泄漏
            _optimizeTask = OptimizeParameterAsync(_optimizeCts.Token);
        }

        private async Task OptimizeParameterAsync(CancellationToken token)
        {
            // H70: 捕获 CTS 为局部变量，避免 finally 块读取 _optimizeCts 时存在竞态
            var cts = _optimizeCts;
            try
            {
                if (!await EnsureScannerReadyAsync().ConfigureAwait(false))
                {
                    return;
                }

                // 使用 ScannerOptimizer 进行优化
                var device = _scannerService;

                (float minExposureTime, float maxExposureTime) = await device.GetExposureRangeAsync(token).ConfigureAwait(false);
                (float minGain, float maxGain) = await device.GetGainRangeAsync(token).ConfigureAwait(false);

                // 更新UI的频率，避免过于频繁，只在一定间隔后更新
                var lastUpdateTime = DateTime.MinValue;
                // L402a: 使用类常量 OptimizeUiUpdateIntervalMs
                var updateIntervalMs = OptimizeUiUpdateIntervalMs;
                Stopwatch stopwatch = Stopwatch.StartNew();
                // M20/M27: RunSimulatedAnnealing 为同步签名，回调内 async 方法用 GetAwaiter().GetResult()
                // 在 Task.Run 线程池线程上执行，无同步上下文死锁风险
                // M66: 回调内 sync-over-async 暂时保留 — 模拟退火回调签名为同步 (Func<float,float,(float,float)>)，
                // 无法直接 await；好在 Task.Run 已在线程池线程执行，无 UI 同步上下文死锁风险
                var result = await Task.Run(() => ScannerOptimizer.RunSimulatedAnnealing(
                    (exposure, gain) =>
                    {
                        // M320a: 优化回调 sync-over-async 传入 token，使设备方法可响应取消
                        device.SetExposureTimeAsync(exposure, token).GetAwaiter().GetResult();
                        device.SetGainAsync(gain, token).GetAwaiter().GetResult();
                        device.ExecuteSoftwareTriggerAsync(token).GetAwaiter().GetResult();

                        // REVIEW-FIX: 用 using 包裹 FrameResult。GetImageAsync 返回的 FrameResult
                        // 持有 ArrayPool<byte>.Shared.Rent 的整帧缓冲区（FrameResult.cs:87），
                        // 不释放则模拟退火每轮迭代泄漏一份帧大小缓冲，长时间优化内存持续上涨。
                        using var image = device.GetImageAsync(cancellationToken: token).GetAwaiter().GetResult();

                        // 计算得分
                        float score = image.BarcodeResults.Length > 0
                            ? image.BarcodeResults.Max(b => b.Confidence())
                            : 0f;

                        // 生成灰度图像，通过计算亮度来辅助得分
                        // L362b: ImageData 可能为 null，跳过亮度计算直接返回得分
                        if (image.ImageData is null)
                        {
                            return (score, 0f);
                        }
                        using Mat colorMat = Mat.ImDecode(image.ImageData, ImreadModes.Color);
                        using Mat grayMat = colorMat.CvtColor(ColorConversionCodes.BGR2GRAY);
                        float avgBrightness = 0;
                        if (image.HasBarcodeResults)
                        {
                            List<Point2f> points = new List<Point2f>();
                            foreach (var p in image.BarcodeResults.First().Location())
                            {
                                points.Add(new Point2f(p.X, p.Y));
                            }
                            var barcodeLocation = Cv2.BoundingRect(points);
                            var barcodeRect = grayMat[barcodeLocation];
                            avgBrightness = (float)barcodeRect.Mean().Val0;
                        }
                        else
                        {
                            avgBrightness = (float)grayMat.Mean().Val0;
                        }
                        var now = DateTime.Now;
                        if ((now - lastUpdateTime).TotalMilliseconds >= updateIntervalMs)
                        {
                            lastUpdateTime = now;

                            colorMat.PutText($"Score: {score:F2}, Brightness: {avgBrightness:F2},Gain:{gain:F2},Exposure:{exposure:F2}",
                                new OpenCvSharp.Point(100, colorMat.Height / 2),
                                HersheyFonts.HersheySimplex, 2.0, Scalar.Red, 3);

                            // 复制 Mat 以便异步更新 UI，避免阻塞主线程
                            UpdateImageForShow(colorMat.ToBitmapSource());
                        }

                        return (score, avgBrightness);
                    }, minExposureTime, maxExposureTime, minGain, maxGain,
                    cancellationToken: token), token).ConfigureAwait(false);
                stopwatch.Stop();
                // H14: 删除 _optimizeCts.Cancel() — 优化已完成，取消 token 会让后续 Task.Delay(500, token) 立即抛异常，
                // 导致 GetImageAsync() 验证图像永不采集。finally 块仍会 Cancel，此处冗余。
                if (ImageTool != null)
                {
                    // L75: 局部变量改为 camelCase 命名
                    var (exposure, gain, score) = (result.BestExposure, result.BestGain, result.BestScore);
                    ImageTool.ExposureTime = exposure;
                    ImageTool.Gain = gain;
                    OnPropertyChanged(nameof(ImageTool));
                    ShowInfo($"优化完成\n曝光时间: {exposure}\n增益: {gain}\n最佳得分: {score}\n耗时: {stopwatch.ElapsedMilliseconds}ms");
                    // H84/M320a: 传递 token
                    await device.SetExposureTimeAsync(exposure, token).ConfigureAwait(false);
                    await device.SetGainAsync(gain, token).ConfigureAwait(false);
                    // H14: 去掉 token，避免已取消的 token 导致 Task.Delay 抛异常
                    // M65: 补齐 ConfigureAwait(false)
                    // M314b: 使用提取的常量 OptimizePostApplyDelayMs
                    await Task.Delay(OptimizePostApplyDelayMs).ConfigureAwait(false);
                    await GetImageAsync().ConfigureAwait(false);
                }
            }
            finally
            {
                // 确保异常路径下也取消优化任务，避免 cts 泄漏
                // H70: 使用局部变量 cts，避免读取 _optimizeCts 时的竞态
                // M341: Dispose 超时后可能已释放 cts，Cancel 会抛 ObjectDisposedException
                // VSTHRD103: CancellationTokenSource.Cancel() 本身是同步方法，分析器误报
#pragma warning disable VSTHRD103
                try { cts?.Cancel(); } catch (ObjectDisposedException) { }
#pragma warning restore VSTHRD103
                IsOptimizing = false;
            }
        }

        #endregion
        private volatile bool _disposed;

        public void Dispose()
        {
            Dispose(true);
        }

        protected virtual void Dispose(bool disposing)
        {
            if (_disposed) return;
            _disposed = true;

            if (disposing)
            {
                // 触发切换：正常关闭路径已由 DeactivateAndClearSessionAsync 中的 DeactivateAsync 完成；
                // 若直接 Dispose（删除配方/列表卸载/退出），fire-and-forget 恢复硬触发
                _ = DeactivateAsync();

                // 1) 停止实时显示与参数优化（二者不使用推理预测池，可先行清理）
                StopAuxiliaryTasks();
                // 2) 取消推理任务并等待其结束——推理正占用预测池/Mat，须先于资源释放
                _cts.Cancel();
                // VSTHRD002: Dispose 不能改为 async，sync-over-async 不可避免；已用 WaitAsync 超时避免无限阻塞
#pragma warning disable VSTHRD002
                try { _inferenceTask?.WaitAsync(TimeSpan.FromSeconds(DisposeTaskTimeoutSec)).GetAwaiter().GetResult(); }
                catch (Exception ex) { LogService.Instance.Warning($"Dispose 等待推理任务结束超时: {ex.Message}"); }
#pragma warning restore VSTHRD002
                _cts.Dispose();

                // 3) 释放会话期资源（Mat/显示图/预测池）
                ReleaseCoreResources();
            }
        }
    }
}
