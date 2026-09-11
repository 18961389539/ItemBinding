namespace JinlongYolo.YoloSharp.Data;

/// <summary>
/// 表示一次推理的通用结果信息，例如图像大小和耗时统计。
/// General result information for a single inference run, such as image size and timing.
/// </summary>
public class YoloResult
{
    /// <summary>
    /// 获取或设置原始输入图像的尺寸。
    /// Gets or sets the size of the original input image.
    /// </summary>
    public required Size ImageSize { get; init; }

    /// <summary>
    /// 获取或设置推理过程各阶段的耗时统计。
    /// Gets or sets the speed statistics of the inference process.
    /// </summary>
    public required SpeedResult Speed { get; init; }
}