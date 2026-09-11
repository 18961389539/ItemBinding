using System;
using System.Windows;
using System.Windows.Media;
using ImageViewer.Controls;
using ImageViewer.Models;
using ImageViewer.Utils;

namespace ImageViewer.Rendering
{
    public sealed partial class RoiRenderService
    {
        private sealed class LineMeasureRenderer : IRoiRenderer
        {
            public bool CanRender(RoiBase roi) => roi is LineMeasureRoi;

            public void Render(RoiBase roi, RoiRenderContext context, Brush? strokeOverride, bool isSelected)
            {
                var line = (LineMeasureRoi)roi;
                if (!line.IsVisible) return;

                Brush brush = RoiRenderContext.ResolveStroke(strokeOverride, line.StrokeColor);
                context.DrawLineSegment(line.P1, line.P2, brush, (isSelected ? 3 : line.StrokeThickness) / context.Scale);

                double handleSize = context.HandleSize / context.Scale;
                context.DrawHandle(line.P1, isSelected ? ResizeHandle.P1 : ResizeHandle.None, handleSize, false, brush);
                context.DrawHandle(line.P2, isSelected ? ResizeHandle.P2 : ResizeHandle.None, handleSize, false, brush);
                context.DrawInfoText(StandardRoiInfoTextFormatter.BuildLineMeasureText(line, context), new Point((line.P1.X + line.P2.X) / 2, (line.P1.Y + line.P2.Y) / 2), brush, true);
            }
        }

        private sealed class AngleMeasureRenderer : IRoiRenderer
        {
            public bool CanRender(RoiBase roi) => roi is AngleMeasureRoi;

            public void Render(RoiBase roi, RoiRenderContext context, Brush? strokeOverride, bool isSelected)
            {
                var angle = (AngleMeasureRoi)roi;
                if (!angle.IsVisible) return;

                Brush brush = RoiRenderContext.ResolveStroke(strokeOverride, angle.StrokeColor);
                if (angle.P1 == angle.Vertex)
                {
                    context.DrawLineSegment(angle.P1, angle.Vertex, brush, angle.StrokeThickness / context.Scale);
                }
                else
                {
                    context.DrawLineSegment(angle.P1, angle.Vertex, brush, angle.StrokeThickness / context.Scale);
                    context.DrawLineSegment(angle.Vertex, angle.P2, brush, angle.StrokeThickness / context.Scale);

                    double angleValue = GeometryUtils.SmallestAngle(angle.P1, angle.Vertex, angle.P2);
                    double radius = context.AngleArcRadius;
                    context.DrawAngleArc(angle.Vertex, angle.P1, angle.P2, radius, brush);

                    Vector v1 = angle.P1 - angle.Vertex;
                    Vector v2 = angle.P2 - angle.Vertex;
                    v1.Normalize();
                    v2.Normalize();
                    Vector vMid = v1 + v2;
                    if (vMid.LengthSquared < 0.0001)
                    {
                        vMid = new Vector(-v1.Y, v1.X);
                    }
                    else
                    {
                        vMid.Normalize();
                    }

                    Point textPos = angle.Vertex + vMid * ((radius + 10) / context.Scale);
                    context.DrawInfoText(StandardRoiInfoTextFormatter.BuildAngleMeasureText(angle, angleValue), textPos, brush, true);
                }

                double handleSize = context.HandleSize / context.Scale;
                context.DrawHandle(angle.P1, isSelected ? ResizeHandle.P1 : ResizeHandle.None, handleSize, false, brush);
                context.DrawHandle(angle.Vertex, isSelected ? ResizeHandle.Vertex : ResizeHandle.None, handleSize, false, brush);
                context.DrawHandle(angle.P2, isSelected ? ResizeHandle.P2 : ResizeHandle.None, handleSize, false, brush);
            }
        }

        private sealed class ArcMeasureRenderer : IRoiRenderer
        {
            public bool CanRender(RoiBase roi) => roi is ArcMeasureRoi;

            public void Render(RoiBase roi, RoiRenderContext context, Brush? strokeOverride, bool isSelected)
            {
                var arc = (ArcMeasureRoi)roi;
                if (!arc.IsVisible) return;

                Brush brush = RoiRenderContext.ResolveStroke(strokeOverride, arc.StrokeColor);
                double thickness = (isSelected ? 3 : arc.StrokeThickness) / context.Scale;

                if (arc.IsValid)
                {
                    context.DrawArc(arc.Center, arc.Radius, arc.StartAngle, arc.SweepAngle, brush, thickness);
                    context.DrawCircleOutline(arc.Center, 3 / context.Scale, brush, 1 / context.Scale);

                    double midAngle = arc.StartAngle + arc.SweepAngle / 2;
                    double radians = midAngle * Math.PI / 180.0;
                    Point textPos = new(
                        arc.Center.X + (arc.Radius + 15 / context.Scale) * Math.Cos(radians),
                        arc.Center.Y + (arc.Radius + 15 / context.Scale) * Math.Sin(radians));
                    context.DrawInfoText(StandardRoiInfoTextFormatter.BuildArcMeasureText(arc, context), textPos, brush, true);
                }
                else
                {
                    context.DrawLineSegment(arc.StartPoint, arc.EndPoint, brush, thickness);
                    context.DrawDot(arc.ArcPoint, 4 / context.Scale, brush);
                    context.DrawInfoText("无效圆弧", arc.ArcPoint, brush, true);
                }

                double handleSize = context.HandleSize / context.Scale;
                context.DrawHandle(arc.StartPoint, isSelected ? ResizeHandle.P1 : ResizeHandle.None, handleSize, false, brush);
                context.DrawHandle(arc.EndPoint, isSelected ? ResizeHandle.P2 : ResizeHandle.None, handleSize, false, brush);
                context.DrawHandle(arc.ArcPoint, isSelected ? ResizeHandle.Vertex : ResizeHandle.None, handleSize, false, brush);
            }
        }

        private sealed class PointToLineDistanceRenderer : IRoiRenderer
        {
            public bool CanRender(RoiBase roi) => roi is PointToLineDistanceRoi;

            public void Render(RoiBase roi, RoiRenderContext context, Brush? strokeOverride, bool isSelected)
            {
                var p2l = (PointToLineDistanceRoi)roi;
                if (!p2l.IsVisible) return;

                Brush brush = RoiRenderContext.ResolveStroke(strokeOverride, p2l.StrokeColor);
                double thickness = (isSelected ? 3 : p2l.StrokeThickness) / context.Scale;

                context.DrawLineSegment(p2l.LineP1, p2l.LineP2, brush, thickness);
                Point foot = p2l.FootPoint;
                context.DrawLineSegment(p2l.Point, foot, brush, thickness * 0.6);
                context.DrawDot(foot, 3 / context.Scale, brush);

                Point textPos = new((p2l.Point.X + foot.X) / 2, (p2l.Point.Y + foot.Y) / 2 - 10 / context.Scale);
                context.DrawInfoText(StandardRoiInfoTextFormatter.BuildPointToLineDistanceText(p2l, context), textPos, brush, true);

                double handleSize = context.HandleSize / context.Scale;
                context.DrawHandle(p2l.Point, isSelected ? ResizeHandle.P1 : ResizeHandle.None, handleSize, false, brush);
                context.DrawHandle(p2l.LineP1, isSelected ? ResizeHandle.P2 : ResizeHandle.None, handleSize, false, brush);
                context.DrawHandle(p2l.LineP2, isSelected ? ResizeHandle.Vertex : ResizeHandle.None, handleSize, false, brush);
            }
        }

        private sealed class PointToCircleDistanceRenderer : IRoiRenderer
        {
            public bool CanRender(RoiBase roi) => roi is PointToCircleDistanceRoi;

            public void Render(RoiBase roi, RoiRenderContext context, Brush? strokeOverride, bool isSelected)
            {
                var p2c = (PointToCircleDistanceRoi)roi;
                if (!p2c.IsVisible) return;

                Brush brush = RoiRenderContext.ResolveStroke(strokeOverride, p2c.StrokeColor);
                double thickness = (isSelected ? 3 : p2c.StrokeThickness) / context.Scale;

                context.DrawCircleOutline(p2c.Center, p2c.Radius, brush, thickness);
                Point nearest = p2c.NearestPointOnCircle;
                context.DrawLineSegment(p2c.Point, nearest, brush, thickness * 0.6);
                context.DrawDot(nearest, 3 / context.Scale, brush);

                Point textPos = new((p2c.Point.X + nearest.X) / 2, (p2c.Point.Y + nearest.Y) / 2 - 10 / context.Scale);
                context.DrawInfoText(StandardRoiInfoTextFormatter.BuildPointToCircleDistanceText(p2c, context), textPos, brush, true);

                double handleSize = context.HandleSize / context.Scale;
                context.DrawHandle(p2c.Point, isSelected ? ResizeHandle.P1 : ResizeHandle.None, handleSize, false, brush);
                context.DrawHandle(p2c.Center, isSelected ? ResizeHandle.P2 : ResizeHandle.None, handleSize, false, brush);
            }
        }

        private sealed class ParallelismMeasureRenderer : IRoiRenderer
        {
            public bool CanRender(RoiBase roi) => roi is ParallelismMeasureRoi;

            public void Render(RoiBase roi, RoiRenderContext context, Brush? strokeOverride, bool isSelected)
            {
                var para = (ParallelismMeasureRoi)roi;
                if (!para.IsVisible) return;

                Brush brush = RoiRenderContext.ResolveStroke(strokeOverride, para.StrokeColor);
                double thickness = (isSelected ? 3 : para.StrokeThickness) / context.Scale;

                context.DrawLineSegment(para.Line1P1, para.Line1P2, brush, thickness);
                context.DrawLineSegment(para.Line2P1, para.Line2P2, brush, thickness);

                Point midpoint = new(
                    (para.Line1P1.X + para.Line1P2.X + para.Line2P1.X + para.Line2P2.X) / 4,
                    (para.Line1P1.Y + para.Line1P2.Y + para.Line2P1.Y + para.Line2P2.Y) / 4);
                context.DrawInfoText(StandardRoiInfoTextFormatter.BuildParallelismText(para, context), midpoint, brush, true);

                double handleSize = context.HandleSize / context.Scale;
                context.DrawHandle(para.Line1P1, isSelected ? ResizeHandle.P1 : ResizeHandle.None, handleSize, false, brush);
                context.DrawHandle(para.Line1P2, isSelected ? ResizeHandle.P2 : ResizeHandle.None, handleSize, false, brush);
                context.DrawHandle(para.Line2P1, isSelected ? ResizeHandle.Vertex : ResizeHandle.None, handleSize, false, brush);
                context.DrawHandle(para.Line2P2, isSelected ? ResizeHandle.P3 : ResizeHandle.None, handleSize, false, brush);
            }
        }

        private sealed class PerpendicularityMeasureRenderer : IRoiRenderer
        {
            public bool CanRender(RoiBase roi) => roi is PerpendicularityMeasureRoi;

            public void Render(RoiBase roi, RoiRenderContext context, Brush? strokeOverride, bool isSelected)
            {
                var perp = (PerpendicularityMeasureRoi)roi;
                if (!perp.IsVisible) return;

                Brush brush = RoiRenderContext.ResolveStroke(strokeOverride, perp.StrokeColor);
                double thickness = (isSelected ? 3 : perp.StrokeThickness) / context.Scale;

                context.DrawLineSegment(perp.Line1P1, perp.Line1P2, brush, thickness);
                context.DrawLineSegment(perp.Line2P1, perp.Line2P2, brush, thickness);

                if (perp.IntersectionPoint.HasValue)
                {
                    context.DrawDot(perp.IntersectionPoint.Value, 4 / context.Scale, brush);
                }

                Point midpoint = new(
                    (perp.Line1P1.X + perp.Line1P2.X + perp.Line2P1.X + perp.Line2P2.X) / 4,
                    (perp.Line1P1.Y + perp.Line1P2.Y + perp.Line2P1.Y + perp.Line2P2.Y) / 4);
                context.DrawInfoText(StandardRoiInfoTextFormatter.BuildPerpendicularityText(perp), midpoint, brush, true);

                double handleSize = context.HandleSize / context.Scale;
                context.DrawHandle(perp.Line1P1, isSelected ? ResizeHandle.P1 : ResizeHandle.None, handleSize, false, brush);
                context.DrawHandle(perp.Line1P2, isSelected ? ResizeHandle.P2 : ResizeHandle.None, handleSize, false, brush);
                context.DrawHandle(perp.Line2P1, isSelected ? ResizeHandle.Vertex : ResizeHandle.None, handleSize, false, brush);
                context.DrawHandle(perp.Line2P2, isSelected ? ResizeHandle.P3 : ResizeHandle.None, handleSize, false, brush);
            }
        }

        private sealed class ConcentricityMeasureRenderer : IRoiRenderer
        {
            public bool CanRender(RoiBase roi) => roi is ConcentricityMeasureRoi;

            public void Render(RoiBase roi, RoiRenderContext context, Brush? strokeOverride, bool isSelected)
            {
                var conc = (ConcentricityMeasureRoi)roi;
                if (!conc.IsVisible) return;

                Brush brush = RoiRenderContext.ResolveStroke(strokeOverride, conc.StrokeColor);
                double thickness = (isSelected ? 3 : conc.StrokeThickness) / context.Scale;

                context.DrawCircleOutline(conc.Center1, conc.Radius1, brush, thickness);
                context.DrawCircleOutline(conc.Center2, conc.Radius2, brush, thickness);
                context.DrawLineSegment(conc.Center1, conc.Center2, brush, thickness * 0.5);
                context.DrawDot(conc.Center1, 3 / context.Scale, brush);
                context.DrawDot(conc.Center2, 3 / context.Scale, brush);
                context.DrawInfoText(StandardRoiInfoTextFormatter.BuildConcentricityText(conc, context), conc.MidCenter, brush, true);

                double handleSize = context.HandleSize / context.Scale;
                context.DrawHandle(conc.Center1, isSelected ? ResizeHandle.P1 : ResizeHandle.None, handleSize, false, brush);
                context.DrawHandle(conc.Center2, isSelected ? ResizeHandle.P2 : ResizeHandle.None, handleSize, false, brush);
            }
        }
    }
}
