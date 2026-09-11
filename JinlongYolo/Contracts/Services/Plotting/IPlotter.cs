// IPlotter.cs
namespace JinlongYolo.YoloSharp.Contracts.Services.Plotting;

/// <summary>
/// 绘制器接口，负责将预测结果绘制到目标（如图像缓冲区）上。
/// Plotter interface, responsible for plotting prediction results onto a target (e.g., image buffer).
/// </summary>
/// <typeparam name="T">预测结果的类型，必须实现 <see cref="IYoloPrediction{T}"/> / The type of prediction result, must implement <see cref="IYoloPrediction{T}"/>.</typeparam>
internal interface IPlotter<T> where T : IYoloPrediction<T>
{
    /// <summary>
    /// 将给定的预测结果绘制到指定的绘图上下文中。
    /// Plots the given prediction results into the specified plotting context.
    /// </summary>
    /// <param name="result">要绘制的预测结果 / The prediction results to plot.</param>
    /// <param name="context">绘图上下文，包含绘制目标与选项 / The plotting context, containing the target and options.</param>
    /// <remarks>
    /// 实现此接口的绘制器应负责将 Detection/Segmentation/Pose/Classification 等结果可视化到目标 Image 上。
    /// Plotters implementing this interface should visualize results such as Detection/Segmentation/Pose/Classification onto the target Image.
    /// </remarks>
    public void Plot(YoloResult<T> result, PlottingContext context);
}
