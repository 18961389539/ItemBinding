namespace JinlongYolo.YoloSharp.Plotting;

/// <summary>
/// 图像分类任务的绘制选项配置。
/// Plotting options configuration for image classification tasks.
/// </summary>
public class ClassificationPlottingOptions : PlottingOptions
{
    /// <summary>
    /// 获取分类绘制选项的默认实例。
    /// Gets the default instance of classification plotting options.
    /// </summary>
    public static ClassificationPlottingOptions Default { get; } = new ClassificationPlottingOptions();

    /// <summary>
    /// 获取或设置在图像上绘制分类文本的起始位置。
    /// Gets or sets the starting location for drawing classification text on the image.
    /// </summary>
    public Point Location { get; set; }

    /// <summary>
    /// 初始化 <see cref="ClassificationPlottingOptions"/> 类的新实例，包含预设默认值。
    /// Initializes a new instance of the <see cref="ClassificationPlottingOptions"/> class with preset defaults.
    /// </summary>
    public ClassificationPlottingOptions()
    {
        FontSize = 16;
        NamePadding = (12, 6);
        Location = new Point(30, 30);
    }
}