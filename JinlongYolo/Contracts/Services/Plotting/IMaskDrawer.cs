namespace JinlongYolo.YoloSharp.Contracts.Services.Plotting;

/// <summary>
/// 定义用于在图像上绘制分割掩码的接口。
/// Defines an interface for drawing segmentation masks on images.
/// </summary>
internal interface IMaskDrawer
{
    /// <summary>
    /// 在指定的绘图上下文中绘制单个分割预测的掩码。
    /// Draws the mask for a single segmentation prediction in the specified plotting context.
    /// </summary>
    /// <param name="prediction">分割预测结果 / The segmentation prediction.</param>
    /// <param name="context">绘图上下文 / The plotting context.</param>
    public void DrawMask(Segmentation prediction, PlottingContext context);
}