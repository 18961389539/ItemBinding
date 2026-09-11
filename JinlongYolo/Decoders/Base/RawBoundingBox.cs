// RawBoundingBox.cs
// 说明：表示模型解码后得到的原始边界框信息，包括类别索引、置信度、包围矩形及角度信息。
namespace JinlongYolo.YoloSharp.Decoders.Base;

internal readonly struct RawBoundingBox : IComparable<RawBoundingBox>
{
    /// <summary>
    /// 在原始输出张量/候选列表中的索引，可用于追溯或排序。
    /// </summary>
    public required int Index { get; init; }

    /// <summary>
    /// 类别名称在标签列表中的索引。
    /// </summary>
    public required int NameIndex { get; init; }

    /// <summary>
    /// 置信度分数（通常为类别置信度或目标存在概率）。
    /// </summary>
    public required float Confidence { get; init; }

    /// <summary>
    /// 边界框的矩形表示（RectangleF），包含位置与宽高。
    /// </summary>
    public required RectangleF Bounds { get; init; }

    /// <summary>
    /// 边界框的角度（用于有方向的检测，例如 OBB）。
    /// </summary>
    public float Angle { get; init; }

    public int CompareTo(RawBoundingBox other) => Confidence.CompareTo(other.Confidence);
}
