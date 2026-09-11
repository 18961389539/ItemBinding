using HikScanner;
using JinlongYolo.YoloSharp;
using JinlongYolo.YoloSharp.Data;
using JinlongYolo.YoloSharp.Metadata;
using MainAPP.Models;
using SixLabors.ImageSharp;
using SixLabors.ImageSharp.PixelFormats;
using System;
using System.Diagnostics;
using System.IO;
using System.Threading.Tasks;

namespace MainAPP.Services
{
    /// <summary>
    /// 推理后端，按性能从高到低排列。
    /// 建池/自愈降级都沿该顺序向下退，且总是以 <see cref="Cpu"/>（纯 ORT CPU EP，不依赖任何提供器）兜底。
    /// </summary>
    public enum InferenceBackend
    {
        /// <summary>CUDA（NVIDIA GPU）。</summary>
        Cuda = 0,
        /// <summary>OpenVINO GPU（Intel GPU）。</summary>
        OpenVinoGpu = 1,
        /// <summary>OpenVINO CPU。</summary>
        OpenVinoCpu = 2,
        /// <summary>纯 ORT CPU EP（onnxruntime.dll 内置，永不失败兜底）。</summary>
        Cpu = 3,
    }

    /// <summary>
    /// 预测器池创建结果：池 + 设备名 + 实际落到的后端层级。
    /// </summary>
    public sealed record PredictorPoolResult(YoloPredictorPool? Pool, string DeviceName, InferenceBackend Backend);

    /// <summary>
    /// YOLO 模型加载服务。
    /// 从 HomeViewModel 提取，负责预测器池的创建与降级重建（CUDA → OpenVINO GPU → OpenVINO CPU → CPU）。
    /// <remarks>
    /// REVIEW(2026-09-04)：建池期降级救不了「运行期才失效」的会话（如 CUDNN_FE failure 7 ——
    /// 会话建得起来、一跑卷积就炸）。自愈由 HomeViewModel 在推理期连续失败后驱动：
    /// 调 <see cref="CreatePredictorPoolResultAsync(Recipe?, InferenceBackend)"/> 从更低一级开始重建。
    /// 相比旧实现新增两个保障：1) 可指定起始层级（跳过已确认失效的后端）；
    /// 2) 末尾追加纯 CPU 兜底层（OpenVINO 运行库缺失时不再整体失败）。
    /// </remarks>
    /// </summary>
    public sealed class ModelLoaderService
    {
        // 池大小常量
        public const int CudaCount = 2;
        public const int OpenVinoGpuCount = 2;
        public const int OpenVinoCpuCount = 2;
        public const int AngleModelPoolSize = 1;

        /// <summary>
        /// 创建主分割（边缘）模型预测器池：优先 GPU，会回退 CPU。
        /// 从 CUDA 起步逐级降级（CUDA → OpenVINO-GPU → OpenVINO-CPU → 纯 CPU），
        /// 运行期连续失败由 HomeViewModel 自愈状态机逐级重建（最终必回退 CPU）。
        /// 决策(2026-09-05)：与角度模型"始终 CPU"分工——角度输入为小裁剪图固定 CPU 规避
        /// CUDNN_FE 类运行期崩溃；边缘跑全图分割优先 GPU 保节拍，失败时靠自愈兜底 CPU。
        /// </summary>
        /// <returns>元组：(预测器池, 推理设备类型名称)。若创建失败则预测器池为 null。</returns>
        public async Task<(YoloPredictorPool? Pool, string DeviceName)> CreatePredictorPoolAsync(Recipe? recipe)
        {
            var result = await CreatePredictorPoolResultAsync(recipe, InferenceBackend.Cuda).ConfigureAwait(false);
            return (result.Pool, result.DeviceName);
        }

        /// <summary>
        /// 创建主分割模型预测器池，从 <paramref name="preferredBackend"/> 起逐级向下降级。
        /// 用于初始化和运行期自愈（自愈时传已确认失效后端的下一级）。
        /// </summary>
        public async Task<PredictorPoolResult> CreatePredictorPoolResultAsync(Recipe? recipe, InferenceBackend preferredBackend)
        {
            LogService.Instance.Info($"正在初始化 AI 模型... (起始后端: {preferredBackend})");
            var edgeTool = recipe?.YoloTool?.EdgeDetection;

            if (edgeTool is null)
            {
                LogService.Instance.Warning("EdgeDetection 配置缺失，跳过初始化");
                return new PredictorPoolResult(null, string.Empty, preferredBackend);
            }

            var modelFullname = Path.Join(AppDomain.CurrentDomain.BaseDirectory, "Models", "best.onnx");
            // 2026-09-09 FIX: 原实现只要应用目录存在默认 best.onnx 就无条件覆盖配方 ModelPath，
            // 导致配方详情页显式设置的模型路径在建池/切配方/自愈重建时被静默回退为 best.onnx。
            // 现仅当配方路径为空、或指向"不存在的默认名 best.onnx"（旧配方遗留默认路径失效迁移）
            // 时才兜底为应用目录实际存在的默认模型；用户显式配置的有效路径绝不被改写。
            if (File.Exists(modelFullname) && NeedsDefaultModelFallback(edgeTool.ModelPath))
            {
                edgeTool.ModelPath = modelFullname;
            }

            if (string.IsNullOrWhiteSpace(edgeTool.ModelPath) || !File.Exists(edgeTool.ModelPath))
            {
                LogService.Instance.Warning($"AI模型文件不存在:{edgeTool.ModelPath},请检查！");
                NotificationService.Error($"AI模型文件不存在:{edgeTool.ModelPath}，请检查！");
                return new PredictorPoolResult(null, string.Empty, preferredBackend);
            }

            try
            {
                var configuration = new YoloConfiguration
                {
                    ApplyAutoOrient = edgeTool.ApplyAutoOrient,
                    Confidence = edgeTool.Confidence,
                    IoU = edgeTool.IoU,
                    SuppressParallelInference = edgeTool.SuppressParallelInference,
                    KeepAspectRatio = edgeTool.KeepAspectRatio,
                    MaximumDetections = 20, // 产线场景上限，防止 NMS 后端差异导致多余框
                };

                return await CreateSegPoolCascadeAsync(
                    edgeTool,
                    configuration,
                    preferredBackend,
                    CudaCount,
                    OpenVinoGpuCount,
                    OpenVinoCpuCount).ConfigureAwait(false);
            }
            catch (Exception ex)
            {
                LogService.Instance.Error($"创建 YoloPredictorPool 失败: {ex}");
                return new PredictorPoolResult(null, string.Empty, preferredBackend);
            }
        }

        /// <summary>
        /// 2026-09-09: 判断某模型路径是否需要兜底替换为应用目录默认 best.onnx。
        /// true = 路径为空/空白，或指向"不存在的默认名 best.onnx"（旧配方默认 Saves/Models/best.onnx 失效迁移）；
        /// false = 路径已指向真实存在的文件（用户自定义模型），或指向不存在的非默认名文件
        /// （保留原值交给上层校验明确报错，不做静默替换）。
        /// </summary>
        internal static bool NeedsDefaultModelFallback(string? modelPath)
            => string.IsNullOrWhiteSpace(modelPath)
               || (!File.Exists(modelPath)
                   && string.Equals(Path.GetFileName(modelPath), "best.onnx", StringComparison.OrdinalIgnoreCase));

        /// <summary>
        /// 创建角度检测模型预测器池（固定纯 CPU 后端）。
        /// 仅当配方启用 <see cref="YoloTools.IsAngleDetectionEnabled"/> 且模型文件存在时创建。
        /// 说明：角度模型输入为"摆正裁剪"的小图（通常数百像素），CPU 推理已满足实时性；
        /// 历史上 CUDA 后端会在运行期抛 CUDNN_FE failure 7（建池期不报错、一跑卷积才炸），
        /// 而角度推理链路无运行期自愈，会逐帧失败。故固定 CPU，规避该类会话级崩溃。
        /// </summary>
        public async Task<YoloPredictorPool?> CreateAnglePredictorPoolAsync(Recipe? recipe)
        {
            var yoloTools = recipe?.YoloTool;
            if (yoloTools is null || !yoloTools.IsAngleDetectionEnabled)
            {
                return null;
            }

            var angleTool = yoloTools.AngleDetection;
            if (angleTool is null || string.IsNullOrWhiteSpace(angleTool.ModelPath) || !File.Exists(angleTool.ModelPath))
            {
                LogService.Instance.Warning($"角度模型未启用或模型文件不存在: {angleTool?.ModelPath}");
                return null;
            }

            try
            {
                var configuration = new YoloConfiguration
                {
                    ApplyAutoOrient = angleTool.ApplyAutoOrient,
                    Confidence = angleTool.Confidence,
                    IoU = angleTool.IoU,
                    SuppressParallelInference = angleTool.SuppressParallelInference,
                    KeepAspectRatio = angleTool.KeepAspectRatio,
                    MaximumDetections = 10,
                };

                // 固定 CPU：见方法注释（规避角度推理无自愈下的 CUDA 运行期崩溃）
                var result = await CreateSegPoolCascadeAsync(
                    angleTool,
                    configuration,
                    InferenceBackend.Cpu,
                    AngleModelPoolSize,
                    AngleModelPoolSize,
                    AngleModelPoolSize).ConfigureAwait(false);

                if (result.Pool is null)
                {
                    LogService.Instance.Warning("角度模型初始化失败，将输出未知角度(-9999)");
                }

                return result.Pool;
            }
            catch (Exception ex)
            {
                LogService.Instance.Warning($"角度模型初始化失败，将输出未知角度(-9999): {ex.Message}");
                return null;
            }
        }

        /// <summary>
        /// 自 <paramref name="preferredBackend"/> 起逐级尝试建池的核心逻辑（分割/角度共用，tool 为两者共同的
        /// <see cref="YoloTool"/> 基类）。
        /// CUDA 层额外要求运行时 DLL 预载成功；末尾总是以纯 CPU（CpuOnly）兜底，
        /// 该层只依赖 onnxruntime.dll 内置 CPU EP，除模型文件问题外不会在建池期失败。
        /// </summary>
        private static async Task<PredictorPoolResult> CreateSegPoolCascadeAsync(
            YoloTool tool,
            YoloConfiguration configuration,
            InferenceBackend preferredBackend,
            int cudaCount,
            int openVinoGpuCount,
            int openVinoCpuCount)
        {
            var modelPath = tool.ModelPath;

            // 只有 CUDA 需要先预载运行库；预载失败视为该层不可用，直接抛给本级捕获。
            if (preferredBackend <= InferenceBackend.Cuda && !CudaRuntimeLoader.TryPreloadCudaDlls())
            {
                LogService.Instance.Warning("CUDA 运行时 DLL 不可用，跳过 CUDA");
                preferredBackend = InferenceBackend.OpenVinoGpu;
            }

            for (var tier = (int)preferredBackend; tier <= (int)InferenceBackend.Cpu; tier++)
            {
                var backend = (InferenceBackend)tier;

                try
                {
                    YoloPredictorPool pool = backend switch
                    {
                        InferenceBackend.Cuda => await BuildCudaPoolAsync(tool, configuration, cudaCount).ConfigureAwait(false),
                        InferenceBackend.OpenVinoGpu => await BuildLayoutPoolAsync(modelPath, new YoloPredictorPoolLayout { OpenVinoGpuCount = openVinoGpuCount, Configuration = configuration }).ConfigureAwait(false),
                        InferenceBackend.OpenVinoCpu => await BuildLayoutPoolAsync(modelPath, new YoloPredictorPoolLayout { OpenVinoCpuCount = openVinoCpuCount, Configuration = configuration }).ConfigureAwait(false),
                        _ => await BuildLayoutPoolAsync(modelPath, new YoloPredictorPoolLayout { CpuOnlyCount = openVinoCpuCount, Configuration = configuration }).ConfigureAwait(false),
                    };

                    // 预热（仅 CUDA）：启动期跑一次空白图推理，消除首帧才发生的 CUDA kernel JIT 编译 /
                    // cuDNN 算法选择 / 显存 lazy 分配等一次性开销，让产线第一个工件的节拍也稳定；同时把
                    // "建得起、跑不动"的运行期故障（如 CUDNN_FE failure）提前暴露——失败则释放该池并降级下一级。
                    if (backend == InferenceBackend.Cuda && !await WarmupCudaPoolAsync(pool).ConfigureAwait(false))
                    {
                        pool.Dispose();
                        LogService.Instance.Warning($"后端 {BackendName(backend)} 预热推理失败，尝试下一级");
                        continue;
                    }

                    LogService.Instance.Info($"AI 模型初始化完成 ({BackendName(backend)})");
                    return new PredictorPoolResult(pool, BackendName(backend), backend);
                }
                catch (Exception ex)
                {
                    LogService.Instance.Warning($"后端 {BackendName(backend)} 不可用，尝试下一级: {ex.Message}");
                }
            }

            return new PredictorPoolResult(null, string.Empty, InferenceBackend.Cpu);
        }

        private static Task<YoloPredictorPool> BuildCudaPoolAsync(YoloTool tool, YoloConfiguration configuration, int count)
        {
            var cudaDeviceId = tool.CudaDeviceId;

            return Task.Run(() =>
            {
                // REVIEW(2026-09-04): 显式构造带显存约束的 SessionOptions（详见 CudaSessionOptionsFactory），
                // 不再裸传 UseCuda=true。注意：传 SessionOptions 时 UseCuda 必须置 false，
                // 否则 YoloPredictorOptions.GetSessionOptions() 抛
                // "'UseCuda', 'OpenVino' and 'SessionOptions' cannot be used together"。
                // 会话选项按进程生命周期持有，供池内所有预测器复用，勿释放。
                var sessionOptions = CudaSessionOptionsFactory.Create(
                    cudaDeviceId,
                    CudaSessionOptionsFactory.FromMiB(CudaSessionOptionsFactory.SegmentationGpuMemoryMiB));

                return YoloPredictorPool.Create(
                    () => new YoloPredictor(tool.ModelPath,
                        new YoloPredictorOptions
                        {
                            UseCuda = false,
                            SessionOptions = sessionOptions,
                            Configuration = configuration
                        }),
                    count);
            });
        }

        private static Task<YoloPredictorPool> BuildLayoutPoolAsync(string modelPath, YoloPredictorPoolLayout layout)
        {
            return Task.Run(() => YoloPredictorPool.Create(modelPath, layout));
        }

        /// <summary>
        /// 对 CUDA 池做一次空白图预热推理：在后台线程跑一次完整前向，触发首帧才发生的
        /// CUDA kernel JIT 编译 / cuDNN 算法选择 / 显存 lazy 分配等一次性开销，消除产线
        /// 第一个工件的节拍抖动；同时把"建得起、跑不动"的运行期故障（如 CUDNN_FE failure）
        /// 提前到启动期暴露——失败返回 false，由调用方释放池并降级到下一后端。
        /// </summary>
        private static async Task<bool> WarmupCudaPoolAsync(YoloPredictorPool pool)
        {
            try
            {
                var sw = Stopwatch.StartNew();
                await Task.Run(() => pool.Use(RunWarmupInference)).ConfigureAwait(false);
                sw.Stop();
                LogService.Instance.Info($"[预热] CUDA 空白图推理完成，耗时 {sw.ElapsedMilliseconds}ms");
                return true;
            }
            catch (Exception ex)
            {
                LogService.Instance.Error($"[预热] CUDA 空白图推理失败: {ex.Message}");
                return false;
            }
        }

        /// <summary>
        /// 预热实际执行体：按模型任务类型构造一张模型输入尺寸的空白图并跑一次前向。
        /// 输入尺寸取 <see cref="YoloMetadata.ImageSize"/>（模型真实输入），确保触发的 kernel /
        /// cuDNN 算法路径与实际推理一致；结果用后即弃并释放，避免占用显存/托管内存。
        /// </summary>
        private static void RunWarmupInference(YoloPredictor predictor)
        {
            var size = predictor.Metadata.ImageSize;
            using var blank = new Image<Rgb24>(size.Width, size.Height);

            switch (predictor.Metadata.Task)
            {
                case YoloTask.Segment:
                    using (predictor.Segment(blank)) { }
                    break;
                case YoloTask.Detect:
                    using (predictor.Detect(blank)) { }
                    break;
                case YoloTask.Obb:
                    using (predictor.DetectObb(blank)) { }
                    break;
                case YoloTask.Pose:
                    using (predictor.Pose(blank)) { }
                    break;
                case YoloTask.Classify:
                    using (predictor.Classify(blank)) { }
                    break;
                default:
                    throw new InvalidOperationException($"未支持的预热任务类型: {predictor.Metadata.Task}");
            }
        }

        /// <summary>
        /// 返回后端对应的展示名（与历史日志保持一致，供 UI/日志展示）。
        /// </summary>
        public static string BackendName(InferenceBackend backend) => backend switch
        {
            InferenceBackend.Cuda => "CUDA",
            InferenceBackend.OpenVinoGpu => "OpenVINO-GPU",
            InferenceBackend.OpenVinoCpu => "OpenVINO-CPU",
            _ => "CPU",
        };

        /// <summary>
        /// 将设备展示名解析回 <see cref="InferenceBackend"/>（供自愈状态机跟踪当前层级）。
        /// 空串/未知名视为 <see cref="InferenceBackend.Cpu"/>（仅影响后续降级起点，安全侧）。
        /// </summary>
        public static InferenceBackend ParseBackend(string? deviceName) => deviceName switch
        {
            "CUDA" => InferenceBackend.Cuda,
            "OpenVINO-GPU" => InferenceBackend.OpenVinoGpu,
            "OpenVINO-CPU" => InferenceBackend.OpenVinoCpu,
            _ => InferenceBackend.Cpu,
        };
    }
}
