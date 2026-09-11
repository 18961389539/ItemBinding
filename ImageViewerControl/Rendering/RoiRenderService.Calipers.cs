using System;
using System.Collections.Generic;
using System.Windows;
using System.Windows.Media;
using ImageViewer.Controls;
using ImageViewer.Models;
using ImageViewer.Utils;

namespace ImageViewer.Rendering
{
    public sealed partial class RoiRenderService
    {
        private sealed class CircularCaliperMeasureRenderer : IRoiRenderer
        {
            public bool CanRender(RoiBase roi) => roi is CircularCaliperMeasureRoi;

            public void Render(RoiBase roi, RoiRenderContext context, Brush? strokeOverride, bool isSelected)
            {
                var caliper = (CircularCaliperMeasureRoi)roi;
                if (!caliper.IsVisible) return;

                Brush brush = RoiRenderContext.ResolveStroke(strokeOverride, caliper.StrokeColor);
                Brush barBrush = Brushes.Gold;
                Brush invalidBrush = Brushes.Gray;
                Brush rejectedBrush = Brushes.IndianRed;

                IEnumerable<LineSegmentOverlay> previewSegments = caliper.RegionSegments.Length > 0
                    ? caliper.RegionSegments
                    : CaliperOverlayGeometryHelper.BuildCircularCaliperRegionSegments(caliper);
                IEnumerable<LineSegmentOverlay> previewBars = caliper.CaliperBars.Length > 0
                    ? caliper.CaliperBars
                    : CaliperOverlayGeometryHelper.BuildCircularCaliperBars(caliper);

                foreach (LineSegmentOverlay segment in previewSegments)
                {
                    context.DrawLineSegment(segment.Start, segment.End, barBrush, 1.1 / context.Scale, new DoubleCollection { 3, 2 });
                }

                foreach (LineSegmentOverlay segment in previewBars)
                {
                    context.DrawLineSegment(segment.Start, segment.End, barBrush, 0.8 / context.Scale, opacity: 0.7);
                }

                SingleEdgeCaliperRenderHelper.DrawEdgeMarkers(context, caliper.InvalidCaliperMarkers, invalidBrush);
                SingleEdgeCaliperRenderHelper.DrawEdgeMarkers(context, caliper.RejectedEdgeMarkers, rejectedBrush);
                SingleEdgeCaliperRenderHelper.DrawEdgeMarkers(context, caliper.EdgeMarkers, Brushes.Cyan);
                SingleEdgeCaliperRenderHelper.DrawScoreOverlays(context, caliper.ScoreOverlays);
                SingleEdgeCaliperRenderHelper.DrawLegend(context, new Point(caliper.Center.X + caliper.Radius / 2, caliper.Center.Y - caliper.Radius / 2), invalidBrush, rejectedBrush);

                context.DrawEllipseOutline(caliper.Center, caliper.Radius, caliper.Radius, 0, brush, (isSelected ? 3 : caliper.StrokeThickness) / context.Scale);

                if (isSelected)
                {
                    StandardRoiLayoutHelper.DrawCircleHandles(context, caliper.Center, caliper.Radius);
                }

                string info = SingleEdgeCaliperRenderHelper.BuildSummaryText(caliper, $"R:{context.FormatLength(caliper.Radius)}", "Circular Caliper");
                context.DrawInfoText(info, StandardRoiLayoutHelper.GetTopInfoAnchor(caliper.Center, caliper.Radius, context), brush, true);
            }
        }

        private sealed class ArcCaliperMeasureRenderer : IRoiRenderer
        {
            public bool CanRender(RoiBase roi) => roi is ArcCaliperMeasureRoi;

            public void Render(RoiBase roi, RoiRenderContext context, Brush? strokeOverride, bool isSelected)
            {
                var caliper = (ArcCaliperMeasureRoi)roi;
                if (!caliper.IsVisible) return;

                Brush brush = RoiRenderContext.ResolveStroke(strokeOverride, caliper.StrokeColor);
                Brush barBrush = Brushes.Gold;
                Brush invalidBrush = Brushes.Gray;
                Brush rejectedBrush = Brushes.IndianRed;

                IEnumerable<LineSegmentOverlay> previewSegments = caliper.RegionSegments.Length > 0
                    ? caliper.RegionSegments
                    : CaliperOverlayGeometryHelper.BuildArcCaliperRegionSegments(caliper);
                IEnumerable<LineSegmentOverlay> previewBars = caliper.CaliperBars.Length > 0
                    ? caliper.CaliperBars
                    : CaliperOverlayGeometryHelper.BuildArcCaliperBars(caliper);

                foreach (LineSegmentOverlay segment in previewSegments)
                {
                    context.DrawLineSegment(segment.Start, segment.End, barBrush, 1.1 / context.Scale, new DoubleCollection { 3, 2 });
                }

                foreach (LineSegmentOverlay segment in previewBars)
                {
                    context.DrawLineSegment(segment.Start, segment.End, barBrush, 0.8 / context.Scale, opacity: 0.7);
                }

                SingleEdgeCaliperRenderHelper.DrawEdgeMarkers(context, caliper.InvalidCaliperMarkers, invalidBrush);
                SingleEdgeCaliperRenderHelper.DrawEdgeMarkers(context, caliper.RejectedEdgeMarkers, rejectedBrush);
                SingleEdgeCaliperRenderHelper.DrawEdgeMarkers(context, caliper.EdgeMarkers, Brushes.Cyan);
                SingleEdgeCaliperRenderHelper.DrawScoreOverlays(context, caliper.ScoreOverlays);

                context.DrawArc(caliper.Center, caliper.Radius, caliper.StartAngle, caliper.SweepAngle, brush, (isSelected ? 3 : caliper.StrokeThickness) / context.Scale);
                DrawArcEndpoints(context, caliper, brush, (isSelected ? 3 : caliper.StrokeThickness) / context.Scale);

                if (isSelected)
                {
                    StandardRoiLayoutHelper.DrawCircleHandles(context, caliper.Center, caliper.Radius);
                }

                string info = SingleEdgeCaliperRenderHelper.BuildSummaryText(caliper, $"R:{context.FormatLength(caliper.Radius)} Arc:{caliper.SweepAngle:F0}°", "Arc Caliper");
                context.DrawInfoText(info, StandardRoiLayoutHelper.GetTopInfoAnchor(caliper.Center, caliper.Radius, context), brush, true);
            }

            private static void DrawArcEndpoints(RoiRenderContext context, ArcCaliperMeasureRoi caliper, Brush brush, double thickness)
            {
                double startRadians = caliper.StartAngle * Math.PI / 180.0;
                double endRadians = (caliper.StartAngle + caliper.SweepAngle) * Math.PI / 180.0;
                Point start = new(caliper.Center.X + Math.Cos(startRadians) * caliper.Radius, caliper.Center.Y + Math.Sin(startRadians) * caliper.Radius);
                Point end = new(caliper.Center.X + Math.Cos(endRadians) * caliper.Radius, caliper.Center.Y + Math.Sin(endRadians) * caliper.Radius);
                context.DrawLineSegment(caliper.Center, start, brush, thickness * 0.6);
                context.DrawLineSegment(caliper.Center, end, brush, thickness * 0.6);
            }
        }

        private sealed class LineCaliperMeasureRenderer : IRoiRenderer
        {
            public bool CanRender(RoiBase roi) => roi is LineCaliperMeasureRoi;

            public void Render(RoiBase roi, RoiRenderContext context, Brush? strokeOverride, bool isSelected)
            {
                var line = (LineCaliperMeasureRoi)roi;
                if (!line.IsVisible) return;

                Brush brush = RoiRenderContext.ResolveStroke(strokeOverride, line.StrokeColor);
                Brush edgeBrush = Brushes.Cyan;
                Brush barBrush = Brushes.Gold;
                Brush invalidBrush = Brushes.Gray;
                Brush rejectedBrush = Brushes.IndianRed;

                LineSegmentOverlay[] previewSegments = line.RegionSegments.Length > 0
                    ? line.RegionSegments
                    : CaliperOverlayGeometryHelper.BuildLineCaliperRegionSegments(line);
                LineSegmentOverlay[] previewBars = line.CaliperBars.Length > 0
                    ? line.CaliperBars
                    : CaliperOverlayGeometryHelper.BuildLineCaliperBars(line);

                foreach (LineSegmentOverlay segment in previewSegments)
                {
                    context.DrawLineSegment(segment.Start, segment.End, barBrush, 1.2 / context.Scale, new DoubleCollection { 3, 2 });
                }

                foreach (LineSegmentOverlay segment in previewBars)
                {
                    context.DrawLineSegment(segment.Start, segment.End, barBrush, 0.8 / context.Scale, opacity: 0.7);
                }

                SingleEdgeCaliperRenderHelper.DrawEdgeMarkers(context, line.InvalidCaliperMarkers, invalidBrush);
                SingleEdgeCaliperRenderHelper.DrawEdgeMarkers(context, line.RejectedEdgeMarkers, rejectedBrush);
                SingleEdgeCaliperRenderHelper.DrawEdgeMarkers(context, line.EdgeMarkers, edgeBrush);
                SingleEdgeCaliperRenderHelper.DrawScoreOverlays(context, line.ScoreOverlays);
                SingleEdgeCaliperRenderHelper.DrawLegend(context, new Point((line.P1.X + line.P2.X) / 2 + 10 / context.Scale, (line.P1.Y + line.P2.Y) / 2 - 28 / context.Scale), invalidBrush, rejectedBrush);

                context.DrawLineSegment(line.P1, line.P2, brush, (isSelected ? 3 : line.StrokeThickness) / context.Scale);

                double handleSize = context.HandleSize / context.Scale;
                context.DrawHandle(line.P1, isSelected ? ResizeHandle.P1 : ResizeHandle.None, handleSize, false, brush);
                context.DrawHandle(line.P2, isSelected ? ResizeHandle.P2 : ResizeHandle.None, handleSize, false, brush);

                string info = SingleEdgeCaliperRenderHelper.BuildSummaryText(line, $"D:{context.FormatLength(GeometryUtils.Distance(line.P1, line.P2))}", "Line Caliper");
                context.DrawInfoText(info, new Point((line.P1.X + line.P2.X) / 2, (line.P1.Y + line.P2.Y) / 2), brush, true);
            }
        }

        private sealed class CaliperMeasureRenderer : IRoiRenderer
        {
            public bool CanRender(RoiBase roi) => roi is CaliperMeasureRoi;

            public void Render(RoiBase roi, RoiRenderContext context, Brush? strokeOverride, bool isSelected)
            {
                var caliper = (CaliperMeasureRoi)roi;
                if (!caliper.IsVisible) return;

                caliper.EnsureCaliperRegion();
                Brush brush = RoiRenderContext.ResolveStroke(strokeOverride, caliper.StrokeColor);
                Brush edgeBrush = Brushes.Lime;
                Brush barBrush = Brushes.Gold;
                Brush invalidBrush = Brushes.Gray;
                Brush rejectedBrush = Brushes.IndianRed;

                LineSegmentOverlay[] previewSegments = caliper.RegionSegments.Length > 0
                    ? caliper.RegionSegments
                    : CaliperOverlayGeometryHelper.BuildDualEdgeCaliperRegionSegments(caliper);
                LineSegmentOverlay[] previewBars = caliper.CaliperBars.Length > 0
                    ? caliper.CaliperBars
                    : CaliperOverlayGeometryHelper.BuildDualEdgeCaliperBars(caliper);

                foreach (LineSegmentOverlay segment in previewSegments)
                {
                    context.DrawLineSegment(segment.Start, segment.End, barBrush, 1.2 / context.Scale, new DoubleCollection { 3, 2 });
                }

                foreach (LineSegmentOverlay segment in previewBars)
                {
                    context.DrawLineSegment(segment.Start, segment.End, barBrush, 0.8 / context.Scale, opacity: 0.7);
                }

                DualEdgeCaliperRenderHelper.DrawDetectionMarkers(context, caliper, invalidBrush, rejectedBrush);
                DualEdgeCaliperRenderHelper.DrawLegend(context, caliper, invalidBrush, rejectedBrush);

                if (caliper.HasDetectedEdges)
                {
                    context.DrawLineSegment(caliper.P1, caliper.P2, brush, (isSelected ? 3 : caliper.StrokeThickness) / context.Scale);
                    context.DrawLineSegment(caliper.Edge1Start, caliper.Edge1End, edgeBrush, 1.8 / context.Scale);
                    context.DrawLineSegment(caliper.Edge2Start, caliper.Edge2End, edgeBrush, 1.8 / context.Scale);
                }

                if (isSelected)
                {
                    DrawResizeHandles(context, caliper);
                }

                context.DrawInfoText(DualEdgeCaliperRenderHelper.BuildSummaryText(context, caliper), caliper.CaliperCenter, brush, true);
            }

            private static void DrawResizeHandles(RoiRenderContext context, CaliperMeasureRoi caliper)
            {
                double halfWidth = caliper.GetResolvedCaliperRegionLength() / 2;
                double halfHeight = caliper.CaliperSearchRange;
                StandardRoiLayoutHelper.DrawRotatedBoxHandles(context, caliper.CaliperCenter, halfWidth, halfHeight, caliper.CaliperAngleDegrees + 90);
            }
        }
    }
}
