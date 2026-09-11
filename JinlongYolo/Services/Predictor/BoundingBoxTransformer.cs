// BoundingBoxTransformer.cs
// 说明：负责将解码得到的相对/网络输入坐标系下的边界框，转换回原始图像坐标系下的整数矩形（Rectangle）。
//      通过计算缩放比和填充偏移（ImageTransform），并应用到每个边界框上完成坐标变换。
namespace JinlongYolo.YoloSharp.Services;

internal class BoundingBoxTransformer(YoloConfiguration configuration, YoloMetadata metadata) : IBoundingBoxTransformer
{
    /// <summary>
    /// 将浮点矩形（通常为解码器输出）应用逆变换，映射回原始图像坐标系并转为整数 Rectangle。
    /// </summary>
    public Rectangle Apply(RectangleF rectangle, ImageTransform transform)
    {
        var padding = transform.Padding;
        var ratio = transform.Ratio;

        var x = (rectangle.X - padding.X) * ratio.X;
        var y = (rectangle.Y - padding.Y) * ratio.Y;
        var w = rectangle.Width * ratio.X;
        var h = rectangle.Height * ratio.Y;

        // REVIEW-FIX: (int) 截断后的负宽高钳制为 0，避免构造出负尺寸的 Rectangle。
        return new Rectangle((int)x, (int)y, Math.Max(0, (int)w), Math.Max(0, (int)h));
    }

    /// <summary>
    /// 计算给定原始图像尺寸下的 ImageTransform（包含填充与缩放比）。
    /// </summary>
    public ImageTransform Compute(Size originalImageSize)
    {
        var padding = CalculatePadding(originalImageSize);
        var ratio = CalculateRatio(originalImageSize);

        return new ImageTransform
        {
            Padding = padding,
            Ratio = ratio,
        };
    }

    private Vector<int> CalculatePadding(Size size)
    {
        PixelsNormalizer.ComputeResizeLayout(size, metadata.ImageSize, configuration.KeepAspectRatio, out _, out _, out var padding);

        return padding;
    }

    private Vector<float> CalculateRatio(Size size)
    {
        var model = metadata.ImageSize;

        var xRatio = (float)size.Width / model.Width;
        var yRatio = (float)size.Height / model.Height;

        if (configuration.KeepAspectRatio)
        {
            PixelsNormalizer.ComputeResizeLayout(size, model, true, out var resizedWidth, out var resizedHeight, out _);

            xRatio = size.Width / (float)resizedWidth;
            yRatio = size.Height / (float)resizedHeight;
        }

        return (xRatio, yRatio);
    }
}
