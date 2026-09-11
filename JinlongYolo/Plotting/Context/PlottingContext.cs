namespace JinlongYolo.YoloSharp.Plotting;

/// <summary>
/// 绘图上下文：封装绘制预测结果所需的目标图像、文本样式和各种视觉参数。
/// </summary>
/// <remarks>
/// 此类由 <see cref="PlottingExtensions.PlotImage{T}"/> 工厂方法创建，
/// 根据不同预测类型（Detection/Segmentation/Pose/Classification）选择合适的绘制选项。
/// </remarks>
internal class PlottingContext
{
    /// <summary>
    /// 绘制目标（以 Rgba32 表示）。
    /// </summary>
    public Image<Rgba32> Target { get; }

    /// <summary>
    /// 文本绘制选项（字体、对齐方式等）。
    /// </summary>
    public TextOptions TextOptions { get; }

    /// <summary>
    /// 颜色调色板，用于为不同类别选择颜色。
    /// </summary>
    public ColorPalette ColorPalette { get; }

    /// <summary>
    /// 边框厚度（像素，已经乘以 Gain）。
    /// </summary>
    public float BorderThickness { get; }

    /// <summary>
    /// 名称文本的内边距（已经乘以 Gain）。
    /// </summary>
    public Vector<float> NamePadding { get; }

    #region Pose

    /// <summary>
    /// 姿态绘制使用的骨架连接定义。默认为人类骨架定义。
    /// </summary>
    public ISkeleton Skeleton { get; } = ISkeleton.Human;

    /// <summary>
    /// 关键点绘制半径（像素，已乘以 Gain）。
    /// <para>建议范围：0.5 - 8（根据输出分辨率与视觉需求调整）。</para>
    /// </summary>
    public float KeypointRadius { get; }

    /// <summary>
    /// 关键点连线的线宽（像素，已乘以 Gain）。
    /// <para>建议范围：0.5 - 6。</para>
    /// </summary>
    public float KeypointLineThickness { get; }

    /// <summary>
    /// 显示关键点所需的最小置信度阈值（0-1）。置信度低于该值的关键点将不会被绘制。
    /// <para>建议默认 0.3 - 0.6，视模型置信度分布而定。</para>
    /// </summary>
    public float KeypointMinimumConfidence { get; }

    #endregion

    #region Segmentation

    /// <summary>
    /// 分割蒙版的不透明度（0-1）。
    /// <para>增大：蒙版更明显但可能遮挡细节；减小：蒙版更透明，便于观察图像细节。</para>
    /// <para>建议范围：0.1 - 0.8。</para>
    /// </summary>
    public float MaskAlpha { get; set; }

    /// <summary>
    /// 分割轮廓线宽（像素，已乘以 Gain）。
    /// <para>建议范围：0.5 - 6。</para>
    /// </summary>
    public float ContoursThickness { get; }

    /// <summary>
    /// 绘制分割蒙版或轮廓所需的最小置信度阈值（0-1）。低于该阈值的分割不予显示。
    /// <para>建议默认 0.3。</para>
    /// </summary>
    public float MaskMinimumConfidence { get; }

    #endregion

    #region Classification

    /// <summary>
    /// 分类文本绘制的位置（相对于图像左上角，已乘以 Gain）。
    /// </summary>
    public PointF Location { get; }

    #endregion

    public static PlottingContext Create<T>(PlottingOptions? options, Image<Rgba32> target, float gain) where T : IYoloPrediction<T>
    {
        if (options is null)
        {
            var predictionType = typeof(T);

            if (predictionType == typeof(Pose))
            {
                options = PosePlottingOptions.Default;
            }
            else if (predictionType == typeof(Segmentation))
            {
                options = SegmentationPlottingOptions.Default;
            }
            else if (predictionType == typeof(Detection))
            {
                options = DetectionPlottingOptions.Default;
            }
            else if (predictionType == typeof(ObbDetection))
            {
                options = DetectionPlottingOptions.Default;
            }
            else if (predictionType == typeof(Classification))
            {
                options = ClassificationPlottingOptions.Default;
            }
        else
        {
            throw new InvalidOperationException("Unsupported plotting options for the given prediction type.");
        }
        }

        if (options is PosePlottingOptions pose)
        {
            return new PlottingContext(pose, target, gain);
        }
        if (options is SegmentationPlottingOptions segmentation)
        {
            return new PlottingContext(segmentation, target, gain);
        }
        else if (options is DetectionPlottingOptions detection)
        {
            return new PlottingContext(detection, target, gain);
        }
        else if (options is ClassificationPlottingOptions classification)
        {
            return new PlottingContext(classification, target, gain);
        }
        else
        {
            throw new InvalidOperationException();
        }
    }

    #region Constructors

    /// <summary>
    /// 构造 Pose 类型的绘图上下文。
    /// </summary>
    /// <param name="options">Pose 特有的绘图选项。</param>
    /// <param name="target">目标绘制图像。</param>
    /// <param name="gain">缩放因子（用于根据目标图像缩放视觉参数）。</param>
    private PlottingContext(PosePlottingOptions options, Image<Rgba32> target, float gain)
        : this(options as PlottingOptions, target, gain)
    {
        Skeleton = options.Skeleton;
        KeypointMinimumConfidence = options.KeypointMinimumConfidence;
        KeypointRadius = options.KeypointRadius * gain;
        KeypointLineThickness = options.KeypointLineThickness * gain;
    }

    /// <summary>
    /// 构造 Detection 类型的绘图上下文。
    /// </summary>
    private PlottingContext(DetectionPlottingOptions options, Image<Rgba32> target, float gain)
        : this(options as PlottingOptions, target, gain)
    { }

    /// <summary>
    /// 构造 Segmentation 类型的绘图上下文。
    /// </summary>
    /// <param name="options">Segmentation 特有的绘图选项。</param>
    /// <param name="target">目标绘制图像。</param>
    /// <param name="gain">缩放因子（用于缩放线宽与字体等）。</param>
    private PlottingContext(SegmentationPlottingOptions options, Image<Rgba32> target, float gain)
        : this(options as PlottingOptions, target, gain)
    {
        MaskAlpha = options.MaskAlpha;
        ContoursThickness = options.ContoursThickness * gain;
        MaskMinimumConfidence = options.MaskMinimumConfidence;
    }

    /// <summary>
    /// 构造 Classification 类型的绘图上下文。
    /// </summary>
    private PlottingContext(ClassificationPlottingOptions options, Image<Rgba32> target, float gain)
        : this(options as PlottingOptions, target, gain)
    {
        Location = new PointF(options.Location.X * gain, options.Location.Y * gain);
    }

    /// <summary>
    /// 基本构造器，初始化通用绘图参数并根据 gain 缩放视觉相关数值。
    /// </summary>
    /// <param name="options">通用绘图选项。</param>
    /// <param name="target">目标绘制图像。</param>
    /// <param name="gain">缩放因子。</param>
    private PlottingContext(PlottingOptions options, Image<Rgba32> target, float gain)
    {
        Target = target;
        TextOptions = CreateTextOptions(options, gain);
        ColorPalette = options.Palette;
        BorderThickness = options.BorderThickness * gain;
        NamePadding =
        (
            options.NamePadding.X * gain,
            options.NamePadding.Y * gain
        );
    }

    #endregion

    private static TextOptions CreateTextOptions(PlottingOptions options, float gain)
    {
        var font = options.FontFamily.CreateFont(options.FontSize * gain);

        return new TextOptions(font)
        {
            VerticalAlignment = VerticalAlignment.Center,
        };
    }

    /// <summary>
    /// 创建文本绘制选项并根据 gain 缩放字体大小。
    /// </summary>
    /// <param name="options">绘图选项。</param>
    /// <param name="gain">缩放因子。</param>
    /// <returns>返回初始化好的 <see cref="TextOptions"/>。</returns>
}