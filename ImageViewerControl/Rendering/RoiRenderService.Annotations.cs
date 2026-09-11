using System.Windows;
using System.Windows.Media;
using ImageViewer.Controls;
using ImageViewer.Models;

namespace ImageViewer.Rendering
{
    public sealed partial class RoiRenderService
    {
        private sealed class PointAnnotationRenderer : IRoiRenderer
        {
            public bool CanRender(RoiBase roi) => roi is PointAnnotationRoi;

            public void Render(RoiBase roi, RoiRenderContext context, Brush? strokeOverride, bool isSelected)
            {
                var annotation = (PointAnnotationRoi)roi;
                if (!annotation.IsVisible) return;

                Brush brush = RoiRenderContext.ResolveStroke(strokeOverride, annotation.StrokeColor);
                double size = (isSelected ? context.PointAnnotationSize + 2 : context.PointAnnotationSize) / context.Scale;
                context.DrawHandle(annotation.Position, ResizeHandle.None, size, false, brush);
                if (!string.IsNullOrWhiteSpace(annotation.Label))
                {
                    context.DrawInfoText(StandardRoiInfoTextFormatter.BuildPointAnnotationText(annotation), StandardRoiLayoutHelper.GetAnnotationInfoAnchor(annotation.Position, context), brush);
                }
            }
        }

        private sealed class TextAnnotationRenderer : IRoiRenderer
        {
            public bool CanRender(RoiBase roi) => roi is TextAnnotationRoi;

            public void Render(RoiBase roi, RoiRenderContext context, Brush? strokeOverride, bool isSelected)
            {
                var annotation = (TextAnnotationRoi)roi;
                if (!annotation.IsVisible || string.IsNullOrWhiteSpace(annotation.Label)) return;

                Brush brush = RoiRenderContext.ResolveStroke(strokeOverride, annotation.StrokeColor);
                context.DrawHandle(annotation.Position, ResizeHandle.None, context.PointAnnotationSize / context.Scale, false, brush);
                context.DrawInfoText(StandardRoiInfoTextFormatter.BuildTextAnnotationText(annotation), StandardRoiLayoutHelper.GetAnnotationInfoAnchor(annotation.Position, context), brush);
                if (isSelected)
                {
                    context.DrawHandle(annotation.Position, ResizeHandle.None, (context.PointAnnotationSize + 2) / context.Scale, true, brush);
                }
            }
        }

        private sealed class ArrowAnnotationRenderer : IRoiRenderer
        {
            public bool CanRender(RoiBase roi) => roi is ArrowAnnotationRoi;

            public void Render(RoiBase roi, RoiRenderContext context, Brush? strokeOverride, bool isSelected)
            {
                var arrow = (ArrowAnnotationRoi)roi;
                if (!arrow.IsVisible) return;

                Brush brush = RoiRenderContext.ResolveStroke(strokeOverride, arrow.StrokeColor);
                double thickness = (isSelected ? 3 : arrow.StrokeThickness) / context.Scale;
                context.DrawLineSegment(arrow.P1, arrow.P2, brush, thickness);

                Vector direction = arrow.P1 - arrow.P2;
                if (direction.LengthSquared > 1e-6)
                {
                    direction.Normalize();
                    Vector normal = new(-direction.Y, direction.X);
                    double headLength = arrow.ArrowHeadLength / context.Scale;
                    Point left = arrow.P2 + direction * headLength + normal * (headLength * 0.45);
                    Point right = arrow.P2 + direction * headLength - normal * (headLength * 0.45);
                    context.DrawLineSegment(arrow.P2, left, brush, thickness);
                    context.DrawLineSegment(arrow.P2, right, brush, thickness);
                }

                double handleSize = context.HandleSize / context.Scale;
                context.DrawHandle(arrow.P1, isSelected ? ResizeHandle.P1 : ResizeHandle.None, handleSize, false, brush);
                context.DrawHandle(arrow.P2, isSelected ? ResizeHandle.P2 : ResizeHandle.None, handleSize, false, brush);

                if (!string.IsNullOrWhiteSpace(arrow.Label))
                {
                    context.DrawInfoText(StandardRoiInfoTextFormatter.BuildArrowAnnotationText(arrow), StandardRoiLayoutHelper.GetMidpointInfoAnchor(arrow.P1, arrow.P2), brush, true);
                }
            }
        }
    }
}
