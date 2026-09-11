namespace JinlongYolo.YoloSharp.Plotting;

/// <summary>
/// 定义姿态估计中骨架连接及其颜色映射的接口。
/// Defines the interface for skeleton connections and their color mappings in pose estimation.
/// </summary>
public interface ISkeleton
{
    /// <summary>
    /// 获取标准的人体骨架实例。
    /// Gets the standard human skeleton instance.
    /// </summary>
    public static ISkeleton Human { get; } = new HumanSkeleton();

    /// <summary>
    /// 获取构成骨架的所有关键点连接。
    /// Gets all the keypoint connections that make up the skeleton.
    /// </summary>
    SkeletonConnection[] Connections { get; }

    /// <summary>
    /// 获取指定索引处关键点的绘制颜色。
    /// Gets the drawing color for the keypoint at the specified index.
    /// </summary>
    /// <param name="index">关键点的索引 / Index of the keypoint.</param>
    /// <returns>关键点的颜色 / Color of the keypoint.</returns>
    Color GetKeypointColor(int index);

    /// <summary>
    /// 获取指定索引处连接线的绘制颜色。
    /// Gets the drawing color for the connection line at the specified index.
    /// </summary>
    /// <param name="index">连接线的索引 / Index of the connection line.</param>
    /// <returns>连接线的颜色 / Color of the connection line.</returns>
    Color GetLineColor(int index);
}