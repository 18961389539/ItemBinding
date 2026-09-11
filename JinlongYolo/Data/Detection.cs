namespace JinlongYolo.YoloSharp.Data;

/// <summary>
/// 单目标检测的结果类型，包含检测框位置信息。
/// Detection result type that contains bounding box information for object detection.
/// </summary>
public class Detection : YoloPrediction, IYoloPrediction<Detection>
{
    /// <summary>
    /// 检测框的矩形区域。
    /// The rectangular bounding box of the detection.
    /// </summary>
    public required Rectangle Bounds { get; init; }

    /// <summary>
    /// 返回检测结果的汇总字符串。
    /// Returns a summary string describing the detection results.
    /// </summary>
    static string IYoloPrediction<Detection>.Describe(Detection[] predictions) => predictions.Summary();
}