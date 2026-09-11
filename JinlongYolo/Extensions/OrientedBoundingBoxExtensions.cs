namespace JinlongYolo.YoloSharp.Extensions;

/// <summary>
/// 为定向边界框（OBB）提供扩展方法。
/// Provides extension methods for Oriented Bounding Boxes (OBB).
/// </summary>
internal static class OrientedBoundingBoxExtensions
{
    /// <summary>
    /// 获取 OBB 检测结果的四个角点。
    /// Gets the four corner points of the OBB detection result.
    /// </summary>
    /// <param name="obb">OBB 检测结果 / The OBB detection result.</param>
    /// <returns>包含四个角点的数组 / An array containing the four corner points.</returns>
    public static Point[] GetCornerPoints(this ObbDetection obb)
    {
        return GetCornerPoints(obb.Bounds, obb.Angle);
    }

    /// <summary>
    /// 获取原始边界框的四个角点。
    /// Gets the four corner points of the raw bounding box.
    /// </summary>
    /// <param name="box">原始边界框 / The raw bounding box.</param>
    /// <returns>包含四个角点的数组 / An array containing the four corner points.</returns>
    public static Point[] GetCornerPoints(this RawBoundingBox box)
    {
        return GetCornerPoints(box.Bounds, box.Angle);
    }

    /// <summary>
    /// 根据边界框和角度计算四个角点。
    /// Calculates the four corner points based on the bounding box and angle.
    /// </summary>
    /// <param name="bounds">边界框的矩形区域 / The rectangular bounds of the box.</param>
    /// <param name="angle">旋转角度（度） / The rotation angle in degrees.</param>
    /// <returns>包含四个角点的数组 / An array containing the four corner points.</returns>
    private static Point[] GetCornerPoints(RectangleF bounds, float angle)
    {
        var _angle = angle * MathF.PI / 180.0f; // Radians

        var b = MathF.Cos(_angle) * .5f;
        var a = MathF.Sin(_angle) * .5f;

        var x = bounds.X;
        var y = bounds.Y;
        var w = bounds.Width;
        var h = bounds.Height;

        var points = new Point[4];

        points[0].X = (int)MathF.Round(x - a * h - b * w, 0);
        points[0].Y = (int)MathF.Round(y + b * h - a * w, 0);

        points[1].X = (int)MathF.Round(x + a * h - b * w, 0);
        points[1].Y = (int)MathF.Round(y - b * h - a * w, 0);

        points[2].X = (int)MathF.Round(2f * x - points[0].X, 0);
        points[2].Y = (int)MathF.Round(2f * y - points[0].Y, 0);

        points[3].X = (int)MathF.Round(2f * x - points[1].X, 0);
        points[3].Y = (int)MathF.Round(2f * y - points[1].Y, 0);

        // Calculate the distances of each point from the origin (0, 0)
        var distance1 = Math.Sqrt(Math.Pow(points[0].X, 2) + Math.Pow(points[0].Y, 2));
        var distance2 = Math.Sqrt(Math.Pow(points[1].X, 2) + Math.Pow(points[1].Y, 2));

        // Rotate if necessary to ensure pt[0] is the top-left point
        if (distance2 < distance1)
        {
            var temp = points[0];
            points[0] = points[1];
            points[1] = points[2];
            points[2] = points[3];
            points[3] = temp;
        }

        return points;
    }
}