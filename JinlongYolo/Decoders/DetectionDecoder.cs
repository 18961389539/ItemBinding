// DetectionDecoder.cs
// 说明：将原始模型输出通过边界框解码器转换为 Detection 类型的预测结果，
//      并使用 BoundingBoxTransformer 将边界框从网络输入坐标系映射回原始图像坐标系。
namespace JinlongYolo.YoloSharp.Decoders;

internal class DetectionDecoder(YoloMetadata metadata,
                                IBoundingBoxDecoder boxDecoder,
                                IBoundingBoxTransformer transformer) : IDecoder<Detection>
{
    /// <summary>
    /// 解码原始输出为 <see cref="Detection"/> 数组并将边界框映射回原始图像坐标系。
    /// </summary>
    /// <param name="output">模型的原始输出封装。</param>
    /// <param name="size">原始图像的尺寸（用于坐标映射）。</param>
    /// <returns>返回解码并映射后的检测结果数组。</returns>
    public Detection[] Decode(IYoloRawOutput output, Size size)
    {
        var boxes = boxDecoder.Decode(output.Output0);

        var transform = transformer.Compute(size);

        var result = new Detection[boxes.Length];

        for (var i = 0; i < boxes.Length; i++)
        {
            var box = boxes[i];

            result[i] = new Detection
            {
                Name = metadata.Names[box.NameIndex],
                Bounds = transformer.Apply(box.Bounds, transform),
                Confidence = box.Confidence,
            };
        }

        return result;
    }
}
