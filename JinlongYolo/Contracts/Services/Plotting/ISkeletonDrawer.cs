namespace JinlongYolo.YoloSharp.Contracts.Services.Plotting;

/// <summary>
/// 定义用于在图像上绘制人体骨架关键点和连接线的接口。
/// Defines an interface for drawing human skeleton keypoints and connections on images.
/// </summary>
internal interface ISkeletonDrawer
{
    /// <summary>
    /// 在指定的绘图上下文中绘制单个姿态预测的骨架。
    /// Draws the skeleton for a single pose prediction in the specified plotting context.
    /// </summary>
    /// <param name="prediction">姿态预测结果 / The pose prediction.</param>
    /// <param name="context">绘图上下文 / The plotting context.</param>
    public void DrawSkeleton(Pose prediction, PlottingContext context);
}