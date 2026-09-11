namespace JinlongYolo.YoloSharp.Memory;

/// <summary>
/// 封装模型推理的原始输出张量，管理其内存生命周期。
/// Encapsulates the raw output tensors from model inference and manages their memory lifecycle.
/// </summary>
/// <param name="output0">主要输出张量的内存所有者 / The memory owner of the primary output tensor.</param>
/// <param name="output1">次要输出张量的内存所有者（可选） / The memory owner of the secondary output tensor (optional).</param>
internal class YoloRawOutput(MemoryTensorOwner<float> output0, MemoryTensorOwner<float>? output1) : IYoloRawOutput
{
    private bool _disposed;

    /// <summary>
    /// 获取模型的主要输出张量。
    /// Gets the primary output tensor of the model.
    /// </summary>
    public MemoryTensor<float> Output0
    {
        get
        {
            EnsureNotDisposed();
            return output0.Tensor;
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
            return output1?.Tensor;
        }
    }

    /// <summary>
    /// 释放底层的张量内存。
    /// Disposes the underlying tensor memory.
    /// </summary>
    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        output0.Dispose();
        output1?.Dispose();

        _disposed = true;
    }

    /// <summary>
    /// 确保对象未被释放。
    /// Ensures that the object has not been disposed.
    /// </summary>
    private void EnsureNotDisposed()
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
    }
}