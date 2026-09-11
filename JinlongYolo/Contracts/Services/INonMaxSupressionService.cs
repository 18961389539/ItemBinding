namespace JinlongYolo.YoloSharp.Contracts.Services;

/// <summary>
/// 非极大值抑制（NMS）服务，用于去除冗余的重叠检测框。
/// Non-Maximum Suppression (NMS) service used to remove redundant overlapping bounding boxes.
/// </summary>
internal interface INonMaxSuppression
{
    /// <summary>
    /// 对给定的边界框集合应用非极大值抑制。
    /// Applies Non-Maximum Suppression to the given collection of bounding boxes.
    /// </summary>
    /// <param name="boxes">包含预测边界框及其置信度的数组跨度 / Span of predicted bounding boxes and their confidences.</param>
    /// <param name="iouThreshold">交并比（IoU）阈值，高于该值的框将被抑制 / Intersection over Union (IoU) threshold above which boxes are suppressed.</param>
    /// <returns>抑制后保留下来的边界框数组 / Array of bounding boxes retained after suppression.</returns>
    public RawBoundingBox[] Apply(Span<RawBoundingBox> boxes, float iouThreshold);
}