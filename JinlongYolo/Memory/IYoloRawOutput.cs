namespace JinlongYolo.YoloSharp.Memory;

/// <summary>
/// 封装模型推理的原始输出张量。
/// Encapsulates the raw output tensors from model inference.
/// </summary>
internal interface IYoloRawOutput : IDisposable
{
    /// <summary>
    /// 模型的主要输出张量（通常包含检测框、置信度和类别信息）。
    /// The primary output tensor of the model (typically containing bounding boxes, confidences, and classes).
    /// </summary>
    public MemoryTensor<float> Output0 { get; }

    /// <summary>
    /// 模型的次要输出张量（对于需要多个输出的模型，如分割掩码或某些特定架构）。
    /// The secondary output tensor of the model (for models requiring multiple outputs, like segmentation masks).
    /// </summary>
    public MemoryTensor<float>? Output1 { get; }
}