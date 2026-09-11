namespace JinlongYolo.YoloSharp.Plotting;

/// <summary>
/// 颜色调色板，用于为不同类别或索引分配颜色。
/// Color palette used to assign colors to different classes or indices.
/// </summary>
public class ColorPalette
{
    /// <summary>
    /// 获取默认的颜色调色板实例，包含预设的一系列颜色。
    /// Gets the default color palette instance containing a preset sequence of colors.
    /// </summary>
    public static ColorPalette Default { get; } = CreateDefault();

    private readonly Func<int, string> _factory;

    /// <summary>
    /// 初始化 <see cref="ColorPalette"/> 类的新实例，使用单一颜色。
    /// Initializes a new instance of the <see cref="ColorPalette"/> class using a single color.
    /// </summary>
    /// <param name="color">十六进制颜色字符串 / Hexadecimal color string.</param>
    public ColorPalette(string color) => _factory = _ => color;

    /// <summary>
    /// 初始化 <see cref="ColorPalette"/> 类的新实例，使用颜色数组（循环分配）。
    /// Initializes a new instance of the <see cref="ColorPalette"/> class using an array of colors (assigned cyclically).
    /// </summary>
    /// <param name="colors">十六进制颜色字符串数组 / Array of hexadecimal color strings.</param>
    public ColorPalette(string[] colors) => _factory = index => colors[index % colors.Length];

    /// <summary>
    /// 初始化 <see cref="ColorPalette"/> 类的新实例，使用自定义颜色选择器函数。
    /// Initializes a new instance of the <see cref="ColorPalette"/> class using a custom color selector function.
    /// </summary>
    /// <param name="selector">根据索引返回颜色的函数 / Function returning a color based on an index.</param>
    public ColorPalette(Func<int, string> selector) => _factory = selector;

    /// <summary>
    /// 根据给定的索引获取对应的颜色。
    /// Gets the color corresponding to the given index.
    /// </summary>
    /// <param name="index">用于选择颜色的索引（如类别ID） / Index used to select the color (e.g., class ID).</param>
    /// <returns>解析后的 <see cref="Color"/> 对象 / The parsed <see cref="Color"/> object.</returns>
    public Color GetColor(int index) => Color.ParseHex(_factory(index));

    private static ColorPalette CreateDefault()
    {
        return new ColorPalette(
        [
            "FF3838",
            "FF9D97",
            "FF701F",
            "FFB21D",
            "CFD231",
            "48F90A",
            "92CC17",
            "3DDB86",
            "1A9334",
            "00D4BB",
            "2C99A8",
            "00C2FF",
            "344593",
            "6473FF",
            "0018EC",
            "8438FF",
            "520085",
            "CB38FF",
            "FF95C8",
            "FF37C7",
        ]);
    }
}