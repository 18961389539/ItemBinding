namespace JinlongYolo.YoloSharp.Decoders;

internal class ObbDetectionDecoder(YoloMetadata metadata,
                                   IBoundingBoxDecoder boxDecoder,
                                   IBoundingBoxTransformer transformer) : IDecoder<ObbDetection>
{
    /// <summary>
    /// 解码 OBB（有向边界框）检测结果并将坐标映射到目标图像尺寸。
    /// </summary>
    /// <param name="output">模型的原始输出封装。</param>
    /// <param name="size">目标图像尺寸。</param>
    /// <returns>返回解码后的 OBB 检测结果数组。</returns>
    public ObbDetection[] Decode(IYoloRawOutput output, Size size)
    {
        var boxes = boxDecoder.Decode(output.Output0);

        var transform = transformer.Compute(size);

        var result = new ObbDetection[boxes.Length];

        for (var i = 0; i < boxes.Length; i++)
        {
            var box = boxes[i];

            result[i] = new ObbDetection
            {
                Name = metadata.Names[box.NameIndex],
                Angle = box.Angle,
                Bounds = transformer.Apply(box.Bounds, transform),
                Confidence = box.Confidence,
            };
        }

        return result;
    }
}
