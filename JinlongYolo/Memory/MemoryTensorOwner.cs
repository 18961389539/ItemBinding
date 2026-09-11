/// <summary>
/// 包装 <see cref="IMemoryOwner{T}"/> 并持有对应的 <see cref="MemoryTensor{T}"/>，负责在 <see cref="Dispose"/> 时释放底层内存所有者。
/// Wraps an <see cref="IMemoryOwner{T}"/> and holds the corresponding <see cref="MemoryTensor{T}"/>, responsible for releasing the underlying memory owner upon <see cref="Dispose"/>.
/// </summary>
/// <typeparam name="T">张量中元素的非托管类型 / The unmanaged type of elements in the tensor.</typeparam>
/// <param name="owner">内存的所有者实例 / The memory owner instance.</param>
/// <param name="dimensions">张量的维度信息 / The dimension information of the tensor.</param>
internal class MemoryTensorOwner<T>(IMemoryOwner<T> owner, int[] dimensions) : IDisposable where T : unmanaged
{
    private MemoryTensor<T>? _tensor = new(owner.Memory, dimensions);

    /// <summary>
    /// 获取当前持有的 MemoryTensor，若已释放则抛出 ObjectDisposedException。
    /// </summary>
    public MemoryTensor<T> Tensor => _tensor
                                     ??
                                     throw new ObjectDisposedException(nameof(MemoryTensorOwner<T>));

    // 析构函数，确保在未显式调用 Dispose 时进行清理。
    ~MemoryTensorOwner() => Dispose();

    /// <summary>
    /// 释放资源：清理内部的 MemoryTensor 引用并释放底层 IMemoryOwner。
    /// </summary>
    public void Dispose()
    {
        // REVIEW-FIX: 用 Interlocked.Exchange 原子取出引用，终结器与显式 Dispose 并发时
        // 底层 IMemoryOwner 只会被 Dispose 一次。
        var tensor = Interlocked.Exchange(ref _tensor, null);

        if (tensor is not null)
        {
            owner.Dispose();
        }

        GC.SuppressFinalize(this);
    }
}
