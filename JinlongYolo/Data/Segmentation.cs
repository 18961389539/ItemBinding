namespace JinlongYolo.YoloSharp.Data;

/// <summary>
/// 实例分割的预测结果，包含目标的边界框与像素级掩码（Mask）。
/// Instance segmentation prediction result, including bounding box and pixel-level mask.
/// </summary>
public class Segmentation : Detection, IYoloPrediction<Segmentation>, IDisposable
{
    private bool _disposed;

    /// <summary>
    /// 表示目标的二进制掩码缓冲区。
    /// Binary mask buffer representing the segmented object.
    /// </summary>
    public required BitmapBuffer Mask { get; init; }

    /// <summary>
    /// 返回实例分割结果的描述摘要。
    /// Returns a summary describing the segmentation prediction results.
    /// </summary>
    static string IYoloPrediction<Segmentation>.Describe(Segmentation[] predictions) => predictions.Summary();

    /// <summary>
    /// 释放掩码缓冲区（归还底层 ArrayPool）。幂等。
    /// Releases the mask buffer (returns underlying array to ArrayPool). Idempotent.
    /// </summary>
    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;
        Mask?.Dispose();
    }
}