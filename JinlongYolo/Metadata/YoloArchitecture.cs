namespace JinlongYolo.YoloSharp.Metadata;

/// <summary>
/// 定义 YOLO 模型的体系结构类型。
/// Defines the architecture type of the YOLO model.
/// </summary>
public enum YoloArchitecture
{
    /// <summary>
    /// 未知或未识别的架构。
    /// Unknown or unrecognized architecture.
    /// </summary>
    Unknown,

    /// <summary>
    /// 无锚框架构（Anchor-free），例如 YOLOv10, YOLO26。
    /// Anchor-free architecture, e.g., YOLOv10, YOLO26.
    /// </summary>
    AnchorFree,

    /// <summary>
    /// 基于锚框的架构（Anchor-based），例如 YOLOv8, YOLO11, YOLOv12。
    /// Anchor-based architecture, e.g., YOLOv8, YOLO11, YOLOv12.
    /// </summary>
    AnchorBased,

    /// <summary>
    /// Ultralytics 官方架构标准，例如 YOLOv8, YOLO11, YOLOv12。
    /// Ultralytics official architecture standard, e.g., YOLOv8, YOLO11, YOLOv12.
    /// </summary>
    Ultralytics,

    /// <summary>
    /// 特定于 YOLOv10 的架构类型。
    /// Architecture type specific to YOLOv10.
    /// </summary>
    YoloV10
}