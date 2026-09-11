// PlottingExtensions.cs
// 说明：提供用于将预测结果绘制到 Image 上的扩展方法，负责创建绘图上下文并调用具体的绘制器实现。
namespace JinlongYolo.YoloSharp.Plotting;

public static class PlottingExtensions
{
    private static readonly PlottingServiceResolver _resolver = PlottingServiceResolver.Default;

    /// <summary>
    /// 在给定的 Image 上绘制预测结果并返回绘制后的 Image（以 Rgba32 格式）。
    ///
    /// 说明（中文）：
    /// 该扩展方法会创建绘图上下文，自动处理图像方向，并委托具体的绘制器实现来完成可视化操作。
    /// </summary>
    public static Image PlotImage<T>(this YoloResult<T> result, Image image, PlottingOptions? options = null) where T : IYoloPrediction<T>
    {
        // Get image as Rgba32 pixel format
        var target = image.CloneAs<Rgba32>();

        // Apply auto orient to target image
        target.AutoOrient();

        // Validate that target size equals to result size
        if (result.ImageSize != target.Size)
        {
            throw new InvalidOperationException("YOLO prediction result size is not equals to the target image size");
        }

        // Calculate required scale factory
        var gain = Math.Max(target.Width, target.Height) / 640f;

        var context = PlottingContext.Create<T>(options, target, gain);

        // Resolve plotter
        var plotter = _resolver.Resolve<IPlotter<T>>();

        // Plot the result
        plotter.Plot(result, context);

        // Return the plotted image
        return target;
    }
}
