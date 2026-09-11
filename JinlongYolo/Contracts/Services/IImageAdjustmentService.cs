namespace JinlongYolo.YoloSharp.Contracts.Services;

/// <summary>
/// 负责调整和转换边界框坐标的服务，适应缩放、填充等图像变换。
/// Service responsible for adjusting and transforming bounding box coordinates, accommodating scaling and padding.
/// </summary>
internal interface IBoundingBoxTransformer
{
    /// <summary>
    /// 将特定的图像变换应用于指定的边界框。
    /// Applies a specific image transformation to the given bounding box.
    /// </summary>
    /// <param name="rectangle">原始边界框 / The original bounding box.</param>
    /// <param name="transform">包含偏移和缩放因子的变换对象 / Transformation object with offset and scale.</param>
    /// <returns>变换后的边界框 / The transformed bounding box.</returns>
    public Rectangle Apply(RectangleF rectangle, ImageTransform transform);

    /// <summary>
    /// 根据原始图像大小和模型输入大小计算所需的图像变换。
    /// Computes the required image transformation based on the original image size and model input size.
    /// </summary>
    /// <param name="originalImageSize">原始图像尺寸 / The original image size.</param>
    /// <returns>计算出的图像变换 / The computed image transformation.</returns>
    public ImageTransform Compute(Size originalImageSize);
}