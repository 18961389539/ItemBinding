/* A modern color palette designed for data visualization with high contrast and accessibility
 */

namespace ScottPlot.Palettes;

public class Modern : IPalette
{
    public string Name { get; } = "Modern";

    public string Description { get; } = "A modern color palette designed for optimal readability " +
        "and accessibility in data visualization";

    public Color[] Colors { get; } = Color.FromHex(HexColors);

    private static readonly string[] HexColors =
    {
        "#2E86AB", // blue
        "#A23B72", // magenta
        "#F19F0F", // yellow-orange
        "#C73E1D", // red-orange
        "#6322A0", // purple
        "#67B66D", // green
        "#FF6B6B", // coral-red
        "#4DC3D4", // cyan
        "#DD7B3F", // amber
        "#8A5E8D", // deep purple
        "#9FE2BF", // light teal
        "#FFE468", // bright yellow
    };

    public Color GetColor(int index)
    {
        return Colors[index % Colors.Length];
    }
}