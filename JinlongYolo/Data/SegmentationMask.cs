namespace JinlongYolo.YoloSharp.Data;

/// <summary>
/// 表示二维图像分割掩码数据结构。
/// Represents a 2D image segmentation mask data structure.
/// </summary>
public class SegmentationMask
{
    /// <summary>
    /// 包含像素级掩码置信度的二维数组。
    /// 2D array containing the pixel-level mask confidences.
    /// </summary>
    public required float[,] Mask { get; init; }

    /// <summary>
    /// 获取指定坐标处的掩码值（置信度）。
    /// Gets the mask value (confidence) at the specified coordinates.
    /// </summary>
    /// <param name="x">X 坐标 / X coordinate.</param>
    /// <param name="y">Y 坐标 / Y coordinate.</param>
    /// <returns>该位置的置信度值 / Confidence value at the location.</returns>
    public float this[int x, int y] => Mask[x, y];

    /// <summary>
    /// 获取掩码图像的宽度。
    /// Gets the width of the mask image.
    /// </summary>
    public int Width => Mask.GetLength(0);

    /// <summary>
    /// 获取掩码图像的高度。
    /// Gets the height of the mask image.
    /// </summary>
    public int Height => Mask.GetLength(1);

    /// <summary>
    /// 获取指定坐标处的置信度分数。
    /// Retrieves the confidence score at a specified location.
    /// </summary>
    /// <param name="x">X 坐标 / X coordinate.</param>
    /// <param name="y">Y 坐标 / Y coordinate.</param>
    /// <returns>置信度值 / Confidence value.</returns>
    public float GetConfidence(int x, int y)
    {
        return Mask[x, y];
    }
}