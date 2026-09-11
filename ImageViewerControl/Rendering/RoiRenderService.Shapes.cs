using System.Linq;
using System.Windows;
using System.Windows.Media;
using System.Windows.Shapes;
using ImageViewer.Controls;
using ImageViewer.Models;
using ImageViewer.Utils;

namespace ImageViewer.Rendering
{
    public sealed partial class RoiRenderService
    {
        private sealed class RotatedRectRenderer : IRoiRenderer
        {
            public bool CanRender(RoiBase roi) => roi is RotatedRect;

            public void Render(RoiBase roi, RoiRenderContext context, Brush? strokeOverride, bool isSelected)
            {
                var rect = (RotatedRect)roi;
                if (!rect.IsVisible) return;

                Brush brush = RoiRenderContext.ResolveStroke(strokeOverride, rect.StrokeColor);
                context.DrawRectangleOutline(rect.Center, rect.Width, rect.Height, rect.Angle, brush, (isSelected ? 3 : rect.StrokeThickness) / context.Scale, RoiRenderContext.CreateTranslucentFill(brush));

                context.DrawInfoText(StandardRoiInfoTextFormatter.BuildRotatedRectText(rect, context), StandardRoiLayoutHelper.GetTopInfoAnchor(rect.Center, rect.Height / 2, context), brush, true);

                if (isSelected)
                {
                    StandardRoiLayoutHelper.DrawRotatedBoxHandles(context, rect.Center, rect.Width / 2, rect.Height / 2, rect.Angle);
                }
            }
        }

        private sealed class BlobAnalysisRenderer : IRoiRenderer
        {
            public bool CanRender(RoiBase roi) => roi is BlobAnalysisRoi;

            public void Render(RoiBase roi, RoiRenderContext context, Brush? strokeOverride, bool isSelected)
            {
                var blobRoi = (BlobAnalysisRoi)roi;
                if (!blobRoi.IsVisible) return;

                Brush brush = RoiRenderContext.ResolveStroke(strokeOverride, blobRoi.StrokeColor);
                context.DrawRectangleOutline(blobRoi.Center, blobRoi.Width, blobRoi.Height, blobRoi.Angle, brush, (isSelected ? 3 : blobRoi.StrokeThickness) / context.Scale, RoiRenderContext.CreateTranslucentFill(brush));
                context.DrawInfoText(StandardRoiInfoTextFormatter.BuildBlobAnalysisText(blobRoi), StandardRoiLayoutHelper.GetTopInfoAnchor(blobRoi.Center, blobRoi.Height / 2, context), brush, true);

                if (isSelected)
                {
                    StandardRoiLayoutHelper.DrawRotatedBoxHandles(context, blobRoi.Center, blobRoi.Width / 2, blobRoi.Height / 2, blobRoi.Angle);
                }

                Brush blobBrush = Brushes.Magenta;
                foreach (var blob in blobRoi.DetectedBlobs)
                {
                    context.DrawRectangleOutline(
                        new Point(blob.BoundingBox.X + blob.BoundingBox.Width / 2, blob.BoundingBox.Y + blob.BoundingBox.Height / 2),
                        blob.BoundingBox.Width,
                        blob.BoundingBox.Height,
                        0,
                        blobBrush,
                        1.0 / context.Scale);
                    context.DrawHandle(blob.Centroid, ResizeHandle.None, 2 / context.Scale, false, blobBrush);

                    if (isSelected)
                    {
                        context.DrawInfoText(
                            $"#{blob.Label} A:{blob.Area} W:{blob.BoundingBox.Width:F0} H:{blob.BoundingBox.Height:F0}",
                            new Point(blob.BoundingBox.X, blob.BoundingBox.Y),
                            blobBrush);
                    }
                }
            }
        }

        private sealed class FittedEllipseRoiRenderer : IRoiRenderer
        {
            public bool CanRender(RoiBase roi) => roi is FittedEllipseRoi;

            public void Render(RoiBase roi, RoiRenderContext context, Brush? strokeOverride, bool isSelected)
            {
                var ellipse = (FittedEllipseRoi)roi;
                if (!ellipse.IsVisible) return;

                Brush brush = RoiRenderContext.ResolveStroke(strokeOverride, ellipse.StrokeColor);
                context.DrawEllipseOutline(ellipse.Center, ellipse.RadiusX, ellipse.RadiusY, ellipse.Angle, brush, (isSelected ? 3 : ellipse.StrokeThickness) / context.Scale, RoiRenderContext.CreateTranslucentFill(brush));
                context.DrawInfoText(StandardRoiInfoTextFormatter.BuildFittedEllipseText(ellipse, context), StandardRoiLayoutHelper.GetTopInfoAnchor(ellipse.Center, ellipse.RadiusY, context), brush, true);

                if (isSelected)
                {
                    StandardRoiLayoutHelper.DrawRotatedBoxHandles(context, ellipse.Center, ellipse.RadiusX, ellipse.RadiusY, ellipse.Angle);
                }
            }
        }

        private sealed class RingRoiRenderer : IRoiRenderer
        {
            public bool CanRender(RoiBase roi) => roi is RingRoi;

            public void Render(RoiBase roi, RoiRenderContext context, Brush? strokeOverride, bool isSelected)
            {
                var ring = (RingRoi)roi;
                if (!ring.IsVisible || ring.OuterRadius <= 0) return;

                Brush brush = RoiRenderContext.ResolveStroke(strokeOverride, ring.StrokeColor);
                double thickness = (isSelected ? 3 : ring.StrokeThickness) / context.Scale;
                Point center = context.ToScreenPoint(ring.Center);
                double outer = context.ToScreenLength(ring.OuterRadius);
                double inner = context.ToScreenLength(ring.InnerRadius);

                var geometry = new PathGeometry { FillRule = FillRule.EvenOdd };
                geometry.Figures.Add(new EllipseGeometry(center, outer, outer).GetOutlinedPathGeometry().Figures[0]);
                if (inner > 0)
                {
                    geometry.Figures.Add(new EllipseGeometry(center, inner, inner).GetOutlinedPathGeometry().Figures[0]);
                }

                var path = new Path
                {
                    Data = geometry,
                    Fill = RoiRenderContext.CreateTranslucentFill(brush),
                    Stroke = brush,
                    StrokeThickness = Math.Max(1, context.ToScreenLength(thickness)),
                    SnapsToDevicePixels = true,
                    IsHitTestVisible = false
                };
                context.ScreenOverlayCanvas.Children.Add(path);
                context.DrawInfoText(StandardRoiInfoTextFormatter.BuildRingText(ring, context), StandardRoiLayoutHelper.GetTopInfoAnchor(ring.Center, ring.OuterRadius, context), brush, true);

                if (isSelected)
                {
                    StandardRoiLayoutHelper.DrawCircleHandles(context, ring.Center, ring.OuterRadius);
                    context.DrawHandle(new Point(ring.Center.X + ring.InnerRadius, ring.Center.Y), ResizeHandle.P1, context.HandleSize / context.Scale, false, brush);
                }
            }
        }

        private sealed class EllipseRoiRenderer : IRoiRenderer
        {
            public bool CanRender(RoiBase roi) => roi is EllipseRoi;

            public void Render(RoiBase roi, RoiRenderContext context, Brush? strokeOverride, bool isSelected)
            {
                var ellipse = (EllipseRoi)roi;
                if (!ellipse.IsVisible) return;

                Brush brush = RoiRenderContext.ResolveStroke(strokeOverride, ellipse.StrokeColor);
                context.DrawEllipseOutline(ellipse.Center, ellipse.RadiusX, ellipse.RadiusY, ellipse.Angle, brush, (isSelected ? 3 : ellipse.StrokeThickness) / context.Scale, RoiRenderContext.CreateTranslucentFill(brush));
                context.DrawInfoText(StandardRoiInfoTextFormatter.BuildEllipseText(ellipse, context), StandardRoiLayoutHelper.GetTopInfoAnchor(ellipse.Center, ellipse.RadiusY, context), brush, true);

                if (isSelected)
                {
                    StandardRoiLayoutHelper.DrawRotatedBoxHandles(context, ellipse.Center, ellipse.RadiusX, ellipse.RadiusY, ellipse.Angle);
                }
            }
        }

        private sealed class CircleRoiRenderer : IRoiRenderer
        {
            public bool CanRender(RoiBase roi) => roi is CircleRoi;

            public void Render(RoiBase roi, RoiRenderContext context, Brush? strokeOverride, bool isSelected)
            {
                var circle = (CircleRoi)roi;
                if (!circle.IsVisible) return;

                Brush brush = RoiRenderContext.ResolveStroke(strokeOverride, circle.StrokeColor);
                context.DrawEllipseOutline(circle.Center, circle.Radius, circle.Radius, 0, brush, (isSelected ? 3 : circle.StrokeThickness) / context.Scale, RoiRenderContext.CreateTranslucentFill(brush));
                context.DrawInfoText(StandardRoiInfoTextFormatter.BuildCircleText(circle, context), StandardRoiLayoutHelper.GetTopInfoAnchor(circle.Center, circle.Radius, context), brush, true);

                if (isSelected)
                {
                    StandardRoiLayoutHelper.DrawCircleHandles(context, circle.Center, circle.Radius);
                }
            }
        }

        private sealed class PolygonRoiRenderer : IRoiRenderer
        {
            public bool CanRender(RoiBase roi) => roi is PolygonRoi;

            public void Render(RoiBase roi, RoiRenderContext context, Brush? strokeOverride, bool isSelected)
            {
                var poly = (PolygonRoi)roi;
                if (!poly.IsVisible) return;

                Brush brush = RoiRenderContext.ResolveStroke(strokeOverride, poly.StrokeColor);
                if (poly.Points.Count > 1)
                {
                    context.DrawPolyline(poly.Points, brush, (isSelected ? 3 : poly.StrokeThickness) / context.Scale, closed: true, fill: RoiRenderContext.CreateTranslucentFill(brush));

                    if (!poly.IsClosed && poly.Points.Count > 2)
                    {
                        context.DrawLineSegment(poly.Points.Last(), poly.Points.First(), brush, 1 / context.Scale, new DoubleCollection { 4, 4 });
                    }
                }

                if (!isSelected)
                {
                    foreach (var point in poly.Points)
                    {
                        context.DrawDot(point, 4 / context.Scale, brush);
                    }
                }

                if (poly.IsClosed)
                {
                    var metrics = GeometryUtils.GetPolygonMetrics(poly.Points);
                    context.DrawInfoText(StandardRoiInfoTextFormatter.BuildPolygonText(poly, metrics, context), StandardRoiLayoutHelper.GetPolygonInfoAnchor(metrics), brush, true);
                }

                if (isSelected)
                {
                    double handleSize = (context.HandleSize + context.PolygonResizeHandlePadding) / context.Scale;
                    foreach (var point in poly.Points)
                    {
                        context.DrawHandle(point, ResizeHandle.Vertex, handleSize, false);
                    }
                }
            }
        }

        private sealed class PolylineRoiRenderer : IRoiRenderer
        {
            public bool CanRender(RoiBase roi) => roi is PolylineRoi;

            public void Render(RoiBase roi, RoiRenderContext context, Brush? strokeOverride, bool isSelected)
            {
                var polyline = (PolylineRoi)roi;
                if (!polyline.IsVisible || polyline.Points.Count == 0) return;

                Brush brush = RoiRenderContext.ResolveStroke(strokeOverride, polyline.StrokeColor);
                context.DrawPolyline(polyline.Points, brush, (isSelected ? 3 : polyline.StrokeThickness) / context.Scale);

                foreach (var point in polyline.Points)
                {
                    context.DrawHandle(point, ResizeHandle.None, context.PointAnnotationSize / context.Scale, false, brush);
                }

                if (!string.IsNullOrWhiteSpace(polyline.Label))
                {
                    context.DrawInfoText(StandardRoiInfoTextFormatter.BuildPolylineText(polyline), StandardRoiLayoutHelper.GetPolylineInfoAnchor(polyline.Points), brush, true);
                }
            }
        }
    }
}
