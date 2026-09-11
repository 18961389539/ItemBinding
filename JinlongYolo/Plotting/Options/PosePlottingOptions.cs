namespace JinlongYolo.YoloSharp.Plotting;

/// <summary>
/// 姿态估计任务的绘制选项配置，继承自检测绘制选项。
/// Plotting options configuration for pose estimation tasks, inheriting from detection plotting options.
/// </summary>
public class PosePlottingOptions : DetectionPlottingOptions
{
    /// <summary>
    /// 获取姿态绘制选项的默认实例。
    /// Gets the default instance of pose plotting options.
    /// </summary>
    public static new PosePlottingOptions Default { get; } = new PosePlottingOptions();

    /// <summary>
    /// 获取或设置用于绘制的关键点骨架连接定义。
    /// Gets or sets the keypoint skeleton connection definition used for plotting.
    /// </summary>
    public ISkeleton Skeleton { get; set; }

    /// <summary>
    /// 获取或设置绘制关键点的半径大小。
    /// Gets or sets the radius size for drawing keypoints.
    /// </summary>
    public float KeypointRadius { get; set; }

    /// <summary>
    /// 获取或设置连接关键点的线条厚度。
    /// Gets or sets the line thickness connecting keypoints.
    /// </summary>
    public float KeypointLineThickness { get; set; }

    /// <summary>
    /// 获取或设置显示关键点所需的最小置信度。
    /// Gets or sets the minimum confidence required to display a keypoint.
    /// </summary>
    public float KeypointMinimumConfidence { get; set; }

    /// <summary>
    /// 初始化 <see cref="PosePlottingOptions"/> 类的新实例，包含人体骨架和默认尺寸参数。
    /// Initializes a new instance of the <see cref="PosePlottingOptions"/> class with human skeleton and default size parameters.
    /// </summary>
    public PosePlottingOptions()
    {
        Skeleton = ISkeleton.Human;
        KeypointRadius = 3F;
        KeypointLineThickness = 1.5F;
        KeypointMinimumConfidence = .5F;
    }
}