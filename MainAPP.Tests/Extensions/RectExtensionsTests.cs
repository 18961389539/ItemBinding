using Extensions;
using OpenCvSharp;
using Xunit;

namespace MainAPP.Tests.Extensions;

/// <summary>
/// RectExtensions 单元测试，覆盖几何运算（面积、中心、交集、并集、包含、距离、缩放等）。
/// </summary>
public class RectExtensionsTests
{
    [Fact]
    public void Area_ReturnsWidthTimesHeight()
    {
        var rect = new Rect(10, 20, 30, 40);

        Assert.Equal(1200, rect.Area());
    }

    [Fact]
    public void Area_NegativeDimensions_ReturnsPositiveArea()
    {
        var rect = new Rect(10, 20, -30, -40);

        Assert.Equal(1200, rect.Area());
    }

    [Fact]
    public void Center_ReturnsCenterPoint()
    {
        var rect = new Rect(0, 0, 100, 60);

        var center = rect.Center();

        Assert.Equal(50, center.X);
        Assert.Equal(30, center.Y);
    }

    [Fact]
    public void CenterF_ReturnsFloatCenter()
    {
        var rect = new Rect(0, 0, 101, 61);

        var center = rect.CenterF();

        Assert.Equal(50.5f, center.X);
        Assert.Equal(30.5f, center.Y);
    }

    [Fact]
    public void InflateBy_ExpandsRect()
    {
        var rect = new Rect(50, 50, 100, 100);

        var inflated = rect.InflateBy(10, 20);

        Assert.Equal(new Rect(40, 30, 120, 140), inflated);
    }

    [Fact]
    public void InflateBy_Negative_ShrinksRect()
    {
        var rect = new Rect(50, 50, 100, 100);

        var shrunk = rect.InflateBy(-10, -20);

        Assert.Equal(new Rect(60, 70, 80, 60), shrunk);
    }

    [Fact]
    public void DeflateBy_ShrinksRect()
    {
        var rect = new Rect(50, 50, 100, 100);

        var deflated = rect.DeflateBy(10, 20);

        Assert.Equal(new Rect(60, 70, 80, 60), deflated);
    }

    [Fact]
    public void Intersect_OverlappingRects_ReturnsIntersection()
    {
        var a = new Rect(0, 0, 100, 100);
        var b = new Rect(50, 50, 100, 100);

        var intersection = a.Intersect(b);

        Assert.Equal(new Rect(50, 50, 50, 50), intersection);
    }

    [Fact]
    public void Intersect_NonOverlappingRects_ReturnsEmpty()
    {
        var a = new Rect(0, 0, 50, 50);
        var b = new Rect(100, 100, 50, 50);

        var intersection = a.Intersect(b);

        Assert.Equal(new Rect(0, 0, 0, 0), intersection);
    }

    [Fact]
    public void Intersect_TouchingRects_ReturnsEmpty()
    {
        // 半开区间：相邻不重叠。显式调用扩展方法（OpenCvSharp.Rect 内置 Intersect
        // 会返回退化矩形 (50,0,0,50)，扩展方法按 x2<=x1 判定返回空矩形）
        var a = new Rect(0, 0, 50, 50);
        var b = new Rect(50, 0, 50, 50);

        var intersection = RectExtensions.Intersect(a, b);

        Assert.Equal(new Rect(0, 0, 0, 0), intersection);
    }

    [Fact]
    public void Union_ReturnsBoundingRect()
    {
        var a = new Rect(0, 0, 50, 50);
        var b = new Rect(100, 100, 50, 50);

        var union = a.Union(b);

        Assert.Equal(new Rect(0, 0, 150, 150), union);
    }

    [Theory]
    [InlineData(10, 10, true)]   // 内部
    [InlineData(0, 0, true)]     // 左上角
    [InlineData(99, 99, true)]   // 右下角前一个
    [InlineData(100, 100, false)] // 右下角（半开区间）
    [InlineData(-1, 50, false)]  // 外部
    public void Contains_Point(int x, int y, bool expected)
    {
        var rect = new Rect(0, 0, 100, 100);

        Assert.Equal(expected, rect.Contains(new Point(x, y)));
    }

    [Fact]
    public void Contains_OtherRect_FullyInside_ReturnsTrue()
    {
        var outer = new Rect(0, 0, 100, 100);
        var inner = new Rect(10, 10, 50, 50);

        Assert.True(outer.Contains(inner));
    }

    [Fact]
    public void Contains_OtherRect_PartiallyOutside_ReturnsFalse()
    {
        var outer = new Rect(0, 0, 100, 100);
        var partial = new Rect(50, 50, 100, 100);

        Assert.False(outer.Contains(partial));
    }

    [Fact]
    public void Overlaps_OverlappingRects_ReturnsTrue()
    {
        var a = new Rect(0, 0, 100, 100);
        var b = new Rect(50, 50, 100, 100);

        Assert.True(a.Overlaps(b));
    }

    [Fact]
    public void Overlaps_NonOverlappingRects_ReturnsFalse()
    {
        var a = new Rect(0, 0, 50, 50);
        var b = new Rect(100, 100, 50, 50);

        Assert.False(a.Overlaps(b));
    }

    [Fact]
    public void DistanceTo_OverlappingRects_ReturnsZero()
    {
        var a = new Rect(0, 0, 100, 100);
        var b = new Rect(50, 50, 100, 100);

        Assert.Equal(0.0, a.DistanceTo(b));
    }

    [Fact]
    public void DistanceTo_HorizontallySeparated_ReturnsHorizontalDistance()
    {
        var a = new Rect(0, 0, 50, 50);
        var b = new Rect(100, 0, 50, 50);

        Assert.Equal(50.0, a.DistanceTo(b));
    }

    [Fact]
    public void DistanceTo_DiagonallySeparated_ReturnsEuclideanDistance()
    {
        var a = new Rect(0, 0, 50, 50);
        var b = new Rect(100, 100, 50, 50);

        // dx=50, dy=50, 距离=sqrt(50^2+50^2)=50*sqrt(2)
        Assert.Equal(50 * Math.Sqrt(2), a.DistanceTo(b), precision: 5);
    }

    [Fact]
    public void Scale_PreservesCenter()
    {
        var rect = new Rect(0, 0, 100, 100);
        var originalCenter = rect.Center();

        var scaled = rect.Scale(2.0f);

        Assert.Equal(originalCenter, scaled.Center());
        Assert.Equal(200, scaled.Width);
        Assert.Equal(200, scaled.Height);
    }

    [Fact]
    public void Scale_Halves_DimensionsHalf()
    {
        var rect = new Rect(0, 0, 100, 100);

        var scaled = rect.Scale(0.5f);

        Assert.Equal(50, scaled.Width);
        Assert.Equal(50, scaled.Height);
    }

    [Fact]
    public void Move_TranslatesRect()
    {
        var rect = new Rect(10, 20, 30, 40);

        var moved = rect.Move(5, -10);

        Assert.Equal(new Rect(15, 10, 30, 40), moved);
    }

    [Fact]
    public void Clip_ConstrainsToBounds()
    {
        var rect = new Rect(-10, -10, 100, 100);
        var bounds = new Rect(0, 0, 50, 50);

        var clipped = rect.Clip(bounds);

        Assert.Equal(new Rect(0, 0, 50, 50), clipped);
    }

    [Fact]
    public void Clip_NoOverlap_ReturnsEmpty()
    {
        var rect = new Rect(100, 100, 50, 50);
        var bounds = new Rect(0, 0, 50, 50);

        var clipped = rect.Clip(bounds);

        Assert.Equal(new Rect(0, 0, 0, 0), clipped);
    }

    [Fact]
    public void ToRotatedRect_CreatesZeroAngleRotatedRect()
    {
        var rect = new Rect(10, 20, 100, 50);

        var rotated = rect.ToRotatedRect();

        Assert.Equal(0f, rotated.Angle);
        Assert.Equal(100f, rotated.Size.Width);
        Assert.Equal(50f, rotated.Size.Height);
    }

    [Fact]
    public void ToPoints_ReturnsFourCornersClockwise()
    {
        var rect = new Rect(10, 20, 100, 50);

        var points = rect.ToPoints();

        Assert.Equal(4, points.Length);
        Assert.Equal(new Point(10, 20), points[0]);
        Assert.Equal(new Point(110, 20), points[1]);
        Assert.Equal(new Point(110, 70), points[2]);
        Assert.Equal(new Point(10, 70), points[3]);
    }

    [Fact]
    public void IsValid_PositiveDimensions_ReturnsTrue()
    {
        Assert.True(new Rect(0, 0, 10, 10).IsValid());
    }

    [Fact]
    public void IsValid_ZeroDimensions_ReturnsFalse()
    {
        Assert.False(new Rect(0, 0, 0, 10).IsValid());
        Assert.False(new Rect(0, 0, 10, 0).IsValid());
    }

    [Fact]
    public void IsValid_NegativeDimensions_ReturnsFalse()
    {
        Assert.False(new Rect(0, 0, -10, 10).IsValid());
    }

    [Fact]
    public void FromPoints_ReturnsBoundingRect()
    {
        var rect = RectExtensions.FromPoints(new Point(50, 60), new Point(10, 20));

        Assert.Equal(new Rect(10, 20, 40, 40), rect);
    }

    [Fact]
    public void FromCenterSize_ReturnsCenteredRect()
    {
        var rect = RectExtensions.FromCenterSize(new Point(100, 100), new Size(50, 40));

        Assert.Equal(new Rect(75, 80, 50, 40), rect);
        Assert.Equal(new Point(100, 100), rect.Center());
    }

    [Fact]
    public void Merge_MultipleRects_ReturnsBoundingRect()
    {
        var rects = new[]
        {
            new Rect(10, 10, 20, 20),
            new Rect(50, 50, 20, 20),
            new Rect(30, 30, 10, 10)
        };

        var merged = rects.Merge();

        Assert.Equal(new Rect(10, 10, 60, 60), merged);
    }

    [Fact]
    public void Merge_EmptyCollection_ReturnsEmptyRect()
    {
        var merged = Array.Empty<Rect>().Merge();

        Assert.Equal(new Rect(0, 0, 0, 0), merged);
    }

    [Fact]
    public void Merge_NullCollection_ThrowsArgumentNullException()
    {
        Assert.Throws<ArgumentNullException>(() =>
            ((IEnumerable<Rect>)null!).Merge());
    }

    [Fact]
    public void InflateToAspectRatio_WiderThanTarget_IncreasesHeight()
    {
        var rect = new Rect(0, 0, 200, 100); // 当前 2:1

        var result = rect.InflateToAspectRatio(1.0f); // 目标 1:1

        Assert.Equal(200, result.Width);
        Assert.Equal(200, result.Height);
    }

    [Fact]
    public void InflateToAspectRatio_TallerThanTarget_IncreasesWidth()
    {
        var rect = new Rect(0, 0, 100, 200); // 当前 1:2

        var result = rect.InflateToAspectRatio(1.0f); // 目标 1:1

        Assert.Equal(200, result.Width);
        Assert.Equal(200, result.Height);
    }

    [Fact]
    public void InflateToAspectRatio_AlreadyMatches_ReturnsOriginal()
    {
        var rect = new Rect(0, 0, 100, 100);

        var result = rect.InflateToAspectRatio(1.0f);

        Assert.Equal(rect, result);
    }

    [Fact]
    public void InflateToAspectRatio_ZeroHeight_ReturnsOriginal()
    {
        var rect = new Rect(0, 0, 100, 0);

        var result = rect.InflateToAspectRatio(1.0f);

        Assert.Equal(rect, result);
    }

    [Fact]
    public void InflateToAspectRatio_ZeroOrNegativeRatio_ThrowsArgumentOutOfRangeException()
    {
        var rect = new Rect(0, 0, 100, 100);

        Assert.Throws<ArgumentOutOfRangeException>(() => rect.InflateToAspectRatio(0f));
        Assert.Throws<ArgumentOutOfRangeException>(() => rect.InflateToAspectRatio(-1f));
    }

    [Fact]
    public void ClipTo_ConstrainsToContainerSize()
    {
        var rect = new Rect(-10, -10, 100, 100);
        var container = new Size(50, 50);

        var clipped = rect.ClipTo(container);

        Assert.Equal(new Rect(0, 0, 50, 50), clipped);
    }

    [Fact]
    public void ClipTo_FullyOutside_ClampsToCornerPixel()
    {
        // ClipTo 实现使用 containerSize-1 作为位置 clamp 上限，完全外部的矩形
        // 会被压到容器右下角像素 (49,49)，宽高退化为 1（非空矩形）
        var rect = new Rect(100, 100, 50, 50);
        var container = new Size(50, 50);

        var clipped = rect.ClipTo(container);

        Assert.Equal(new Rect(49, 49, 1, 1), clipped);
    }

    [Fact]
    public void RotatedRect_Area_ReturnsWidthTimesHeight()
    {
        var rotated = new RotatedRect(
            new Point2f(50, 50),
            new Size2f(100, 50),
            30f);

        Assert.Equal(5000f, rotated.Area());
    }

    [Fact]
    public void RotatedRect_Scale_PreservesCenterAndAngle()
    {
        var rotated = new RotatedRect(
            new Point2f(50, 50),
            new Size2f(100, 50),
            30f);

        var scaled = rotated.Scale(2.0f);

        Assert.Equal(rotated.Center, scaled.Center);
        Assert.Equal(rotated.Angle, scaled.Angle);
        Assert.Equal(200f, scaled.Size.Width);
        Assert.Equal(100f, scaled.Size.Height);
    }

    [Fact]
    public void RotatedRect_Rotate_AddsAngle()
    {
        var rotated = new RotatedRect(
            new Point2f(50, 50),
            new Size2f(100, 50),
            30f);

        var rotatedMore = rotated.Rotate(15.0);

        Assert.Equal(45f, rotatedMore.Angle, precision: 5);
    }
}
