namespace JinlongYolo.YoloSharp.Metadata;

/// <summary>
/// 表示姿态估计模型中关键点的形状信息。
/// Represents the shape information of keypoints in a pose estimation model.
/// </summary>
/// <param name="count">关键点的数量 / The number of keypoints.</param>
/// <param name="channels">每个关键点的通道数（通常为 2 [x, y] 或 3 [x, y, confidence]） / The number of channels per keypoint (usually 2 [x, y] or 3 [x, y, confidence]).</param>
public readonly struct KeypointShape(int count, int channels)
{
    /// <summary>
    /// 获取关键点的数量。
    /// Gets the number of keypoints.
    /// </summary>
    public int Count { get; } = count;

    /// <summary>
    /// 获取每个关键点的通道数。
    /// Gets the number of channels per keypoint.
    /// </summary>
    public int Channels { get; } = channels;
}