namespace JinlongYolo.YoloSharp.Services;

internal class NonMaxSuppression : INonMaxSuppression
{
    public RawBoundingBox[] Apply(Span<RawBoundingBox> boxes, float iouThreshold)
    {
        if (boxes.Length == 0)
        {
            return [];
        }

        if (boxes.Length == 1)
        {
            return [boxes[0]];
        }

        // Decoder outputs are usually already sorted by confidence, so only sort when needed.
        if (!RawBoundingBoxOperations.IsSortedDescending(boxes))
        {
            boxes.Sort(static (x, y) => y.CompareTo(x));
        }

        var accepted = GC.AllocateUninitializedArray<RawBoundingBox>(boxes.Length);
        var acceptedCount = 1;
        accepted[0] = boxes[0];

        // Iterate boxes (Skip with the first box because it already has been added)
        for (var i = 1; i < boxes.Length; i++)
        {
            var box1 = boxes[i];
            var addToResult = true;

            for (var j = 0; j < acceptedCount; j++)
            {
                var box2 = accepted[j];

                // Skip boxers with different label
                if (box1.NameIndex != box2.NameIndex)
                {
                    continue;
                }

                // If the box overlaps another box already in the results 
                if (CalculateIoU(box1, box2) > iouThreshold)
                {
                    addToResult = false;
                    break;
                }
            }

            if (addToResult)
            {
                accepted[acceptedCount++] = box1;
            }
        }

        if (acceptedCount == accepted.Length)
        {
            return accepted;
        }

        var result = new RawBoundingBox[acceptedCount];
        Array.Copy(accepted, result, acceptedCount);

        return result;
    }

    protected virtual float CalculateIoU(RawBoundingBox box1, RawBoundingBox box2)
    {
        var rect1 = box1.Bounds;
        var rect2 = box2.Bounds;

        var area1 = rect1.Width * rect1.Height;

        if (area1 <= 0f)
        {
            return 0f;
        }

        var area2 = rect2.Width * rect2.Height;

        if (area2 <= 0f)
        {
            return 0f;
        }

        var intersection = RectangleF.Intersect(rect1, rect2);
        var intersectionArea = intersection.Width * intersection.Height;

        return (float)intersectionArea / (area1 + area2 - intersectionArea);
    }
}