using System.Windows;
using System.Windows.Media;
using ImageViewer.Models;
using ImageViewer.Utils;

namespace ImageViewer.Rendering
{
    internal static class DualEdgeCaliperRenderHelper
    {
        public static void DrawDetectionMarkers(RoiRenderContext context, CaliperMeasureRoi caliper, Brush invalidBrush, Brush rejectedBrush)
        {
            SingleEdgeCaliperRenderHelper.DrawEdgeMarkers(context, caliper.InvalidCaliperMarkers, invalidBrush);
            SingleEdgeCaliperRenderHelper.DrawEdgeMarkers(context, caliper.RejectedEdge1Markers, rejectedBrush);
            SingleEdgeCaliperRenderHelper.DrawEdgeMarkers(context, caliper.RejectedEdge2Markers, rejectedBrush);
            SingleEdgeCaliperRenderHelper.DrawEdgeMarkers(context, caliper.Edge1Markers, Brushes.Cyan);
            SingleEdgeCaliperRenderHelper.DrawEdgeMarkers(context, caliper.Edge2Markers, Brushes.Orange);
            SingleEdgeCaliperRenderHelper.DrawScoreOverlays(context, caliper.ScoreOverlays);
        }

        public static void DrawLegend(RoiRenderContext context, CaliperMeasureRoi caliper, Brush invalidBrush, Brush rejectedBrush)
        {
            Point anchor = new(caliper.CaliperCenter.X + 10 / context.Scale, caliper.CaliperCenter.Y - 28 / context.Scale);
            SingleEdgeCaliperRenderHelper.DrawLegend(context, anchor, invalidBrush, rejectedBrush);
        }

        public static string BuildSummaryText(RoiRenderContext context, CaliperMeasureRoi caliper)
        {
            string geometryText = $"D:{context.FormatLength(GeometryUtils.Distance(caliper.P1, caliper.P2))}";
            return SingleEdgeCaliperRenderHelper.BuildSummaryText(caliper, geometryText, "Caliper");
        }
    }
}
