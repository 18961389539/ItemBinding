namespace JinlongYolo.YoloSharp.Contracts.Services;

/// <summary>
/// 定义边界框解码器接口，用于将模型输出的张量（tensor）解码为原始的边界框数组（RawBoundingBox）。
/// Defines the bounding box decoder interface used to decode the model output tensor into an array of raw bounding boxes.
/// </summary>
internal interface IBoundingBoxDecoder
{
    /// <summary>
    /// 将模型输出的张量解码为原始边界框数组。
    /// Decodes the model output tensor into an array of raw bounding boxes.
    /// </summary>
    /// <param name="tensor">表示模型输出的多维张量 / The multi-dimensional tensor representing the model output.</param>
    /// <returns>解码得到的 RawBoundingBox 数组，包含边界框的位置、宽高、置信度和类别等原始信息 / The decoded array of RawBoundingBox, containing raw info like position, size, confidence, and class.</returns>
    public RawBoundingBox[] Decode(MemoryTensor<float> tensor);
}