/// <summary>
/// 表示一个通用的多维张量（tensor）封装，底层使用 Memory<T> 存储数据。
/// Represents a generic multi-dimensional tensor wrapper backed by Memory<T>.
/// </summary>
/// <typeparam name="T">张量中元素的非托管类型 / The unmanaged type of elements in the tensor.</typeparam>
internal class MemoryTensor<T> where T : unmanaged
{
    /// <summary>
    /// 内部缓冲区的包装（Memory&lt;T&gt;），存放张量的原始元素。
    /// </summary>
    private readonly Memory<T> _buffer;

    /// <summary>
    /// 每个维度对应的一维步长（stride），用于将多维下标映射到一维索引。
    /// 以只读列表暴露，防止外部修改破坏内部一致性。
    /// </summary>
    public IReadOnlyList<int> Strides => _stridesArray;

    /// <summary>
    /// 张量的每个维度长度（int 表示）。
    /// 以只读列表暴露，防止外部修改破坏内部一致性。
    /// </summary>
    public IReadOnlyList<int> Dimensions => _dimensionsArray;

    /// <summary>
    /// 张量的每个维度长度的 64 位表示，便于需要 long 类型的 API 使用。
    /// 以只读列表暴露，防止外部修改破坏内部一致性。
    /// </summary>
    public IReadOnlyList<long> Dimensions64 => _dimensions64Array;

    // 内部原始数组，供同程序集内需要 int[]/long[] 的高频路径使用，避免每次 ToArray 分配
    internal int[] StridesArray => _stridesArray;
    internal int[] DimensionsArray => _dimensionsArray;
    internal long[] Dimensions64Array => _dimensions64Array;

    private readonly int[] _stridesArray;
    private readonly int[] _dimensionsArray;
    private readonly long[] _dimensions64Array;

    /// <summary>
    /// 直接访问底层 Span，用于高性能读取/写入。
    /// </summary>
    public Span<T> Span => _buffer.Span;

    /// <summary>
    /// 返回底层 Memory&lt;T&gt; 缓冲区。
    /// </summary>
    public Memory<T> Buffer => _buffer;

    /// <summary>
    /// 三维索引访问器，按 (index0, index1, index2) 返回或设置元素。
    /// </summary>
    public T this[int index0, int index1, int index2]
    {
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        get => Span[GetIndex(index0, index1, index2)];

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        set => Span[GetIndex(index0, index1, index2)] = value;
    }

    /// <summary>
    /// 四维索引访问器，按 (index0, index1, index2, index3) 返回或设置元素。
    /// </summary>
    public T this[int index0, int index1, int index2, int index3]
    {
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        get => Span[GetIndex(index0, index1, index2, index3)];

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        set => Span[GetIndex(index0, index1, index2, index3)] = value;
    }

    /// <summary>
    /// 构造函数：创建一个 MemoryTensor，并根据传入的维度计算步长与维度信息。
    /// </summary>
    /// <param name="buffer">底层缓冲区，长度应等于所有维度长度的乘积。</param>
    /// <param name="dimensions">每个维度的长度数组。</param>
    public MemoryTensor(Memory<T> buffer, int[] dimensions)
    {
        // REVIEW-FIX: 用 long 中间量累乘，避免 int 乘法溢出；最终与 buffer.Length(int) 比较
        // 天然校验了 int 上限，超出则抛 InvalidOperationException。
        var size = 1L;

        for (int i = 0; i < dimensions.Length; i++)
        {
            if (dimensions[i] < 0)
            {
                throw new ArgumentOutOfRangeException(nameof(dimensions), "Dimensions must be non-negative");
            }

            size *= dimensions[i];
        }

        if (size != buffer.Length)
        {
            throw new InvalidOperationException();
        }

        _stridesArray = GetStrides(dimensions);

        _dimensionsArray = dimensions;
        _dimensions64Array = [.. dimensions.Select(x => (long)x)];

        _buffer = buffer;
    }

    /// <summary>
    /// 将三维下标映射为一维索引（用于三维张量）。
    /// </summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private int GetIndex(int index0, int index1, int index2)
    {
        Debug.Assert(_stridesArray.Length == 3);

        return (_stridesArray[0] * index0)
               + (_stridesArray[1] * index1)
               + (_stridesArray[2] * index2);
    }

    /// <summary>
    /// 将四维下标映射为一维索引（用于四维张量）。
    /// </summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private int GetIndex(int index0, int index1, int index2, int index3)
    {
        Debug.Assert(_stridesArray.Length == 4);

        return (_stridesArray[0] * index0)
               + (_stridesArray[1] * index1)
               + (_stridesArray[2] * index2)
               + (_stridesArray[3] * index3);
    }

    /// <summary>
    /// 计算每个维度的一维步长（从最后一个维度开始为 1），用于索引映射。
    /// </summary>
    private static int[] GetStrides(ReadOnlySpan<int> dimensions)
    {
        if (dimensions.Length == 0)
        {
            return [];
        }

        var strides = new int[dimensions.Length];
        var stride = 1;

        for (var i = strides.Length - 1; i >= 0; i--)
        {
            strides[i] = stride;

            stride *= dimensions[i];
        }

        return strides;
    }
}
