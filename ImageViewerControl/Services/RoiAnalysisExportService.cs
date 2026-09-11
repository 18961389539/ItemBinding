using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Media.Imaging;
using ImageViewer.Models;
using ImageViewer.Utils;

namespace ImageViewer.Services
{
    public static class RoiAnalysisExportService
    {
        public static string BuildSummary(IEnumerable<RoiBase> rois, BitmapSource? bitmap, double pixelSize, string? physicalUnit)
        {
            ArgumentNullException.ThrowIfNull(rois);

            var roiList = rois.ToList();
            var lines = new List<string>
            {
                $"Total ROIs: {roiList.Count}"
            };

            foreach (var group in roiList.GroupBy(roi => roi.RoiTypeName).OrderBy(group => group.Key, StringComparer.Ordinal))
            {
                lines.Add($"- {group.Key}: {group.Count()}");
            }

            double totalLineLength = roiList.OfType<LineMeasureRoi>().Sum(line => GeometryUtils.Distance(line.P1, line.P2) * pixelSize);
            double totalPolylineLength = roiList.OfType<PolylineRoi>().Sum(GetPolylineLength) * pixelSize;
            double totalPolygonArea = roiList.OfType<PolygonRoi>().Sum(poly => GeometryUtils.PolygonArea(poly.Points)) * pixelSize * pixelSize;
            double totalRectArea = roiList.OfType<RotatedRect>().Sum(rect => rect.Width * rect.Height) * pixelSize * pixelSize;
            double totalEllipseArea = roiList.OfType<EllipseRoi>().Sum(ellipse => Math.PI * ellipse.RadiusX * ellipse.RadiusY) * pixelSize * pixelSize;
            double totalCircleArea = roiList.OfType<CircleRoi>().Sum(circle => Math.PI * circle.Radius * circle.Radius) * pixelSize * pixelSize;
            string unit = string.IsNullOrWhiteSpace(physicalUnit) ? "px" : physicalUnit;

            lines.Add(string.Empty);
            lines.Add($"Line Length Total: {totalLineLength:F2} {unit}");
            lines.Add($"Polyline Length Total: {totalPolylineLength:F2} {unit}");
            lines.Add($"Area Total: {totalPolygonArea + totalRectArea + totalEllipseArea + totalCircleArea:F2} {unit}²");
            if (roiList.OfType<AngleMeasureRoi>().Any())
            {
                lines.Add($"Average Angle: {roiList.OfType<AngleMeasureRoi>().Average(angle => GeometryUtils.SmallestAngle(angle.P1, angle.Vertex, angle.P2)):F2}°");
            }

            if (bitmap != null)
            {
                int statisticsCount = roiList.Count(roi => ImageAnalysisService.TryCalculateStatistics(bitmap, roi, out _));
                lines.Add($"ROIs With Statistics: {statisticsCount}");
            }

            return string.Join(Environment.NewLine, lines);
        }

        public static void SaveCsv(string filePath, IEnumerable<RoiBase> rois, BitmapSource? bitmap, double pixelSize, string? physicalUnit)
        {
            ArgumentException.ThrowIfNullOrWhiteSpace(filePath);
            ArgumentNullException.ThrowIfNull(rois);
            File.WriteAllText(filePath, BuildCsv(rois, bitmap, pixelSize, physicalUnit), Encoding.UTF8);
        }

        public static Task SaveCsvAsync(string filePath, IEnumerable<RoiBase> rois, BitmapSource? bitmap, double pixelSize, string? physicalUnit, CancellationToken cancellationToken = default)
        {
            ArgumentException.ThrowIfNullOrWhiteSpace(filePath);
            ArgumentNullException.ThrowIfNull(rois);
            return File.WriteAllTextAsync(filePath, BuildCsv(rois, bitmap, pixelSize, physicalUnit), Encoding.UTF8, cancellationToken);
        }

        public static string BuildCsv(IEnumerable<RoiBase> rois, BitmapSource? bitmap, double pixelSize, string? physicalUnit)
        {
            ArgumentNullException.ThrowIfNull(rois);

            string unit = string.IsNullOrWhiteSpace(physicalUnit) ? "px" : physicalUnit;
            var builder = new StringBuilder();
            builder.AppendLine("Type,Label,Metric1,Metric2,Metric3,Mean,Min,Max,StdDev,PixelCount");

            foreach (var roi in rois)
            {
                var metrics = GetMetrics(roi, pixelSize, unit);
                string mean = string.Empty;
                string min = string.Empty;
                string max = string.Empty;
                string stddev = string.Empty;
                string pixelCount = string.Empty;

                if (bitmap != null && ImageAnalysisService.TryCalculateStatistics(bitmap, roi, out RoiStatistics statistics))
                {
                    mean = statistics.Mean.ToString("F2", CultureInfo.InvariantCulture);
                    min = statistics.Min.ToString(CultureInfo.InvariantCulture);
                    max = statistics.Max.ToString(CultureInfo.InvariantCulture);
                    stddev = statistics.StandardDeviation.ToString("F2", CultureInfo.InvariantCulture);
                    pixelCount = statistics.PixelCount.ToString(CultureInfo.InvariantCulture);
                }

                builder.AppendLine(string.Join(",",
                    Escape(roi.RoiTypeName),
                    Escape(roi.Label),
                    Escape(metrics.Metric1),
                    Escape(metrics.Metric2),
                    Escape(metrics.Metric3),
                    mean,
                    min,
                    max,
                    stddev,
                    pixelCount));
            }

            return builder.ToString();
        }

        private static (string Metric1, string Metric2, string Metric3) GetMetrics(RoiBase roi, double pixelSize, string unit)
        {
            return roi switch
            {
                RotatedRect rect => ($"Width={rect.Width * pixelSize:F2} {unit}", $"Height={rect.Height * pixelSize:F2} {unit}", $"Angle={rect.Angle:F1}°"),
                EllipseRoi ellipse => ($"RadiusX={ellipse.RadiusX * pixelSize:F2} {unit}", $"RadiusY={ellipse.RadiusY * pixelSize:F2} {unit}", $"Angle={ellipse.Angle:F1}°"),
                CircleRoi circle => ($"Radius={circle.Radius * pixelSize:F2} {unit}", string.Empty, string.Empty),
                PolygonRoi polygon => ($"Area={GeometryUtils.PolygonArea(polygon.Points) * pixelSize * pixelSize:F2} {unit}²", $"Perimeter={GeometryUtils.PolygonPerimeter(polygon.Points) * pixelSize:F2} {unit}", $"Vertices={polygon.Points.Count}"),
                PolylineRoi polyline => ($"Length={GetPolylineLength(polyline) * pixelSize:F2} {unit}", $"Points={polyline.Points.Count}", $"Freehand={polyline.IsFreehand}"),
                PointAnnotationRoi point => ($"X={point.Position.X:F1}", $"Y={point.Position.Y:F1}", string.Empty),
                TextAnnotationRoi text => ($"X={text.Position.X:F1}", $"Y={text.Position.Y:F1}", string.Empty),
                LineMeasureRoi line => ($"Length={GeometryUtils.Distance(line.P1, line.P2) * pixelSize:F2} {unit}", $"dX={(line.P2.X - line.P1.X) * pixelSize:F2} {unit}", $"dY={(line.P2.Y - line.P1.Y) * pixelSize:F2} {unit}"),
                AngleMeasureRoi angle => ($"Angle={GeometryUtils.SmallestAngle(angle.P1, angle.Vertex, angle.P2):F1}°", $"Leg1={GeometryUtils.Distance(angle.P1, angle.Vertex) * pixelSize:F2} {unit}", $"Leg2={GeometryUtils.Distance(angle.Vertex, angle.P2) * pixelSize:F2} {unit}"),
                _ => (string.Empty, string.Empty, string.Empty)
            };
        }

        private static double GetPolylineLength(PolylineRoi polyline)
        {
            double length = 0;
            for (int i = 1; i < polyline.Points.Count; i++)
            {
                length += GeometryUtils.Distance(polyline.Points[i - 1], polyline.Points[i]);
            }

            return length;
        }

        private static string Escape(string? value)
        {
            return $"\"{(value ?? string.Empty).Replace("\"", "\"\"")}\"";
        }
    }
}
