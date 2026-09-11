namespace ScottPlot.PlotStyles;

public class ModernDark : PlotStyle
{
    public ModernDark()
    {
        Palette = new Palettes.Modern();
        AxisColor = Color.FromHex("#e0e0e0");
        GridMajorLineColor = Color.FromHex("#303030").WithOpacity(0.6);
        FigureBackgroundColor = Color.FromHex("#1e1e1e");
        DataBackgroundColor = Color.FromHex("#252526");
        LegendBackgroundColor = Color.FromHex("#3c3c3c");
        LegendFontColor = Color.FromHex("#ffffff");
        LegendOutlineColor = Color.FromHex("#555555");
    }
}