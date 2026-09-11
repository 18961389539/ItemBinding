namespace JinlongYolo.YoloSharp.Metadata;

/// <summary>
/// 模型元数据（从 ONNX 模型的 CustomMetadataMap 解析得到）。
/// Model metadata (parsed from the CustomMetadataMap of the ONNX model).
///
/// 本类封装了从模型文件中读取到的描述性信息（作者、版本、任务类型等）以及
/// 与推理相关的形状信息。它用于在运行时确定解码器行为、输入输出形状以及
/// 是否为端到端模型等信息。
/// This class encapsulates descriptive information read from the model file (author, version, task type, etc.)
/// and shape information related to inference. It is used at runtime to determine decoder behavior,
/// input/output shapes, and whether it is an end-to-end model.
///
/// 主要用途 / Primary uses:
/// - 在创建预测器时解析模型并据此选择合适的解码器与后处理流程 / Parse the model when creating a predictor to select appropriate decoders and post-processing pipelines.
/// - 提供类名映射（Names）以便绘制或文本化输出结果 / Provide class name mappings (Names) for plotting or textual output results.
/// </summary>
public class YoloMetadata
{
    /// <summary>
    /// 获取模型作者（来自 metadata['author']）。
    /// Gets the model author (from metadata['author']).
    /// </summary>
    public string Author { get; }

    /// <summary>
    /// 获取模型描述（来自 metadata['description']）。
    /// Gets the model description (from metadata['description']).
    /// </summary>
    public string Description { get; }

    /// <summary>
    /// 获取模型版本（来自 metadata['version']）。
    /// Gets the model version (from metadata['version']).
    /// </summary>
    public string Version { get; }

    /// <summary>
    /// 获取模型支持的批次大小（来自 metadata['batch']）。
    /// Gets the batch size supported by the model (from metadata['batch']).
    /// </summary>
    public int BatchSize { get; }

    /// <summary>
    /// 获取模型期望的图像输入尺寸（宽、高）。
    /// Gets the expected image input size (width, height) for the model.
    /// </summary>
    public Size ImageSize { get; }

    /// <summary>
    /// 获取模型任务类型（检测、分割、姿态、分类等）。
    /// Gets the model task type (detection, segmentation, pose, classification, etc.).
    /// </summary>
    public YoloTask Task { get; }

    /// <summary>
    /// 获取类别名称映射数组，索引对应类别 id，用于后处理与绘制。
    /// Gets the array of class name mappings, indexed by class id, used for post-processing and plotting.
    /// </summary>
    public YoloName[] Names { get; }

    /// <summary>
    /// 获取模型的体系结构类型（例如 AnchorFree / AnchorBased / Ultralytics / YoloV10）。
    /// Gets the model architecture type (e.g., AnchorFree / AnchorBased / Ultralytics / YoloV10).
    /// </summary>
    public YoloArchitecture Architecture { get; }

    /// <summary>
    /// 获取一个值，该值指示是否为端到端模型（模型直接输出最终检测结果而非需要大量后处理）。
    /// Gets a value indicating whether it is an end-to-end model (model outputs final detection results directly rather than requiring extensive post-processing).
    /// </summary>
    internal bool IsEndToEnd { get; }

    /// <summary>
    /// 获取批次轴索引（通常为 0）。
    /// Gets the batch axis index (usually 0).
    /// </summary>
    internal int BatchAxis { get; }

    /// <summary>
    /// 获取预测轴索引（模型输出中预测相关维度的位置），用于解析输出张量。
    /// Gets the prediction axis index (position of prediction-related dimensions in model output), used for parsing the output tensor.
    /// </summary>
    internal int PredictionAxis { get; }

    /// <summary>
    /// 获取特征轴索引（特征维度在输出张量中的位置）。
    /// Gets the feature axis index (position of the feature dimension in the output tensor).
    /// </summary>
    internal int FeatureAxis { get; }

    /// <summary>
    /// 获取属性偏移量：在输出向量中，类别与框坐标等属性的起始偏移量（用于解析张量）。
    /// Gets the attribute offset: the starting offset of attributes like class and box coordinates in the output vector (used for parsing tensors).
    /// </summary>
    internal int AttributeOffset { get; }

    /// <summary>
    /// 从 ONNX Runtime 的 InferenceSession 构建模型元数据实例。
    /// Builds a model metadata instance from the ONNX Runtime InferenceSession.
    /// </summary>
    /// <param name="session">ONNX 推理会话 / The ONNX inference session.</param>
    internal YoloMetadata(InferenceSession session)
        :
        this(session, ParseYoloArchitecture(session))
    { }

    /// <summary>
    /// 解析模型 CustomMetadataMap 与输出形状以提取必要的元数据和结构信息。
    /// Parses the model CustomMetadataMap and output shapes to extract necessary metadata and structural information.
    /// </summary>
    /// <param name="session">ONNX 推理会话 / The ONNX inference session.</param>
    /// <param name="architecture">YOLO 模型架构类型 / The YOLO model architecture type.</param>
    internal YoloMetadata(InferenceSession session, YoloArchitecture architecture)
    {
        var metadata = session.ModelMetadata.CustomMetadataMap;
        var output0 = session.OutputMetadata[session.OutputNames[0]];
        var output0Dimensions = output0.Dimensions;

        // 从模型元数据中读取常见信息（若缺失会抛出）
        Author = metadata["author"];
        Description = metadata["description"];
        Version = metadata["version"];

        Task = metadata["task"] switch
        {
            "obb" => YoloTask.Obb,
            "pose" => YoloTask.Pose,
            "detect" => YoloTask.Detect,
            "segment" => YoloTask.Segment,
            "classify" => YoloTask.Classify,
            _ => throw new InvalidOperationException("Unknow YOLO 'task' value")
        };

        // 基于 metadata 字段解析批次大小、输入图像尺寸与类别映射
        BatchSize = int.Parse(metadata["batch"]);
        ImageSize = ParseSize(metadata["imgsz"]);
        Names = ParseNames(metadata["names"]);

        // 尝试读取 end2end 标识；若未显式声明则根据输出形状做隐式判断
        if (metadata.TryGetValue("end2end", out var value))
        {
            IsEndToEnd = value.Equals("true", StringComparison.CurrentCultureIgnoreCase);
        }

        if (!IsEndToEnd && IsImplicitEndToEnd(Task, output0Dimensions, session.OutputMetadata.Count))
        {
            IsEndToEnd = true;
        }

        Architecture = DetectArchitecture(Names.Length, output0Dimensions, Task);

        BatchAxis = 0;

        if (Architecture == YoloArchitecture.AnchorFree)
        {
            PredictionAxis = 1;
            FeatureAxis = 2;
            AttributeOffset = 6;
        }
        else
        {
            PredictionAxis = 2;
            FeatureAxis = 1;
            AttributeOffset = 4 + Names.Length;
        }
    }

    public static YoloMetadata Parse(InferenceSession session)
    {
        try
        {
            if (session.ModelMetadata.CustomMetadataMap["task"] == "pose")
            {
                return new YoloPoseMetadata(session);
            }

            return new YoloMetadata(session);
        }
        catch (Exception inner)
        {
            throw new InvalidOperationException("The metadata parsing failed, making sure you use an official YOLO model", inner);
        }
    }

    private static YoloArchitecture DetectArchitecture(int outputCount, int[] shape, YoloTask task)
    {
        if (shape.Length != 3)
        {
            return YoloArchitecture.Unknown;
        }

        if (task == YoloTask.Detect && shape[2] == 6)
        {
            return YoloArchitecture.AnchorFree;
        }

        if (task == YoloTask.Obb && shape[2] == 7)
        {
            return YoloArchitecture.AnchorFree;
        }

        if (shape[1] == 300)
        {
            return YoloArchitecture.AnchorFree;
        }

        return YoloArchitecture.AnchorBased;
    }

    private static YoloArchitecture ParseYoloArchitecture(InferenceSession session)
    {
        var metadata = session.ModelMetadata.CustomMetadataMap;

        if (metadata.TryGetValue("task", out var task) == false)
        {
            throw new InvalidOperationException();
        }

        if (task == "detect")
        {
            var output0 = session.OutputMetadata["output0"];

            if (output0.Dimensions[2] == 6) // YOLOv10 output0: [1, 300, 6]
            {
                return YoloArchitecture.YoloV10;
            }
        }

        return YoloArchitecture.Ultralytics;
    }

    private static bool IsImplicitEndToEnd(YoloTask task, int[] shape, int outputCount)
    {
        if (outputCount != 1 || shape.Length != 3)
        {
            return false;
        }

        return task switch
        {
            YoloTask.Detect => shape[2] == 6,
            YoloTask.Obb => shape[2] == 7,
            _ => false
        };
    }

    #region Parsers

    private static Size ParseSize(string text)
    {
        // 期望格式为类似 "[640, 640]"。若格式不符合会抛出异常。
        text = text[1..^1]; // '[640, 640]' => '640, 640'

        var split = text.Split(", ");

        var y = int.Parse(split[0]);
        var x = int.Parse(split[1]);

        return new Size(x, y);
    }

    private static YoloName[] ParseNames(string text)
    {
        text = text[1..^1];

        var split = text.Split(", ");
        var count = split.Length;

        var names = new YoloName[count];

        for (int i = 0; i < count; i++)
        {
            var value = split[i];

            var valueSplit = value.Split(": ");

            var id = int.Parse(valueSplit[0]);
            var name = valueSplit[1][1..^1].Replace('_', ' ');

            // REVIEW-FIX: 校验 id 在 [0, Names.Length) 范围内再赋值，越界的 id 跳过，
            // 避免直接下标赋值导致 IndexOutOfRangeException。
            if (id < 0 || id >= names.Length)
            {
                continue;
            }

            names[id] = new YoloName(id, name);
        }

        return names;
    }

    #endregion
}