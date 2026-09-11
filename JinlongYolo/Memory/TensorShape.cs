// TensorShape.cs
namespace JinlongYolo.YoloSharp.Memory;

/// <summary>
/// 用于表示张量的形状信息，包含各维度长度、是否为动态形状（存在 -1）以及总元素数量（Length）。
/// Represents tensor shape information, including dimension lengths, whether it's a dynamic shape (contains -1), and total element count (Length).
/// </summary>
internal readonly struct TensorShape
{
    /// <summary>
    /// 获取张量的总元素数量（所有维度乘积）。若为动态形状则为 -1。
    /// Gets the total element count of the tensor (product of all dimensions). -1 if dynamic shape.
    /// </summary>
    public int Length { get; }

    /// <summary>
    /// 获取指示该形状是否包含动态维度（例如 -1）的值。
    /// Gets a value indicating whether the shape contains dynamic dimensions (e.g., -1).
    /// </summary>
    public bool IsDynamic { get; }

    /// <summary>
    /// 获取每个维度的长度（int 表示）。
    /// 以只读列表暴露，防止外部修改破坏内部一致性。
    /// </summary>
    public IReadOnlyList<int> Dimensions => _dimensionsArray;

    /// <summary>
    /// 获取每个维度的长度的 64 位表示。
    /// 以只读列表暴露，防止外部修改破坏内部一致性。
    /// </summary>
    public IReadOnlyList<long> Dimensions64 => _dimensions64Array;

    // 内部原始数组，供同程序集内需要 int[] 的高频路径使用，避免每次 ToArray 分配
    internal int[] DimensionsArray => _dimensionsArray;

    private readonly int[] _dimensionsArray;
    private readonly long[] _dimensions64Array;

    /// <summary>
    /// 根据给定维度数组构建 TensorShape 实例。
    /// Constructs a TensorShape instance based on the given dimension array.
    /// </summary>
    /// <param name="shape">维度数组 / The array of dimensions.</param>
    public TensorShape(int[] shape)
    {
        if (shape.Any(x => x < 0))
        {
            IsDynamic = true;
            Length = -1;
        }
        else
        {
            Length = GetSizeForShape(shape);
        }

        _dimensionsArray = shape;
        _dimensions64Array = [.. shape.Select(x => (long)x)];
    }

    private static int GetSizeForShape(ReadOnlySpan<int> shape)
    {
        var product = 1;

        for (var i = 0; i < shape.Length; i++)
        {
            var dimension = shape[i];

            if (dimension < 0)
            {
                throw new ArgumentOutOfRangeException($"Shape must not have negative elements: {dimension}");
            }

            product = checked(product * dimension);
        }

        return product;
    }
}
