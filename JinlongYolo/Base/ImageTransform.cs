namespace JinlongYolo.YoloSharp;

/// <summary>
/// 表示图像预处理/变换信息的结构体，用于记录图像缩放与填充信息，
/// 以便在预测后将检测框从网络输入坐标系还原到原始图像坐标系。
/// Structure representing image transformation info for reverting coordinates to original scale.
/// </summary>
internal readonly struct ImageTransform
{
    /// <summary>
    /// 获取或设置填充信息（以像素为单位），表示在宽、高方向上分别填充的像素数。
    /// Gets or sets the padding information in pixels (width, height).
    /// </summary>
    public Vector<int> Padding { get; init; }

    /// <summary>
    /// 获取或设置缩放比率（宽、高方向），用于将原始图像坐标与网络输入坐标之间进行缩放变换。
    /// Gets or sets the scaling ratio (width, height) used for transforming between original and network input coordinates.
    /// </summary>
    public Vector<float> Ratio { get; init; }
}
