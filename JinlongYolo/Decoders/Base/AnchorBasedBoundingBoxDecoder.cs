// AnchorBasedBoundingBoxDecoder.cs
// 说明：基于 anchor 的边界框解码器基类，实现了从张量中提取候选框、置信度过滤和 NMS 调用的通用流程。
namespace JinlongYolo.YoloSharp.Decoders.Base;

internal class AnchorBasedBoundingBoxDecoder(YoloMetadata metadata,
                                            YoloConfiguration configuration,
                                            INonMaxSuppression nonMaxSuppression) : IBoundingBoxDecoder
{
    /// <summary>
    /// 从给定张量中解码出原始边界框数组，包含置信度阈值过滤和非极大值抑制（NMS）。
    /// </summary>
    /// <param name="tensor">模型输出张量封装。</param>
    /// <returns>解码并经过 NMS/过滤后的原始边界框数组。</returns>
    /// <remarks>
    /// 调整 <see cref="YoloConfiguration"/> 中的 Confidence/IoU/MaximumCandidateBoxes/MaximumDetections
    /// 会直接影响候选框数量、NMS 行为以及最终返回的检测数量。
    /// </remarks>
    public RawBoundingBox[] Decode(MemoryTensor<float> tensor)
    {
        var boxStride = tensor.Strides[1];
        var boxesCount = tensor.Dimensions[2];
        var namesCount = metadata.Names.Length;
        var tensorSpan = tensor.Buffer.Span;
        var limit = metadata.IsEndToEnd
                    ? configuration.MaximumDetections
                    : configuration.MaximumCandidateBoxes;

        PriorityQueue<RawBoundingBox, float>? candidates = null;

        for (var boxIndex = 0; boxIndex < boxesCount; boxIndex++)
        {
            for (var nameIndex = 0; nameIndex < namesCount; nameIndex++)
            {
                var confidence = tensorSpan[(nameIndex + 4) * boxStride + boxIndex];

                // REVIEW-FIX: 与 AnchorFreeBoxDecoder 保持一致的 NaN 防护。`confidence <= threshold`
                // 对 NaN 恒为 false，NaN 置信度框会绕过过滤进入候选队列与 NMS 产生垃圾假阳性。
                if (!(confidence > configuration.Confidence))
                {
                    continue;
                }

                DecodeBox(tensorSpan, boxStride, boxIndex, out var bounds, out var angle);

                if (bounds.Width == 0 || bounds.Height == 0)
                {
                    continue;
                }

                RawBoundingBoxOperations.EnqueueTop(ref candidates, new RawBoundingBox
                {
                    Index = boxIndex,
                    NameIndex = nameIndex,
                    Confidence = confidence,
                    Bounds = bounds,
                    Angle = angle
                }, limit);
            }
        }

        var boxes = RawBoundingBoxOperations.DrainDescending(candidates);

        if (metadata.IsEndToEnd)
        {
            return boxes;
        }

        boxes = nonMaxSuppression.Apply(boxes.AsSpan(), configuration.IoU);

        return RawBoundingBoxOperations.Limit(boxes, configuration.MaximumDetections);
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    protected virtual void DecodeBox(Span<float> tensor, int boxStride, int boxIndex, out RectangleF bounds, out float angle)
    {
        var x = tensor[0 + boxIndex];
        var y = tensor[1 * boxStride + boxIndex];
        var w = tensor[2 * boxStride + boxIndex];
        var h = tensor[3 * boxStride + boxIndex];

        bounds = new RectangleF(x - w / 2, y - h / 2, w, h);

        angle = float.NegativeZero;
    }
}
