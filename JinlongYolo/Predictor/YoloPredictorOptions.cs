using System.Reflection;

namespace JinlongYolo.YoloSharp;

/// <summary>
/// 用于创建和配置 `YoloPredictor` 的选项集合。
/// A collection of options for creating and configuring `YoloPredictor`.
///
/// - 控制会话创建（是否使用 CUDA、是否优先使用 OpenVINO、以及自定义 SessionOptions）。
/// - 提供一个可选的 `YoloConfiguration` 的默认实例，用于注入预测器的配置。
/// - 当未显式指定 SessionOptions 或 OpenVino 配置，并且 `PreferOpenVinoCpuForDetection` 为 true
///   且 `UseCuda` 为 false 时，对于检测类模型会自动尝试使用 OpenVINO CPU 提供器以提高性能。
///
/// - Controls session creation (whether to use CUDA, whether to prefer OpenVINO, and custom SessionOptions).
/// - Provides an optional default instance of `YoloConfiguration` to inject predictor configurations.
/// - When SessionOptions or OpenVino configurations are not explicitly specified, and `PreferOpenVinoCpuForDetection` is true
///   and `UseCuda` is false, it automatically attempts to use the OpenVINO CPU provider for detection models to improve performance.
/// </summary>
public class YoloPredictorOptions
{
    /// <summary>
    /// 默认选项实例，使用库的默认行为。
    /// Default options instance, using the library's default behavior.
    /// </summary>
    public static YoloPredictorOptions Default { get; } = new();

    private static readonly OpenVinoOptions DefaultDetectionOpenVinoOptions = new();

#if USE_CUDA_DEFAULT
    /// <summary>
    /// 指示是否使用 CUDA 执行提供器（如果可用）。
    /// Indicates whether to use the CUDA execution provider (if available).
    /// </summary>
    public bool UseCuda { get; init; } = true;
#else 
    /// <summary>
    /// 指示是否使用 CUDA 执行提供器（如果可用）。
    /// Indicates whether to use the CUDA execution provider (if available).
    /// </summary>
    public bool UseCuda { get; init; }
#endif
    /// <summary>
    /// CUDA 设备 ID。
    /// The CUDA device ID.
    /// </summary>
    public int CudaDeviceId { get; init; }

    /// <summary>
    /// 指示在满足条件时是否优先为检测任务使用 OpenVINO CPU 提供器。
    /// Indicates whether to prefer the OpenVINO CPU provider for detection tasks when conditions are met.
    /// </summary>
    public bool PreferOpenVinoCpuForDetection { get; init; } = true;

    /// <summary>
    /// 自定义的 ONNX Runtime 会话选项。
    /// Custom ONNX Runtime session options.
    /// </summary>
    public SessionOptions? SessionOptions { get; init; }

    /// <summary>
    /// 如果非空，则使用指定的 OpenVinoOptions 构建 SessionOptions 并强制使用 OpenVINO 提供器。
    /// If not null, uses the specified OpenVinoOptions to build SessionOptions and forces the use of the OpenVINO provider.
    /// </summary>
    public OpenVinoOptions? OpenVino { get; init; }

    /// <summary>
    /// 可选的预测器配置实例，用于覆盖默认的 `YoloConfiguration`。
    /// Optional predictor configuration instance, used to override the default `YoloConfiguration`.
    /// </summary>
    public YoloConfiguration? Configuration { get; init; }

    // 增减影响说明（中文）：
    // - 将 PreferOpenVinoCpuForDetection 设置为 true 且满足自动判定条件时，会尝试使用 OpenVINO CPU 提供器：
    //   * 优点：在支持的环境中通常可以获得更好的推理性能（尤其是在 CPU 上）。
    //   * 缺点：需要 OpenVINO 运行时/提供器支持，若不可用则回退到普通会话。
    // - 将 UseCuda 设为 true 会尝试使用 CUDA 提供器运行（需要正确的 ONNX Runtime 与 CUDA 环境）：
    //   * 优点：对 GPU 加速推理，吞吐和延迟一般更好。
    //   * 缺点：需要额外依赖，并且可能不适用于所有部署环境。

    /// <summary>
    /// 使用指定的目录或内存模型创建推理会话时的内部方法，用于创建带或不带自定义 SessionOptions 的 InferenceSession。
    /// Internal helper to create an InferenceSession from a path or byte[] with optional SessionOptions.
    /// </summary>

    internal InferenceSession CreateSession(string path)
    {
        return CreateSessionCore(
            options => CreatePathSession(path, options),
            () => OnnxCustomMetadataReader.TryReadTask(path, out var task) ? task : null);
    }

    internal InferenceSession CreateSession(byte[] model)
    {
        return CreateSessionCore(
            options => CreateModelSession(model, options),
            () => OnnxCustomMetadataReader.TryReadTask(model, out var task) ? task : null);
    }

    private InferenceSession CreateSessionCore(Func<SessionOptions?, InferenceSession> sessionFactory,
                                              Func<YoloTask?> taskResolver)
    {
        var (sessionOptions, disposeSessionOptions) = GetSessionOptions();

        if (!ShouldAutoPreferOpenVinoForDetection || sessionOptions != null || !Services.OpenVinoSessionConfigurator.IsAvailable())
        {
            return CreateSession(sessionFactory, sessionOptions, disposeSessionOptions);
        }

        var resolvedTask = taskResolver();

        if (resolvedTask is YoloTask task)
        {
            if (!IsDetectionTask(task))
            {
                return sessionFactory(null);
            }

            try
            {
                using var openVinoSessionOptions = CreateOpenVinoSessionOptions(DefaultDetectionOpenVinoOptions);
                return sessionFactory(openVinoSessionOptions);
            }
            catch (Exception ex) when (IsAutoOpenVinoFallbackException(ex))
            {
                return sessionFactory(null);
            }
        }

        var fallbackSession = sessionFactory(null);

        if (!IsDetectionTask(fallbackSession))
        {
            return fallbackSession;
        }

        try
        {
            using var openVinoSessionOptions = CreateOpenVinoSessionOptions(DefaultDetectionOpenVinoOptions);
            var openVinoSession = sessionFactory(openVinoSessionOptions);
            fallbackSession.Dispose();
            return openVinoSession;
        }
        catch (Exception ex) when (IsAutoOpenVinoFallbackException(ex))
        {
            return fallbackSession;
        }
        catch
        {
            // REVIEW-FIX: 非回退类异常路径下释放 fallbackSession，避免会话泄漏。
            fallbackSession.Dispose();
            throw;
        }
    }

    private bool ShouldAutoPreferOpenVinoForDetection => PreferOpenVinoCpuForDetection && !UseCuda && SessionOptions is null && OpenVino is null;

    private static bool IsAutoOpenVinoFallbackException(Exception exception)
    {
        return exception is DllNotFoundException
               or EntryPointNotFoundException
               or NotSupportedException
               or OnnxRuntimeException
               or TargetInvocationException;
    }

    private static bool IsDetectionTask(YoloTask task)
    {
        return task is YoloTask.Detect or YoloTask.Obb;
    }

    private static InferenceSession CreateSession(Func<SessionOptions?, InferenceSession> sessionFactory,
                                                  SessionOptions? sessionOptions,
                                                  bool disposeSessionOptions)
    {
        if (sessionOptions is null || !disposeSessionOptions)
        {
            return sessionFactory(sessionOptions);
        }

        using (sessionOptions)
        {
            return sessionFactory(sessionOptions);
        }
    }

    private static InferenceSession CreatePathSession(string path, SessionOptions? sessionOptions)
    {
        if (sessionOptions != null)
        {
            return new InferenceSession(path, sessionOptions);
        }

        return new InferenceSession(path);
    }

    private static InferenceSession CreateModelSession(byte[] model, SessionOptions? sessionOptions)
    {
        if (sessionOptions != null)
        {
            return new InferenceSession(model, sessionOptions);
        }

        return new InferenceSession(model);
    }

    private static bool IsDetectionTask(InferenceSession session)
    {
        if (!session.ModelMetadata.CustomMetadataMap.TryGetValue("task", out var task))
        {
            return false;
        }

        return task.Equals("detect", StringComparison.OrdinalIgnoreCase)
               || task.Equals("obb", StringComparison.OrdinalIgnoreCase);
    }

    private (SessionOptions? Options, bool DisposeAfterUse) GetSessionOptions()
    {
        if (UseCuda)
        {
            if (SessionOptions is not null || OpenVino is not null)
            {
                throw new InvalidOperationException("'UseCuda', 'OpenVino' and 'SessionOptions' cannot be used together");
            }

            return (SessionOptions.MakeSessionOptionWithCudaProvider(CudaDeviceId), true);
        }

        if (OpenVino is not null)
        {
            if (SessionOptions is not null)
            {
                throw new InvalidOperationException("'OpenVino' and 'SessionOptions' cannot be used together");
            }

            return (CreateOpenVinoSessionOptions(OpenVino), true);
        }

        return (SessionOptions, false);
    }

    private static SessionOptions CreateOpenVinoSessionOptions(OpenVinoOptions openVinoOptions)
    {
        var sessionOptions = new SessionOptions();
        Services.OpenVinoSessionConfigurator.Apply(sessionOptions, openVinoOptions);

        return sessionOptions;
    }
}
