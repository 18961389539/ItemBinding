namespace JinlongYolo.YoloSharp.Plotting;

/// <summary>
/// 绘制选项的基类，提供通用的视觉参数配置。
/// Base class for plotting options, providing common visual parameter configurations.
/// </summary>
public abstract class PlottingOptions
{
    /// <summary>
    /// 获取或设置用于绘制文本的字体家族。
    /// Gets or sets the font family used for drawing text.
    /// </summary>
    public FontFamily FontFamily { get; set; }

    /// <summary>
    /// 获取或设置文本的字体大小。
    /// Gets or sets the font size of the text.
    /// </summary>
    public float FontSize { get; set; }

    /// <summary>
    /// 获取或设置名称标签的内边距。
    /// Gets or sets the padding for the name label.
    /// </summary>
    public Vector<float> NamePadding { get; set; }

    /// <summary>
    /// 获取或设置边界框的线条厚度。
    /// Gets or sets the line thickness of the bounding boxes.
    /// </summary>
    public float BorderThickness { get; set; }

    /// <summary>
    /// 获取或设置用于分配类别的颜色调色板。
    /// Gets or sets the color palette used for assigning colors to classes.
    /// </summary>
    public ColorPalette Palette { get; set; }

    /// <summary>
    /// 初始化 <see cref="PlottingOptions"/> 类的新实例，包含默认的字体、大小和边距设置。
    /// Initializes a new instance of the <see cref="PlottingOptions"/> class with default font, size, and padding settings.
    /// </summary>
    public PlottingOptions()
    {
        FontFamily = GetDefaultFontFamily();
        FontSize = 12f;
        BorderThickness = 1;
        NamePadding = (6, 4);
        Palette = ColorPalette.Default;
    }

    private static FontFamily GetDefaultFontFamily()
    {
        if (OperatingSystem.IsWindows() && SystemFonts.TryGet("Microsoft YaHei", out var family))
        {
            return family;
        }

        if (OperatingSystem.IsAndroid() && SystemFonts.TryGet("Robot", out family))
        {
            return family;
        }

        return SystemFonts.Families.FirstOrDefault();
    }
}