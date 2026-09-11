using System.Windows;
using System.Windows.Media;
using System.Windows.Shapes;
using ImageViewer.Models;
using ImageViewer.Utils;

namespace ImageViewer.Controls
{
    public partial class ImageViewer
    {
        private void DrawLineMeasure(LineMeasureRoi line, Brush stroke, bool isSelected)
        {
            if (!line.IsVisible) return;
            Brush brush = ResolveStroke(stroke, line.StrokeColor);

            var shape = new Line
            {
                X1 = line.P1.X,
                Y1 = line.P1.Y,
                X2 = line.P2.X,
                Y2 = line.P2.Y,
                Stroke = brush,
                StrokeThickness = (isSelected ? 3 : line.StrokeThickness) / Scale,
                IsHitTestVisible = false
            };
            overlayCanvas.Children.Add(shape);

            double hSize = HandleSize / Scale;
            DrawHandle(line.P1, isSelected ? ResizeHandle.P1 : ResizeHandle.None, hSize, false, brush);
            DrawHandle(line.P2, isSelected ? ResizeHandle.P2 : ResizeHandle.None, hSize, false, brush);

            double dist = GeometryUtils.Distance(line.P1, line.P2);
            string info = string.IsNullOrEmpty(line.Label) ? string.Empty : $"{line.Label}: ";
            info += $"D:{FormatLength(dist)} ΔX:{FormatLength(line.P2.X - line.P1.X)} ΔY:{FormatLength(line.P2.Y - line.P1.Y)}";
            DrawInfoText(info, new Point((line.P1.X + line.P2.X) / 2, (line.P1.Y + line.P2.Y) / 2), brush, true);
        }

        private void DrawAngleMeasure(AngleMeasureRoi angle, Brush stroke, bool isSelected)
        {
            if (!angle.IsVisible) return;
            Brush brush = ResolveStroke(stroke, angle.StrokeColor);

            if (angle.P1 == angle.Vertex)
            {
                var line1 = new Line { X1 = angle.P1.X, Y1 = angle.P1.Y, X2 = angle.Vertex.X, Y2 = angle.Vertex.Y, Stroke = brush, StrokeThickness = angle.StrokeThickness / Scale, IsHitTestVisible = false };
                overlayCanvas.Children.Add(line1);
            }
            else
            {
                var line1 = new Line { X1 = angle.P1.X, Y1 = angle.P1.Y, X2 = angle.Vertex.X, Y2 = angle.Vertex.Y, Stroke = brush, StrokeThickness = angle.StrokeThickness / Scale, IsHitTestVisible = false };
                var line2 = new Line { X1 = angle.Vertex.X, Y1 = angle.Vertex.Y, X2 = angle.P2.X, Y2 = angle.P2.Y, Stroke = brush, StrokeThickness = angle.StrokeThickness / Scale, IsHitTestVisible = false };
                overlayCanvas.Children.Add(line1);
                overlayCanvas.Children.Add(line2);

                double angleVal = GeometryUtils.SmallestAngle(angle.P1, angle.Vertex, angle.P2);
                string info = string.IsNullOrEmpty(angle.Label) ? string.Empty : $"{angle.Label}: ";
                info += $"{angleVal:F1}° L1:{FormatLength(GeometryUtils.Distance(angle.P1, angle.Vertex))} L2:{FormatLength(GeometryUtils.Distance(angle.Vertex, angle.P2))}";

                double radius = AngleArcRadius / Scale;
                DrawAngleArc(angle.Vertex, angle.P1, angle.P2, radius, brush);

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

                Point textPos = angle.Vertex + vMid * (radius + HitTestTolerance * 2 / Scale);
                DrawInfoText(info, textPos, brush, true);
            }

            double hSize = HandleSize / Scale;
            DrawHandle(angle.P1, isSelected ? ResizeHandle.P1 : ResizeHandle.None, hSize, false, brush);
            DrawHandle(angle.Vertex, isSelected ? ResizeHandle.Vertex : ResizeHandle.None, hSize, false, brush);
            DrawHandle(angle.P2, isSelected ? ResizeHandle.P2 : ResizeHandle.None, hSize, false, brush);
        }

        private void DrawAngleArc(Point center, Point p1, Point p2, double radius, Brush brush)
        {
            Vector v1 = p1 - center;
            Vector v2 = p2 - center;
            v1.Normalize();
            v2.Normalize();

            Point startPoint = center + v1 * radius;
            Point endPoint = center + v2 * radius;

            double crossProduct = v1.X * v2.Y - v1.Y * v2.X;
            SweepDirection sweepDir = crossProduct > 0 ? SweepDirection.Clockwise : SweepDirection.Counterclockwise;

            var path = new Path
            {
                Stroke = brush,
                StrokeThickness = 1 / Scale,
                IsHitTestVisible = false
            };

            var geometry = new StreamGeometry();
            using (var ctx = geometry.Open())
            {
                ctx.BeginFigure(startPoint, false, false);
                ctx.ArcTo(endPoint, new Size(radius, radius), 0, false, sweepDir, true, false);
            }
            path.Data = geometry;
            overlayCanvas.Children.Add(path);
        }
    }
}
