/// <summary>
/// 分割解码器：基于边界框解码器提取候选框并将其映射回原图坐标。
///
/// 说明（中文）：
/// 用于将模型原始输出转换为 <see cref="Segmentation"/> 类型结果，并负责坐标变换。
/// </summary>
namespace JinlongYolo.YoloSharp.Decoders;

internal class SegmentationDecoder(YoloMetadata metadata,
                                   IBoundingBoxDecoder boxDecoder,
                                   IBoundingBoxTransformer transformer,
                                   IMemoryAllocator memoryAllocator) : IDecoder<Segmentation>
{
    public Segmentation[] Decode(IYoloRawOutput output, Size size)
    {
        var transform = transformer.Compute(size);

        var output0 = output.Output0;
        var output1 = output.Output1
                      ??
                      throw new InvalidOperationException();

        var maskWidth = output1.Dimensions[3];
        var maskHeight = output1.Dimensions[2];
        var maskChannelCount = output1.Dimensions[1];

        var maskPaddingX = transform.Padding.X * maskWidth / metadata.ImageSize.Width;
        var maskPaddingY = transform.Padding.Y * maskHeight / metadata.ImageSize.Height;

        // REVIEW-FIX: 减去填充后可能为负，用 Math.Max(1, ...) 钳制保证有效尺寸。
        maskWidth = Math.Max(1, maskWidth - (maskPaddingX * 2));
        maskHeight = Math.Max(1, maskHeight - (maskPaddingY * 2));

        using var rawMaskBuffer = memoryAllocator.Allocate<float>(maskWidth * maskHeight);
        using var weightsBuffer = memoryAllocator.Allocate<float>(maskChannelCount);

        var weightsSpan = weightsBuffer.Memory.Span;
        var mask = new BitmapBuffer(rawMaskBuffer.Memory, maskWidth, maskHeight);
        var maskSpan = rawMaskBuffer.Memory.Span;

        var offsetToWeights = metadata.AttributeOffset;

        var strideF = output0.Strides[metadata.FeatureAxis];
        var strideP = output0.Strides[metadata.PredictionAxis];

        var output0Span = output0.Span;
        var output1Span = output1.Span;
        var prototypeStrideC = output1.Strides[1];
        var prototypeStrideY = output1.Strides[2];
        var prototypeStrideX = output1.Strides[3];

        var boxes = boxDecoder.Decode(output0);

        var result = new Segmentation[boxes.Length];

        for (var index = 0; index < boxes.Length; index++)
        {
            var box = boxes[index];
            var boxIndex = box.Index;

            var bounds = transformer.Apply(box.Bounds, transform);

            // Collect the weights for this box
            for (var i = 0; i < maskChannelCount; i++)
            {
                weightsSpan[i] = output0Span[boxIndex * strideP + (offsetToWeights + i) * strideF];
            }

            BuildMask(maskSpan,
                      output1Span,
                      weightsSpan,
                      maskWidth,
                      maskHeight,
                      maskPaddingX,
                      maskPaddingY,
                      prototypeStrideC,
                      prototypeStrideY,
                      prototypeStrideX);

            // L501: 通过 IMemoryAllocator 池化分配 resizedMask 底层 float[]，避免每帧每框直接 new float[] 进 LOH。
            // Pool the resizedMask backing float[] via IMemoryAllocator to avoid per-frame per-box LOH allocations.
            var resizedMaskOwner = memoryAllocator.Allocate<float>(bounds.Width * bounds.Height);
            var resizedMask = new BitmapBuffer(resizedMaskOwner, bounds.Width, bounds.Height);

            ResizeToTarget(maskSpan, mask.Width, mask.Height, resizedMask, bounds.Location, size);

            result[index] = new Segmentation
            {
                Mask = resizedMask,
                Name = metadata.Names[box.NameIndex],
                Bounds = bounds,
                Confidence = box.Confidence,
            };
        }

        return result;
    }

    private static void BuildMask(Span<float> target,
                                  ReadOnlySpan<float> source,
                                  ReadOnlySpan<float> weights,
                                  int maskWidth,
                                  int maskHeight,
                                  int maskPaddingX,
                                  int maskPaddingY,
                                  int prototypeStrideC,
                                  int prototypeStrideY,
                                  int prototypeStrideX)
    {
        target.Clear();

        for (var channel = 0; channel < weights.Length; channel++)
        {
            var weight = weights[channel];

            if (weight == 0f)
            {
                continue;
            }

            var channelOffset = (channel * prototypeStrideC)
                                + (maskPaddingY * prototypeStrideY)
                                + (maskPaddingX * prototypeStrideX);

            for (var y = 0; y < maskHeight; y++)
            {
                var sourceOffset = channelOffset + (y * prototypeStrideY);
                var targetOffset = y * maskWidth;

                if (prototypeStrideX == 1)
                {
                    MultiplyAdd(target.Slice(targetOffset, maskWidth), source.Slice(sourceOffset, maskWidth), weight);
                    continue;
                }

                for (var x = 0; x < maskWidth; x++)
                {
                    target[targetOffset + x] += source[sourceOffset + (x * prototypeStrideX)] * weight;
                }
            }
        }

        for (var index = 0; index < target.Length; index++)
        {
            target[index] = Sigmoid(target[index]);
        }
    }

    private static void MultiplyAdd(Span<float> target, ReadOnlySpan<float> source, float weight)
    {
        var vectorLength = System.Numerics.Vector<float>.Count;
        var index = 0;

        if (vectorLength > 1)
        {
            var weightVector = new System.Numerics.Vector<float>(weight);

            for (; index <= source.Length - vectorLength; index += vectorLength)
            {
                var targetVector = new System.Numerics.Vector<float>(target.Slice(index, vectorLength));
                var sourceVector = new System.Numerics.Vector<float>(source.Slice(index, vectorLength));

                (targetVector + (sourceVector * weightVector)).CopyTo(target.Slice(index, vectorLength));
            }
        }

        for (; index < source.Length; index++)
        {
            target[index] += source[index] * weight;
        }
    }

    private static void ResizeToTarget(ReadOnlySpan<float> source, int sourceWidth, int sourceHeight, BitmapBuffer target, Point position, Size size)
    {
        var targetSpan = target.Span;
        var sourceScaleX = size.Width > 1 ? (sourceWidth - 1f) / (size.Width - 1f) : 0f;
        var sourceScaleY = size.Height > 1 ? (sourceHeight - 1f) / (size.Height - 1f) : 0f;

        for (var y = 0; y < target.Height; y++)
        {
            var targetRowOffset = y * target.Width;

            for (var x = 0; x < target.Width; x++)
            {
                // Calculate source coordinates
                var sourceX = (x + position.X) * sourceScaleX;
                var sourceY = (y + position.Y) * sourceScaleY;

                // Check if source coordinates are out of bounds
                if (sourceY < 0 || sourceY >= sourceHeight ||
                    sourceX < 0 || sourceX >= sourceWidth)
                {
                    targetSpan[targetRowOffset + x] = 0f;
                    continue;
                }

                // Ensure coordinates are within valid range for interpolation
                var x0 = Math.Max(0, Math.Min((int)sourceX, Math.Max(sourceWidth - 2, 0)));
                var y0 = Math.Max(0, Math.Min((int)sourceY, Math.Max(sourceHeight - 2, 0)));

                var x1 = sourceWidth > 1 ? x0 + 1 : x0;
                var y1 = sourceHeight > 1 ? y0 + 1 : y0;
                var topOffset = (y0 * sourceWidth) + x0;
                var bottomOffset = (y1 * sourceWidth) + x0;

                // Calculate interpolation factors
                var xLerp = sourceX - x0;
                var yLerp = sourceY - y0;

                // Perform bilinear interpolation
                var top = Lerp(source[topOffset], source[(y0 * sourceWidth) + x1], xLerp);
                var bottom = Lerp(source[bottomOffset], source[(y1 * sourceWidth) + x1], xLerp);

                targetSpan[targetRowOffset + x] = Lerp(top, bottom, yLerp);
            }
        }
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static float Lerp(float a, float b, float t) => a + (b - a) * t;

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static float Sigmoid(float value) => 1 / (1 + MathF.Exp(-value));
}