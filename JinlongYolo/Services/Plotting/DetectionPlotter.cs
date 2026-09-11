namespace JinlongYolo.YoloSharp.Services.Plotting;

internal class DetectionPlotter(IBoxDrawer boxPlotter,
                                INameDrawer namePlotter) : IPlotter<Detection>
{
    /// <summary>
    /// 绘制检测结果：绘制边框并在其附近绘制类别名称。
    /// </summary>
    public void Plot(YoloResult<Detection> result, PlottingContext context)
    {
        foreach (var box in result)
        {
            boxPlotter.DrawBox(box, context);
            namePlotter.DrawName(box, box.Bounds.Location, false, context);
        }
    }
}
