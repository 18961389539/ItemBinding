using CommunityToolkit.Mvvm.ComponentModel;
using Extensions;
using HikScanner;
using HikScannerType = HikScanner.HikScanner;
using MainAPP.Application;
using JinlongYolo.YoloSharp;
using JinlongYolo.YoloSharp.Data;
using MainAPP.Models;
using MainAPP.Services;
using OpenCvSharp;
using CommunityToolkit.Mvvm.Messaging;
using MainAPP.Messages;
using SixLabors.Fonts;
using SixLabors.ImageSharp;
using SixLabors.ImageSharp.Drawing.Processing;

using SixLabors.ImageSharp.Formats;
using SixLabors.ImageSharp.PixelFormats;
using SixLabors.ImageSharp.Processing;
using System.Collections.ObjectModel;
using System.Threading.Channels;
using System.IO;
using JinlongYolo.YoloSharp.Extensions;
using System.Runtime;
using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Diagnostics;
using Serilog;
using MainAPP;
using Microsoft.Extensions.DependencyInjection;

// VSTHRD001: 使用 Dispatcher.BeginInvoke 切换到 UI 线程是 WPF 标准模式，无需 JoinableTaskFactory
#pragma warning disable VSTHRD001

namespace MainAPP.ViewModels
{
    public partial class HomeViewModel : ObservableObject, IDisposable
    {
        private volatile bool _disposed;
        // 输送线产品去重跟踪器
        private readonly ProductTracker _productTracker = new();
        // H78b: 保护 _activeInferenceCount 与 _inferenceDrainedTcs 一致性的锁
        private readonly object _inferenceLock = new();
        private volatile TaskCompletionSource<bool> _inferenceDrainedTcs = CreateCompletedTcs();
        private int _activeInferenceCount;
        // L: 丢帧计数与通知节流计数器，用于状态栏显示与 Growl 节流提示
        private int _dropFrameCount;
        private int _dropNotifyCounter;
        private const int DropFrameNotifyInterval = 10;
        // 已处理帧计数（用于 FPS 计算），每处理完一帧递增，与检测结果无关
        private long _processedFrameCount;

        // M700: 配方页打开时主循环跳过取帧。简单布尔，无竞态。
        private static volatile bool s_isPaused;
        // REVIEW(2026-08-05): 首帧已记录相机帧格式（诊断用）
        private static bool _frameFormatLogged;

        public static void PauseLoop() => s_isPaused = true;
        public static void ResumeLoop() => s_isPaused = false;

        // L362c: 记录上一次 TryEnsureProcessingReady 的错误消息，避免配方未就绪时重复刷屏日志
        private string? _lastProcessingReadyError;
        // P1-1: 预测器池为 null 时仅记录一次日志，避免每帧刷屏（配置错误已在 CreatePredictorPoolAsync 中通过 NotificationService.Error 提示）
        private volatile bool _hasLoggedPredictorNotReady;
        // L362c: 配方未就绪时重试间隔（毫秒）
        private const int ProcessingReadyRetryMs = 2000;

        // 内存优化：限制并发推理数，避免大量帧同时持有大对象导致 OOM
        private readonly SemaphoreSlim _inferenceSemaphore = new(4, 4);
        // L113: Dispose 超时常量
        private const int DisposeLockTimeoutSec = 5;
        private const int DisposeTaskTimeoutSec = 2;
        // L101: HikScanner 取图超时错误码
        private const int HikErrorCodeTimeout = unchecked((int)0x80020006);
        // L102/L103: 取图重试相关常量
        private const int MaxGrabRetryAttempts = 1; // M700: 不重试——释放锁更快，避免阻塞 SDK SwitchToSoftTrigger
        private const int GrabRetryDelayMs = 150;
        // L328: 推理并发日志采样间隔与阈值，避免每帧刷屏
        private const int InferenceLogSamplingInterval = 2;
        private const int InferenceLogActiveThreshold = 4;
        // L328: DrawPolygon 线宽
        private const float DrawPolygonLineWidth = 3f;
        // 2026-09-09: 头尾分割线（LimeGreen，短轴方向"一分为二"）与 发送角度向量箭头（橙，主轴方向）并存。
        // 分割线沿用旧"一分为二"参考线：沿掩码矩形短轴穿过中心，两端外扩，直观划分产品头/尾两半；
        // 向量箭头从掩码最小外接矩形中心（=发送 X/Y 图像点）沿矩形宽度轴（长轴）伸出，表达发送朝向。
        // 二者垂直交于矩形中心。颜色全限定（文件同时 using System.Windows.Media，裸 Color 会产生 CS0104 二义）
        private static readonly SixLabors.ImageSharp.Color MaskAxisLineColor = SixLabors.ImageSharp.Color.LimeGreen;
        private const float MaskAxisLineWidth = 1.5f;
        private const float MaskAxisPadRatio = 0.25f;
        private const float MaskAxisMinPadPixels = 6f;
        private static readonly SixLabors.ImageSharp.Color DirectionArrowColor = SixLabors.ImageSharp.Color.Orange;
        private const float DirectionArrowLineWidth = 2f;
        private const float DirectionArrowLengthPx = 56f;
        private const float ArrowHeadLengthPx = 14f;
        // 2026-09-09: 边界过滤"禁入线"辅助线（黄色）——按 Settings.Algorithm.EdgeMargin* 在原图上画出
        // 检测框不允许越过的四条参考线，供现场观察"半个产品/贴边产品"被拒的原因。
        // 仅可视化，不参与过滤（实际过滤见 DetectionRecordService.BuildAndSaveAsync 越界判定）。
        private static readonly SixLabors.ImageSharp.Color EdgeMarginLineColor = SixLabors.ImageSharp.Color.Yellow;
        private const float EdgeMarginLineWidth = 2f;
        /// <summary>
        /// 图像队列最大长度，超过后新帧会标记为 NeedDrop。建议在 3-20 之间调整以平衡延迟与突发吞吐。
        /// </summary>
        private const int MaxImagesinQueue = 8;

        /// <summary>
        /// 当前使用的配方，始终从配方管理器获取最新值。
        /// </summary>
        private Recipe? CurrentRecipe => RecipesManage.Instance.CurrentRecipe;

        /// <summary>
        /// 后台任务：负责读取图像与处理图像的循环任务句柄。
        /// </summary>
        // L392a: 多字段单行声明拆为两行
        private Task? _readImageLoopTask;
        private Task? _processImageLoopTask;
        // M272a: 跟踪 InitializeAsync fire-and-forget 任务，便于 Dispose 中带超时等待
        private Task? _initializeTask;
        // H80a: 跟踪 ReloadPredictorAsync fire-and-forget 任务，便于 Dispose 中带超时等待
        // L438: 标记 volatile，确保 OnCurrentRecipeChanged 写入后 Dispose 线程能读到最新值
        private volatile Task? _reloadPredictorTask;

        /// <summary>
        /// 取消标记源，用于停止后台循环任务。
        /// </summary>
        private readonly CancellationTokenSource _cts = new();

        /// <summary>
        /// 坐标系变换器，将图像坐标映射到物理世界坐标。
        /// </summary>
        private readonly CoordinateSystemMapping.CoordinateTransformer _transformer = new();

        /// <summary>
        /// 推理设备类型（"GPU" 或 "CPU"），用于 Timing 日志标注。
        /// </summary>
        private string _inferenceDevice = "CUDA";
        private bool _configLogged;

        /// <summary>
        /// 条码标签绘制字体。2026-09-09: 由 32 缩小为 18，避免大字号遮挡画面
        /// （当前全文件仅条码 DrawText 使用本字体）。
        /// </summary>
        private readonly Font _font = CreateDefaultFont();

        private static Font CreateDefaultFont()
        {
            try { return SixLabors.Fonts.SystemFonts.CreateFont("Arial", 18); }
            catch (Exception ex)
            {
                // M285a: 记录失败日志，fallback 前检查字体列表非空
                LogService.Instance.Warning($"创建默认字体失败: {ex}");
                if (!SixLabors.Fonts.SystemFonts.Families.Any())
                {
                    throw new InvalidOperationException("系统字体列表为空，无法创建默认字体", ex);
                }
                return SixLabors.Fonts.SystemFonts.CreateFont(SixLabors.Fonts.SystemFonts.Families.First().Name, 18);
            }
        }

        /// <summary>
        /// YOLO 预测器实例（在后台初始化）。可能为 null（初始化失败或未配置模型）。
        /// </summary>
        // L253: 移除 null! 初始化器——字段本身为 nullable，运行时初始为 null，null! 仅为编译期抑制且语义矛盾
        private volatile YoloPredictorPool? _predictorPool;
        /// <summary>
        /// 角度检测模型预测器池（分割任务）。仅当配方启用 IsAngleDetectionEnabled 且模型文件存在时非空。
        /// 角度推理与分割推理共用本字段判定是否可用。
        /// </summary>
        private volatile YoloPredictorPool? _anglePredictorPool;

        // REVIEW(2026-09-04): 分割推理后端自愈状态机。
        // CUDA 会话可能在运行期失效（如 CUDNN_FE failure 7：会话建得起来、一跑卷积就炸），
        // 这类失效建池期降级救不了，只能等人工重启。这里在推理期连续失败
        // SegHealFailThreshold 次后，自动沿 Cuda → OpenVINO GPU → OpenVINO CPU → CPU 逐级降级重建，
        // 并在换入新池前用一张小图跑一次冒烟推理验证，避免换入又一个"建得起、跑不动"的池。
        private const int SegHealFailThreshold = 2;     // 连续失败达到该次数才触发自愈（容忍偶发单次抖动）
        private const long SegHealCooldownMs = 30_000;  // 自愈冷却：坏环境下避免反复重建的抖动
        private InferenceBackend _segBackend = InferenceBackend.Cuda; // 当前主池实际后端，随 重载/自愈 更新（GPU 优先、失败回退 CPU）
        private int _segConsecutiveFails;               // 当前连续推理失败计数
        private int _segHealRunning;                    // 1=自愈任务运行中（防重入）
        private long _lastSegHealAttemptTicks;

        // VGT 通信服务（构造函数注入；若为 null 则回退到 DI 容器解析）
        private readonly IToVGTService _toVgtService;
        private readonly ModelLoaderService _modelLoader;
        private readonly DetectionRecordService _detectionRecord;

        public HomeViewModel(IToVGTService? toVgtService = null, ModelLoaderService? modelLoader = null, DetectionRecordService? detectionRecord = null)
        {
            _toVgtService = toVgtService ?? App.Services.GetRequiredService<IToVGTService>();
            _modelLoader = modelLoader ?? App.Services.GetRequiredService<ModelLoaderService>();
            _detectionRecord = detectionRecord ?? App.Services.GetRequiredService<DetectionRecordService>();

            try
            {
                RecipesManage.Instance.CurrentRecipeChanged += OnCurrentRecipeChanged;

                // 构造函数说明：仅执行轻量级、快速完成的初始化（避免在 UI 线程执行耗时操作）。
                // 重度初始化（创建 YoloPredictor）在 InitializeAsync 中异步完成。
                // Keep constructor lightweight and defensive: avoid doing heavy I/O or long-running work on the UI thread.
                var recipe = CurrentRecipe;
                var edgeTool = recipe?.YoloTool?.EdgeDetection;

                // 2026-09-09 FIX: 原逻辑只要应用目录存在默认 best.onnx 就无条件覆盖配方模型路径，
                // 导致配方详情页显式设置的模型在启动/切配方时被静默回退为 best.onnx。
                // 现仅在配方未配置有效路径时兜底（判定见 ModelLoaderService.NeedsDefaultModelFallback），
                // 用户显式设置且存在的模型路径绝不被改写。
                var modelFullname = Path.Join(AppDomain.CurrentDomain.BaseDirectory, "Models", "best.onnx");
                if (edgeTool is not null && File.Exists(modelFullname) && ModelLoaderService.NeedsDefaultModelFallback(edgeTool.ModelPath))
                {
                    edgeTool.ModelPath = modelFullname;
                }

                // 启动后台初始化：在非 UI 线程创建预测器并启动处理循环。
                // 注意：此调用不会等待初始化完成。
                // 示例：若模型路径不可用或依赖项缺失，_predictor 将被设置为 null，并在日志中记录错误。
                // M272a: 跟踪 fire-and-forget 任务，便于 Dispose 中带超时等待
                _initializeTask = InitializeAsync();
            }
            catch (Exception ex)
            {
                // Last-resort logging to avoid blowing up the constructor
                LogService.Instance.Error($"HomeViewModel 构造函数内部异常: {ex}");
            }
        }


        /// <summary>
        /// 异步初始化：验证配置并在后台创建 <see cref="YoloPredictor"/> 实例，然后启动主循环。
        /// </summary>
        /// <remarks>
        /// 建议：此方法在后台线程执行模型加载（避免阻塞 UI）。如果模型较大或需要构建优化器（OpenVINO/CUDA），
        /// 加载时间可能较长，请根据应用场景提供用户提示或超时策略。
        /// </remarks>
        /// <example>
        /// 示例：默认情况下若启用 OpenVINO 或 CUDA，可能需要安装相应运行时环境。
        /// </example>
        private async Task InitializeAsync()
        {
            try
            {
                await ReloadPredictorAsync(CurrentRecipe).ConfigureAwait(false);
                // M150: 补齐 ConfigureAwait(false)，与文件内其余 await 保持一致
                await Loop().ConfigureAwait(false);
            }
            catch (Exception ex)
            {
                LogService.Instance.Error($"InitializeAsync 初始化失败: {ex}");
            }
        }

        private void OnCurrentRecipeChanged(Recipe? recipe)
        {
            if (_disposed)
            {
                return;
            }

            // 配方切换时清空去重跟踪列表，避免旧配方的跟踪数据影响新配方计数
            _productTracker.Clear();
            // 角度跨帧锁定同样按配方重置，避免沿用旧配方的锁定角度
            AngleTracker.Instance.Clear();
            _reloadPredictorTask = ReloadPredictorAsync(recipe);
        }

        /// <summary>
        /// 重置产品去重跟踪列表（清零已跟踪产品，用于换班/换产品场景）。
        /// 注意：此方法仅清零 ProductTracker 的跟踪列表，不重置读码数量和识别数量计数（由 MainViewModel 负责）。
        /// </summary>
        public void ResetProductTracker()
        {
            if (_disposed)
            {
                return;
            }

            try
            {
                _productTracker.Clear();
                AngleTracker.Instance.Clear();
                LogService.Instance.Info("[HomeViewModel] 产品去重跟踪列表已清零");
            }
            catch (Exception ex)
            {
                LogService.Instance.Error($"清零 ProductTracker 失败: {ex}");
            }
        }

        private async Task ReloadPredictorAsync(Recipe? recipe)
        {
            // M219: fire-and-forget 调用需在入口检查 _disposed，避免 Dispose 后仍触发重建
            if (_disposed) return;
            // M28: fire-and-forget 调用需顶层 try-catch，防止未观察异常导致应用崩溃
            try
            {
                var (newPredictorPool, deviceName) = await _modelLoader.CreatePredictorPoolAsync(recipe).ConfigureAwait(false);
                if (!string.IsNullOrEmpty(deviceName))
                {
                    _inferenceDevice = deviceName;
                }
                var newAnglePredictorPool = await _modelLoader.CreateAnglePredictorPoolAsync(recipe).ConfigureAwait(false);

                if (_disposed)
                {
                    newPredictorPool?.Dispose();
                    newAnglePredictorPool?.Dispose();
                    return;
                }
                var oldPredictorPool = _predictorPool;
                var oldAnglePredictorPool = _anglePredictorPool;
                _predictorPool = newPredictorPool;
                _anglePredictorPool = newAnglePredictorPool;
                // REVIEW(2026-09-04): 重载按 CUDA 优先重建，重置自愈状态机，避免旧状态污染新池。
                ResetSegHealState(ModelLoaderService.ParseBackend(deviceName));
                // P1-1: 预测器池成功重建后重置日志标志，后续若再次变为 null 可重新记录一次日志
                if (newPredictorPool is not null)
                {
                    _hasLoggedPredictorNotReady = false;
                }
                await WaitForInferenceDrainAsync().ConfigureAwait(false);
                oldPredictorPool?.Dispose();
                oldAnglePredictorPool?.Dispose();
            }
            catch (Exception ex)
            {
                LogService.Instance.Error($"ReloadPredictorAsync 失败: {ex}");
                // L: 模型重载失败属于关键错误，提示用户检查配置后重试
                NotificationService.Error("AI 模型重载失败，请检查配置后重试。");
            }
        }

        // ===================== 分割推理后端运行期自愈 =====================

        /// <summary>
        /// 是否为「后端会话已失效」类错误。只有这类错误值得触发降级重建；
        /// 其余（如取消、图像解码等）不属于推理后端问题，交给外层原有逻辑处理。
        /// </summary>
        private static bool IsRuntimeBackendFailure(Exception ex) =>
            ex is Microsoft.ML.OnnxRuntime.OnnxRuntimeException;

        /// <summary>
        /// 分割推理失败后调用（同步入口，返回前不阻塞调用帧）：
        /// 连续失败达到阈值且通过冷却/防重入检查后，异步触发逐级降级重建。
        /// </summary>
        /// <param name="failingPool">正在失效的池（调用帧持有其租约）。重建只在该池仍是当前池时换入，
        /// 避免覆盖重载/另一自愈任务期间产生的更新池。</param>
        private void ScheduleSegBackendHeal(YoloPredictorPool failingPool)
        {
            if (_disposed) return;

            if (Interlocked.Increment(ref _segConsecutiveFails) < SegHealFailThreshold)
            {
                return; // 容忍偶发单次抖动，两次以上才认为会话已死
            }

            // 冷却：坏环境下避免反复重建的抖动（一旦已低于 CUDA 且仍失败，也受同样冷却约束）
            var now = Environment.TickCount64;
            var last = Interlocked.Read(ref _lastSegHealAttemptTicks);
            if (now - last < SegHealCooldownMs)
            {
                return;
            }

            if (Interlocked.Exchange(ref _segHealRunning, 1) == 1)
            {
                return; // 已有自愈任务在跑
            }

            _ = HealSegBackendAsync(failingPool);
        }

        /// <summary>
        /// 从当前后端降一级开始，逐级尝试重建主分割池；新池通过冒烟推理后才原子换入。
        /// 旧池换出后立即 Dispose：池内已借出的预测器在归还时若发现池已 Dispose 会自动释放自身，
        /// 无需等待在途租约，故不需要 WaitForInferenceDrainAsync。
        /// </summary>
        /// <param name="failingPool">触发自愈的失效池；若期间池已被替换（如配方重载），放弃本次自愈。</param>
        private async Task HealSegBackendAsync(YoloPredictorPool failingPool)
        {
            try
            {
                if (_disposed) return;
                if (!ReferenceEquals(_predictorPool, failingPool))
                {
                    LogService.Instance.Warning("[AI 自愈] 触发后预测器池已被替换，放弃本次自愈");
                    return;
                }
                Interlocked.Exchange(ref _lastSegHealAttemptTicks, Environment.TickCount64);

                var recipe = CurrentRecipe; // 快照，避免处理过程中配方切换
                var previousBackend = _segBackend;
                var startTier = Math.Min((int)previousBackend + 1, (int)InferenceBackend.Cpu);

                for (var tier = startTier; tier <= (int)InferenceBackend.Cpu; tier++)
                {
                    if (_disposed) return;
                    if (!ReferenceEquals(_predictorPool, failingPool))
                    {
                        LogService.Instance.Warning("[AI 自愈] 建池期间预测器池已被替换，放弃本次自愈");
                        return;
                    }

                    var requested = (InferenceBackend)tier;
                    LogService.Instance.Warning($"[AI 自愈] 分割后端 {ModelLoaderService.BackendName(previousBackend)} 连续失败，尝试重建为 {ModelLoaderService.BackendName(requested)}");

                    PredictorPoolResult result;
                    try
                    {
                        result = await _modelLoader.CreatePredictorPoolResultAsync(recipe, requested).ConfigureAwait(false);
                    }
                    catch (Exception ex)
                    {
                        // 加载器内部已逐级捕获，此处仅兜底
                        LogService.Instance.Warning($"[AI 自愈] 建池调用异常: {ex.Message}");
                        continue;
                    }

                    if (result.Pool is null)
                    {
                        // 连纯 CPU 兜底都失败（通常是模型文件问题），继续向上已无意义
                        LogService.Instance.Error("[AI 自愈] 所有后端建池失败（含 CPU），AI 检测无法恢复，请检查模型配置");
                        break;
                    }

                    // 冒烟推理：换入前验证新池真的能跑，避免换入又一个"建得起、跑不动"的池
                    if (!await SmokeTestSegPoolAsync(result.Pool).ConfigureAwait(false))
                    {
                        LogService.Instance.Warning($"[AI 自愈] {result.DeviceName} 冒烟推理失败，尝试下一级");
                        result.Pool.Dispose();
                        continue;
                    }

                    // 换入前的最终守卫：建池/冒烟期间池若已被替换（配方重载等），丢弃新池
                    if (!ReferenceEquals(_predictorPool, failingPool))
                    {
                        LogService.Instance.Warning("[AI 自愈] 冒烟后预测器池已被替换，丢弃新池并放弃本次自愈");
                        result.Pool.Dispose();
                        return;
                    }

                    // 原子换入（_predictorPool 为 volatile 引用）
                    var oldPool = _predictorPool;
                    _predictorPool = result.Pool;
                    _segBackend = result.Backend;
                    Interlocked.Exchange(ref _segConsecutiveFails, 0);
                    if (!string.IsNullOrEmpty(result.DeviceName))
                    {
                        _inferenceDevice = result.DeviceName;
                    }
                    _hasLoggedPredictorNotReady = false;
                    oldPool?.Dispose();

                    LogService.Instance.Error(
                        $"[AI 自愈] 分割推理后端已切换: {ModelLoaderService.BackendName(previousBackend)} → {result.DeviceName}");
                    NotificationService.Warning($"AI 检测后端已降级为 {result.DeviceName}，请留意检测性能。");
                    return;
                }
            }
            catch (Exception ex)
            {
                LogService.Instance.Error($"[AI 自愈] 后端降级重建异常: {ex}");
            }
            finally
            {
                Interlocked.Exchange(ref _segHealRunning, 0);
            }
        }

        /// <summary>
        /// 用一张固定小图跑一次完整分割推理，验证新池在运行期可用
        /// （能覆盖 CUDA 卷积执行这类建池期探测不到的故障）。
        /// </summary>
        private static async Task<bool> SmokeTestSegPoolAsync(YoloPredictorPool pool)
        {
            try
            {
                using var smokeImage = new Image<Rgb24>(64, 48);
                using var lease = pool.Acquire(timeoutMilliseconds: 5000);
                using var result = await lease.Predictor.SegmentAsync(smokeImage).ConfigureAwait(false);
                return true;
            }
            catch (Exception ex)
            {
                LogService.Instance.Warning($"[AI 自愈] 冒烟推理失败: {ex.Message}");
                return false;
            }
        }

        /// <summary>重载/换池后重置自愈状态机，避免旧状态污染新池。</summary>
        private void ResetSegHealState(InferenceBackend backend)
        {
            _segBackend = backend;
            Interlocked.Exchange(ref _segConsecutiveFails, 0);
            Interlocked.Exchange(ref _segHealRunning, 0);
        }

        /// <summary>
        /// 当前配方是否启用角度检测流程。
        /// </summary>
        private bool IsAngleDetectionEnabled =>
            CurrentRecipe?.YoloTool?.IsAngleDetectionEnabled ?? false;

        private readonly object _imageLock = new();
        private ImageSource? _imageForShow;
        /// <summary>
        /// 复用的 WriteableBitmap，避免每帧 new 导致 LOH 碎片。
        /// </summary>
        private WriteableBitmap? _reusableShowBitmap;
        public ImageSource? ImageForShow
        {
            get { return _imageForShow; }
            set
            {
                // REVIEW-FIX (跨线程回归修复): 未冻结的 WriteableBitmap 由 UI 线程创建，
                // 赋给绑定（DependencyProperty）也必须在 UI 线程，否则 WPF 抛
                // "必须与 DependencyObject 相同的 Thread 上创建 DependencySource"。
                // 推理线程调用 setter 时转发到 UI 线程执行（含 OnPropertyChanged 与绑定更新）。
                var dispatcher = System.Windows.Application.Current?.Dispatcher;
                if (dispatcher is not null && !dispatcher.CheckAccess())
                {
                    // VSTHRD110: discard 观察 InvokeAsync 结果（异步转发，无需等待）
                    _ = dispatcher.InvokeAsync(() => ImageForShow = value);
                    return;
                }

                // H87a: 加锁保护 setter，避免并发推理线程导致 double Dispose 或 GDI+ 句柄泄漏
                lock (_imageLock)
                {
                    // WriteableBitmap 原生支持跨线程，无需 Freeze（复用场景下 Freeze 会导致下次 WritePixels 失败）
                    if (value is Freezable freezable && !freezable.IsFrozen && value is not WriteableBitmap)
                    {
                        freezable.Freeze();
                    }

                    // 释放旧图像源（复用的 WriteableBitmap 不从 setter 释放）
                    if (_imageForShow is IDisposable disposable && !ReferenceEquals(_imageForShow, value))
                    {
                        disposable.Dispose();
                    }

                    _imageForShow = value;
                }
                OnPropertyChanged();
            }
        }

    // 推理结果详细信息列表
    private ObservableCollection<InferenceResultItem> _inferenceResults = new();
    /// <summary>
    /// 推理结果详细信息列表，供 UI DataGrid 绑定展示每个目标的类别、置信度、坐标与角度。
    /// </summary>
    public ObservableCollection<InferenceResultItem> InferenceResults
    {
        get => _inferenceResults;
        private set => SetProperty(ref _inferenceResults, value);
    }

    /// <summary>当前结果总数，供 UI 标题栏显示。</summary>
    public int ResultCount => InferenceResults.Count;

    // P0-2: 扫描枪连接状态指示
    private ScannerConnectionState _scannerStatus = ScannerConnectionState.Disconnected;

    /// <summary>
    /// 扫描枪连接状态（Disconnected/Connecting/Connected）。
    /// </summary>
    public ScannerConnectionState ScannerStatus
    {
        get => _scannerStatus;
        set
        {
            if (SetProperty(ref _scannerStatus, value))
            {
                OnPropertyChanged(nameof(ScannerStatusText));
                OnPropertyChanged(nameof(ScannerStatusColor));
            }
        }
    }

    /// <summary>
    /// 扫描枪连接状态文本（"已连接"/"未连接"/"重连中"），供 UI 绑定。
    /// </summary>
    public string ScannerStatusText => ScannerStatus switch
    {
        ScannerConnectionState.Connected => "已连接",
        ScannerConnectionState.Connecting => "重连中",
        _ => "未连接"
    };

    /// <summary>
    /// 扫描枪连接状态颜色（绿色/红色/橙色），使用十六进制字符串便于 XAML 绑定。
    /// </summary>
    public string ScannerStatusColor => ScannerStatus switch
    {
        ScannerConnectionState.Connected => "#4CAF50",   // 绿色
        ScannerConnectionState.Connecting => "#FF9800", // 橙色
        _ => "#F44336"                                   // 红色
    };

    /// <summary>
    /// 累计丢帧/跳帧数，供状态栏显示。
    /// </summary>
    public int DropFrameCount
    {
        get => _dropFrameCount;
        private set
        {
            if (SetProperty(ref _dropFrameCount, value))
            {
                OnPropertyChanged(nameof(DropFrameDisplay));
            }
        }
    }

    /// <summary>状态栏显示用的丢帧文本。</summary>
    public string DropFrameDisplay => $"丢帧: {_dropFrameCount}";

    /// <summary>
    /// 已处理帧计数（用于 FPS 计算），每处理完一帧递增。
    /// 与检测结果无关，即使没有检测到目标也会计数。
    /// </summary>
    public long ProcessedFrameCount => Interlocked.Read(ref _processedFrameCount);

    /// <summary>
    /// 线程安全地累计丢帧数并通知 UI（WPF 绑定引擎支持跨线程 PropertyChanged）。
    /// </summary>
    private void IncrementDropFrameCount(int count = 1)
    {
        if (count <= 0) return;
        Interlocked.Add(ref _dropFrameCount, count);
        OnPropertyChanged(nameof(DropFrameCount));
        OnPropertyChanged(nameof(DropFrameDisplay));
        // 通过 messenger 推送最新帧指标给 MainViewModel，替代原 Func 反向回调
        SendFrameMetrics();
    }

    /// <summary>
    /// 通过 WeakReferenceMessenger 广播当前帧计数指标（ProcessedFrameCount / DropFrameCount），
    /// 供 MainViewModel 订阅以更新 FPS / 丢帧显示。
    /// 替代原 MainViewModel.GetProcessedFrameCount / GetDropFrameCount 的反向回调。
    /// </summary>
    private void SendFrameMetrics()
    {
        try
        {
            WeakReferenceMessenger.Default.Send(new FrameMetricsMessage(
                Interlocked.Read(ref _processedFrameCount),
                _dropFrameCount));
        }
        catch (Exception ex)
        {
            LogService.Instance.Error($"SendFrameMetrics 失败: {ex}");
        }
    }

    /// <summary>
    /// 丢帧通知节流：首次立即提示，之后每 DropFrameNotifyInterval 次提示一次，避免每帧弹窗刷屏。
    /// </summary>
    private void NotifyFrameDrop(string message, bool isError = false)
    {
        int n = Interlocked.Increment(ref _dropNotifyCounter);
        if (n == 1 || n % DropFrameNotifyInterval == 0)
        {
            if (isError) NotificationService.Error(message);
            else NotificationService.Warning(message);
        }
    }

    /// <summary>
    /// 线程安全地更新扫描枪连接状态。
    /// 后台线程通过 Dispatcher 切换到 UI 线程，确保 PropertyChanged 事件在 UI 线程触发。
    /// </summary>
    private void UpdateScannerStatus(ScannerConnectionState newState)
    {
        // 快速路径：状态未变化时直接返回，避免不必要的 Dispatcher 调度
        if (_scannerStatus == newState)
        {
            return;
        }

        try
        {
            var dispatcher = System.Windows.Application.Current?.Dispatcher;
            if (dispatcher is not null)
            {
                _ = dispatcher.BeginInvoke(new Action(() => ScannerStatus = newState));
            }
            else
            {
                ScannerStatus = newState;
            }
        }
        catch
        {
            // Dispatcher 不可用时直接设置（INPC 在 WPF 中支持跨线程绑定更新）
            ScannerStatus = newState;
        }
    }

    private YoloTool EdgeDetection => CurrentRecipe?.YoloTool?.EdgeDetection ?? throw new InvalidOperationException("EdgeDetection 未配置");
        // REVIEW-FIX: CoordinateTool getter 改为可空返回。原实现 `?? throw` 导致 L1372 的
        // "未标定"判空分支永远走不到——配方配了 YOLO 但未标定时 getter 直接抛异常，TryEnsureProcessingReady
        // 未捕获，异常上抛后主循环停摆。现在由调用方判空并返回可读错误信息。
        private CoordinateTool? CoordinateTool => CurrentRecipe?.CoordinateTool;
        #region Loop
        /// <summary>
        /// 主循环：完成一系列校验后启动图像读取与处理的后台任务。
        /// </summary>
        /// <remarks>
        /// 该方法不在 UI 线程阻塞，它会启动两个后台任务：一个用于从扫描设备读取图像，另一个用于处理队列中的图像。
        /// 若需要提高吞吐量，可调整队列长度（MaxImagesinQueue）与处理并发策略；若遇到并发问题，可通过配置 SuppressParallelInference。
        /// </remarks>
        private async Task Loop()
        {
            // L362c: 配方可能在 HomeViewModel 构造后才初始化（App.OnStartup 中 base.OnStartup 先于 InitializeRecipes 执行），
            // 因此 TryEnsureProcessingReady 失败时不能直接 return，需等待配方就绪后继续
            while (!_cts.Token.IsCancellationRequested)
            {
                if (TryEnsureProcessingReady(out var errorMessage))
                {
                    _lastProcessingReadyError = null;
                    break;
                }
                // 只在错误消息变化时记录日志，避免重复刷屏
                if (_lastProcessingReadyError != errorMessage)
                {
                    LogService.Instance.Warning(errorMessage);
                    _lastProcessingReadyError = errorMessage;
                }
                try
                {
                    await Task.Delay(ProcessingReadyRetryMs, _cts.Token).ConfigureAwait(false);
                }
                catch (OperationCanceledException)
                {
                    return;
                }
            }
            if (_cts.Token.IsCancellationRequested)
            {
                return;
            }
            // H40: 扫描枪就绪检查 —— 启动阶段静默等待，初始化完成后才报 Warning
            HikScannerType? scanner = null;
            while (!_cts.Token.IsCancellationRequested)
            {
                scanner = Devices.Scanners.HikScaner;
                if (scanner is not null && scanner.IsConnected)
                {
                    break;
                }

                // 仅在已检测到扫码枪后仍断连时才报 Warning（启动阶段静默等待）
                if (Devices.Scanners.HasScanners)
                {
                    LogService.Instance.Warning("扫描枪未连接，10 秒后重试...");
                    // P0-2: 已检测到扫码枪但未连接，标记为"重连中"（橙色）
                    UpdateScannerStatus(ScannerConnectionState.Connecting);
                }
                else
                {
                    // P0-2: 扫码枪尚未检测到，标记为"未连接"（红色）
                    UpdateScannerStatus(ScannerConnectionState.Disconnected);
                }

                await Task.Delay(10_000, _cts.Token).ConfigureAwait(false);
            }

            scanner ??= Devices.Scanners.HikScaner;
            if (scanner is null)
            {
                LogService.Instance.Error("扫描枪未初始化，无法启动图像读取循环。");
                // P0-2: 扫码枪未初始化
                UpdateScannerStatus(ScannerConnectionState.Disconnected);
                return;
            }

            // P0-2: 扫描枪已就绪，更新连接状态为已连接（绿色）
            UpdateScannerStatus(ScannerConnectionState.Connected);

            // 记录处理循环启动前的内存基线
            MemoryDiagnostics.LogSnapshot("LoopStart");

            // 启动后台图像读取循环，读取出的 FrameResult 被入队供处理任务消费。
            _readImageLoopTask = Task.Run(async () =>
            {
                while (!_cts.Token.IsCancellationRequested)
                {
                    // M700: 配方页打开时跳过整个取帧循环，杜绝 ReadImageOneLoop 进入 AcquireAsync 的竞态
                    if (s_isPaused)
                    {
                        await Task.Delay(200, _cts.Token).ConfigureAwait(false);
                        continue;
                    }

                    try
                    {
                        await ReadImageOneLoop(scanner).ConfigureAwait(false);
                    }
                    catch (OperationCanceledException)
                    {
                        break;
                    }
                    catch (Exception ex)
                    {
                        LogService.Instance.Error($"Loop 主循环捕获未处理异常: {ex}");
                        TemporaryLog.Log?.Error(ex.ToString());
                        // L22: 异常后短暂退避，传入 token 以便取消时立即退出
                        // M153: 补齐 ConfigureAwait(false)
                        try { await Task.Delay(100, _cts.Token).ConfigureAwait(false); }
                        catch (OperationCanceledException) { break; }
                    }
                }
                LogService.Instance.Info("Loop 任务结束");
                MemoryDiagnostics.LogSnapshot("ReadLoopEnd");
            }, _cts.Token);

            // 启动后台图像处理循环
            _processImageLoopTask = Task.Run(async () =>
            {
                try
                {
                    await foreach (var scanerResult in _imageChannel.Reader.ReadAllAsync(_cts.Token).ConfigureAwait(false))
                    {
                        try
                        {
                            StartProcessImageAsync(scanerResult);
                        }
                        catch (Exception ex)
                        {
                            LogService.Instance.Error($"ProcessImageAsync 捕获未处理异常: {ex}");
                        }
                    }
                }
                catch (OperationCanceledException) { }
                catch (Exception ex)
                {
                    LogService.Instance.Error($"图像通道读取异常: {ex}");
                }
                LogService.Instance.Info("ProcessImageAsync 任务结束");
                MemoryDiagnostics.LogSnapshot("ProcessLoopEnd");
            }, _cts.Token);
        }
        #endregion

        /// <summary>
        /// REVIEW(2026-08-05): 解码压缩帧为 Image&lt;Rgb24&gt;，按相机帧格式三路适配：
        /// - JPEG：Cv2.ImDecode（libjpeg-turbo）+ BGR→RGB + 零拷贝包装（替代 ImageSharp 纯 CPU 解码）；
        /// - RGB8 Packed：原始像素，无解码，拷贝一次后包装（ArrayPool 租约不能直接被 Image 引用）；
        /// - Mono8：灰度，展开为 RGB24。
        /// 主管线类型不变，下游零改动。
        /// </summary>
        /// <param name="result">当前帧（含图像数据与格式标志）。</param>
        /// <param name="isResize">是否缩放解码（推理图缩小）。</param>
        /// <param name="targetWidth">缩放目标宽（原图宽 / ResizeScale）。</param>
        /// <param name="targetHeight">缩放目标高（原图高 / ResizeScaleY）。</param>
        /// <param name="failed">解码是否失败。</param>
        private static Image<Rgb24>? DecodeImageData(FrameResult result, bool isResize, int targetWidth, int targetHeight, out bool failed)
        {
            failed = false;
            var data = result.ImageData;
            if (data is null || data.Length == 0)
            {
                failed = true;
                return null;
            }

            // REVIEW(2026-08-05): 首帧记录相机实际输出的像素格式/分辨率（诊断 LoadImage 耗时构成用）
            if (!_frameFormatLogged)
            {
                _frameFormatLogged = true;
                LogService.Instance.Info(
                    $"相机帧格式: {result.PixelFormatName}, 分辨率: {result.Width}x{result.Height}, " +
                    $"数据大小: {data.Length / 1024.0:F1} KB");
            }

            try
            {
                Image<Rgb24> image;
                if (result.IsRgb8Packed)
                {
                    // 原始 RGB8：无需解码；ArrayPool 租约在调用后立即归还（内存会被复用），必须拷贝一份再包装
                    var copy = new byte[data.Length];
                    Array.Copy(data, copy, data.Length);
                    image = Image.WrapMemory<Rgb24>(copy, (int)result.Width, (int)result.Height);
                }
                else if (result.IsMono8)
                {
                    // 灰度：展开为 RGB24（每像素 1 → 3 字节）
                    image = new Image<Rgb24>((int)result.Width, (int)result.Height);
                    image.ProcessPixelRows(accessor =>
                    {
                        var w = accessor.Width;
                        for (int y = 0; y < accessor.Height; y++)
                        {
                            var row = accessor.GetRowSpan(y);
                            int srcIdx = y * w;
                            for (int x = 0; x < row.Length; x++)
                            {
                                byte v = data[srcIdx + x];
                                row[x] = new Rgb24(v, v, v);
                            }
                        }
                    });
                }
                else
                {
                    // JPEG：libjpeg-turbo 原生解码
                    using var decodedMat = Cv2.ImDecode(data, ImreadModes.Color);
                    if (decodedMat.Empty() || decodedMat.Width <= 0 || decodedMat.Height <= 0)
                    {
                        failed = true;
                        return null;
                    }

                    Mat working = decodedMat;
                    using var resizedMat = isResize
                        ? decodedMat.Resize(new OpenCvSharp.Size(Math.Max(1, targetWidth), Math.Max(1, targetHeight)))
                        : null;
                    if (resizedMat is not null)
                    {
                        working = resizedMat;
                    }

                    // BGR → RGB 通道交换后零拷贝包装（字节数组由本方法新建，生命周期由调用方 using 保证）
                    using var rgbMat = new Mat();
                    Cv2.CvtColor(working, rgbMat, ColorConversionCodes.BGR2RGB);
                    var bytes = new byte[rgbMat.Total() * rgbMat.ElemSize()];
                    System.Runtime.InteropServices.Marshal.Copy(rgbMat.Data, bytes, 0, bytes.Length);
                    image = Image.WrapMemory<Rgb24>(bytes, rgbMat.Width, rgbMat.Height);
                }

                // 统一缩放（仅 RGB8/Mono8 路径需要；JPEG 路径已在解码时缩）
                if (isResize && (image.Width != targetWidth || image.Height != targetHeight))
                {
                    image.Mutate(x => x.Resize(Math.Max(1, targetWidth), Math.Max(1, targetHeight)));
                }
                return image;
            }
            catch (Exception ex)
            {
                // 解码失败（坏帧/格式不支持）：记录并跳过该帧，不中断主循环
                LogService.Instance.Error($"图像解码失败: {ex.Message}");
                failed = true;
                return null;
            }
        }

        private void StartProcessImageAsync(FrameResult scanerResult)
        {
            var currentActive = Volatile.Read(ref _activeInferenceCount);
            // 仅在并发数发生显著变化时记录日志，避免每帧刷屏
            if (currentActive % InferenceLogSamplingInterval == 0 || currentActive >= InferenceLogActiveThreshold)
            {
                MemoryDiagnostics.LogActiveTasks("Inference", currentActive);
            }

            _ = Task.Run(async () =>
            {
                // 内存优化：用 SemaphoreSlim 限制并发推理，避免大量帧同时持有大对象触发 OOM
                // M338: 将 WaitAsync 纳入 try-catch，避免 Dispose 取消 CTS 时 OCE 逃逸成为未观察异常
                bool acquired;
                try
                {
                    acquired = await _inferenceSemaphore.WaitAsync(TimeSpan.FromMilliseconds(200), _cts.Token).ConfigureAwait(false);
                }
                catch (OperationCanceledException)
                {
                    // P0-FIX: 信号量等待被取消时 scanerResult 尚未被 ProcessImageAsync 接管，显式释放避免泄漏
                    scanerResult.Dispose();
                    return;
                }
                catch (ObjectDisposedException)
                {
                    // P0-FIX: 信号量已释放，显式释放 scanerResult 避免泄漏
                    scanerResult.Dispose();
                    return;
                }

                if (!acquired)
                {
                    // L360a: 缓存 Volatile.Read 结果，复用 currentActive，避免连续两次调用
                    LogService.Instance.Warning(
                        $"并发推理槽位已满(Active={currentActive})，跳过帧 {scanerResult.FrameNumber} 以释放内存压力");
                    // L: 计入丢帧统计并通过节流策略提示用户（性能警告）
                    IncrementDropFrameCount();
                    NotifyFrameDrop($"并发推理槽位已满，已跳过帧 {scanerResult.FrameNumber}");
                    // P0-FIX: 未能进入处理流程时显式释放 scanerResult，避免 ArrayPool 缓冲区泄漏
                    scanerResult.Dispose();
                    return;
                }

                try
                {
                    await ProcessImageAsync(scanerResult).ConfigureAwait(false);
                }
                catch (OperationCanceledException)
                {
                    // M354: 应用关闭时 _cts 取消导致 OCE，属正常退出，不记录为错误
                    // P0-FIX: 取消时 ProcessImageAsync 可能未执行到 using 块，显式释放避免泄漏
                    scanerResult.Dispose();
                }
                catch (Exception ex)
                {
                    LogService.Instance.Error($"ProcessImageAsync 异步任务异常: {ex}");
                    // P0-FIX: 异常逃逸时同样确保释放，Dispose 幂等所以重复调用安全
                    scanerResult.Dispose();
                }
                finally
                {
                    // H21: Dispose 后信号量可能已释放
                    try { _inferenceSemaphore.Release(); }
                    catch (ObjectDisposedException) { }
                }
            }, _cts.Token);
        }

        private async Task WaitForInferenceDrainAsync()
        {
            TaskCompletionSource<bool> waitTcs;

            // H78b: 用 lock 保护 count 与 TCS 读取的原子性，避免 TOCTOU 竞态
            // （count>0 但读到已 completed 的旧 TCS 导致 drain 提前返回）
            lock (_inferenceLock)
            {
                if (Volatile.Read(ref _activeInferenceCount) == 0)
                    return;
                waitTcs = _inferenceDrainedTcs;
            }

            await waitTcs.Task.ConfigureAwait(false);
        }

        private void ResetInferenceDrainSignalIfNeeded(int previousCount)
        {
            if (previousCount != 0)
            {
                return;
            }

            // H78b: 在 lock 内替换 TCS，确保 WaitForInferenceDrainAsync 不会读到旧值
            lock (_inferenceLock)
            {
                _inferenceDrainedTcs = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
            }
        }

        private void SignalInferenceDrainedIfNeeded(int currentCount)
        {
            if (currentCount != 0)
            {
                return;
            }

            // H78b: 在 lock 内读取 TCS 再 TrySetResult，确保与 WaitForInferenceDrainAsync 一致
            TaskCompletionSource<bool>? toSignal;
            lock (_inferenceLock)
            {
                toSignal = _inferenceDrainedTcs;
            }

            toSignal.TrySetResult(true);
        }

        private static TaskCompletionSource<bool> CreateCompletedTcs()
        {
            var tcs = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
            tcs.TrySetResult(true);
            return tcs;
        }

        /// <summary>
        /// 处理单帧图像的异步流程：执行模型推理、匹配条码、生成记录并绘制结果。
        /// </summary>
        /// <param name="scanerResult">来自扫描设备的一帧图像及其附带的条码检测结果。</param>
        /// <remarks>
        /// 处理流程大致为：加载/缩放图像 -> 从池中借用预测器执行 SegmentAsync -> 将检测框与条码位置匹配 -> 保存与绘制。
        /// 注意：此方法会检查 _predictorPool 是否为 null，为 null 时记入丢帧并跳过当前帧。
        /// </remarks>
        private async Task ProcessImageAsync(FrameResult scanerResult)
        {
            // H78a: 先读取 EdgeDetection 属性，若抛 InvalidOperationException 则不进行自增，避免 _activeInferenceCount 泄漏
            // M76: 入口处对 CurrentRecipe 的 EdgeDetection 做局部快照，后续全部使用局部变量，避免处理过程中配方切换导致属性返回不同实例
            var edge = EdgeDetection;
            var previousCount = Interlocked.Increment(ref _activeInferenceCount) - 1;
            ResetInferenceDrainSignalIfNeeded(previousCount);
            var imageDataSize = scanerResult.ImageData?.Length ?? 0;
            try
            {
                // P0-FIX: 用 using 模式包装 scanerResult，确保所有异常路径自动归还 ArrayPool 租用缓冲区。
                // 内部仍保留 ReleaseImageData 的提前归还调用以减少内存占用；Dispose 幂等，重复调用安全。
                using (scanerResult)
                {
                    // L362b: ImageData 可能为 null（Image 为 null 时），提前跳过避免 ImDecode/Image.Load 抛异常
                    if (scanerResult.ImageData is null)
                    {
                        LogService.Instance.Warning($"图像数据为空，跳过处理: Frame={scanerResult.FrameNumber}");
                        // L: 计入丢帧统计并通过节流策略提示用户（性能警告）
                        IncrementDropFrameCount();
                        NotifyFrameDrop($"图像数据为空，已跳过处理: 帧 {scanerResult.FrameNumber}");
                        return;
                    }
                    using var timings = new TimingLogger($"{scanerResult.FrameNumber}", _inferenceDevice);
                    // REVIEW(2026-08-05): 图像解码改用 OpenCvSharp（Cv2.ImDecode，底层 libjpeg-turbo），
                    // 替代 ImageSharp 纯 CPU JPEG 解码（实测 LoadImage 50ms → 原生解码约 15-25ms）。
                    // 同时适配相机三种帧格式：JPEG 原生解码 / RGB8 零解码拷贝 / Mono8 灰度展开。
                    // 主管线仍保持 Image<Rgb24>，下游（推理/绘制/显示/保存）零改动。
                    using var sourceImg = DecodeImageData(
                        scanerResult,
                        edge.IsResize,
                        (int)scanerResult.Width / edge.ResizeScale,
                        (int)scanerResult.Height / edge.ResizeScaleY,
                        out var decodedFailed);
                    if (decodedFailed || sourceImg is null)
                    {
                        IncrementDropFrameCount();
                        NotifyFrameDrop($"图像解码失败，已跳过处理: 帧 {scanerResult.FrameNumber}");
                        return;
                    }

                    // M700: 解码后立即归还 ArrayPool 租用的 byte[] 缓冲区，避免 LOH 持续膨胀
                    scanerResult.ReleaseImageData();

                    var sourceImgSize = MemoryDiagnostics.EstimateImageSize(sourceImg.Width, sourceImg.Height);
                    if (scanerResult.IsFrameLoss || scanerResult.NeedDrop)
                    {
                        _toVgtService.SendTo([], scanerResult, Settings.Instance.MessageReceiver);
                        _reusableShowBitmap = Tools.UpdateShow(sourceImg, _reusableShowBitmap);
                        ImageForShow = _reusableShowBitmap;
                        LogService.Instance.Warning($"跳过处理: Num={scanerResult.FrameNumber},Loss={scanerResult.IsFrameLoss}, Drop={scanerResult.NeedDrop}");
                        return;
                    }

                    string? folder = GetOrCreateTodayFolder();
                    timings.Split("LoadImage");
                    timings.Split("PrepareFolder");
                    // 发起异步推理请求（SegmentAsync）。
                    // 建议：若需要更低延迟与更高吞吐量，考虑调整 YoloConfiguration 中的 MaximumCandidateBoxes 与 SuppressParallelInference。
                    YoloResult<Segmentation>? edgeResults;
                    var predictorPool = _predictorPool;
                    if (predictorPool is null)
                    {
                        // P1-1: 预测器池为 null 时仅记录一次日志，避免每帧刷屏
                        // 配置错误（模型文件缺失等）已在 CreatePredictorPoolAsync 中通过 NotificationService.Error 提示用户，
                        // 这里无需重复弹窗或累计丢帧计数（属于配置问题而非运行时丢帧）
                        if (!_hasLoggedPredictorNotReady)
                        {
                            _hasLoggedPredictorNotReady = true;
                            LogService.Instance.Warning("预测器池尚未初始化，将跳过后续帧处理直至模型加载完成");
                        }
                        return;
                    }

                    // M22: lease.Acquire 已传入 _cts.Token，租出阶段即可响应取消；
                    // SegmentAsync 本身不接收 token（底层 Task.Run 同步封装），取消在 lease 层处理
                    using var lease = predictorPool.Acquire(cancellationToken: _cts.Token);
                    // 诊断：打印实际配置
                    if (!_configLogged)
                    {
                        _configLogged = true;
                        var cfg = lease.Predictor.Configuration;
                        LogService.Instance.Info($"CUDA推理配置确认: Confidence={cfg.Confidence}, IoU={cfg.IoU}, MaxDetection={cfg.MaximumDetections}");
                    }
                    // REVIEW(2026-09-04): 分割推理加后端自愈触发点。
                    // ORT CUDA 的运行期错误（如 CUDNN_FE failure 7）意味着该会话已不可用，
                    // 之后每帧都会失败。这里：成功后清连续失败计数；失败时若达到阈值则触发
                    // 逐级降级重建（fire-and-forget），随后照常向上抛、由外层统一记日志/丢帧。
                    try
                    {
                        edgeResults = await lease.Predictor.SegmentAsync(sourceImg).ConfigureAwait(false);
                        if (Volatile.Read(ref _segConsecutiveFails) != 0)
                        {
                            Interlocked.Exchange(ref _segConsecutiveFails, 0);
                        }
                    }
                    catch (Exception ex) when (IsRuntimeBackendFailure(ex))
                    {
                        ScheduleSegBackendHeal(predictorPool);
                        throw;
                    }
                    try
                    {
                        timings.Split("Inference");
                        // 角度推理需要 Mat 源图（与分割结果同一推理图坐标系）。灰度判向（无角度模型时对掩码
                        // 回退角度做 180° 去歧义，见 DetectionRecordService）同样需要推理图 BGR Mat。
                        // 任一启用才 ToMat，避免每帧无谓转换开销。
                        var angleEnabled = IsAngleDetectionEnabled && _anglePredictorPool is not null;
                        // 2026-09-08: 灰度判向配方级覆盖（null=跟随设置页全局开关，与面积过滤的
                        // "配方覆盖→全局回退"一致）。仅未启用角度检测时参与（见 DetectionRecordService else 分支）。
                        var brightnessOverride = CurrentRecipe?.YoloTool?.IsBrightnessDirectionEnabled;
                        bool grayDirectionEnabled = !angleEnabled
                            && (brightnessOverride ?? Settings.Instance.Algorithm.BrightnessDirectionEnabled);
                        using var angleMat = (angleEnabled || grayDirectionEnabled) ? sourceImg.ToMat() : null;
                        var buildResult = await _detectionRecord.BuildAndSaveAsync(
                            scanerResult,
                            edgeResults!,
                            _transformer,
                            edge.IsResize,
                            edge.ResizeScale,
                            edge.ResizeScaleY,
                            Settings.Instance.IsSaveDraw,
                            folder,
                            _toVgtService.Speed,
                            timings.TotalTime,
                            angleMat,
                            _anglePredictorPool,
                            CurrentRecipe?.YoloTool?.AngleDetection,
                            CurrentRecipe?.OffsetAngle ?? 0f,
                            angleEnabled,
                            _cts.Token,
                            // REVIEW(2026-08-05): 配方平移补偿（mm），ImageToPhysical 转换后加在最终世界坐标上
                            CurrentRecipe?.OffsetX ?? 0f,
                            CurrentRecipe?.OffsetY ?? 0f,
                            // 2026-09-07: 配方级面积过滤覆盖（null=回退全局设置）
                            edge.MinMaskAreaPixels,
                            edge.MaxMaskAreaPixels,
                            // 2026-09-08: 配方级灰度判向覆盖（null=回退全局 Algorithm.BrightnessDirectionEnabled）
                            brightnessOverride).ConfigureAwait(false);
                        timings.Split("BuildAndSave");

                        // H17: 发送 AI 识别计数到 MainWindow（经 ProductTracker 去重后仅计新增产品）
                        if (buildResult.ValidRecords.Count > 0)
                        {
                            int newCount = _productTracker.FilterNewAndCount(buildResult.ValidRecords);
                            if (newCount > 0)
                            {
                                WeakReferenceMessenger.Default.Send(new Messages.AddDetectionMessage(newCount));
                            }
                        }

                        _toVgtService.SendTo(buildResult.ValidRecords, scanerResult, Settings.Instance.MessageReceiver);
                        // 绘制前先保存未绘制的原图（IsSaveSource 与 IsSaveDraw 平级，可独立开启）。
                        // 必须在 DrawImage 就地绘制之前执行，否则保存的 source 图也会带上检测框。
                        if (Settings.Instance.IsSaveSource && (scanerResult.HasBarcodeResults || edgeResults?.Count > 0))
                        {
                            QueueSaveSourceImage(sourceImg, folder, scanerResult.FrameNumber, _cts.Token);
                            timings.Split("SaveSourceQueued");
                        }
                        // 直接在 sourceImg 上绘制，省掉 CloneAs 的 0.89MB LOH 分配
                        await DrawImage(sourceImg, scanerResult, edgeResults!, buildResult.AngleDrawInfos, buildResult.HeadFlips, buildResult.IndexedRecords, folder, timings, edge).ConfigureAwait(false);
                        // 在推理完成后更新结果列表（在 finally 释放 edgeResults 之前完成数据拷贝）
                        // 传入 IndexedRecords：与 edgeResults 严格同序，UI 按索引直接取值避免 Width×Height 错配
                        // 2026-09-07: 一并传入 MaskAreaByEdgeIndex，让主页表格能显示过滤后目标的掩码面积
                        UpdateInferenceResults(edgeResults, buildResult.IndexedRecords, buildResult.MaskAreaByEdgeIndex);
                        timings.Split("DrawImage");
                        timings.Split("TotalCost");

                        // 帧处理完成后的内存摘要
                        MemoryDiagnostics.LogFrameSummary(
                            scanerResult.FrameNumber,
                            imageDataSize,
                            sourceImgSize,
                            sourceImgSize,
                            _imageChannel.Reader.Count,
                            Volatile.Read(ref _activeInferenceCount),
                            timings.TotalTime);
                    }
                    finally
                    {
                        // L501: 显式释放 edgeResults 中每个 Segmentation 的 Mask 缓冲区（归还 ArrayPool），
                        // 避免每帧每框的 float[] 沉淀到 LOH/Gen2 导致内存持续上涨。
                        edgeResults?.Dispose();
                    }
                }
            }
            finally
            {
                // 每进入 ProcessImageAsync 即计为一帧处理（含跳过/异常帧），用于 FPS 计算
                Interlocked.Increment(ref _processedFrameCount);
                SignalInferenceDrainedIfNeeded(Interlocked.Decrement(ref _activeInferenceCount));
                // 推送最新帧指标给 MainViewModel，替代原 Func 反向回调
                SendFrameMetrics();
            }
        }
        #region DrawImage
        /// <summary>
        /// 在给定图像上绘制检测框与条码文本，并根据设置保存绘制结果与原图。
        /// </summary>
        /// <example>
        /// DrawImage 会在 image 上按类别名分色绘制矩形（参考 InferenceColorPalette），并用红色绘制条码末 6 位作为标签。
        /// </example>
        private async Task DrawImage(Image<Rgb24> drawImg, FrameResult scanerResult, YoloResult<Segmentation> edgeResults,
            IReadOnlyList<AngleDrawInfo?>? angleDrawInfos, IReadOnlyList<bool?>? headFlips,
            IReadOnlyList<DbModel?>? indexedRecords,
            string? folder, TimingLogger timings, YoloTool edge)
        {
            drawImg.Mutate(x =>
            {
                // 2026-09-09: 边界过滤"禁入线"辅助线——先于检测框绘制（目标框叠在线之上更清晰）。
                // EdgeMargin* 语义为原图像素（DetectionRecordService 中与还原后的 Bounds 比较），
                // drawImg 为推理图分辨率（IsResize 时原图已缩放），故按缩放比例折算到绘制坐标，
                // 与下方条码文本/角度绘制的缩放语义一致（IsResize=false 时坐标即原图，无需折算）。
                var marginAlg = MainAPP.Models.Settings.Instance.Algorithm;
                float mL = edge.IsResize ? (float)(marginAlg.EdgeMarginLeftPixels / edge.ResizeScale) : (float)marginAlg.EdgeMarginLeftPixels;
                float mT = edge.IsResize ? (float)(marginAlg.EdgeMarginTopPixels / edge.ResizeScaleY) : (float)marginAlg.EdgeMarginTopPixels;
                float mR = edge.IsResize ? (float)(marginAlg.EdgeMarginRightPixels / edge.ResizeScale) : (float)marginAlg.EdgeMarginRightPixels;
                float mB = edge.IsResize ? (float)(marginAlg.EdgeMarginBottomPixels / edge.ResizeScaleY) : (float)marginAlg.EdgeMarginBottomPixels;
                float drawW = drawImg.Width;
                float drawH = drawImg.Height;
                // 四条禁入线端点取内缩后的矩形四边（线与检测框同口径：框越线即被边界过滤拒收）
                x.DrawLine(EdgeMarginLineColor, EdgeMarginLineWidth,
                    new PointF(mL, mT), new PointF(drawW - mR, mT));
                x.DrawLine(EdgeMarginLineColor, EdgeMarginLineWidth,
                    new PointF(mL, drawH - mB), new PointF(drawW - mR, drawH - mB));
                x.DrawLine(EdgeMarginLineColor, EdgeMarginLineWidth,
                    new PointF(mL, mT), new PointF(mL, drawH - mB));
                x.DrawLine(EdgeMarginLineColor, EdgeMarginLineWidth,
                    new PointF(drawW - mR, mT), new PointF(drawW - mR, drawH - mB));

                int edgeIdx = 0;
                foreach (var edgeResult in edgeResults)
                {
                    // 按类别名分配稳定颜色，保证 DataGrid 中显示的颜色与图像上一致
                    var label = edgeResult.Name?.Name ?? string.Empty;
                    var colorHex = InferenceColorPalette.GetColorForLabel(label);
                    var drawColor = InferenceColorPalette.ToImageSharpColor(colorHex);
                    // 掩码最小外接旋转矩形：与 drawImg 同坐标系（drawImg 已按 IsResize 缩放为推理图，
                    // Segmentation 的 Bounds/掩码坐标与之天然一致，无需再缩放）。
                    // 提取一次供轮廓多边形与下方"掩码主轴参考线"复用，避免对掩码重复全扫描。
                    var maskRect = edgeResult.GetMaskMinAreaRect();
                    x.DrawPolygon(drawColor, DrawPolygonLineWidth, maskRect.Points);

                    // 目标是否有效（通过边界/面积过滤 → 入 UI 列表/落库/发送）。仅有效目标画方向参考线，
                    // 被过滤目标（半个产品/面积异常误检）不强调方向，避免误导现场人员。
                    bool isRecorded = indexedRecords is null
                        || (edgeIdx < indexedRecords.Count && indexedRecords[edgeIdx] is not null);

                    // 角度模型结果绘制：产品质心 → 特征质心连线 + 两点 + 角度文本（与 edgeResults 同序）
                    AngleDrawInfo? angleInfo = angleDrawInfos is not null && edgeIdx < angleDrawInfos.Count
                        ? angleDrawInfos[edgeIdx]
                        : null;
                    // 2026-09-11: 服务端下发的"头尾是否翻转 180°"（掩码角度路径专用，其余为 null）。
                    // 画面方向箭头据此复现实发朝向，不再自行按灰度统计判定（见下方箭头段注释）。
                    bool? headFlip = headFlips is not null && edgeIdx < headFlips.Count
                        ? headFlips[edgeIdx]
                        : null;
                    if (angleInfo is not null)
                    {
                        // 原图像素 → 推理图坐标（与条码文本绘制逻辑一致）
                        PointF ScaleFromOriginal(PointF p) => edge.IsResize
                            ? new PointF(p.X / edge.ResizeScale, p.Y / edge.ResizeScaleY)
                            : p;
                        var featureCentroid = ScaleFromOriginal(new PointF(angleInfo.FeatureCentroidX, angleInfo.FeatureCentroidY));
                        var productCentroid = ScaleFromOriginal(new PointF(angleInfo.ProductCentroidX, angleInfo.ProductCentroidY));
                        x.DrawLine(SixLabors.ImageSharp.Color.Red, 1f, productCentroid, featureCentroid);
                        // 质心点：用 2x2 小方块（DrawPolygon）代替圆，兼容 ImageSharp 3.x API
                        PointF[] Square(PointF c) => new[]
                        {
                            new PointF(c.X - 3, c.Y - 3), new PointF(c.X + 3, c.Y - 3),
                            new PointF(c.X + 3, c.Y + 3), new PointF(c.X - 3, c.Y + 3),
                        };
                        x.DrawPolygon(SixLabors.ImageSharp.Color.Blue, 2f, Square(productCentroid));
                        x.DrawPolygon(SixLabors.ImageSharp.Color.Red, 2f, Square(featureCentroid));
                        // 2026-09-09: 不绘制角度数值文本（现场无需画面核对角度值，仅保留方向连线/质心标记）
                    }
                    else if (isRecorded && maskRect.MaskArea > 0 && headFlip is not null)
                    {
                        // 2026-09-09: "发送位置 + 发送角度"向量箭头（掩码角度路径：未启用角度模型，或其方向退化回落）。
                        // 起点 = 掩码最小外接旋转矩形中心（正是落库/发送 X/Y 的图像对应点），
                        // 方向 = 矩形宽度轴（长轴，GetMaskMinAreaRect 已宽≥高归一化，与掩码回退发送角同源），
                        // 直接使用矩形自身中心/角度，无需世界坐标逆变换；带箭头头表达唯一朝向。
                        // 仅在有真实掩码像素（MaskArea>0）时绘制——掩码退化回退到外接框时方向无意义。
                        // 2026-09-11: 判据由 headFlip 是否为空决定（= 服务端确实走了掩码角度路径），
                        // 因此"角度模型启用但方向退化回落"的情形也会绘制箭头（此前恒不绘制，画面无任何朝向提示）。
                        // 2026-09-11: 头尾朝向**一律以服务端下发的 headFlip 为准**——见下方 arrowAngleDeg。
                        // 2026-09-09: 与箭头并存绘制"头尾分割线"（短轴方向、LimeGreen），二者垂直交于矩形中心：
                        // 分割线把产品划成头/尾两半（配合灰度判向核对头尾假设），箭头指示发送朝向。
                        var rectCenter = new PointF(maskRect.Center.X, maskRect.Center.Y);
                        // —— 头尾分割线（短轴）——
                        var (axisP1, axisP2) = ComputeMaskShortAxisSegment(rectCenter, maskRect.Width, maskRect.Height, maskRect.Angle);
                        x.DrawLine(MaskAxisLineColor, MaskAxisLineWidth, axisP1, axisP2);
                        // —— 发送角度向量箭头（长轴/主轴，橙色，区别于绿色分割线）——
                        // 长轴是无向轴，箭头方向不能只看几何角，否则指向会在 ±180° 间飘移；必须补上"头尾"。
                        // 2026-09-11 重构：头尾由**服务端下发的 headFlip** 决定，不再在 UI 侧二次判定。
                        //   背景：P5 起头尾判定优先级为"二维码位置 → 灰度统计"，而 UI 原先只看灰度统计
                        //   （且死区内不翻转），于是二维码把实发角翻 180° 时箭头仍指原方向 → 画面与实发角
                        //   相差 180°（所见非所发）。现直接复现服务端已经施加的那一次翻转，二者恒同源。
                        //   注：maskRect.Angle 与 fallbackAngle 仅差常量偏移（offsetAngle + 标定旋转），
                        //   两者同轴，"是否翻转 180°"这一关系在图像系与关系世界系中一致，故 bool 足够。
                        double arrowAngleDeg = maskRect.Angle + (headFlip == true ? 180.0 : 0.0);
                        float arrowRad = (float)(arrowAngleDeg * Math.PI / 180.0);
                        float arrowUx = (float)Math.Cos(arrowRad);
                        float arrowUy = (float)Math.Sin(arrowRad);
                        float arrowEndX = rectCenter.X + arrowUx * DirectionArrowLengthPx;
                        float arrowEndY = rectCenter.Y + arrowUy * DirectionArrowLengthPx;
                        // 主线段
                        x.DrawLine(DirectionArrowColor, DirectionArrowLineWidth, rectCenter, new PointF(arrowEndX, arrowEndY));
                        // 箭头头部：以端点为顶点、沿反向回折 ±24° 的两条短折线
                        float hx = -arrowUx * ArrowHeadLengthPx;
                        float hy = -arrowUy * ArrowHeadLengthPx;
                        float headCos = 0.9135f; // cos(24°)
                        float headSin = 0.4067f; // sin(24°)
                        var head1 = new PointF(
                            arrowEndX + hx * headCos - hy * headSin,
                            arrowEndY + hx * headSin + hy * headCos);
                        var head2 = new PointF(
                            arrowEndX + hx * headCos + hy * headSin,
                            arrowEndY - hx * headSin + hy * headCos);
                        x.DrawLine(DirectionArrowColor, DirectionArrowLineWidth, new PointF(arrowEndX, arrowEndY), head1);
                        x.DrawLine(DirectionArrowColor, DirectionArrowLineWidth, new PointF(arrowEndX, arrowEndY), head2);
                    }
                    edgeIdx++;
                }
                foreach (var barcode in scanerResult.BarcodeResults)
                {
                    var codeStr = barcode.CodeString();
                    if (codeStr.Contains("http", StringComparison.OrdinalIgnoreCase))
                    {
                        continue;
                    }
                    // H10: IsResize=false 时不做缩放转换，避免文本位置偏移到左上角
                    var center = barcode.Center();
                    var textPos = edge.IsResize
                        ? new PointF(center.X / edge.ResizeScale, center.Y / edge.ResizeScaleY)
                        : new PointF(center.X, center.Y);
                    // 2026-09-09: 画面二维码标签 = 条码末尾 2 个字符（如 0123456789 → 89），
                    // 缩短画面文字避免遮挡；条码不足 2 位时显示完整码（避免 Range 异常）。
                    var label = codeStr.Length >= 2 ? codeStr[^2..] : codeStr;
                    x.DrawText(label, _font, SixLabors.ImageSharp.Color.Red, textPos);
                }

            });
            timings.Split("MutateDraw");
            _reusableShowBitmap = Tools.UpdateShow(drawImg, _reusableShowBitmap);
            ImageForShow = _reusableShowBitmap;
            timings.Split("UpdateShow");
            if (Settings.Instance.IsSaveDraw && (scanerResult.HasBarcodeResults || edgeResults?.Count > 0))
            {
                // P0-FIX: 保存图片/日志失败不应中断推理主流程（如可移动磁盘未就绪、网络驱动器掉线）
                // 失败时降级为 Warning，避免每帧刷屏 ERROR；主流程（绘制+UI 显示）已完成，仅落盘失败
                // L501b: folder 为 null 表示目录创建失败（如 E 盘未就绪），跳过所有保存
                if (string.IsNullOrEmpty(folder))
                {
                    // 目录不可用，跳过保存（GetOrCreateTodayFolder 已记录 Warning）
                    timings.Split("SaveDraw");
                    return;
                }
                var saveFolder = folder!;
                try
                {
                    // M42: 传递 _cts.Token 以便应用退出时取消 I/O
                    await drawImg.SaveAsJpegAsync(Path.Combine(saveFolder, $"draw_{scanerResult.FrameNumber}.jpg"), _cts.Token).ConfigureAwait(false);
                    timings.Split("SaveDraw");
                    // 原图保存已移至 DrawImage 之前的 ProcessImageAsync 中执行，避免保存到已绘制的图像
                    await File.WriteAllTextAsync(Path.Combine(saveFolder, $"log_{scanerResult.FrameNumber}.txt"), timings.GetLog(), _cts.Token).ConfigureAwait(false);
                    timings.Split("SaveLog");
                }
                catch (OperationCanceledException) { throw; }
                catch (Exception saveEx) when (saveEx is IOException or UnauthorizedAccessException or DirectoryNotFoundException)
                {
                    // 设备未就绪/路径不可访问：降级为 Warning，避免 ERROR 刷屏
                    LogService.Instance.Warning($"保存检测结果图片失败（路径: {folder}），已跳过本帧保存: {saveEx.Message}");
                }
            }
        }

        /// <summary>
        /// 计算掩码最小外接旋转矩形"短边方向一分为二"头尾分割线的两个端点。
        /// 端点 = 矩形中心 ± 短轴单位向量 × (半短边 + 外扩)，线段与矩形短边平行、穿过中心、两端略微穿出分割区域，
        /// 直观把产品划为头/尾两半（配合灰度判向的"头端明暗"假设核对）。
        /// <para>坐标系：与传入的 <paramref name="center"/> 一致（推理图坐标，DrawImage 的画布同坐标系）。</para>
        /// <para>方向说明：angle 描述掩码矩形宽度轴方向（度，逆时针、相对 X 轴，即 GetMaskMinAreaRect().Angle）；
        /// 短轴与长轴正交：宽度为长边时短轴 = 高度轴（angle + 90°），高度为长边时短轴 = 宽度轴（angle）。</para>
        /// </summary>
        /// <param name="center">掩码矩形中心（推理图坐标）。</param>
        /// <param name="width">掩码矩形宽度（宽度轴跨度）。</param>
        /// <param name="height">掩码矩形高度（高度轴跨度）。</param>
        /// <param name="angle">掩码矩形宽度轴角度（度）。</param>
        private static (PointF P1, PointF P2) ComputeMaskShortAxisSegment(PointF center, float width, float height, float angle)
        {
            // 短轴方向：宽度为长边时短轴 = 高度轴（angle+90°），否则短轴 = 宽度轴（angle）
            bool longIsWidth = width >= height;
            var shortSide = longIsWidth ? height : width;
            var angleDeg = longIsWidth ? angle + 90f : angle;

            // 外扩：max(最小像素, 短边 × 比例)。短边很小时线本身短，需保证外扩可见；外扩过大则失去"贯穿"感
            var pad = MathF.Max(MaskAxisMinPadPixels, shortSide * MaskAxisPadRatio);
            var halfLen = shortSide / 2f + pad;
            var rad = angleDeg * MathF.PI / 180f;
            var dx = MathF.Cos(rad);
            var dy = MathF.Sin(rad);
            return (new PointF(center.X - dx * halfLen, center.Y - dy * halfLen),
                    new PointF(center.X + dx * halfLen, center.Y + dy * halfLen));
        }

        /// <summary>
        /// 将推理结果转换为 UI 友好的列表项并更新 <see cref="InferenceResults"/>。
        /// 在后台线程完成数据拷贝，仅集合更新切换到 UI 线程。
        /// 必须在 edgeResults.Dispose() 之前调用。
        /// </summary>
        /// <param name="result">YOLO 推理结果（用于读取 Bounds 与 Confidence）</param>
        /// <param name="indexedRecords">
        /// 与 <paramref name="result"/> 严格同序的 DbModel 列表，由 <see cref="DetectionRecordService.BuildAndSaveAsync"/> 返回。
        /// 被边界过滤跳过的位置为 null，UI 也会跳过对应检测框显示。
        /// </param>
        private void UpdateInferenceResults(YoloResult<Segmentation>? result, IReadOnlyList<DbModel?> indexedRecords,
            IReadOnlyDictionary<int, double>? maskAreaByEdgeIndex = null)
        {
            // 在后台线程构建结果列表，避免在 UI 线程执行 CPU 密集计算
            var items = new List<InferenceResultItem>(result?.Count ?? 0);
            if (result is not null)
            {
                int idx = 0;
                foreach (var pred in result)
                {
                    // 按索引从 indexedRecords 取对应 DbModel，避免 Width×Height 匹配在多目标同尺寸时错配
                    var dbm = (idx < indexedRecords.Count) ? indexedRecords[idx] : null;
                    idx++;

                    // 被边界过滤跳过的检测框：UI 也不显示
                    if (dbm is null)
                    {
                        continue;
                    }

                    // Angle/Barcode 全部来自 DbModel，与数据库记录/发送机器人值完全一致：
                    // - Barcode=扫码枪位置匹配的真实字符串（无匹配时 "noread"）
                    // - Angle=DetectionRecordService 最终角度（角度模型或掩码回退，经跨帧锁定后由
                    //   AngleTracker 归一化到 (-180,180] 落库），与配方页/ToVGT 出口同域，直接显示即可；
                    //   未知哨兵(-9999)原样保留，AngleDisplay 据此显示"未知"
                    // Bounds 与 Confidence 取自 pred（原始推理数据），用于显示中心点与置信度
                    double maskArea = 0;
                    if (maskAreaByEdgeIndex is not null && maskAreaByEdgeIndex.TryGetValue(idx - 1, out var area))
                    {
                        maskArea = area;
                    }
                    items.Add(new InferenceResultItem
                    {
                        Confidence = pred.Confidence,
                        X = pred.Bounds.X,
                        Y = pred.Bounds.Y,
                        Width = pred.Bounds.Width,
                        Height = pred.Bounds.Height,
                        Angle = dbm.Angle,
                        Barcode = dbm.Barcode,
                        MaskAreaOriginalPixels = maskArea,
                        // 2026-09-08: 灰度判向统计随主页推理结果展示（null=未判向，与落库值同源，
                        // 方便现场直接核对头端明暗/死区，无需切到数据库页）
                        BrightMean = dbm.BrightMean,
                        DarkMean = dbm.DarkMean,
                        BrightnessDiff = dbm.BrightnessDiff,
                    });
                }
            }

            // 2026-09-07: 本帧无有效目标（edgeResults 为空，或检测框全部被边界/面积过滤 → items 为空）时
            // 保留上一帧的产品信息，不做清空刷新，避免产线无产品经过时主页结果列表闪空。
            if (items.Count == 0)
            {
                return;
            }

            // 在 UI 线程更新 ObservableCollection（推理在后台线程，集合修改必须在 UI 线程）
            // 使用全限定名避免与 MainAPP.Application 命名空间冲突
            _ = System.Windows.Application.Current?.Dispatcher?.BeginInvoke(new Action(() =>
            {
                InferenceResults.Clear();
                foreach (var item in items)
                {
                    InferenceResults.Add(item);
                }
                OnPropertyChanged(nameof(ResultCount));
            }));
        }

        // M21: 接受 CancellationToken，在应用退出时取消未完成的保存任务
        private static void QueueSaveSourceImage(Image<Rgb24> sourceImg, string? folder, uint frameNumber, CancellationToken cancellationToken)
        {
            // L501b: folder 为 null 表示目录创建失败（如 E 盘未就绪），跳过保存
            if (string.IsNullOrEmpty(folder))
            {
                return;
            }
            var saveFolder = folder!;
            var sourceCopy = sourceImg.CloneAs<Rgb24>();
            // H69: Task.Run 前检查取消，若已取消则立即释放克隆图像，避免 sourceCopy 泄漏
            if (cancellationToken.IsCancellationRequested)
            {
                sourceCopy.Dispose();
                return;
            }
            // L370a: 不向 Task.Run 传 token（让 lambda 内部的 SaveAsPngAsync 处理取消），确保 finally 总能执行
            _ = Task.Run(async () =>
            {
                try
                {
                    await sourceCopy.SaveAsPngAsync(Path.Combine(saveFolder, $"source_{frameNumber}.png"), cancellationToken).ConfigureAwait(false);
                }
                catch (OperationCanceledException)
                {
                    // 应用关闭时取消，无需记录错误
                }
                catch (Exception ex)
                {
                    LogService.Instance.Error($"保存原图失败(Frame={frameNumber}): {ex}");
                }
                finally
                {
                    sourceCopy.Dispose();
                }
            });
        }
        #endregion
        private uint? _lastFrameNumber;
        // M15: 跟踪 scanner 实例变化，实例变化时复位 _lastFrameNumber
        private HikScannerType? _lastScannerInstance;
        // L365a: 图像帧通道（生产者=ReadImageOneLoop，消费者=_processImageLoopTask）。
        // Bounded(MaxImagesinQueue) + Wait：队列满时 WriteAsync 阻塞；这里仅用作容量上限，
        // 实际丢帧逻辑由 ReadImageOneLoop 中通过 Reader.Count 判断后置 NeedDrop 实现。
        private readonly Channel<FrameResult> _imageChannel = Channel.CreateBounded<FrameResult>(new BoundedChannelOptions(MaxImagesinQueue)
        {
            FullMode = BoundedChannelFullMode.Wait,
            SingleReader = true,
            SingleWriter = false,
        });
        private bool _hasLoggedWaitForCapture;
        // 当日图片目录缓存：避免每帧都调用 Directory.CreateDirectory
        // null 表示目录创建失败（如 E 盘未就绪），调用方应跳过保存
        private string? _cachedFolder;
        private string? _cachedFolderDate;

        /// <summary>
        /// 获取当日图片保存目录，仅在日期/保存路径/配方名变化或首次调用时创建目录，避免每帧 IO。
        /// 返回 null 表示目录不可用（如可移动磁盘未就绪），调用方应跳过所有图片/日志保存。
        /// </summary>
        private string? GetOrCreateTodayFolder()
        {
            // L28: 目录名使用本地日期便于用户在文件系统中直接查找，不参与时间比对
            var today = DateTime.Now.ToString("yyyyMMdd");
            var saveFolder = Settings.Instance.PicturesSaveFolder;
            var recipeName = Settings.Instance.CurrentRecipeName;
            // M16: 缓存键包含保存路径和配方名，设置变更时自动重建目录
            var cacheKey = $"{saveFolder}|{recipeName}|{today}";
            if (_cachedFolderDate == cacheKey)
            {
                return _cachedFolder;
            }

            _cachedFolder = Path.Combine(saveFolder, recipeName, today);
            _cachedFolderDate = cacheKey;
            try
            {
                Directory.CreateDirectory(_cachedFolder);
            }
            catch (Exception ex)
            {
                // 设备未就绪/路径不可访问：降级为 Warning，避免每帧 ERROR 刷屏；
                // 清空缓存路径，调用方见 null 跳过保存
                _cachedFolder = null;
                LogService.Instance.Warning($"创建图片保存目录失败（路径: {saveFolder}），将跳过图片保存: {ex.Message}");
            }
            return _cachedFolder;
        }

        //增加丢包和丢帧
        #region ReadImageOneLoop
        private async Task ReadImageOneLoop(HikScannerType scanner)
        {
            // P0-2: Dispose 后 _cts 可能已释放，访问 Token 会抛 ObjectDisposedException，直接退出
            if (_disposed) return;
            CancellationToken token;
            try { token = _cts.Token; }
            catch (ObjectDisposedException) { return; }

            // 每次迭代重新获取最新的 scanner 实例，以感知 Scanners.Initialize 重建的新对象（如重连后的实例）
            var currentScanner = Devices.Scanners.HikScaner ?? scanner;
            if (!currentScanner.IsConnected)
            {
                // 设备未连接（可能在重连中），短暂等待后跳过本轮
                // M15: 设备重连后 FrameNumber 会从 0 重新开始，复位 _lastFrameNumber 避免误报大量丢包
                _lastFrameNumber = null;
                // L366a: 扫描枪断开后重置日志标志，重连后再次记录"开始等待拍照"
                _hasLoggedWaitForCapture = false;
                // P0-2: 设备断连，更新连接状态为"重连中"（橙色）
                UpdateScannerStatus(ScannerConnectionState.Connecting);
                try { await Task.Delay(100, token).ConfigureAwait(false); }
                catch (OperationCanceledException) { }
                catch (ObjectDisposedException) { }
                return;
            }

            // P0-2: 设备已连接，更新连接状态（每轮循环都更新，确保重连后状态及时刷新为已连接）
            UpdateScannerStatus(ScannerConnectionState.Connected);

            // M15: 检测 scanner 实例变化（重连后可能创建新实例），复位帧号计数器
            if (_lastScannerInstance != currentScanner)
            {
                _lastFrameNumber = null;
                _lastScannerInstance = currentScanner;
            }

            FrameResult scanerResult;
            try
            {
                if (!_hasLoggedWaitForCapture)
                {
                    LogService.Instance.Info("开始等待拍照");
                    _hasLoggedWaitForCapture = true;
                }
                scanerResult = await GetImageWithRetryAsync(currentScanner, token).ConfigureAwait(false);
            }
            catch (HikScannerException ex)
            {
                HandleFrameGrabFailure(ex);
                return;
            }
            // M60: 删除冗余的 catch (OperationCanceledException) { throw; }，让取消异常自然传播

            if (scanerResult.HasBarcodeResults)
            {
                // 将每个识别到的条码通过消息发送到 MainWindow（MVVM-friendly）
                foreach (var br in scanerResult.BarcodeResults)
                {
                    // L423: 缓存 CodeString() 避免重复调用
                    var code = br.CodeString();
                    if (!string.IsNullOrEmpty(code))
                    {
                        WeakReferenceMessenger.Default.Send(new AddBarcodeMessage(code));
                    }
                }
            }
            DateTime grabTime = DateTime.Now;
            var (encoder, time) = _toVgtService.MostRecentDateEncode(grabTime);
            // P0-0: MostRecentDateEncode 在无编码器数据或所有记录都晚于 grabTime 时返回 DateTime.MinValue，
            // 会导致 DbModel.EncodeTime 存为 0001-01-01，下游 ChartsViewModel 时间范围查询异常。
            // 回退到 grabTime（图像采集时刻）保证时间戳始终有效。
            scanerResult.EncoderReceivedTime = time == DateTime.MinValue ? grabTime : time;
            scanerResult.EncoderValue = encoder;
            if (!_lastFrameNumber.HasValue)
            {
                // 首次收到帧，只初始化，不判为丢包
                _lastFrameNumber = scanerResult.FrameNumber;
            }
            else
            {
                uint expected = _lastFrameNumber.Value == uint.MaxValue ? 0u : _lastFrameNumber.Value + 1u;
                if (scanerResult.FrameNumber != expected)
                {
                    // P1-2: 处理包号回绕与设备 reset 后 FrameNumber 从 0 重新开始的情况
                    // 若 当前包 < 预期，说明设备 reset 或包号回绕，不应误报为大量丢包
                    if (scanerResult.FrameNumber < expected)
                    {
                        // 设备 reset 或包号回绕：复位基线，不累计丢包
                        LogService.Instance.Info($"包号回绕或设备复位：当前包:{scanerResult.FrameNumber}, 预期:{expected}，已复位基线不计丢包");
                    }
                    else
                    {
                        // 正向丢包：当前包 > 预期
                        uint lost = scanerResult.FrameNumber - expected;
                        // P0-5: 设备 reset 后 FrameNumber 可能从一个新的大值开始（如内部计数器），
                        // 单次差值过大会被误判为瞬间丢失大量帧（如日志中的 1173369）。
                        // 设阈值 FrameNumberJumpThreshold：超过则视为设备 reset，复位基线不累计丢包。
                        // 阈值依据：10 FPS × 30s = 300 帧，断连 30s 已是严重故障，单次丢 1000 帧以上不合理。
                        const int FrameNumberJumpThreshold = 1000;
                        if (lost > FrameNumberJumpThreshold)
                        {
                            LogService.Instance.Warning($"检测到包号异常跳变（差值={lost} > {FrameNumberJumpThreshold}），疑似设备 reset，复位基线不计丢包：当前包:{scanerResult.FrameNumber}, 预期:{expected}");
                        }
                        else
                        {
                            LogService.Instance.Warning($"丢包/跳帧：当前包:{scanerResult.FrameNumber}, 预期:{expected}, 丢失:{lost}");
                            scanerResult.IsFrameLoss = true;
                            // L: 上游检测到丢包即累计丢帧统计，并通过节流策略提示用户（性能警告）
                            // P2: clamp 避免 uint→int 溢出为负数（lost 被阈值限制为 ≤1000，但防御性 clamp）
                            int lostInt = lost > (uint)int.MaxValue ? int.MaxValue : (int)lost;
                            IncrementDropFrameCount(lostInt);
                            NotifyFrameDrop($"检测到丢包/跳帧：当前包 {scanerResult.FrameNumber}，预期 {expected}，丢失 {lost}");
                        }
                    }
                }
                _lastFrameNumber = scanerResult.FrameNumber;
            }
            // 当队列拥挤时，标记为需要丢弃以降低系统压力
            // L365a: 改用 Channel.Reader.Count 反映当前队列深度（替代原 Volatile.Read(_queuedImageCount)）
            var currentCount = _imageChannel.Reader.Count;
            if (currentCount >= MaxImagesinQueue)
            {
                scanerResult.NeedDrop = true;
                MemoryDiagnostics.LogQueueDepth("ImageInfo(队列满-丢弃)", currentCount, MaxImagesinQueue);
                // L: 队列满时计入丢帧统计并通过节流策略提示用户（性能警告）
                IncrementDropFrameCount();
                NotifyFrameDrop($"图像队列已满({currentCount}/{MaxImagesinQueue})，已标记当前帧为丢弃");
            }

            // 入队到 Bounded Channel；NeedDrop 帧也需入队，由 ProcessImageAsync 发送空结果给 VGT。
            // H21/M221: Dispose 后 Writer 可能已 Complete，WriteAsync 抛 ChannelClosedException —— 直接退出循环。
            // P0-2: 使用本地 token 避免在 Dispose 后访问 _cts.Token 抛 ObjectDisposedException
            try
            {
                await _imageChannel.Writer.WriteAsync(scanerResult, token).ConfigureAwait(false);
            }
            catch (OperationCanceledException) { return; }
            catch (ChannelClosedException) { return; }
            catch (ObjectDisposedException) { return; }
        }

        // H15: 相机取帧超时上限。
        // 2026-09-05（用户拍板）: 10s → 3600s(1小时)。触发模式无触发信号(流水线未开)时 SDK 会阻塞
        // 至超时才返回空帧(Status=Timeout)——原 10s 导致每 10 秒产生一次"空帧→丢帧误计+误导日志"；
        // 延长到 1 小时使等待期基本静默（有帧时 SDK 立即返回，不受超时影响）。
        private const uint DefaultGrabTimeoutMs = 3_600_000;
        private async Task<FrameResult> GetImageWithRetryAsync(HikScannerType scanner, CancellationToken cancellationToken, uint timeoutMs = DefaultGrabTimeoutMs)
        {
            // L102: 重试次数使用提取的常量
            Exception? lastException = null;

            for (int attempt = 1; attempt <= MaxGrabRetryAttempts; attempt++)
            {
                try
                {
                    var grab = await scanner.GetImageAsync(timeoutMs).ConfigureAwait(false);
                    return FrameResult.From(grab);
                }
                catch (HikScannerException ex) when (IsTransientFrameGrabFailure(ex))
                {
                    lastException = ex;
                    if (attempt < MaxGrabRetryAttempts)
                    {
                        LogService.Instance.Info($"获取图像失败，准备重试({attempt}/{MaxGrabRetryAttempts})，错误码: 0x{ex.ErrorCode:X8}");
                        // L103: 退避时间使用提取的常量
                        // P0-2: 使用传入的 cancellationToken 避免访问已释放的 _cts
                        await Task.Delay(GrabRetryDelayMs, cancellationToken).ConfigureAwait(false);
                        continue;
                    }

                    throw;
                }
            }

            throw lastException ?? new InvalidOperationException("获取图像失败，且未返回有效异常信息。");
        }

        private static bool IsTransientFrameGrabFailure(HikScannerException ex)
        {
            // L101: 使用提取的错误码常量
            return ex.ErrorCode == HikErrorCodeTimeout;
        }

        private void HandleFrameGrabFailure(Exception exception)
        {
            if (exception is HikScannerException readerException && IsTransientFrameGrabFailure(readerException))
            {
                LogService.Instance.Warning($"扫描枪取图失败，已跳过本帧，错误码: 0x{readerException.ErrorCode:X8}");
                return;
            }

            LogService.Instance.Error($"扫描枪取图失败: {exception}");
        }

        private bool TryEnsureProcessingReady(out string errorMessage)
        {
            if (CurrentRecipe is null)
            {
                errorMessage = "没有任何配方被设置成当前配方，请检查！";
                return false;
            }

            if (CurrentRecipe.ImageTool is null)
            {
                errorMessage = $"配方:{CurrentRecipe.Name}没有进行图像配置,请检查！";
                return false;
            }

            if (CurrentRecipe.YoloTool is null || CurrentRecipe.YoloTool.EdgeDetection is null)
            {
                errorMessage = $"配方:{CurrentRecipe.Name}没有进行AI模型配置,请检查！";
                return false;
            }

            if (CoordinateTool is null)
            {
                errorMessage = $"配方:{CurrentRecipe.Name}没有标定,请检查！";
                return false;
            }

            // REVIEW-FIX: 取局部变量，避免每次访问属性触发可空流分析警告（属性每次求值可能变化）
            var coordinateTool = CoordinateTool;
            if (!_transformer.IsInitialized && IsCoordinateToolCalibrated(coordinateTool))
            {
                if (coordinateTool.SquareSize <= 0)
                {
                    errorMessage = $"配方:{CurrentRecipe.Name}的方格尺寸无效，当前值:{coordinateTool.SquareSize}";
                    return false;
                }

                _transformer.Initialize(
                    coordinateTool.Origin,
                    coordinateTool.XPoint,
                    coordinateTool.YPoint,
                    coordinateTool.SquareSize * 4d,
                    coordinateTool.SquareSize * 2d);
            }

            if (!_transformer.IsInitialized)
            {
                errorMessage = $"配方:{CurrentRecipe.Name}没有完成坐标系初始化,请检查！";
                return false;
            }

            if (string.IsNullOrWhiteSpace(EdgeDetection.ModelPath) || !File.Exists(EdgeDetection.ModelPath))
            {
                errorMessage = $"AI模型文件不存在:{EdgeDetection.ModelPath},请检查！";
                return false;
            }

            // H23: IsResize 时 ResizeScale/ResizeScaleY 不能为 0，否则除零崩溃
            if (EdgeDetection.IsResize && (EdgeDetection.ResizeScale <= 0 || EdgeDetection.ResizeScaleY <= 0))
            {
                errorMessage = $"配方:{CurrentRecipe.Name}的缩放比例无效（ResizeScale={EdgeDetection.ResizeScale}, ResizeScaleY={EdgeDetection.ResizeScaleY}），必须大于0";
                return false;
            }

            errorMessage = string.Empty;
            return true;
        }

        /// <summary>
        /// 判断坐标系标定是否完成：三个标定点不共点且 XPoint/YPoint 不与原点重合。
        /// 原判断 XPoint.X > 0.1f 不严谨，标定后 XPoint.X 可能为 0 或负值（取决于相机安装角度）。
        /// </summary>
        private static bool IsCoordinateToolCalibrated(CoordinateTool tool)
        {
            // 三个点中任意两个重合则未完成标定
            if (IsSamePoint(tool.Origin, tool.XPoint)) return false;
            if (IsSamePoint(tool.Origin, tool.YPoint)) return false;
            if (IsSamePoint(tool.XPoint, tool.YPoint)) return false;
            // 三个点共线则无法构成有效坐标系（叉积为 0）
            var cross = (tool.XPoint.X - tool.Origin.X) * (tool.YPoint.Y - tool.Origin.Y)
                      - (tool.XPoint.Y - tool.Origin.Y) * (tool.YPoint.X - tool.Origin.X);
            return Math.Abs(cross) > 0.5f;
        }

        private static bool IsSamePoint(Point2f a, Point2f b, float tolerance = 0.5f)
        {
            return Math.Abs(a.X - b.X) < tolerance && Math.Abs(a.Y - b.Y) < tolerance;
        }

        #endregion
        #region Dispose
        public void Dispose()
        {
            Dispose(true);
        }

        protected virtual void Dispose(bool disposing)
        {
            if (_disposed)
            {
                return;
            }

            _disposed = true;

            if (disposing)
            {
                RecipesManage.Instance.CurrentRecipeChanged -= OnCurrentRecipeChanged;
                _cts.Cancel();
                if (_imageForShow is IDisposable disposable)
                {
                    disposable.Dispose();
                }
                _imageForShow = null;
                _reusableShowBitmap = null;
                // 先等待后台任务结束再释放 _cts，避免任务中 WaitAsync(_cts.Token) 抛 ObjectDisposedException
                try
                {
                    // M272a: 等待初始化任务结束（带超时保护）
                    WaitForTask(_initializeTask);
                    // H80a: 等待 ReloadPredictor 任务结束（带超时保护，与 _initializeTask 一致）
                    WaitForTask(_reloadPredictorTask);
                    WaitForTask(_readImageLoopTask);
                    WaitForTask(_processImageLoopTask);
                }
                finally
                {
                    _initializeTask = null;
                    _reloadPredictorTask = null;
                    _readImageLoopTask = null;
                    _processImageLoopTask = null;
                }

                // L365a: 关闭通道写入端，通知消费者不再有新帧；残留帧由消费者读取完毕后自然退出
                try { _imageChannel.Writer.Complete(); }
                catch (ChannelClosedException) { /* 已关闭，忽略 */ }

                WaitForTask(WaitForInferenceDrainAsync());
                _predictorPool?.Dispose();
                _predictorPool = null;
                _anglePredictorPool?.Dispose();
                _anglePredictorPool = null;

                _inferenceSemaphore.Dispose();
                _cts.Dispose();

                // 记录处理停止后的最终内存快照
                MemoryDiagnostics.LogSnapshot("HomeViewModel_Disposed");
            }
        }

        private static void WaitForTask(Task? task)
        {
            if (task is null)
            {
                return;
            }

            try
            {
                // L113: 超时值使用提取的常量 DisposeTaskTimeoutSec
                // VSTHRD002: Dispose 不能改为 async，使用 Task.Run 包装避免 UI 线程死锁
#pragma warning disable VSTHRD002
                task.WaitAsync(TimeSpan.FromSeconds(DisposeTaskTimeoutSec)).GetAwaiter().GetResult();
#pragma warning restore VSTHRD002
            }
            // M57: 记录超时/异常而非静默吞掉
            catch (Exception ex)
            {
                LogService.Instance.Warning($"WaitForTask 超时或异常: {ex.Message}");
            }
        }

        // 删除终结器：HomeViewModel 不持有非托管资源，终结器只是空操作且增加 GC 压力
        #endregion
    }
}
