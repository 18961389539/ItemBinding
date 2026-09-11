namespace JinlongYolo.YoloSharp.Metadata;

/// <summary>
/// 扩展的 YOLO 元数据，专用于姿态估计模型，包含关键点形状信息。
/// Extended YOLO metadata specific to pose estimation models, containing keypoint shape information.
/// </summary>
public class YoloPoseMetadata : YoloMetadata
{
    /// <summary>
    /// 获取姿态模型预测的关键点形状。
    /// Gets the keypoint shape predicted by the pose model.
    /// </summary>
    public KeypointShape KeypointShape { get; }

    /// <summary>
    /// 使用推理会话初始化 <see cref="YoloPoseMetadata"/> 类的新实例。
    /// Initializes a new instance of the <see cref="YoloPoseMetadata"/> class using an inference session.
    /// </summary>
    /// <param name="session">ONNX 推理会话 / The ONNX inference session.</param>
    internal YoloPoseMetadata(InferenceSession session) : base(session)
    {
        var metadata = session.ModelMetadata.CustomMetadataMap;

        KeypointShape = ParseKeypointShape(metadata["kpt_shape"]);
    }

    /// <summary>
    /// 从字符串解析关键点形状。
    /// Parses the keypoint shape from a string.
    /// </summary>
    /// <param name="text">包含形状信息的字符串（例如 "[17, 3]"） / The string containing shape info (e.g., "[17, 3]").</param>
    /// <returns>解析出的关键点形状对象 / The parsed keypoint shape object.</returns>
    private static KeypointShape ParseKeypointShape(string text)
    {
        text = text[1..^1]; // '[17, 3]' => '17, 3'

        var split = text.Split(", ");

        var count = int.Parse(split[0]);
        var channels = int.Parse(split[1]);

        return new KeypointShape(count, channels);
    }
}