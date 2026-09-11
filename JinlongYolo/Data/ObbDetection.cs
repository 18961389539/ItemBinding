namespace JinlongYolo.YoloSharp.Data;

/// <summary>
/// 面向旋转矩形（OBB）检测结果，包含角度信息。
/// Oriented bounding box detection result containing angle information.
/// </summary>
public class ObbDetection : Detection, IYoloPrediction<ObbDetection>
{
    /// <summary>
    /// 获取旋转矩形的角度（通常以弧度或度表示，取决于具体实现）。
    /// Gets the angle of the oriented bounding box (usually in radians or degrees, depending on implementation).
    /// </summary>
    public required float Angle { get; init; }

    /// <summary>
    /// 返回 OBB 预测结果的描述摘要。
    /// Returns a summary describing the OBB prediction results.
    /// </summary>
    static string IYoloPrediction<ObbDetection>.Describe(ObbDetection[] predictions) => predictions.Summary();
}