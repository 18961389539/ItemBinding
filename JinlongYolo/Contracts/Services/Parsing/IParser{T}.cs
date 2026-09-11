/// <summary>
/// 通用的解码器接口，用于将原始模型输出解析为具体的预测对象数组。
/// Generic decoder interface used to parse raw model output into an array of specific prediction objects.
/// </summary>
/// <typeparam name="T">预测对象的类型，必须实现 <see cref="IYoloPrediction{T}"/> / The type of the prediction object, must implement <see cref="IYoloPrediction{T}"/>.</typeparam>
internal interface IDecoder<T> where T : IYoloPrediction<T>
{
    /// <summary>
    /// 将原始模型输出解析为预测结果数组。
    /// Decodes the raw model output into an array of prediction results.
    /// </summary>
    /// <param name="output">模型原始输出封装 / The raw model output encapsulation.</param>
    /// <param name="size">原始输入图像的尺寸，用于将坐标映射回原始图像空间 / The size of the original input image, used to map coordinates back to the original image space.</param>
    /// <returns>解码后的预测结果数组 / An array of decoded prediction results.</returns>
    public T[] Decode(IYoloRawOutput output, Size size);
}
