namespace JinlongYolo.YoloSharp.Plotting;

/// <summary>
/// 实例分割任务的绘制选项配置，继承自检测绘制选项。
/// Plotting options configuration for instance segmentation tasks, inheriting from detection plotting options.
/// </summary>
public class SegmentationPlottingOptions : DetectionPlottingOptions
{
    /// <summary>
    /// 获取分割绘制选项的默认实例。
    /// Gets the default instance of segmentation plotting options.
    /// </summary>
    public static new SegmentationPlottingOptions Default { get; } = new SegmentationPlottingOptions();

    /// <summary>
    /// 获取或设置分割掩码的不透明度（0.0 到 1.0）。
    /// Gets or sets the opacity of the segmentation mask (0.0 to 1.0).
    /// </summary>
    public float MaskAlpha { get; set; }

    /// <summary>
    /// 获取或设置掩码轮廓的线条厚度。
    /// Gets or sets the line thickness of the mask contours.
    /// </summary>
    public float ContoursThickness { get; set; }

    /// <summary>
    /// 获取或设置显示分割掩码所需的最小置信度。
    /// Gets or sets the minimum confidence required to display the segmentation mask.
    /// </summary>
    public float MaskMinimumConfidence { get; set; }

    /// <summary>
    /// 初始化 <see cref="SegmentationPlottingOptions"/> 类的新实例，包含默认的透明度和线条厚度参数。
    /// Initializes a new instance of the <see cref="SegmentationPlottingOptions"/> class with default opacity and line thickness parameters.
    /// </summary>
    public SegmentationPlottingOptions()
    {
        MaskAlpha = .4f;
        ContoursThickness = 1f;
        MaskMinimumConfidence = .5F;
    }
}