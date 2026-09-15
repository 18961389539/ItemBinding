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
        /// 抓取点沿产品长轴的偏移（mm，正值 = 朝产品头部）。
        /// <para>产品局部坐标系，随产品角度旋转 —— 用于表达夹爪偏心、抓取点不在产品中心。
        /// 0 = 抓取点即掩码最小外接旋转矩形中心（默认，与改造前一致）。</para>
        /// <para>2026-09-15 起取代原"世界坐标系常量平移补偿 OffsetX/OffsetY"；
        /// 旧配方的补偿值在加载时已一次性自动迁移到本项与 <see cref="GrabOffsetShortMm"/>。</para>
        /// <para>朝向不可信（消歧链全部回退到无向角）时本项不生效，自动退化为中心，避免抓反。</para>
        /// </summary>
        public double GrabOffsetLongMm
        {
            get => _recipe.GrabOffsetLongMm;
            set
            {
                var v = (float)value;
                if (Math.Abs(_recipe.GrabOffsetLongMm - v) > 0.001)
                {
                    _recipe.GrabOffsetLongMm = v;
                    OnPropertyChanged();
                }
            }
        }

        /// <summary>
        /// 抓取点沿产品短轴的偏移（mm，正值 = 面朝头部时的右手侧）。
        /// 若现场方向与预期相反，直接填负值即可（图像的"左右"取决于相机安装朝向，代码无法自行判断）。
        /// </summary>
        public double GrabOffsetShortMm
        {
            get => _recipe.GrabOffsetShortMm;
            set
            {
                var v = (float)value;
                if (Math.Abs(_recipe.GrabOffsetShortMm - v) > 0.001)
                {
                    _recipe.GrabOffsetShortMm = v;
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
        /// 配方页激活：暂停主循环 → 等在途推理租约归还 → 切软触发。
        /// 软触发模式下实时显示和"获取图像"都能正常出图。
        /// </summary>
        public async Task ActivateAsync()
        {
            HomeViewModel.PauseLoop();
            // 按真实状态等待主循环在途推理结束（原实现为固定 12 秒盲等，正常情况白等 12 秒）。
            // 12 秒上限覆盖主循环单帧取图超时（10s）+ 余量，仅在异常路径才会真正等满。
            var drained = await HomeViewModel.WaitForMainLoopDrainAsync(TimeSpan.FromSeconds(12)).ConfigureAwait(false);
            if (!drained)
            {
                LogService.Instance.Warning("配方页激活：等待主循环排空超时（12 秒），仍继续切换软触发");
            }
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