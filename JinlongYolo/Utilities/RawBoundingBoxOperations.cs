namespace JinlongYolo.YoloSharp.Utilities;

/// <summary>
/// 提供针对原始边界框集合的操作方法。
/// Provides operation methods for collections of raw bounding boxes.
/// </summary>
internal static class RawBoundingBoxOperations
{
    /// <summary>
    /// 检查指定的边界框数组是否已按置信度降序排序。
    /// Checks if the specified array of bounding boxes is sorted in descending order by confidence.
    /// </summary>
    /// <param name="boxes">要检查的边界框只读片段 / The read-only span of bounding boxes to check.</param>
    /// <returns>如果已按置信度降序排序，则为 true；否则为 false / True if sorted descending by confidence, otherwise false.</returns>
    public static bool IsSortedDescending(ReadOnlySpan<RawBoundingBox> boxes)
    {
        for (var index = 1; index < boxes.Length; index++)
        {
            if (boxes[index - 1].Confidence < boxes[index].Confidence)
            {
                return false;
            }
        }

        return true;
    }

    public static void EnqueueTop(ref PriorityQueue<RawBoundingBox, float>? queue, RawBoundingBox box, int limit)
    {
        if (limit <= 0)
        {
            return;
        }

        queue ??= new PriorityQueue<RawBoundingBox, float>(limit);

        if (queue.Count < limit)
        {
            queue.Enqueue(box, box.Confidence);
            return;
        }

        queue.TryPeek(out _, out var minConfidence);

        if (box.Confidence <= minConfidence)
        {
            return;
        }

        queue.Dequeue();
        queue.Enqueue(box, box.Confidence);
    }

    public static RawBoundingBox[] DrainDescending(PriorityQueue<RawBoundingBox, float>? queue)
    {
        if (queue == null || queue.Count == 0)
        {
            return [];
        }

        var result = new RawBoundingBox[queue.Count];

        for (var index = result.Length - 1; index >= 0; index--)
        {
            result[index] = queue.Dequeue();
        }

        return result;
    }

    public static RawBoundingBox[] Limit(RawBoundingBox[] boxes, int limit)
    {
        if (limit <= 0 || boxes.Length <= limit)
        {
            return boxes;
        }

        if (!IsSortedDescending(boxes))
        {
            Array.Sort(boxes, static (x, y) => y.CompareTo(x));
        }

        Array.Resize(ref boxes, limit);

        return boxes;
    }
}