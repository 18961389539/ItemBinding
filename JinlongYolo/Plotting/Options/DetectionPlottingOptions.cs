namespace JinlongYolo.YoloSharp.Plotting;

/// <summary>
/// 目标检测任务的绘制选项配置。
/// Plotting options configuration for object detection tasks.
/// </summary>
public class DetectionPlottingOptions : PlottingOptions
{
    /// <summary>
    /// 获取检测绘制选项的默认实例。
    /// Gets the default instance of detection plotting options.
    /// </summary>
    public static DetectionPlottingOptions Default { get; } = new DetectionPlottingOptions();
}