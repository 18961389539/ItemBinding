namespace JinlongYolo.YoloSharp.Plotting;

/// <summary>
/// 表示骨架中两个关键点之间的连接。
/// Represents a connection between two keypoints in a skeleton.
/// </summary>
/// <param name="first">第一个关键点的索引 / Index of the first keypoint.</param>
/// <param name="second">第二个关键点的索引 / Index of the second keypoint.</param>
public readonly struct SkeletonConnection(int first, int second)
{
    /// <summary>
    /// 获取第一个关键点的索引。
    /// Gets the index of the first keypoint.
    /// </summary>
    public int First { get; } = first;

    /// <summary>
    /// 获取第二个关键点的索引。
    /// Gets the index of the second keypoint.
    /// </summary>
    public int Second { get; } = second;

    /// <summary>
    /// 定义从 <see cref="ValueTuple{T1, T2}"/> 到 <see cref="SkeletonConnection"/> 的隐式转换。
    /// Defines an implicit conversion from <see cref="ValueTuple{T1, T2}"/> to <see cref="SkeletonConnection"/>.
    /// </summary>
    /// <param name="tuple">包含两个索引的元组 / Tuple containing the two indices.</param>
    public static implicit operator SkeletonConnection(ValueTuple<int, int> tuple) => new(tuple.Item1, tuple.Item2);
}