namespace JinlongYolo.YoloSharp;

public static class YoloPredictorExtensions
{
    private static readonly DecoderOptions _skipMetadataOptions = new()
    {
        SkipMetadata = true,
    };

    /// <summary>
/// 一组扩展方法，提供从路径/流/字节数组以及不同 Image 重载的便捷 Predict 调用。
/// A set of extension methods providing convenient Predict calls from path/stream/byte array and different Image overloads.
/// 
/// 这些方法会处理图像加载（依据配置决定是否应用 EXIF 方向校正），
/// 并最终调用底层的同步 Predict 方法。对于异步场景可使用对应的 Async 扩展方法（若可用）。
/// These methods handle image loading (applying EXIF orientation correction based on configuration),
/// and ultimately call the underlying synchronous Predict method. For asynchronous scenarios, use the corresponding Async extension methods (if available).
/// </summary>

    #region Predict Image From Path

    public static YoloResult<Pose> Pose(this YoloPredictor predictor, string path, YoloConfiguration? configuration = null)
        => Pose(predictor, LoadImage(path, configuration ?? predictor.Configuration), configuration);

    public static YoloResult<Detection> Detect(this YoloPredictor predictor, string path, YoloConfiguration? configuration = null)
        => Detect(predictor, LoadImage(path, configuration ?? predictor.Configuration), configuration);

    public static YoloResult<ObbDetection> DetectObb(this YoloPredictor predictor, string path, YoloConfiguration? configuration = null)
        => DetectObb(predictor, LoadImage(path, configuration ?? predictor.Configuration), configuration);

    public static YoloResult<Segmentation> Segment(this YoloPredictor predictor, string path, YoloConfiguration? configuration = null)
        => Segment(predictor, LoadImage(path, configuration ?? predictor.Configuration), configuration);

    public static YoloResult<Classification> Classify(this YoloPredictor predictor, string path, YoloConfiguration? configuration = null)
        => Classify(predictor, LoadImage(path, configuration ?? predictor.Configuration), configuration);

    #endregion

    #region Predict Image From Stream

    public static YoloResult<Pose> Pose(this YoloPredictor predictor, Stream stream, YoloConfiguration? configuration = null)
        => Pose(predictor, LoadImage(stream, configuration ?? predictor.Configuration), configuration);

    public static YoloResult<Detection> Detect(this YoloPredictor predictor, Stream stream, YoloConfiguration? configuration = null)
        => Detect(predictor, LoadImage(stream, configuration ?? predictor.Configuration), configuration);

    public static YoloResult<ObbDetection> DetectObb(this YoloPredictor predictor, Stream stream, YoloConfiguration? configuration = null)
        => DetectObb(predictor, LoadImage(stream, configuration ?? predictor.Configuration), configuration);

    public static YoloResult<Segmentation> Segment(this YoloPredictor predictor, Stream stream, YoloConfiguration? configuration = null)
        => Segment(predictor, LoadImage(stream, configuration ?? predictor.Configuration), configuration);

    public static YoloResult<Classification> Classify(this YoloPredictor predictor, Stream stream, YoloConfiguration? configuration = null)
        => Classify(predictor, LoadImage(stream, configuration ?? predictor.Configuration), configuration);

    #endregion

    #region Predict Image From Buffer

    public static YoloResult<Pose> Pose(this YoloPredictor predictor, byte[] buffer, YoloConfiguration? configuration = null)
        => Pose(predictor, LoadImage(buffer, configuration ?? predictor.Configuration), configuration);

    public static YoloResult<Detection> Detect(this YoloPredictor predictor, byte[] buffer, YoloConfiguration? configuration = null)
        => Detect(predictor, LoadImage(buffer, configuration ?? predictor.Configuration), configuration);

    public static YoloResult<ObbDetection> DetectObb(this YoloPredictor predictor, byte[] buffer, YoloConfiguration? configuration = null)
        => DetectObb(predictor, LoadImage(buffer, configuration ?? predictor.Configuration), configuration);

    public static YoloResult<Segmentation> Segment(this YoloPredictor predictor, byte[] buffer, YoloConfiguration? configuration = null)
        => Segment(predictor, LoadImage(buffer, configuration ?? predictor.Configuration), configuration);

    public static YoloResult<Classification> Classify(this YoloPredictor predictor, byte[] buffer, YoloConfiguration? configuration = null)
        => Classify(predictor, LoadImage(buffer, configuration ?? predictor.Configuration), configuration);

    #endregion

    #region Predict Image

    public static YoloResult<Pose> Pose(this YoloPredictor predictor, Image image, YoloConfiguration? configuration = null) => Pose(predictor, image.As<Rgb24>(), configuration);

    public static YoloResult<Detection> Detect(this YoloPredictor predictor, Image image, YoloConfiguration? configuration = null) => Detect(predictor, image.As<Rgb24>(), configuration);

    public static YoloResult<ObbDetection> DetectObb(this YoloPredictor predictor, Image image, YoloConfiguration? configuration = null) => DetectObb(predictor, image.As<Rgb24>(), configuration);

    public static YoloResult<Segmentation> Segment(this YoloPredictor predictor, Image image, YoloConfiguration? configuration = null) => Segment(predictor, image.As<Rgb24>(), configuration);

    public static YoloResult<Classification> Classify(this YoloPredictor predictor, Image image, YoloConfiguration? configuration = null) => Classify(predictor, image.As<Rgb24>(), configuration);

    #endregion

    #region Predict Image<Rgb24>

    public static YoloResult<Pose> Pose(this YoloPredictor predictor, Image<Rgb24> image, YoloConfiguration? configuration = null)
        => predictor.Predict<Pose>(image, configuration);

    public static YoloResult<Detection> Detect(this YoloPredictor predictor, Image<Rgb24> image, YoloConfiguration? configuration = null)
        => predictor.Predict<Detection>(image, configuration);

    public static YoloResult<ObbDetection> DetectObb(this YoloPredictor predictor, Image<Rgb24> image, YoloConfiguration? configuration = null)
        => predictor.Predict<ObbDetection>(image, configuration);

    public static YoloResult<Segmentation> Segment(this YoloPredictor predictor, Image<Rgb24> image, YoloConfiguration? configuration = null)
        => predictor.Predict<Segmentation>(image, configuration);

    public static YoloResult<Classification> Classify(this YoloPredictor predictor, Image<Rgb24> image, YoloConfiguration? configuration = null)
        => predictor.Predict<Classification>(image, configuration);

    #endregion

    #region LoadImage

    /// <summary>
    /// 从磁盘路径加载图像为 <see cref="Image{Rgb24}"/>，并根据配置决定是否应用 EXIF 自动校正。
    /// </summary>
    /// <param name="path">图像文件路径。</param>
    /// <param name="configuration">预测配置（用于决定是否应用自动方向校正）。</param>
    /// <returns>返回解码后的 <see cref="Image{Rgb24}"/> 实例。</returns>
    /// <exception cref="UnknownImageFormatException">当图像格式无法识别时抛出。</exception>
    private static Image<Rgb24> LoadImage(string path, YoloConfiguration configuration)
    {
        return configuration.ApplyAutoOrient
               ? Image.Load<Rgb24>(path)
               : Image.Load<Rgb24>(_skipMetadataOptions, path);
    }

    /// <summary>
    /// 从流中加载图像为 <see cref="Image{Rgb24}"/>，并根据配置决定是否应用 EXIF 自动校正。
    /// </summary>
    /// <param name="stream">包含图像数据的流。</param>
    /// <param name="configuration">预测配置（用于决定是否应用自动方向校正）。</param>
    /// <returns>返回解码后的 <see cref="Image{Rgb24}"/> 实例。</returns>
    private static Image<Rgb24> LoadImage(Stream stream, YoloConfiguration configuration)
    {
        return configuration.ApplyAutoOrient
               ? Image.Load<Rgb24>(stream)
               : Image.Load<Rgb24>(_skipMetadataOptions, stream);
    }

    /// <summary>
    /// 从字节数组加载图像为 <see cref="Image{Rgb24}"/>，并根据配置决定是否应用 EXIF 自动校正。
    /// </summary>
    /// <param name="buffer">包含图像数据的字节数组。</param>
    /// <param name="configuration">预测配置（用于决定是否应用自动方向校正）。</param>
    /// <returns>返回解码后的 <see cref="Image{Rgb24}"/> 实例。</returns>
    private static Image<Rgb24> LoadImage(byte[] buffer, YoloConfiguration configuration)
    {
        return configuration.ApplyAutoOrient
               ? Image.Load<Rgb24>(buffer)
               : Image.Load<Rgb24>(_skipMetadataOptions, buffer);
    }

    #endregion
}