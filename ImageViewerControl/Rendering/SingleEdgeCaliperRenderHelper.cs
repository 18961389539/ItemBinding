using System.Collections.Generic;
using System.Windows;
using System.Windows.Media;
using ImageViewer.Models;
using ImageViewer.Services;

namespace ImageViewer.Rendering
{
    internal static class SingleEdgeCaliperRenderHelper
    {
        public static void DrawEdgeMarkers(RoiRenderContext context, IReadOnlyList<LineSegmentOverlay> markers, Brush brush)
        {
            foreach (LineSegmentOverlay marker in markers)
            {
                context.DrawLineSegment(marker.Start, marker.End, brush, 1.4 / context.Scale);
            }
        }

        public static void DrawScoreOverlays(RoiRenderContext context, IReadOnlyList<CaliperScoreOverlay> overlays)
        {
            if (!context.ShowCaliperScores)
            {
                return;
            }

            foreach (CaliperScoreOverlay overlay in overlays)
            {
                Brush brush = overlay.Status switch
                {
                    CaliperOverlayStatus.Invalid => Brushes.Gray,
                    CaliperOverlayStatus.Rejected => Brushes.IndianRed,
                    _ => Brushes.Gold
                };

                context.DrawInfoText(overlay.Text, overlay.Position, brush, true);
            }
        }

        public static void DrawLegend(RoiRenderContext context, Point anchor, Brush invalidBrush, Brush rejectedBrush)
        {
            DrawLegendItem(context, anchor, Brushes.Cyan, "有效点");
            DrawLegendItem(context, new Point(anchor.X, anchor.Y + 12 / context.Scale), rejectedBrush, "剔除点");
            DrawLegendItem(context, new Point(anchor.X, anchor.Y + 24 / context.Scale), invalidBrush, "无效点");
        }

        public static string BuildSummaryText(ICaliperInfoSource caliper, string geometryText, string fallbackText)
        {
            string info = string.IsNullOrEmpty(caliper.Label) ? string.Empty : $"{caliper.Label}: ";
            info += caliper.HasDetection
                ? geometryText
                : fallbackText;
            if (caliper.HasDetection)
            {
                info += $"\nFit:{caliper.ValidCaliperCount} Score:{ImageAnalysisService.NormalizeCaliperScore(caliper.AverageScore):F0}% RMS:{caliper.ResidualRms:F2}";
            }

            return info;
        }

        private static void DrawLegendItem(RoiRenderContext context, Point start, Brush brush, string text)
        {
            context.DrawLineSegment(start, new Point(start.X + 10 / context.Scale, start.Y), brush, 2 / context.Scale);
            context.DrawInfoText(text, new Point(start.X + 14 / context.Scale, start.Y - 5 / context.Scale), brush);
        }
    }
}
