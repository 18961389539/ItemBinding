// AnchorFreeBoxDecoder.cs
// 说明：Anchor-free 的边界框解码器实现，从张量中读取边界框坐标（xmin,ymin,xmax,ymax）并构造 RawBoundingBox。
namespace JinlongYolo.YoloSharp.Decoders.Base;

internal class AnchorFreeBoxDecoder(YoloMetadata metadata,
                                            YoloConfiguration configuration,
                                            INonMaxSuppression nonMaxSuppression) : IBoundingBoxDecoder
{
    /// <summary>
    /// 解码方法：从张量中读取每个候选框的信息，过滤低置信度并按需要执行 NMS。
    /// </summary>
    public RawBoundingBox[] Decode(MemoryTensor<float> tensor)
    {
        var strideP = tensor.Strides[metadata.PredictionAxis];
        var strideF = tensor.Strides[metadata.FeatureAxis];

        var boxesCount = tensor.Dimensions[metadata.PredictionAxis];
        var tensorSpan = tensor.Span;
        var limit = metadata.IsEndToEnd
                    ? configuration.MaximumDetections
                    : configuration.MaximumCandidateBoxes;

        PriorityQueue<RawBoundingBox, float>? candidates = null;

        for (var boxIndex = 0; boxIndex < boxesCount; boxIndex++)
        {
            var boxOffset = boxIndex * strideP;

            var confidence = tensorSpan[boxOffset + 4 * strideF];

            // REVIEW-FIX: 改为 !(confidence > threshold)，对 NaN 置信度返回 false 从而被过滤掉
            //（原 confidence <= threshold 对 NaN 恒为 false）。
            if (!(confidence > configuration.Confidence))
            {
                continue;
            }

            var xMin = (int)tensorSpan[boxOffset + 0 * strideF];
            var yMin = (int)tensorSpan[boxOffset + 1 * strideF];
            var xMax = (int)tensorSpan[boxOffset + 2 * strideF];
            var yMax = (int)tensorSpan[boxOffset + 3 * strideF];

            var bounds = new RectangleF(xMin, yMin, xMax - xMin, yMax - yMin);

            if (bounds.Width == 0 || bounds.Height == 0)
            {
                continue;
            }

            var nameIndex = (int)tensorSpan[boxOffset + 5 * strideF];

            RawBoundingBoxOperations.EnqueueTop(ref candidates, new RawBoundingBox
            {
                Index = boxIndex,
                NameIndex = nameIndex,
                Confidence = confidence,
                Bounds = bounds
            }, limit);
        }

        var boxes = RawBoundingBoxOperations.DrainDescending(candidates);

        if (metadata.IsEndToEnd)
        {
            return boxes;
        }

        boxes = nonMaxSuppression.Apply(boxes.AsSpan(), configuration.IoU);

        return RawBoundingBoxOperations.Limit(boxes, configuration.MaximumDetections);
    }
}
