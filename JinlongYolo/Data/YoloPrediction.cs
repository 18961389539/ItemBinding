namespace JinlongYolo.YoloSharp.Data;

/// <summary>
/// 表示单个预测项的基类，包含名称和置信度等通用字段。
/// Base class representing a single prediction item with common fields like name and confidence.
/// </summary>
public abstract class YoloPrediction
{
    /// <summary>
    /// 预测的类别名称及其关联的类别ID。
    /// The predicted class name and its associated class ID.
    /// </summary>
    public required YoloName Name { get; init; }

    /// <summary>
    /// 模型预测该类别的置信度分数 (0.0 到 1.0)。
    /// The confidence score (0.0 to 1.0) of the prediction.
    /// </summary>
    public required float Confidence { get; init; }

    /// <summary>
    /// 返回包含类别名称和置信度百分比的字符串表示。
    /// Returns a string representation containing the class name and confidence percentage.
    /// </summary>
    /// <returns>格式化后的字符串 / Formatted string.</returns>
    public override string ToString()
    {
        return $"{Name.Name} ({(int)(Confidence * 100f)}%)";
    }
}