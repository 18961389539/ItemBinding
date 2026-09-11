using Extensions;
using OpenCvSharp;
using Xunit;

namespace MainAPP.Tests.Extensions;

/// <summary>
/// PointExtensions 单元测试，覆盖 LineAngle、Distance、Midpoint、Rotate、ProjectToLine、IsOnSegment 等。
/// </summary>
public class PointExtensionsTests
{
    const double DoublePrecision = 5;

    #region LineAngle

    [Fact]
    public void LineAngle_HorizontalRight_Returns0()
    {
        var start = new Point2f(0, 0);
        var end = new Point2f(100, 0);

        var angle = start.LineAngle(end);

        Assert.Equal(0, angle, DoublePrecision);
    }

    [Fact]
    public void LineAngle_VerticalDown_Returns90()
    {
        var start = new Point2f(0, 0);
        var end = new Point2f(0, 100);

        var angle = start.LineAngle(end);

        Assert.Equal(90, angle, DoublePrecision);
    }

    [Fact]
    public void LineAngle_HorizontalLeft_Returns180()
    {
        var start = new Point2f(100, 0);
        var end = new Point2f(0, 0);

        var angle = start.LineAngle(end);

        Assert.Equal(180, angle, DoublePrecision);
    }

    [Fact]
    public void LineAngle_VerticalUp_Returns270()
    {
        var start = new Point2f(0, 100);
        var end = new Point2f(0, 0);

        var angle = start.LineAngle(end);

        Assert.Equal(270, angle, DoublePrecision);
    }

    [Fact]
    public void LineAngle_Diagonal45Degrees_Returns45()
    {
        var start = new Point2f(0, 0);
        var end = new Point2f(100, 100);

        var angle = start.LineAngle(end);

        Assert.Equal(45, angle, DoublePrecision);
    }

    [Fact]
    public void LineAngle_NormalizeFalse_ReturnsNegativeForUpDirection()
    {
        var start = new Point2f(0, 100);
        var end = new Point2f(0, 0);

        var angle = start.LineAngle(end, normalizeTo360: false);

        Assert.Equal(-90, angle, DoublePrecision);
    }

    [Fact]
    public void LineAngle_PointVariant_MatchesFloatVariant()
    {
        var start = new OpenCvSharp.Point(0, 0);
        var end = new OpenCvSharp.Point(100, 100);

        var angle = start.LineAngle(end);

        Assert.Equal(45, angle, DoublePrecision);
    }

    [Fact]
    public void LineAngle_Point2dVariant_MatchesFloatVariant()
    {
        var start = new Point2d(0, 0);
        var end = new Point2d(100, 100);

        var angle = start.LineAngle(end);

        Assert.Equal(45, angle, DoublePrecision);
    }

    #endregion

    #region Distance

    [Fact]
    public void Distance_Point2f_ReturnsEuclideanDistance()
    {
        var p1 = new Point2f(0, 0);
        var p2 = new Point2f(3, 4);

        var distance = p1.Distance(p2);

        Assert.Equal(5, distance, DoublePrecision);
    }

    [Fact]
    public void Distance_SamePoint_ReturnsZero()
    {
        var p1 = new Point2f(5, 5);
        var p2 = new Point2f(5, 5);

        Assert.Equal(0, p1.Distance(p2));
    }

    [Fact]
    public void Distance_Point2dVariant_ReturnsEuclideanDistance()
    {
        var p1 = new Point2d(0, 0);
        var p2 = new Point2d(3, 4);

        Assert.Equal(5, p1.Distance(p2), DoublePrecision);
    }

    [Fact]
    public void Distance_PointVariant_ReturnsEuclideanDistance()
    {
        var p1 = new OpenCvSharp.Point(0, 0);
        var p2 = new OpenCvSharp.Point(3, 4);

        Assert.Equal(5, p1.Distance(p2), DoublePrecision);
    }

    #endregion

    #region DistanceToLine

    [Fact]
    public void DistanceToLine_PointOnLine_ReturnsZero()
    {
        var point = new Point2f(5, 0);
        var lineStart = new Point2f(0, 0);
        var lineEnd = new Point2f(10, 0);

        Assert.Equal(0, point.DistanceToLine(lineStart, lineEnd), DoublePrecision);
    }

    [Fact]
    public void DistanceToLine_PointAboveHorizontalLine_ReturnsVerticalDistance()
    {
        var point = new Point2f(5, 3);
        var lineStart = new Point2f(0, 0);
        var lineEnd = new Point2f(10, 0);

        Assert.Equal(3, point.DistanceToLine(lineStart, lineEnd), DoublePrecision);
    }

    [Fact]
    public void DistanceToLine_DegenerateLine_ReturnsPointDistance()
    {
        var point = new Point2f(3, 4);
        var lineStart = new Point2f(0, 0);
        var lineEnd = new Point2f(0, 0); // 退化直线

        Assert.Equal(5, point.DistanceToLine(lineStart, lineEnd), DoublePrecision);
    }

    [Fact]
    public void DistanceToLine_PointVariant_ReturnsSameResult()
    {
        var point = new OpenCvSharp.Point(5, 3);
        var lineStart = new OpenCvSharp.Point(0, 0);
        var lineEnd = new OpenCvSharp.Point(10, 0);

        Assert.Equal(3, point.DistanceToLine(lineStart, lineEnd), DoublePrecision);
    }

    #endregion

    #region Midpoint

    [Fact]
    public void Midpoint_Point2f_ReturnsMidpoint()
    {
        var p1 = new Point2f(0, 0);
        var p2 = new Point2f(10, 20);

        var mid = p1.Midpoint(p2);

        Assert.Equal(5f, mid.X);
        Assert.Equal(10f, mid.Y);
    }

    [Fact]
    public void Midpoint_Point2d_ReturnsMidpoint()
    {
        var p1 = new Point2d(0, 0);
        var p2 = new Point2d(10, 20);

        var mid = p1.Midpoint(p2);

        Assert.Equal(5.0, mid.X);
        Assert.Equal(10.0, mid.Y);
    }

    [Fact]
    public void Midpoint_PointVariant_ReturnsMidpoint()
    {
        var p1 = new OpenCvSharp.Point(0, 0);
        var p2 = new OpenCvSharp.Point(10, 20);

        var mid = p1.Midpoint(p2);

        Assert.Equal(5, mid.X);
        Assert.Equal(10, mid.Y);
    }

    #endregion

    #region Rotate

    [Fact]
    public void Rotate_0Degrees_ReturnsSamePoint()
    {
        var point = new Point2f(10, 20);
        var center = new Point2f(0, 0);

        var rotated = point.Rotate(center, 0);

        Assert.Equal(10f, rotated.X, precision: 3);
        Assert.Equal(20f, rotated.Y, precision: 3);
    }

    [Fact]
    public void Rotate_90Degrees_ReturnsPerpendicularPoint()
    {
        var point = new Point2f(10, 0);
        var center = new Point2f(0, 0);

        var rotated = point.Rotate(center, 90);

        Assert.Equal(0f, rotated.X, precision: 3);
        Assert.Equal(10f, rotated.Y, precision: 3);
    }

    [Fact]
    public void Rotate_360Degrees_ReturnsOriginalPoint()
    {
        var point = new Point2f(10, 20);
        var center = new Point2f(5, 5);

        var rotated = point.Rotate(center, 360);

        Assert.Equal(10f, rotated.X, precision: 3);
        Assert.Equal(20f, rotated.Y, precision: 3);
    }

    [Fact]
    public void Rotate_AroundCenter_PreservesDistance()
    {
        var point = new Point2f(10, 0);
        var center = new Point2f(0, 0);

        var rotated = point.Rotate(center, 45);
        var originalDistance = point.Distance(center);
        var rotatedDistance = rotated.Distance(center);

        Assert.Equal(originalDistance, rotatedDistance, DoublePrecision);
    }

    #endregion

    #region ProjectToLine

    [Fact]
    public void ProjectToLine_PointAlreadyOnLine_ReturnsSamePoint()
    {
        var point = new Point2f(5, 0);
        var lineStart = new Point2f(0, 0);
        var lineEnd = new Point2f(10, 0);

        var projection = point.ProjectToLine(lineStart, lineEnd);

        Assert.Equal(5f, projection.X, precision: 3);
        Assert.Equal(0f, projection.Y, precision: 3);
    }

    [Fact]
    public void ProjectToLine_PointAboveLine_ReturnsFootOfPerpendicular()
    {
        var point = new Point2f(5, 10);
        var lineStart = new Point2f(0, 0);
        var lineEnd = new Point2f(10, 0);

        var projection = point.ProjectToLine(lineStart, lineEnd);

        Assert.Equal(5f, projection.X, precision: 3);
        Assert.Equal(0f, projection.Y, precision: 3);
    }

    [Fact]
    public void ProjectToLine_DegenerateLine_ReturnsLineStart()
    {
        var point = new Point2f(5, 10);
        var lineStart = new Point2f(3, 4);
        var lineEnd = new Point2f(3, 4);

        var projection = point.ProjectToLine(lineStart, lineEnd);

        Assert.Equal(lineStart, projection);
    }

    #endregion

    #region IsOnSegment

    [Fact]
    public void IsOnSegment_PointOnSegment_ReturnsTrue()
    {
        var point = new Point2f(5, 0);
        var segStart = new Point2f(0, 0);
        var segEnd = new Point2f(10, 0);

        Assert.True(point.IsOnSegment(segStart, segEnd));
    }

    [Fact]
    public void IsOnSegment_PointOffSegment_ReturnsFalse()
    {
        var point = new Point2f(5, 10);
        var segStart = new Point2f(0, 0);
        var segEnd = new Point2f(10, 0);

        Assert.False(point.IsOnSegment(segStart, segEnd));
    }

    [Fact]
    public void IsOnSegment_PointBeforeStart_ReturnsFalse()
    {
        var point = new Point2f(-5, 0);
        var segStart = new Point2f(0, 0);
        var segEnd = new Point2f(10, 0);

        Assert.False(point.IsOnSegment(segStart, segEnd));
    }

    [Fact]
    public void IsOnSegment_PointAfterEnd_ReturnsFalse()
    {
        var point = new Point2f(15, 0);
        var segStart = new Point2f(0, 0);
        var segEnd = new Point2f(10, 0);

        Assert.False(point.IsOnSegment(segStart, segEnd));
    }

    #endregion

    #region ApproximatelyEqual

    [Fact]
    public void ApproximatelyEqual_SamePoints_ReturnsTrue()
    {
        var p1 = new Point2f(5, 5);
        var p2 = new Point2f(5, 5);

        Assert.True(p1.ApproximatelyEqual(p2));
    }

    [Fact]
    public void ApproximatelyEqual_ClosePoints_ReturnsTrue()
    {
        var p1 = new Point2f(5, 5);
        var p2 = new Point2f(5.0000001f, 5.0000001f);

        Assert.True(p1.ApproximatelyEqual(p2, tolerance: 1e-5));
    }

    [Fact]
    public void ApproximatelyEqual_DistantPoints_ReturnsFalse()
    {
        var p1 = new Point2f(5, 5);
        var p2 = new Point2f(10, 10);

        Assert.False(p1.ApproximatelyEqual(p2));
    }

    #endregion
}
