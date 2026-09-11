namespace JinlongYolo.YoloSharp.Contracts.Services;

/// <summary>
/// 负责将图像像素规范化并转换为张量格式的服务。
/// Service responsible for normalizing image pixels and converting them to tensor format.
/// </summary>
internal interface IPixelsNormalizer
{
    /// <summary>
    /// 将图像的像素值规范化并写入指定的张量中。
    /// Normalizes the pixel values of an image and writes them into a specified tensor.
    /// </summary>
    /// <param name="image">输入图像 / The input image.</param>
    /// <param name="tensor">目标张量 / The target tensor.</param>
    /// <param name="padding">填充偏移量 / Padding offset.</param>
    public void NormalizerPixelsToTensor(Image<Rgb24> image, MemoryTensor<float> tensor, Vector<int> padding);

    /// <summary>
    /// 调整检测图像的大小并将其像素规范化后写入张量，支持保持宽高比。
    /// Resizes and normalizes a detection image into a tensor, with support for keeping the aspect ratio.
    /// </summary>
    /// <param name="image">输入图像 / The input image.</param>
    /// <param name="tensor">目标张量 / The target tensor.</param>
    /// <param name="targetSize">目标尺寸 / The target size.</param>
    /// <param name="keepAspectRatio">是否保持宽高比 / Whether to maintain the aspect ratio.</param>
    /// <param name="padding">输出的填充偏移量 / Output padding offset.</param>
    public void ResizeAndNormalizeDetectionToTensor(Image<Rgb24> image, MemoryTensor<float> tensor, Size targetSize, bool keepAspectRatio, out Vector<int> padding);

    /// <summary>
    /// 调整图像大小并将其像素规范化后写入张量，适用于通用任务。
    /// Resizes and normalizes an image into a tensor for general tasks.
    /// </summary>
    /// <param name="image">输入图像 / The input image.</param>
    /// <param name="tensor">目标张量 / The target tensor.</param>
    /// <param name="targetSize">目标尺寸 / The target size.</param>
    /// <param name="keepAspectRatio">是否保持宽高比 / Whether to maintain the aspect ratio.</param>
    /// <param name="padding">输出的填充偏移量 / Output padding offset.</param>
    public void ResizeAndNormalizeToTensor(Image<Rgb24> image, MemoryTensor<float> tensor, Size targetSize, bool keepAspectRatio, out Vector<int> padding);
}