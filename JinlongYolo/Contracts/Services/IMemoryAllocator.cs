namespace JinlongYolo.YoloSharp.Contracts.Services;

/// <summary>
/// 定义用于在整个应用程序中分配托管内存池的服务。
/// Defines a service for allocating managed memory pools throughout the application.
/// </summary>
internal interface IMemoryAllocator
{
    /// <summary>
    /// 从池中分配指定长度的内存块。
    /// Allocates a memory block of the specified length from the pool.
    /// </summary>
    /// <typeparam name="T">要分配的数据类型 / The type of data to allocate.</typeparam>
    /// <param name="length">需要分配的元素数量 / The number of elements to allocate.</param>
    /// <param name="clean">指示是否在返回前清除内存 / Indicates whether to clear the memory before returning.</param>
    /// <returns>拥有底层数组所有权的 IMemoryOwner / An IMemoryOwner containing the underlying array.</returns>
    public IMemoryOwner<T> Allocate<T>(int length, bool clean = false);
}