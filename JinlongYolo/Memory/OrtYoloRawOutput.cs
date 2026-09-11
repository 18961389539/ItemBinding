namespace JinlongYolo.YoloSharp.Memory;

/// <summary>
/// 封装 ONNX Runtime 的原始模型输出，实现了 <see cref="IYoloRawOutput"/> 接口。
/// Encapsulates the raw model output from ONNX Runtime, implementing the <see cref="IYoloRawOutput"/> interface.
/// </summary>
internal class OrtYoloRawOutput : IYoloRawOutput
{
    private readonly IDisposable _disposable;
    private bool _disposed;

    /// <summary>
    /// 使用 ONNX Runtime 的结果集合初始化 <see cref="OrtYoloRawOutput"/> 的新实例。
    /// Initializes a new instance of the <see cref="OrtYoloRawOutput"/> class using the result collection from ONNX Runtime.
    /// </summary>
    /// <param name="result">ONNX Runtime 的推理结果集合 / The inference result collection from ONNX Runtime.</param>
    public OrtYoloRawOutput(IDisposableReadOnlyCollection<DisposableNamedOnnxValue> result)
    {
        // REVIEW-FIX: _disposable 提前到任何可能抛异常的调用之前赋值，
        // 避免 CreateMemoryTensor 中途抛异常时原生输出泄漏。
        _disposable = result;

        Output0Core = CreateMemoryTensor(result[0]);

        if (result.Count > 1)
        {
            Output1Core = CreateMemoryTensor(result[1]);
        }
    }

    /// <summary>
    /// 获取模型的主要输出张量。
    /// Gets the primary output tensor of the model.
    /// </summary>
    public MemoryTensor<float> Output0
    {
        get
        {
            EnsureNotDisposed();
            return Output0Core;
        }
    }

    /// <summary>
    /// 获取模型的次要输出张量（如果存在）。
    /// Gets the secondary output tensor of the model (if present).
    /// </summary>
    public MemoryTensor<float>? Output1
    {
        get
        {
            EnsureNotDisposed();
            return Output1Core;
        }
    }

    // 原始数据核心字段，仅在构造时赋值，避免与 EnsureNotDisposed 守卫属性递归
    private readonly MemoryTensor<float> Output0Core;
    private readonly MemoryTensor<float>? Output1Core;

    /// <summary>
    /// 释放底层的 ONNX Runtime 结果资源。
    /// Disposes the underlying ONNX Runtime result resources.
    /// </summary>
    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;
        _disposable.Dispose();
    }

    /// <summary>
    /// 确保对象未被释放。
    /// Ensures that the object has not been disposed.
    /// </summary>
    private void EnsureNotDisposed()
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
    }

    /// <summary>
    /// 从 ONNX 值创建 <see cref="MemoryTensor{T}"/>。
    /// Creates a <see cref="MemoryTensor{T}"/> from an ONNX value.
    /// </summary>
    /// <param name="value">命名的 ONNX 值 / The named ONNX value.</param>
    /// <returns>创建的内存张量 / The created memory tensor.</returns>
    private static MemoryTensor<float> CreateMemoryTensor(NamedOnnxValue value)
    {
        var tensor = value.AsTensor<float>() as DenseTensor<float>
                     ??
                     throw new InvalidOperationException("The ort result is not DenseTensor");

        // tensor.Dimensions 是 IReadOnlyList<int>，需要转为 int[] 传入 MemoryTensor 构造函数
        return new MemoryTensor<float>(tensor.Buffer, [.. tensor.Dimensions]);
    }
}