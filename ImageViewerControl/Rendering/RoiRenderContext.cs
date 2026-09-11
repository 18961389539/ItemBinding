using System;
using System.Collections.Generic;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Shapes;
using ImageViewer.Controls;

namespace ImageViewer.Rendering
{
    public sealed class RoiRenderContext
    {
        private static readonly Dictionary<Color, Brush> StrokeBrushCache = new();

        public RoiRenderContext(
            Canvas overlayCanvas,
            Canvas screenOverlayCanvas,
            Func<Point, Point> imageToScreen,
            double scale,
            double pixelSize,
            string? physicalUnit,
            bool showCaliperScores,
            double handleSize,
            double infoTextOffset,
            double angleArcRadius,
            double pointAnnotationSize,
            double polygonResizeHandlePadding)
        {
            OverlayCanvas = overlayCanvas;
            ScreenOverlayCanvas = screenOverlayCanvas;
            ImageToScreen = imageToScreen;
            Scale = scale;
            PixelSize = pixelSize;
            PhysicalUnit = physicalUnit;
            ShowCaliperScores = showCaliperScores;
            HandleSize = handleSize;
            InfoTextOffset = infoTextOffset;
            AngleArcRadius = angleArcRadius;
            PointAnnotationSize = pointAnnotationSize;
            PolygonResizeHandlePadding = polygonResizeHandlePadding;
        }

        public Canvas OverlayCanvas { get; }
        public Canvas ScreenOverlayCanvas { get; }
        public Func<Point, Point> ImageToScreen { get; }
        public double Scale { get; }
        public double PixelSize { get; }
        public string? PhysicalUnit { get; }
        public bool ShowCaliperScores { get; }
        public double HandleSize { get; }
        public double InfoTextOffset { get; }
        public double AngleArcRadius { get; }
        public double PointAnnotationSize { get; }
        public double PolygonResizeHandlePadding { get; }

        public double ToScreenLength(double imageLength)
        {
            return imageLength * Scale;
        }

        public Point ToScreenPoint(Point imagePoint)
        {
            return ImageToScreen(imagePoint);
        }

        public void DrawLineSegment(Point start, Point end, Brush stroke, double thickness, DoubleCollection? dashArray = null, double opacity = 1.0)
        {
            Point screenStart = ToScreenPoint(start);
            Point screenEnd = ToScreenPoint(end);
            double screenThickness = Math.Max(1, ToScreenLength(thickness));

            var line = new Line
            {
                X1 = AlignCoordinate(screenStart.X, screenThickness),
                Y1 = AlignCoordinate(screenStart.Y, screenThickness),
                X2 = AlignCoordinate(screenEnd.X, screenThickness),
                Y2 = AlignCoordinate(screenEnd.Y, screenThickness),
                Stroke = stroke,
                StrokeThickness = screenThickness,
                Opacity = opacity,
                SnapsToDevicePixels = true,
                IsHitTestVisible = false
            };

            if (dashArray != null)
            {
                line.StrokeDashArray = dashArray;
            }

            ScreenOverlayCanvas.Children.Add(line);
        }

        public void DrawRectangleOutline(Point center, double width, double height, double angle, Brush stroke, double thickness, Brush? fill = null)
        {
            double screenWidth = ToScreenLength(width);
            double screenHeight = ToScreenLength(height);
            Point screenCenter = ToScreenPoint(center);

            var shape = new Rectangle
            {
                Width = screenWidth,
                Height = screenHeight,
                Fill = fill,
                Stroke = stroke,
                StrokeThickness = Math.Max(1, ToScreenLength(thickness)),
                SnapsToDevicePixels = true,
                IsHitTestVisible = false,
                RenderTransform = new RotateTransform(angle, screenWidth / 2, screenHeight / 2)
            };

            Canvas.SetLeft(shape, screenCenter.X - screenWidth / 2);
            Canvas.SetTop(shape, screenCenter.Y - screenHeight / 2);
            ScreenOverlayCanvas.Children.Add(shape);
        }

        public void DrawEllipseOutline(Point center, double radiusX, double radiusY, double angle, Brush stroke, double thickness, Brush? fill = null)
        {
            double screenWidth = ToScreenLength(radiusX * 2);
            double screenHeight = ToScreenLength(radiusY * 2);
            Point screenCenter = ToScreenPoint(center);

            var shape = new Ellipse
            {
                Width = screenWidth,
                Height = screenHeight,
                Fill = fill,
                Stroke = stroke,
                StrokeThickness = Math.Max(1, ToScreenLength(thickness)),
                SnapsToDevicePixels = true,
                IsHitTestVisible = false,
                RenderTransform = new RotateTransform(angle, screenWidth / 2, screenHeight / 2)
            };

            Canvas.SetLeft(shape, screenCenter.X - screenWidth / 2);
            Canvas.SetTop(shape, screenCenter.Y - screenHeight / 2);
            ScreenOverlayCanvas.Children.Add(shape);
        }

        public void DrawPolyline(IReadOnlyList<Point> points, Brush stroke, double thickness, bool closed = false, DoubleCollection? dashArray = null, Brush? fill = null)
        {
            if (points.Count == 0)
            {
                return;
            }

            var screenPoints = new PointCollection(points.Select(ToScreenPoint));
            Shape shape = closed
                ? new Polygon { Points = screenPoints }
                : new Polyline { Points = screenPoints };

            shape.Stroke = stroke;
            if (closed && shape is Polygon polygon)
            {
                polygon.Fill = fill;
            }
            shape.StrokeThickness = Math.Max(1, ToScreenLength(thickness));
            shape.SnapsToDevicePixels = true;
            shape.IsHitTestVisible = false;
            if (dashArray != null)
            {
                shape.StrokeDashArray = dashArray;
            }

            ScreenOverlayCanvas.Children.Add(shape);
        }

        public void DrawDot(Point pos, double size, Brush fill, Brush? stroke = null, double thickness = 1)
        {
            double screenSize = Math.Max(1, ToScreenLength(size));
            Point screenPos = ToScreenPoint(pos);
            var dot = new Ellipse
            {
                Width = screenSize,
                Height = screenSize,
                Fill = fill,
                Stroke = stroke,
                StrokeThickness = stroke == null ? 0 : Math.Max(1, ToScreenLength(thickness)),
                SnapsToDevicePixels = true,
                IsHitTestVisible = false
            };

            Canvas.SetLeft(dot, screenPos.X - screenSize / 2);
            Canvas.SetTop(dot, screenPos.Y - screenSize / 2);
            ScreenOverlayCanvas.Children.Add(dot);
        }

        public static Brush ResolveStroke(Brush? strokeOverride, Color fallback)
        {
            if (strokeOverride != null)
            {
                return strokeOverride;
            }

            if (StrokeBrushCache.TryGetValue(fallback, out Brush? cachedBrush))
            {
                return cachedBrush;
            }

            var brush = new SolidColorBrush(fallback);
            brush.Freeze();
            StrokeBrushCache[fallback] = brush;
            return brush;
        }

        public static Brush CreateTranslucentFill(Brush stroke, byte alpha = 64)
        {
            if (stroke is SolidColorBrush solid)
            {
                Color color = solid.Color;
                var fill = new SolidColorBrush(Color.FromArgb(alpha, color.R, color.G, color.B));
                fill.Freeze();
                return fill;
            }

            return new SolidColorBrush(Color.FromArgb(alpha, 0, 255, 255));
        }

        public string FormatLength(double pixelLength)
        {
            return $"{pixelLength * PixelSize:F2} {GetDisplayUnit()}";
        }

        public string FormatArea(double pixelArea)
        {
            return $"{pixelArea * PixelSize * PixelSize:F2} {GetDisplayUnit()}²";
        }

        public string FormatPerimeter(double pixelPerimeter)
        {
            return FormatLength(pixelPerimeter);
        }

        public void DrawHandle(Point pos, ResizeHandle handleType, double size, bool isRect = true, Brush? stroke = null)
        {
            Shape handle = isRect ? new Rectangle() : new Ellipse();
            double screenSize = Math.Max(1, ToScreenLength(size));
            Point screenPos = ToScreenPoint(pos);
            handle.Width = screenSize;
            handle.Height = screenSize;
            handle.Fill = Brushes.White;
            handle.Stroke = stroke ?? Brushes.Blue;
            handle.StrokeThickness = Math.Max(1, ToScreenLength(1 / Scale));
            handle.Tag = handleType;
            handle.SnapsToDevicePixels = true;
            handle.IsHitTestVisible = false;

            Canvas.SetLeft(handle, screenPos.X - screenSize / 2);
            Canvas.SetTop(handle, screenPos.Y - screenSize / 2);
            ScreenOverlayCanvas.Children.Add(handle);
        }

        public void DrawInfoText(string text, Point pos, Brush brush, bool centerAlign = false)
        {
            var textBlock = new TextBlock
            {
                Text = text,
                Foreground = brush,
                FontSize = 12,
                FontWeight = FontWeights.Bold,
                Background = new SolidColorBrush(Color.FromArgb(128, 0, 0, 0)),
                Padding = new Thickness(2),
                SnapsToDevicePixels = true,
                IsHitTestVisible = false
            };
            TextOptions.SetTextFormattingMode(textBlock, TextFormattingMode.Display);
            TextOptions.SetTextRenderingMode(textBlock, TextRenderingMode.ClearType);

            textBlock.Measure(new Size(double.PositiveInfinity, double.PositiveInfinity));
            Point screenPos = ImageToScreen(pos);
            double offsetX = centerAlign ? -textBlock.DesiredSize.Width / 2 : 0;
            double offsetY = centerAlign ? -textBlock.DesiredSize.Height / 2 : 0;

            Canvas.SetLeft(textBlock, Math.Round(screenPos.X + offsetX));
            Canvas.SetTop(textBlock, Math.Round(screenPos.Y + offsetY));
            ScreenOverlayCanvas.Children.Add(textBlock);
        }

        public void DrawAngleArc(Point center, Point p1, Point p2, double radius, Brush brush)
        {
            Point screenCenter = ToScreenPoint(center);
            Point screenP1 = ToScreenPoint(p1);
            Point screenP2 = ToScreenPoint(p2);

            Vector v1 = screenP1 - screenCenter;
            Vector v2 = screenP2 - screenCenter;
            v1.Normalize();
            v2.Normalize();

            Point startPoint = screenCenter + v1 * radius;
            Point endPoint = screenCenter + v2 * radius;
            double crossProduct = v1.X * v2.Y - v1.Y * v2.X;
            SweepDirection sweepDir = crossProduct > 0 ? SweepDirection.Clockwise : SweepDirection.Counterclockwise;

            var path = new Path
            {
                Stroke = brush,
                StrokeThickness = Math.Max(1, ToScreenLength(1 / Scale)),
                SnapsToDevicePixels = true,
                IsHitTestVisible = false
            };

            var geometry = new StreamGeometry();
            using (var ctx = geometry.Open())
            {
                ctx.BeginFigure(startPoint, false, false);
                ctx.ArcTo(endPoint, new Size(radius, radius), 0, false, sweepDir, true, false);
            }

            path.Data = geometry;
            ScreenOverlayCanvas.Children.Add(path);
        }

        public void DrawRotationHandle(Point topCenter, RotateTransform rotateTransform, double handleSize)
        {
            Point rotationHandlePoint = new(topCenter.X, topCenter.Y - InfoTextOffset / Scale);
            Point rotatedTopCenter = rotateTransform.Transform(topCenter);
            Point rotatedRotationPoint = rotateTransform.Transform(rotationHandlePoint);
            DrawLineSegment(rotatedTopCenter, rotatedRotationPoint, Brushes.Blue, 1 / Scale);
            DrawHandle(rotatedRotationPoint, ResizeHandle.Rotation, handleSize, false);
        }

        private static double AlignCoordinate(double value, double thickness)
        {
            return thickness <= 1.5 ? Math.Round(value) + 0.5 : Math.Round(value);
        }

        public static Dictionary<ResizeHandle, Point> CreateBoxHandlePositions(Point center, double halfWidth, double halfHeight)
        {
            return new Dictionary<ResizeHandle, Point>
            {
                { ResizeHandle.TopLeft, new Point(center.X - halfWidth, center.Y - halfHeight) },
                { ResizeHandle.TopCenter, new Point(center.X, center.Y - halfHeight) },
                { ResizeHandle.TopRight, new Point(center.X + halfWidth, center.Y - halfHeight) },
                { ResizeHandle.MiddleRight, new Point(center.X + halfWidth, center.Y) },
                { ResizeHandle.BottomRight, new Point(center.X + halfWidth, center.Y + halfHeight) },
                { ResizeHandle.BottomCenter, new Point(center.X, center.Y + halfHeight) },
                { ResizeHandle.BottomLeft, new Point(center.X - halfWidth, center.Y + halfHeight) },
                { ResizeHandle.MiddleLeft, new Point(center.X - halfWidth, center.Y) }
            };
        }

        public void DrawArc(Point center, double radius, double startAngle, double sweepAngle, Brush stroke, double thickness)
        {
            if (radius <= 0) return;

            Point screenCenter = ToScreenPoint(center);
            double screenRadius = ToScreenLength(radius);

            double startAngleRad = startAngle * Math.PI / 180.0;
            Point startPoint = new Point(
                screenCenter.X + screenRadius * Math.Cos(startAngleRad),
                screenCenter.Y + screenRadius * Math.Sin(startAngleRad));

            double endAngleRad = (startAngle + sweepAngle) * Math.PI / 180.0;
            Point endPoint = new Point(
                screenCenter.X + screenRadius * Math.Cos(endAngleRad),
                screenCenter.Y + screenRadius * Math.Sin(endAngleRad));

            bool isLargeArc = Math.Abs(sweepAngle) > 180;
            SweepDirection sweepDir = sweepAngle >= 0 ? SweepDirection.Clockwise : SweepDirection.Counterclockwise;

            var path = new Path
            {
                Stroke = stroke,
                StrokeThickness = Math.Max(1, ToScreenLength(thickness)),
                SnapsToDevicePixels = true,
                IsHitTestVisible = false
            };

            var geometry = new StreamGeometry();
            using (var ctx = geometry.Open())
            {
                ctx.BeginFigure(startPoint, false, false);
                ctx.ArcTo(endPoint, new Size(screenRadius, screenRadius), 0, isLargeArc, sweepDir, true, false);
            }

            path.Data = geometry;
            ScreenOverlayCanvas.Children.Add(path);
        }

        public void DrawCircleOutline(Point center, double radius, Brush stroke, double thickness, Brush? fill = null)
        {
            double screenDiameter = ToScreenLength(radius * 2);
            Point screenCenter = ToScreenPoint(center);

            var shape = new Ellipse
            {
                Width = screenDiameter,
                Height = screenDiameter,
                Fill = fill,
                Stroke = stroke,
                StrokeThickness = Math.Max(1, ToScreenLength(thickness)),
                SnapsToDevicePixels = true,
                IsHitTestVisible = false
            };

            Canvas.SetLeft(shape, screenCenter.X - screenDiameter / 2);
            Canvas.SetTop(shape, screenCenter.Y - screenDiameter / 2);
            ScreenOverlayCanvas.Children.Add(shape);
        }

        private string GetDisplayUnit()
        {
            return string.IsNullOrWhiteSpace(PhysicalUnit) ? "px" : PhysicalUnit;
        }
    }
}
