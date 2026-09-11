namespace JinlongYolo.YoloSharp;

/// <summary>
/// YOLO 模型预测器，封装 ONNX 推理会话及相关服务，用于对输入图像执行推理并解析结果。
///
/// 说明（中文）：
/// 本类负责：
/// - 使用 `YoloPredictorOptions` 创建或接收已创建的 `InferenceSession`。
/// - 解析模型元数据并构建内部服务解析器（`PredictorServiceResolver`）。
/// - 提供同步的 `Predict` 方法用于对 `Image<Rgb24>` 进行推理并返回解码后的结果。
/// - 管理底层会话与解析器的生命周期（实现 `IDisposable`）。
///
/// 使用建议：优先通过 `YoloPredictorOptions` 控制推理后端（CUDA / OpenVINO / 默认 CPU）。
///
/// Yolo model predictor that wraps an ONNX inference session and related services to run inference on images and decode results.
/// </summary>
public class YoloPredictor : IDisposable
{
    private readonly InferenceSession _session;
    private readonly PredictorServiceResolver _resolver;

    private bool _disposed;

    /// <summary>
    /// 模型元数据（任务类型、类别信息等）。
    ///
    /// 说明（中文）：
    /// 包含用于解码与绘制的模型信息，例如任务类型（检测 / 分割 / 姿态等）、类别名称和输入尺寸等。
    /// 可用于在运行时验证模型类型或为结果绘制提供类名映射。
    ///
    /// Model metadata (task type, class names, etc.).
    /// </summary>
    public YoloMetadata Metadata { get; }

    /// <summary>
    /// 当前使用的预测配置（置信度、IoU、图像处理选项等）。
    ///
    /// 说明（中文）：
    /// 此配置由 `YoloPredictorOptions.Configuration` 在创建时注入，或使用默认配置。
    /// 它影响预处理与后处理行为（例如置信度阈值、NMS 阈值以及是否保持纵横比）。
    ///
    /// The configuration used by the predictor (confidence, IoU, image options, etc.).
    /// </summary>
    public YoloConfiguration Configuration { get; }

    #region Constractor

    /// <summary>
    /// 使用指定模型文件路径创建 <see cref="YoloPredictor"/>。
    /// Create a <see cref="YoloPredictor"/> from a model file path.
    /// </summary>
    /// <param name="path">ONNX 模型文件路径 / Path to the ONNX model file.</param>
    public YoloPredictor(string path) : this(path, YoloPredictorOptions.Default) { }

    /// <summary>
    /// 使用内存中的模型字节数组创建 <see cref="YoloPredictor"/>。
    /// Create a <see cref="YoloPredictor"/> from an in-memory model byte array.
    /// </summary>
    /// <param name="model">ONNX 模型的字节数组 / Byte array containing the ONNX model.</param>
    public YoloPredictor(byte[] model) : this(model, YoloPredictorOptions.Default) { }

    /// <summary>
    /// 使用模型文件路径和自定义选项创建 <see cref="YoloPredictor"/>。
    /// Create a <see cref="YoloPredictor"/> from a model file path with custom options.
    /// </summary>
    /// <param name="path">ONNX 模型文件路径 / Path to the ONNX model file.</param>
    /// <param name="options">预测器选项 / Predictor options.</param>
    public YoloPredictor(string path, YoloPredictorOptions options) : this(options.CreateSession(path), options) { }
    
    /// <summary>
    /// 使用模型字节数组和自定义选项创建 <see cref="YoloPredictor"/>。
    /// Create a <see cref="YoloPredictor"/> from a model byte array with custom options.
    /// </summary>
    /// <param name="model">ONNX 模型的字节数组 / Byte array containing the ONNX model.</param>
    /// <param name="options">预测器选项 / Predictor options.</param>
    public YoloPredictor(byte[] model, YoloPredictorOptions options) : this(options.CreateSession(model), options) { }

    private YoloPredictor(InferenceSession session, YoloPredictorOptions options)
    {
        _session = session;
        _resolver = new PredictorServiceResolver(_session, options.Configuration ?? YoloConfiguration.Default);
        Metadata = _resolver.Resolve<YoloMetadata>();
        Configuration = _resolver.Resolve<YoloConfiguration>();
    }

    #endregion

    #region Predict

    /// <summary>
    /// 对指定图像执行推理并使用相应的解码器解析结果。
    ///
    /// 说明（中文）：
    /// - 该方法为同步接口，会执行预处理、模型推理与后处理（解码）。
    /// - `T` 决定使用的解码器与期望任务类型（检测 / 分割 / 分类 / 姿态等），
    ///   并在内部通过 <see cref="ValidateTask{T}"/> 验证模型是否支持该任务。
    /// - 返回的 `YoloResult<T>` 包含解码后的预测、推理速度统计与图像尺寸信息。
    ///
    /// Runs inference on the provided image and decodes the outputs using the appropriate decoder.
    /// </summary>
    internal YoloResult<T> Predict<T>(Image<Rgb24> image, YoloConfiguration? configuration) where T : IYoloPrediction<T>
    {
        // Validate the model task
        ValidateTask<T>();

        // Resolve runner service
        var runner = _resolver.Resolve<ISessionRunner>(configuration);

        // Run the model (include pre-process)
        using var output = runner.PreprocessAndRun(image, out var timer);

        // Start postprocess timer
        timer.StartPostprocess();

        // Resolve the decoder
        var decoder = _resolver.Resolve<IDecoder<T>>(configuration);

        // Parse the tensor to result
        var result = decoder.Decode(output, image.Size);

        // Create YoloResult
        return new YoloResult<T>(result)
        {
            Speed = timer.Stop(),
            ImageSize = image.Size,
        };
    }

    #endregion

    internal T ResolveService<T>(YoloConfiguration? configuration) where T : notnull => _resolver.Resolve<T>(configuration);

    private void ValidateTask<T>() where T : IYoloPrediction<T>
    {
        YoloTask task;

        if (typeof(T) == typeof(Pose))
        {
            task = YoloTask.Pose;
        }
        else if (typeof(T) == typeof(Detection))
        {
            task = YoloTask.Detect;
        }
        else if (typeof(T) == typeof(ObbDetection))
        {
            task = YoloTask.Obb;
        }
        else if (typeof(T) == typeof(Segmentation))
        {
            task = YoloTask.Segment;
        }
        else if (typeof(T) == typeof(Classification))
        {
            task = YoloTask.Classify;
        }
        else
        {
            throw new InvalidOperationException();
        }

        var currentTask = Metadata.Task;

        if (currentTask != task)
        {
            throw new InvalidOperationException($"The loaded model does not support this task (expected: '{task.ToString().ToLower()}' actual: '{currentTask.ToString().ToLower()}')");
        }
    }

    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _session.Dispose();
        _resolver.Dispose();
        _disposed = true;

        // 释放托管资源并禁止终结器调用
        // Dispose managed resources and suppress finalizer
        GC.SuppressFinalize(this);
    }
}