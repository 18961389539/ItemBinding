namespace JinlongYolo.YoloSharp.Services;

internal class PixelsNormalizer : IPixelsNormalizer
{
    private const float PixelScale = 1f / 255f;

    public void NormalizerPixelsToTensor(Image<Rgb24> image, MemoryTensor<float> tensor, Vector<int> padding)
    {
        // Verify tensor dimensions
        if (image.Height + (padding.Y * 2) != tensor.Dimensions[2] || image.Width + (padding.X * 2) != tensor.Dimensions[3])
        {
            throw new InvalidOperationException("The image size and target tensor dimensions is not match");
        }

        ClearPadding(tensor, image.Size, padding);

        // Process core
        ProcessToTensorCore(image, tensor, padding);
    }

    public void ResizeAndNormalizeToTensor(Image<Rgb24> image, MemoryTensor<float> tensor, Size targetSize, bool keepAspectRatio, out Vector<int> padding)
    {
        if (tensor.Dimensions[2] != targetSize.Height || tensor.Dimensions[3] != targetSize.Width)
        {
            throw new InvalidOperationException("The target size and tensor dimensions is not match");
        }

        var outputWidth = targetSize.Width;
        var outputHeight = targetSize.Height;

        if (image.Width == outputWidth && image.Height == outputHeight)
        {
            padding = default;
            NormalizerPixelsToTensor(image, tensor, padding);
            return;
        }

        ComputeResizeLayout(image.Size, targetSize, keepAspectRatio, out var resizedWidth, out var resizedHeight, out padding);

        var tensorSpan = tensor.Span;
        var strideY = tensor.Strides[2];
        var strideX = tensor.Strides[3];
        var greenOffset = tensor.Strides[1];
        var blueOffset = tensor.Strides[1] * 2;
        var baseOffset = padding.Y * strideY + padding.X * strideX;
        var xLookupBuffer = ArrayPool<int>.Shared.Rent(resizedWidth);
        var yLookupBuffer = ArrayPool<int>.Shared.Rent(resizedHeight);

        try
        {
            var xLookup = xLookupBuffer.AsSpan(0, resizedWidth);
            var yLookup = yLookupBuffer.AsSpan(0, resizedHeight);

            FillLookup(xLookup, image.Width, resizedWidth);
            FillLookup(yLookup, image.Height, resizedHeight);

            if (padding.X != 0 || padding.Y != 0 || resizedWidth != outputWidth || resizedHeight != outputHeight)
            {
                tensorSpan.Clear();
            }

            if (image.DangerousTryGetSinglePixelMemory(out var memory))
            {
                WriteResizedPixels(memory.Span, image.Width, yLookup, xLookup, tensorSpan, baseOffset, strideY, strideX, greenOffset, blueOffset);
                return;
            }

            WriteResizedPixels(image, yLookup, xLookup, tensorSpan, baseOffset, strideY, strideX, greenOffset, blueOffset);
        }
        finally
        {
            ArrayPool<int>.Shared.Return(xLookupBuffer);
            ArrayPool<int>.Shared.Return(yLookupBuffer);
        }
    }

    public void ResizeAndNormalizeDetectionToTensor(Image<Rgb24> image, MemoryTensor<float> tensor, Size targetSize, bool keepAspectRatio, out Vector<int> padding)
    {
        if (tensor.Dimensions[2] != targetSize.Height || tensor.Dimensions[3] != targetSize.Width)
        {
            throw new InvalidOperationException("The target size and tensor dimensions is not match");
        }

        if (image.Width == targetSize.Width && image.Height == targetSize.Height)
        {
            padding = default;
            NormalizerPixelsToTensor(image, tensor, padding);
            return;
        }

        ComputeResizeLayout(image.Size, targetSize, keepAspectRatio, out var resizedWidth, out var resizedHeight, out padding);

        using var resized = image.Clone(context => context.Resize(resizedWidth, resizedHeight));
        NormalizerPixelsToTensor(resized, tensor, padding);
    }

    internal static void ComputeResizeLayout(Size sourceSize, Size targetSize, bool keepAspectRatio, out int resizedWidth, out int resizedHeight, out Vector<int> padding)
    {
        if (!keepAspectRatio)
        {
            resizedWidth = targetSize.Width;
            resizedHeight = targetSize.Height;
            padding = default;
            return;
        }

        var scale = Math.Min(targetSize.Width / (float)sourceSize.Width,
                             targetSize.Height / (float)sourceSize.Height);

        resizedWidth = Math.Clamp((int)(sourceSize.Width * scale), 1, targetSize.Width);
        resizedHeight = Math.Clamp((int)(sourceSize.Height * scale), 1, targetSize.Height);
        padding = ((targetSize.Width - resizedWidth) / 2, (targetSize.Height - resizedHeight) / 2);
    }

    private static void ClearPadding(MemoryTensor<float> tensor, Size imageSize, Vector<int> padding)
    {
        var tensorSpan = tensor.Span;

        if (tensor.Dimensions[0] != 1)
        {
            tensorSpan.Clear();
            return;
        }

        if (padding.X == 0 && padding.Y == 0)
        {
            return;
        }

        var totalWidth = tensor.Dimensions[3];
        var totalHeight = tensor.Dimensions[2];
        var strideChannel = tensor.Strides[1];
        var strideY = tensor.Strides[2];
        var paddedWidth = totalWidth - imageSize.Width - padding.X;
        var paddedBottom = totalHeight - imageSize.Height - padding.Y;

        for (var channel = 0; channel < 3; channel++)
        {
            var channelOffset = channel * strideChannel;

            if (padding.Y > 0)
            {
                tensorSpan.Slice(channelOffset, padding.Y * strideY).Clear();
            }

            if (padding.X > 0 || paddedWidth > 0)
            {
                var contentOffset = channelOffset + (padding.Y * strideY);

                for (var y = 0; y < imageSize.Height; y++)
                {
                    var rowOffset = contentOffset + (y * strideY);

                    if (padding.X > 0)
                    {
                        tensorSpan.Slice(rowOffset, padding.X).Clear();
                    }

                    if (paddedWidth > 0)
                    {
                        tensorSpan.Slice(rowOffset + padding.X + imageSize.Width, paddedWidth).Clear();
                    }
                }
            }

            if (paddedBottom > 0)
            {
                var bottomOffset = channelOffset + ((padding.Y + imageSize.Height) * strideY);
                tensorSpan.Slice(bottomOffset, paddedBottom * strideY).Clear();
            }
        }
    }

    private static void ProcessToTensorCore(Image<Rgb24> image, MemoryTensor<float> tensor, Vector<int> padding)
    {
        var width = image.Width;
        var height = image.Height;

        // Pre-calculate strides for performance
        var strideY = tensor.Strides[2];
        var strideX = tensor.Strides[3];
        var greenOffset = tensor.Strides[1];
        var blueOffset = tensor.Strides[1] * 2;
        var baseOffset = padding.Y * strideY + padding.X * strideX;

        // Get a span of the whole tensor for fast access
        var tensorSpan = tensor.Span;

        // Try get continuous memory block of the entire image data
        if (image.DangerousTryGetSinglePixelMemory(out var memory))
        {
            var pixels = memory.Span;

            for (var y = 0; y < height; y++)
            {
                var sourceOffset = y * width;
                var tensorIndex = baseOffset + y * strideY;

                for (var x = 0; x < width; x++, tensorIndex += strideX)
                {
                    WritePixel(tensorSpan, tensorIndex, pixels[sourceOffset + x], greenOffset, blueOffset);
                }
            }
        }
        else
        {
            for (var y = 0; y < height; y++)
            {
                var rowSpan = image.DangerousGetPixelRowMemory(y).Span;
                var tensorIndex = baseOffset + y * strideY;

                for (var x = 0; x < width; x++, tensorIndex += strideX)
                {
                    WritePixel(tensorSpan, tensorIndex, rowSpan[x], greenOffset, blueOffset);
                }
            }
        }
    }

    private static void FillLookup(Span<int> lookup, int sourceLength, int targetLength)
    {
        for (var index = 0; index < lookup.Length; index++)
        {
            lookup[index] = Math.Min((index * sourceLength) / targetLength, sourceLength - 1);
        }
    }

    private static void WriteResizedPixels(ReadOnlySpan<Rgb24> pixels,
                                           int sourceWidth,
                                           ReadOnlySpan<int> yLookup,
                                           ReadOnlySpan<int> xLookup,
                                           Span<float> target,
                                           int baseOffset,
                                           int strideY,
                                           int strideX,
                                           int greenOffset,
                                           int blueOffset)
    {
        for (var y = 0; y < yLookup.Length; y++)
        {
            var sourceRowOffset = yLookup[y] * sourceWidth;
            var targetIndex = baseOffset + (y * strideY);

            for (var x = 0; x < xLookup.Length; x++, targetIndex += strideX)
            {
                WritePixel(target, targetIndex, pixels[sourceRowOffset + xLookup[x]], greenOffset, blueOffset);
            }
        }
    }

    private static void WriteResizedPixels(Image<Rgb24> image,
                                           ReadOnlySpan<int> yLookup,
                                           ReadOnlySpan<int> xLookup,
                                           Span<float> target,
                                           int baseOffset,
                                           int strideY,
                                           int strideX,
                                           int greenOffset,
                                           int blueOffset)
    {
        for (var y = 0; y < yLookup.Length; y++)
        {
            var rowSpan = image.DangerousGetPixelRowMemory(yLookup[y]).Span;
            var targetIndex = baseOffset + (y * strideY);

            for (var x = 0; x < xLookup.Length; x++, targetIndex += strideX)
            {
                WritePixel(target, targetIndex, rowSpan[xLookup[x]], greenOffset, blueOffset);
            }
        }
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static void WritePixel(Span<float> target, int index, Rgb24 pixel, int greenOffset, int blueOffset)
    {
        target[index] = pixel.R * PixelScale;
        target[index + greenOffset] = pixel.G * PixelScale;
        target[index + blueOffset] = pixel.B * PixelScale;
    }
}