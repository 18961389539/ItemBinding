namespace JinlongYolo.YoloSharp.Contracts.Services.Plotting;

/// <summary>
/// 定义用于在图像上绘制检测框的接口。
/// Defines an interface for drawing bounding boxes on images.
/// </summary>
internal interface IBoxDrawer
{
    /// <summary>
    /// 在指定的绘图上下文中绘制单个预测的边界框。
    /// Draws a bounding box for a single prediction in the specified plotting context.
    /// </summary>
    /// <param name="prediction">检测预测结果 / The detection prediction.</param>
    /// <param name="context">绘图上下文 / The plotting context.</param>
    public void DrawBox(Detection prediction, PlottingContext context);

    /// <summary>
    /// 根据指定的顶点数组，在绘图上下文中绘制多边形边界框（如定向边界框）。
    /// Draws a polygonal bounding box (e.g., oriented bounding box) using specified points.
    /// </summary>
    /// <param name="prediction">检测预测结果 / The detection prediction.</param>
    /// <param name="points">多边形顶点数组 / Array of points defining the polygon.</param>
    /// <param name="context">绘图上下文 / The plotting context.</param>
    public void DrawBox(Detection prediction, PointF[] points, PlottingContext context);
}