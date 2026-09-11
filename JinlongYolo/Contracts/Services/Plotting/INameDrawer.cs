namespace JinlongYolo.YoloSharp.Contracts.Services.Plotting;

/// <summary>
/// 定义用于在图像上绘制类别名称和置信度标签的接口。
/// Defines an interface for drawing class names and confidence labels on images.
/// </summary>
internal interface INameDrawer
{
    /// <summary>
    /// 在指定位置绘制预测结果的名称标签。
    /// Draws the name label of the prediction at a specified position.
    /// </summary>
    /// <param name="prediction">预测结果对象 / The prediction result.</param>
    /// <param name="position">标签的基准位置 / The base position for the label.</param>
    /// <param name="inside">指示标签是否应绘制在边界框内部 / Indicates whether the label should be drawn inside the bounding box.</param>
    /// <param name="context">绘图上下文 / The plotting context.</param>
    public void DrawName(YoloPrediction prediction, PointF position, bool inside, PlottingContext context);
}