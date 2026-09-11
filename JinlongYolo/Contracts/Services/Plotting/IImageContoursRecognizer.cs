namespace JinlongYolo.YoloSharp.Contracts.Services.Plotting;

/// <summary>
/// 定义用于从图像中识别轮廓的服务接口。
/// Defines a service interface for recognizing contours from an image.
/// </summary>
internal interface IImageContoursRecognizer
{
    /// <summary>
    /// 从亮度图像中提取轮廓顶点。
    /// Extracts contour points from a luminance image.
    /// </summary>
    /// <param name="luminance">用于提取轮廓的单通道（亮度）图像 / Single-channel (luminance) image for contour extraction.</param>
    /// <returns>包含多组轮廓多边形顶点的数组 / An array of arrays containing contour polygon points.</returns>
    public Point[][] Recognize(Image luminance);
}