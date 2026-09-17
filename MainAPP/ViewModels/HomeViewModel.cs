using CommunityToolkit.Mvvm.ComponentModel;
using Extensions;
using HikScanner;
using HikScannerType = HikScanner.HikScanner;
using MainAPP.Application;
using JinlongYolo.YoloSharp;
using JinlongYolo.YoloSharp.Data;
using System.Windows.Threading;
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

        // ── 采集健康监控（2026-09-12）：丢帧率趋势 + 编码器绑定延迟 + 编码器链路 ──
        // 快照基线：DispatcherTimer 每秒取 (处理数, 丢帧数) 增量算丢帧率（不用时间戳队列，O(1)）
        private long _lastHealthProcessed;
        private long _lastHealthDropped;
        private long _lastBindDelayMs;
        private DateTime _lastBindDelayWarnAt = DateTime.MinValue;
        private DateTime _lastDropRateWarnAt = DateTime.MinValue;
        // 2026-09-16: 标记 volatile —— EnsureHealthTimer 可能被后台线程调用（线程切换兜底分支），
        // 需保证对 Dispose/构造路径立即可见
        private volatile DispatcherTimer? _healthTimer;

        /// <summary>绑定延迟告警阈值（ms）：编码器记录时间与图像到达时刻的差超过该值，
        /// 说明编码器上报断流/恢复或积压——位置与编码器的对应关系已系统性滞后该差值 × 线速。</summary>
        private const int BindDelayWarnMs = 1000;

        /// <summary>丢帧率告警阈值（0-100）：丢帧是处理过载的直接信号，
        /// 过载反压会造成"旧图新编码器"的系统性绑定错位（详见 AlgorithmSettings.TrackerExpireSeconds 上下文）。</summary>
        private const int DropRateWarnPercent = 5;

        /// <summary>告警节流窗口（秒），避免持续过载时刷屏。</summary>
        private const int HealthWarnThrottleSec = 30;

        /// <summary>丢帧率采样窗口（秒）。</summary>
        private const int HealthSampleWindowSec = 1;

        /// <summary>窗口内最小样本数：低于该值不计算丢帧率（避免小样本误报）。</summary>
        private const long MinSamplesForRate = 3;
        private int _dropNotifyCounter;
        private const int DropFrameNotifyInterval = 10;
        // 已处理帧计数（用于 FPS 计算），每处理完一帧递增，与检测结果无关
        private long _processedFrameCount;

        // 2026-09-16: 原先此处是 `private static volatile bool s_isPaused` + PauseLoop/ResumeLoop/IsLoopPaused。
        // 三方使用者（相机调试 Web / 配方页 / AI 对话）各自手写"读状态 → 若未暂停则暂停 → finally 恢复"
        // 的防踩踏逻辑，既有读-改-写竞态，又无法回答"是谁把循环停住了"。
        // 现统一为具有**持有者语义**的 Services.MainLoopGate（按持有者记账、只能释放自己那份、
        // 并记录各持有者已持有时间）。主循环读取处见 HomeViewModel.MainLoop.cs。
        // REVIEW(2026-08-05): 首帧已记录相机帧格式（诊断用）
        private static bool _frameFormatLogged;

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

        // VGT 通信服务（构造函数注入）
        private readonly IToVGTService _toVgtService;
        private readonly ModelLoaderService _modelLoader;
        private readonly DetectionRecordService _detectionRecord;

        /// <summary>
        /// 2026-09-16: 依赖改为**必填构造参数**（原先为可空参数 + <c>?? App.Services.GetRequiredService&lt;&gt;()</c> 兜底）。
        /// 那个兜底是 Service Locator 反模式：既让依赖关系在签名上不可见，又使本类无法脱离
        /// 容器初始化（单测里连构造都做不到）。现在依赖由 DI 注入（<c>App.ConfigureServices</c> 中
        /// <c>AddSingleton&lt;HomeViewModel&gt;</c>），缺失依赖会在容器解析期立刻报错，而不是运行到某帧才 NRE。
        /// </summary>
        public HomeViewModel(IToVGTService toVgtService, ModelLoaderService modelLoader, DetectionRecordService detectionRecord)
        {
            _toVgtService = toVgtService;
            _modelLoader = modelLoader;
            _detectionRecord = detectionRecord;

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
                // 2026-09-16: 采集健康监控定时器在构造函数（UI 线程）建立，
                // 修复"仅在配方切换时创建 → 启动后状态栏永不刷新"的问题（见 EnsureHealthTimer）
                EnsureHealthTimer();
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

            // 采集健康监控：每秒刷新丢帧率/绑定延迟/编码器链路到状态栏（2026-09-12）。
            // 2026-09-16: 改为幂等入口——本方法可能被调用多次（每次配方切换），
            // 原实现在这里 new 定时器并 Start，旧的既不 Stop 也不退订，会累积出多个每秒触发的定时器。
            EnsureHealthTimer();
        }

        /// <summary>
        /// 采集健康监控定时器的唯一创建入口（幂等：已创建则直接返回）。
        /// <para>2026-09-16 修复两个问题：</para>
        /// <list type="number">
        ///   <item><b>启动即失效</b>：原先只在 <see cref="OnCurrentRecipeChanged"/> 中创建定时器，
        ///   而构造函数只订阅事件、不触发它。若启动时配方管理器尚未设置当前配方（或设置当前配方
        ///   不经过该事件），<c>_healthTimer</c> 永远为 null、<c>OnHealthTick</c> 从不执行——
        ///   状态栏的丢帧率 / 绑定延迟 / 编码器链路会一直停在初始值，直到首次手动切换配方。
        ///   现由构造函数与配方切换共同调用，保证启动后即开始采集。</item>
        ///   <item><b>重复创建</b>：原实现每次配方切换都 <c>new</c> 一个新 <see cref="DispatcherTimer"/>
        ///   并 <c>Start()</c>，旧定时器既未停止也未退订 <see cref="OnHealthTick"/>，
        ///   每切一次配方就多一个每秒触发的定时器（回调重复执行 + 对象与事件累积）。</item>
        /// </list>
        /// </summary>
        private void EnsureHealthTimer()
        {
            if (_disposed || _healthTimer is not null)
            {
                return;
            }

            // DispatcherTimer 绑定的是**创建线程**的 Dispatcher：若在后台线程创建，
            // Tick 永远不会被调度（该线程新建的 Dispatcher 没有消息循环），属于静默失效。
            // 构造函数与配方切换事件都在 UI 线程，正常直接走创建分支；
            // 此处保留一次线程切换兜底，防止将来从后台线程调用时静默失效。
            // 注：本文件已 using MainAPP.Application，裸 Application 会解析为命名空间，故用全名。
            var dispatcher = System.Windows.Application.Current?.Dispatcher;
            if (dispatcher is not null && !dispatcher.CheckAccess())
            {
                // `_ =` 丢弃返回值：与本文件其余 Dispatcher 调用保持一致（VSTHRD110 要求显式观察 awaitable）
                _ = dispatcher.BeginInvoke(new Action(EnsureHealthTimer));
                return;
            }

            var timer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(HealthSampleWindowSec) };
            timer.Tick += OnHealthTick;
            _healthTimer = timer;
            timer.Start();
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
                    // 2026-09-16: 统一入口——同时更新 Timing 日志用的 _inferenceDevice 与 HUD 上的
                    // "当前推理后端"（含降级标记）；本方法在后台线程执行，入口内部会转发到 UI 线程
                    SetInferenceBackend(deviceName, ModelLoaderService.ParseBackend(deviceName));
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
        /// 是否为「推理后端会话已失效 / 原生后端执行失败」类错误。判定逻辑见
        /// <see cref="InferenceBackendFailure.IsBackendFailure"/>（纯函数，已抽出便于单测）。
        /// <para>2026-09-16: 原先内联在这里且只识别 <c>OnnxRuntimeException</c>——
        /// CUDA/cuDNN/OpenVINO 的原生失败常以其它包装类型或纯消息形式抛出（如
        /// <c>CUDNN_FE failure 7</c>、<c>AssertionFailed: device 0</c>），会被漏判，
        /// 造成"会话建得起、一跑就炸"时每帧静默失败且永不自愈（重建机制本身是对的，只是入口没收全）。</para>
        /// </summary>
        private static bool IsRuntimeBackendFailure(Exception ex) =>
            InferenceBackendFailure.IsBackendFailure(ex);

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
                        // 2026-09-16: 统一入口（顺带把 HUD 切到"已降级"配色）
                        SetInferenceBackend(result.DeviceName, result.Backend);
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


// ------------------------------------------------------------------
// This class is split into partial files by responsibility (index: docs/knowledge/代码结构与维护约定.md):
//   HomeViewModel.Display.cs     - ImageForShow cache and status-bar metrics (drop rate / bind delay / encoder link / scanner status)
//   HomeViewModel.MainLoop.cs    - main loop, decode, inference scheduling, drain wait, image channel
//   HomeViewModel.Process.cs     - per-frame pipeline ProcessImageAsync (inference -> bind -> persist -> send entry)
//   HomeViewModel.Draw.cs        - boxes / barcode / direction-arrow drawing and inference-results refresh
//   HomeViewModel.ReadImage.cs   - frame grab loop, packet-loss detection, readiness checks, calibration helpers
// Put new members in the matching file; keep this file for fields and lifecycle only.
// ------------------------------------------------------------------

    private YoloTool EdgeDetection => CurrentRecipe?.YoloTool?.EdgeDetection ?? throw new InvalidOperationException("EdgeDetection 未配置");
        // REVIEW-FIX: CoordinateTool getter 改为可空返回。原实现 `?? throw` 导致 L1372 的
        // "未标定"判空分支永远走不到——配方配了 YOLO 但未标定时 getter 直接抛异常，TryEnsureProcessingReady
        // 未捕获，异常上抛后主循环停摆。现在由调用方判空并返回可读错误信息。
        private CoordinateTool? CoordinateTool => CurrentRecipe?.CoordinateTool;
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
                // 2026-09-16: 停止并退订健康监控定时器。幂等入口保证全生命周期只有一个实例，
                // 此处是唯一的回收点（原先每次配方切换都会新建且不回收旧的）。
                var healthTimer = _healthTimer;
                if (healthTimer is not null)
                {
                    healthTimer.Stop();
                    healthTimer.Tick -= OnHealthTick;
                    _healthTimer = null;
                }
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