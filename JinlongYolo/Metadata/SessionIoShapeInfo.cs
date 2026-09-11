namespace JinlongYolo.YoloSharp.Metadata;

/// <summary>
/// 封装推理会话的输入和输出形状信息。
/// Encapsulates the input and output shape information of an inference session.
/// </summary>
internal class SessionIoShapeInfo
{
    /// <summary>
    /// 模型的主要输入形状。
    /// The primary input shape of the model.
    /// </summary>
    public TensorShape Input0 { get; }

    /// <summary>
    /// 模型的主要输出形状。
    /// The primary output shape of the model.
    /// </summary>
    public TensorShape Output0 { get; }

    /// <summary>
    /// 模型的次要输出形状（如果存在）。
    /// The secondary output shape of the model (if present).
    /// </summary>
    public TensorShape? Output1 { get; }

    /// <summary>
    /// 指示模型的输出形状是否包含动态维度。
    /// Indicates whether the model's output shapes contain dynamic dimensions.
    /// </summary>
    public bool IsDynamicOutput
    {
        get
        {
            if (Output0.IsDynamic)
            {
                return true;
            }

            if (Output1 != null && Output1.Value.IsDynamic)
            {
                return true;
            }

            return false;
        }
    }

    /// <summary>
    /// 使用推理会话和模型元数据初始化 <see cref="SessionIoShapeInfo"/> 类的新实例。
    /// Initializes a new instance of the <see cref="SessionIoShapeInfo"/> class using an inference session and model metadata.
    /// </summary>
    /// <param name="session">ONNX 推理会话 / The ONNX inference session.</param>
    /// <param name="metadata">YOLO 模型元数据 / The YOLO model metadata.</param>
    public SessionIoShapeInfo(InferenceSession session, YoloMetadata metadata)
    {
        var inputMetadata = session.InputMetadata.Values;
        var outputMetadata = session.OutputMetadata.Values;

        var input0 = new TensorShape(inputMetadata.First().Dimensions);

        if (input0.IsDynamic)
        {
            Input0 = new TensorShape([metadata.BatchSize, 3, metadata.ImageSize.Height, metadata.ImageSize.Width]);
        }
        else
        {
            Input0 = input0;
        }

        Output0 = new TensorShape(outputMetadata.First().Dimensions);

        if (session.OutputMetadata.Count == 2)
        {
            Output1 = new TensorShape(outputMetadata.Last().Dimensions);
        }
    }
}