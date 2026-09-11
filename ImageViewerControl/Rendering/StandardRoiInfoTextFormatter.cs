using System;
using System.Linq;
using System.Windows;
using ImageViewer.Models;
using ImageViewer.Utils;

namespace ImageViewer.Rendering
{
    internal static class StandardRoiInfoTextFormatter
    {
        public static string BuildRotatedRectText(RotatedRect rect, RoiRenderContext context)
        {
            return BuildMultiline(
                rect.Label,
                $"W:{context.FormatLength(rect.Width)} H:{context.FormatLength(rect.Height)} Area:{context.FormatArea(rect.Width * rect.Height)} Angle:{rect.Angle:F1}°");
        }

        public static string BuildBlobAnalysisText(BlobAnalysisRoi roi)
        {
            return BuildMultiline(
                roi.Label,
                $"Blobs:{roi.DetectedBlobs.Count} Total:{roi.DetectedBlobs.Sum(blob => blob.Area)}px Angle:{roi.Angle:F1}°",
                roi.DetectedBlobs.Count > 0 ? $"Max:{roi.DetectedBlobs.Max(blob => blob.Area)}px MinArea:{roi.MinArea}px" : $"MinArea:{roi.MinArea}px");
        }

        public static string BuildFittedEllipseText(FittedEllipseRoi ellipse, RoiRenderContext context)
        {
            return BuildMultiline(ellipse.Label, $"Fit RX:{context.FormatLength(ellipse.RadiusX)} RY:{context.FormatLength(ellipse.RadiusY)} Pts:{ellipse.SourcePointCount}");
        }

        public static string BuildRingText(RingRoi ring, RoiRenderContext context)
        {
            return BuildMultiline(ring.Label, $"Inner:{context.FormatLength(ring.InnerRadius)} Outer:{context.FormatLength(ring.OuterRadius)}");
        }

        public static string BuildEllipseText(EllipseRoi ellipse, RoiRenderContext context)
        {
            return BuildMultiline(
                ellipse.Label,
                $"RX:{context.FormatLength(ellipse.RadiusX)} RY:{context.FormatLength(ellipse.RadiusY)} Area:{context.FormatArea(Math.PI * ellipse.RadiusX * ellipse.RadiusY)} Angle:{ellipse.Angle:F1}°");
        }

        public static string BuildCircleText(CircleRoi circle, RoiRenderContext context)
        {
            return BuildMultiline(circle.Label, $"R:{context.FormatLength(circle.Radius)} Area:{context.FormatArea(Math.PI * circle.Radius * circle.Radius)}");
        }

        public static string BuildPolygonText(PolygonRoi polygon, (double Area, double Perimeter, Point Centroid) metrics, RoiRenderContext context)
        {
            return BuildMultiline(polygon.Label, $"Area:{context.FormatArea(metrics.Area)} Peri:{context.FormatPerimeter(metrics.Perimeter)}");
        }

        public static string BuildPolylineText(PolylineRoi polyline)
        {
            return polyline.Label ?? string.Empty;
        }

        public static string BuildPointAnnotationText(PointAnnotationRoi annotation)
        {
            return annotation.Label ?? string.Empty;
        }

        public static string BuildTextAnnotationText(TextAnnotationRoi annotation)
        {
            return annotation.Label ?? string.Empty;
        }

        public static string BuildArrowAnnotationText(ArrowAnnotationRoi arrow)
        {
            return arrow.Label ?? string.Empty;
        }

        public static string BuildLineMeasureText(LineMeasureRoi line, RoiRenderContext context)
        {
            return BuildInline(line.Label, $"D:{context.FormatLength(GeometryUtils.Distance(line.P1, line.P2))}");
        }

        public static string BuildAngleMeasureText(AngleMeasureRoi angle, double angleValue)
        {
            return BuildInline(angle.Label, $"{angleValue:F1}°");
        }

        public static string BuildArcMeasureText(ArcMeasureRoi arc, RoiRenderContext context)
        {
            return BuildMultiline(
                arc.Label,
                $"R:{context.FormatLength(arc.Radius)} Arc:{context.FormatLength(arc.ArcLength)} Angle:{arc.CentralAngle:F1}°");
        }

        public static string BuildPointToLineDistanceText(PointToLineDistanceRoi roi, RoiRenderContext context)
        {
            return BuildMultiline(roi.Label, $"Distance: {context.FormatLength(roi.Distance)}");
        }

        public static string BuildPointToCircleDistanceText(PointToCircleDistanceRoi roi, RoiRenderContext context)
        {
            return BuildMultiline(
                roi.Label,
                $"To Circle: {context.FormatLength(roi.DistanceToCircle)}",
                $"To Center: {context.FormatLength(roi.DistanceToCenter)}");
        }

        public static string BuildParallelismText(ParallelismMeasureRoi roi, RoiRenderContext context)
        {
            return BuildMultiline(
                roi.Label,
                $"Angle Diff: {roi.AngleDifference:F2}°",
                $"Avg Dist: {context.FormatLength(roi.AverageDistance)}");
        }

        public static string BuildPerpendicularityText(PerpendicularityMeasureRoi roi)
        {
            return BuildMultiline(
                roi.Label,
                $"Angle: {roi.AngleBetweenLines:F1}°",
                $"Error: {roi.PerpendicularityError:F2}°");
        }

        public static string BuildConcentricityText(ConcentricityMeasureRoi roi, RoiRenderContext context)
        {
            return BuildMultiline(
                roi.Label,
                $"Center Dist: {context.FormatLength(roi.CenterDistance)}",
                $"R1: {context.FormatLength(roi.Radius1)} R2: {context.FormatLength(roi.Radius2)}");
        }

        private static string BuildInline(string? label, string content)
        {
            return string.IsNullOrWhiteSpace(label) ? content : $"{label}: {content}";
        }

        private static string BuildMultiline(string? label, params string[] lines)
        {
            string content = string.Join(Environment.NewLine, lines);
            return string.IsNullOrWhiteSpace(label) ? content : $"{label}{Environment.NewLine}{content}";
        }
    }
}
