// AnchorFreeOrientedBoxDecoder.cs
// 说明：Anchor-free 的有向边界框解码器，从张量中读取 (x,y,w,h,score,class,angle) 等信息并标准化角度。
namespace JinlongYolo.YoloSharp.Decoders.Base;

internal class AnchorFreeOrientedBoxDecoder(YoloMetadata metadata,
                                            YoloConfiguration configuration,
                                            INonMaxSuppression nonMaxSuppression) : IBoundingBoxDecoder
{
    public RawBoundingBox[] Decode(MemoryTensor<float> tensor)
    {
        var strideP = tensor.Strides[metadata.PredictionAxis];
        var strideF = tensor.Strides[metadata.FeatureAxis];

        var predictionCount = tensor.Dimensions[metadata.PredictionAxis];
        var outputSpan = tensor.Span;
        var limit = metadata.IsEndToEnd
                    ? configuration.MaximumDetections
                    : configuration.MaximumCandidateBoxes;

        PriorityQueue<RawBoundingBox, float>? candidates = null;

        for (var boxIndex = 0; boxIndex < predictionCount; boxIndex++)
        {
            var boxOffset = boxIndex * strideP;

            var confidence = outputSpan[boxOffset + 4 * strideF];

            // REVIEW-FIX: 改为 !(confidence > threshold)，对 NaN 置信度返回 false 从而被过滤掉
            //（原 confidence <= threshold 对 NaN 恒为 false）。
            if (!(confidence > configuration.Confidence))
            {
                continue;
            }

            var x = outputSpan[boxOffset + 0 * strideF];
            var y = outputSpan[boxOffset + 1 * strideF];
            var w = outputSpan[boxOffset + 2 * strideF];
            var h = outputSpan[boxOffset + 3 * strideF];

            var bounds = new RectangleF(x, y, w, h);

            if (bounds.Width == 0 || bounds.Height == 0)
            {
                continue;
            }

            var angle = NormalizeAngle(outputSpan[boxOffset + 6 * strideF]);

            var classIndex = (int)outputSpan[boxOffset + 5 * strideF];

            RawBoundingBoxOperations.EnqueueTop(ref candidates, new RawBoundingBox
            {
                Index = boxIndex,
                NameIndex = classIndex,
                Confidence = confidence,
                Bounds = bounds,
                Angle = angle
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

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static float NormalizeAngle(float angle)
    {
        // 输入范围 [-π/4, 3π/4)，归一化到 [-π/2, π/2)：
        // - [-π/4, π/2) 已在目标范围内，保持不变
        // - [π/2, 3π/4) 减去 π 映射到 [-π/2, -π/4)
        if (angle >= MathF.PI / 2 && angle < 0.75f * MathF.PI)
        {
            angle -= MathF.PI;
        }

        // Degrees
        return angle * 180f / MathF.PI;
    }
}
