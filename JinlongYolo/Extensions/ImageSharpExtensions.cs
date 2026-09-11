namespace JinlongYolo.YoloSharp.Extensions;

/// <summary>
/// 提供针对 ImageSharp 的内部扩展方法。
/// Provides internal extension methods for ImageSharp.
/// </summary>
internal static class ImageSharpExtensions
{
    /// <summary>
    /// 将图像转换为指定像素类型的图像。如果当前已经是该类型，则直接返回，否则克隆为新类型。
    /// Converts the image to the specified pixel type. If it's already of that type, returns it directly; otherwise, clones it as the new type.
    /// </summary>
    /// <typeparam name="TPixel">目标像素类型 / The target pixel type.</typeparam>
    /// <param name="image">要转换的图像 / The image to convert.</param>
    /// <returns>指定像素类型的图像 / The image of the specified pixel type.</returns>
    public static Image<TPixel> As<TPixel>(this Image image) where TPixel : unmanaged, IPixel<TPixel>
    {
        if (image is Image<TPixel> result)
        {
            return result;
        }

        return image.CloneAs<TPixel>();
    }

    /// <summary>
    /// 根据 EXIF 信息自动调整图像方向。
    /// Automatically orients the image based on EXIF information.
    /// </summary>
    /// <param name="image">要调整方向的图像 / The image to orient.</param>
    public static void AutoOrient(this Image image) => image.Mutate(x => x.AutoOrient());
}