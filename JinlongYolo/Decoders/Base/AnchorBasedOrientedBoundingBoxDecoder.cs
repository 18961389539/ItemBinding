// AnchorBasedOrientedBoundingBoxDecoder.cs
// 说明：基于 anchor 的有方向边界框解码器，扩展自 AnchorBasedBoundingBoxDecoder，
//      额外解析角度信息并将其以度为单位返回。
namespace JinlongYolo.YoloSharp.Decoders.Base;

internal class AnchorBasedOrientedBoundingBoxDecoder(YoloMetadata metadata,
                                                     YoloConfiguration configuration,
                                                     INonMaxSuppression nonMaxSuppression)
    : AnchorBasedBoundingBoxDecoder(metadata,
                                   configuration,
                                   nonMaxSuppression)
{
    private readonly int _namesCount = metadata.Names.Length;

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    protected override void DecodeBox(Span<float> tensor, int boxStride, int boxIndex, out RectangleF bounds, out float angle)
    {
        var x = tensor[boxIndex];
        var y = tensor[1 * boxStride + boxIndex];
        var w = tensor[2 * boxStride + boxIndex];
        var h = tensor[3 * boxStride + boxIndex];

        bounds = new RectangleF(x, y, w, h);

        // Radians
        angle = tensor[(4 + _namesCount) * boxStride + boxIndex];

        // 输入范围 [-π/4, 3π/4)，归一化到 [-π/2, π/2)：
        // - [-π/4, π/2) 已在目标范围内，保持不变
        // - [π/2, 3π/4) 减去 π 映射到 [-π/2, -π/4)
        if (angle >= MathF.PI / 2 && angle < 0.75f * MathF.PI)
        {
            angle -= MathF.PI;
        }

        // Degrees
        angle *= 180f / MathF.PI;
    }
}
