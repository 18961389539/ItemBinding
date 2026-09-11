namespace JinlongYolo.YoloSharp.Data;

/// <summary>
/// 表示姿态估计中的一个关键点，包含其索引、坐标位置和置信度。
/// Represents a keypoint in pose estimation, including its index, coordinate position, and confidence.
/// </summary>
public class Keypoint
{
    /// <summary>
    /// 获取关键点的索引。
    /// Gets the index of the keypoint.
    /// </summary>
    public required int Index { get; init; }

    /// <summary>
    /// 获取关键点在图像中的坐标位置。
    /// Gets the coordinate position of the keypoint in the image.
    /// </summary>
    public required Point Point { get; init; }

    /// <summary>
    /// 获取该关键点预测的置信度。
    /// Gets the prediction confidence of the keypoint.
    /// </summary>
    public required float Confidence { get; init; }
}