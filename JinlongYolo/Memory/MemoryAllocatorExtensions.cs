namespace JinlongYolo.YoloSharp.Memory;

/// <summary>
/// 为 <see cref="IMemoryAllocator"/> 提供扩展方法。
/// Provides extension methods for <see cref="IMemoryAllocator"/>.
/// </summary>
internal static class MemoryAllocatorExtensions
{
    /// <summary>
    /// 根据指定的形状分配一个内存张量。
    /// Allocates a memory tensor according to the specified shape.
    /// </summary>
    /// <typeparam name="T">张量元素的非托管类型 / The unmanaged type of the tensor elements.</typeparam>
    /// <param name="allocator">内存分配器 / The memory allocator.</param>
    /// <param name="shape">要分配的张量的形状 / The shape of the tensor to allocate.</param>
    /// <param name="clean">是否在分配后清理（清零）内存 / Whether to clean (zero out) the memory after allocation.</param>
    /// <returns>拥有所分配内存的 <see cref="MemoryTensorOwner{T}"/> 实例 / A <see cref="MemoryTensorOwner{T}"/> instance owning the allocated memory.</returns>
    public static MemoryTensorOwner<T> AllocateTensor<T>(this IMemoryAllocator allocator, TensorShape shape, bool clean = false)
        where T : unmanaged
    {
        var memory = allocator.Allocate<T>(shape.Length, clean);

        // shape.DimensionsArray 是内部 int[] 访问器，避免每次 ToArray 分配
        return new MemoryTensorOwner<T>(memory, shape.DimensionsArray);
    }
}