namespace JinlongYolo.YoloSharp.Services;

/// <summary>
/// 会话运行器，负责预处理输入、执行推理并提供原始输出。
///
/// 说明（中文）：
/// - 管理输入/输出缓冲区的分配（支持固定/动态输出形状两种模式）。
/// - 每次推理均创建新的 OrtIoBinding 与输出缓冲区，不复用绑定。
///   原因：在 CUDA EP 上复用 OrtIoBinding 会导致输出数据错乱（CPU EP 不受影响）。
/// - 根据 `YoloConfiguration.SuppressParallelInference` 控制是否对推理调用加锁以避免并发问题。
/// </summary>
internal class SessionRunner : ISessionRunner, IDisposable
{
    private readonly object _lock = new();
    private readonly YoloSession _yoloSession;
    private readonly YoloConfiguration _configuration;
    private readonly IMemoryAllocator _allocator;
    private readonly IPixelsNormalizer _normalizer;
    private readonly RunOptions _options = new();
    private readonly string _inputName;
    private readonly string _output0Name;
    private readonly string? _output1Name;
    private readonly Size _inputSize;

    private bool _disposed;

    /// <summary>
    /// 创建一个新的 <see cref="SessionRunner"/> 实例。
    /// </summary>
    public SessionRunner(YoloSession yoloSession,
                         YoloConfiguration configuration,
                         IMemoryAllocator allocator,
                         IPixelsNormalizer normalizer)
    {
        _yoloSession = yoloSession;
        _configuration = configuration;
        _allocator = allocator;
        _normalizer = normalizer;

        _inputName = yoloSession.Session.InputNames[0];
        _output0Name = yoloSession.Session.OutputNames[0];
        _output1Name = yoloSession.Session.OutputNames.Count > 1 ? yoloSession.Session.OutputNames[1] : null;
        _inputSize = yoloSession.Metadata.ImageSize;

        // 注意：不再使用 ThreadLocal<FixedBufferContext> 缓存固定缓冲区。
        // 原因：在 CUDA EP 上复用 OrtIoBinding 会导致输出数据错乱
        // （ORT CUDA EP 的 IoBinding 复用 bug，CPU EP 不受影响）。
    }

    public InferenceSession Session => _yoloSession.Session;

    public SessionIoShapeInfo IoShapeInfo => _yoloSession.ShapeInfo;

    /// <summary>
    /// 将输入图像预处理为模型输入张量，执行推理并返回原始模型输出。
    /// </summary>
    public IYoloRawOutput PreprocessAndRun(Image<Rgb24> image, out PredictorTimer timer)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);

        // Create timer
        timer = new PredictorTimer();

        // Start pre-process timer
        timer.StartPreprocess();

        // Allocate the input tensor
        using var input = _allocator.AllocateTensor<float>(IoShapeInfo.Input0);

        // Preprocess image to tensor
        NormalizeInput(image, input.Tensor);

        // Start inference timer
        timer.StartInference();

        // 注意：不使用复用的 FixedBufferContext。
        // 原因：在 CUDA EP 上复用 OrtIoBinding 会导致输出数据错乱
        // （ORT CUDA EP 的 IoBinding 复用 bug，CPU EP 不受影响）。
        // 改为每次创建新的 binding + 输出缓冲区，保证正确性。
        return RunWithTransientFixedOutput(input.Tensor);
    }

    /// <summary>
    /// 当输入已被预先准备为张量时，直接提交推理并返回输出。
    /// </summary>
    internal IYoloRawOutput RunPreparedInput(MemoryTensor<float> input)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);

        if (IoShapeInfo.IsDynamicOutput)
        {
            return RunWithDynamicOutput(input);
        }

        return RunWithTransientFixedOutput(input);
    }

    /// <summary>
    /// 运行具有动态输出形状的模型，返回基于 Ort 结果包装的输出对象。
    /// </summary>
    private OrtYoloRawOutput RunWithDynamicOutput(MemoryTensor<float> input)
    {
        var inputs = new NamedOnnxValue[]
        {
            NamedOnnxValue.CreateFromTensor(_inputName, new DenseTensor<float>(input.Buffer, input.DimensionsArray))
        };

        OrtYoloRawOutput Run()
        {
            var result = Session.Run(inputs);

            return new OrtYoloRawOutput(result);
        }

        if (_configuration.SuppressParallelInference)
        {
            lock (_lock)
            {
                return Run();
            }
        }
        else
        {
            return Run();
        }
    }

    /// <summary>
    /// 在固定输出模式下为单次调用创建瞬态输出缓冲区并运行推理。
    /// 返回包装好的 YoloRawOutput，调用方负责在适当时机释放。
    /// </summary>
    private YoloRawOutput RunWithTransientFixedOutput(MemoryTensor<float> input)
    {
        using var binding = Session.CreateIoBinding();
        using var ortInput = CreateOrtValue(input);

        binding.BindInput(_inputName, ortInput);

        using var _ = CreateTransientRawOutput(binding, out var output);

        if (_configuration.SuppressParallelInference)
        {
            lock (_lock)
            {
                Session.RunWithBinding(_options, binding);
            }
        }
        else
        {
            Session.RunWithBinding(_options, binding);
        }

        return output;
    }

    /// <summary>
    /// 创建瞬态输出缓冲区并绑定到 Ort I/O，返回可释放资源集合。
    /// </summary>
    private CompositeDisposable CreateTransientRawOutput(OrtIoBinding binding, out YoloRawOutput output)
    {
        MemoryTensorOwner<float>? output0 = null;
        MemoryTensorOwner<float>? output1 = null;
        OrtValue? ortOutput0 = null;
        OrtValue? ortOutput1 = null;

        try
        {
            output0 = _allocator.AllocateTensor<float>(IoShapeInfo.Output0);
            ortOutput0 = CreateOrtValue(output0.Tensor);

            binding.BindOutput(_output0Name, ortOutput0);

            if (IoShapeInfo.Output1 is TensorShape output1Shape)
            {
                output1 = _allocator.AllocateTensor<float>(output1Shape);
                ortOutput1 = CreateOrtValue(output1.Tensor);

                binding.BindOutput(_output1Name!, ortOutput1);

                output = new YoloRawOutput(output0, output1);
                output0 = null; // 所有权已移交 YoloRawOutput
                output1 = null;

                return new CompositeDisposable([ortOutput0, ortOutput1]);
            }

            output = new YoloRawOutput(output0, null);
            output0 = null; // 所有权已移交 YoloRawOutput

            return new CompositeDisposable([ortOutput0]);
        }
        catch
        {
            // REVIEW-FIX: output0 分配后若后续步骤抛异常，释放已分配的资源后再重抛，
            // 避免张量内存与 OrtValue 泄漏。
            output0?.Dispose();
            output1?.Dispose();
            ortOutput0?.Dispose();
            ortOutput1?.Dispose();
            throw;
        }
    }

    #region Preprocess

    /// <summary>
    /// 预处理：根据模型任务类型执行缩放、归一化并写入目标张量。
    /// </summary>
    private void NormalizeInput(Image<Rgb24> image, MemoryTensor<float> target)
    {
        // Apply auto orient if required
        if (_configuration.ApplyAutoOrient && image.Metadata.ExifProfile != null)
        {
            image.AutoOrient();
        }

        if (_yoloSession.Metadata.Task is YoloTask.Detect or YoloTask.Obb)
        {
            _normalizer.ResizeAndNormalizeDetectionToTensor(image, target, _inputSize, _configuration.KeepAspectRatio, out _);
            return;
        }

        _normalizer.ResizeAndNormalizeToTensor(image, target, _inputSize, _configuration.KeepAspectRatio, out _);
    }

    #endregion

    /// <summary>
    /// 从 MemoryTensor 创建 OrtValue（用于 ORT I/O 绑定）。
    /// </summary>
    private static OrtValue CreateOrtValue(MemoryTensor<float> tensor)
    {
        return CreateOrtValue(tensor.Buffer, tensor.Dimensions64Array);
    }

    private static OrtValue CreateOrtValue(Memory<float> buffer, long[] shape)
    {
        return OrtValue.CreateTensorValueFromMemory(OrtMemoryInfo.DefaultInstance, buffer, shape);
    }

    /// <summary>
    /// 释放运行时资源。
    /// 注意：每次推理的绑定与输出缓冲区在 <see cref="RunWithTransientFixedOutput"/> 中通过
    /// `using` 自行释放，此处仅释放会话级资源。
    /// </summary>
    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _options.Dispose();
        _disposed = true;
    }
}