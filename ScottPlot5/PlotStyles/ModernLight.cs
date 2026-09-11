namespace ScottPlot.PlotStyles;

public class ModernLight : PlotStyle
{
    public ModernLight()
    {
        Palette = new Palettes.Modern();
        AxisColor = Color.FromHex("#333333");
        GridMajorLineColor = Color.FromHex("#cccccc").WithOpacity(0.5);
        FigureBackgroundColor = Color.FromHex("#fafafa");
        DataBackgroundColor = Color.FromHex("#ffffff");
        LegendBackgroundColor = Color.FromHex("#f0f0f0");
        LegendFontColor = Color.FromHex("#333333");
        LegendOutlineColor = Color.FromHex("#cccccc");
    }
}