using System;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Shapes;
using ImageViewer.Models;
using ImageViewer.Rendering;
using ImageViewer.Utils;

namespace ImageViewer.Controls
{
    public partial class ImageViewer
    {
        private static bool IsNear(Point p1, Point p2, double threshold)
        {
            return Math.Abs(p1.X - p2.X) < threshold / 2 && Math.Abs(p1.Y - p2.Y) < threshold / 2;
        }

        private static Brush ResolveStroke(Brush stroke, Color fallback)
        {
            return stroke ?? new SolidColorBrush(fallback);
        }

        private RoiBase? CreateFittedEllipseAt(Point position)
        {
            return CreateRoiFromSelectionCore(position, CreateFittedEllipseFromSource);
        }

        private RoiRenderContext CreateRoiRenderContext(Canvas targetCanvas)
        {
            return new RoiRenderContext(
                targetCanvas,
                ScreenOverlayCanvas,
                ImageToScreen,
                Scale,
                PixelSize,
                PhysicalUnit,
                ShowCaliperScores,
                HandleSize,
                InfoTextOffset,
                AngleArcRadius,
                PointAnnotationSize,
                PolygonResizeHandlePadding);
        }

        private Point ImageToScreen(Point point)
        {
            return new Point(point.X * Scale + translateTransform.X, point.Y * Scale + translateTransform.Y);
        }

        private string GetDisplayUnit()
        {
            return string.IsNullOrWhiteSpace(PhysicalUnit) ? "px" : PhysicalUnit;
        }

        private string FormatLength(double pixelLength)
        {
            return $"{pixelLength * PixelSize:F2} {GetDisplayUnit()}";
        }

        public ResizeHandle GetHandleAt(Point point)
        {
            return RoiInteraction.GetHandleAt(ViewModel.SelectedRoi, point, Scale, HandleSize, HandleHitPadding, InfoTextOffset, PolygonVertexHitPadding);
        }

        private void ResizeRoi(RoiBase roi, ResizeHandle handle, double dx, double dy, Point currentPos)
        {
            RoiInteraction.ResizeRoi(roi, handle, dx, dy, currentPos, MinimumRoiDimension);
            if (roi is CaliperMeasureRoi line)
            {
                line.ClearDetectedEdges();
            }
            else if (roi is CircularCaliperMeasureRoi circular)
            {
                circular.ClearDetectedEdges();
            }
        }

        public RoiBase? HitTest(Point point)
        {
            return RoiInteraction.HitTest(ViewModel, point, Scale, HitTestTolerance);
        }

        private void DrawHandle(Point pos, ResizeHandle handleType, double size, bool isRect = true, Brush? stroke = null)
        {
            Shape handle = isRect ? new Rectangle() : new Ellipse();
            handle.Width = size;
            handle.Height = size;
            handle.Fill = Brushes.White;
            handle.Stroke = stroke ?? Brushes.Blue;
            handle.StrokeThickness = 1 / Scale;
            handle.Tag = handleType;
            handle.IsHitTestVisible = false;

            Canvas.SetLeft(handle, pos.X - size / 2);
            Canvas.SetTop(handle, pos.Y - size / 2);
            overlayCanvas.Children.Add(handle);
        }

        private void DrawInfoText(string text, Point pos, Brush brush, bool centerAlign = false)
        {
            double fontSize = 12 / Scale;
            double paddingValue = 2 / Scale;
            var cacheKey = (text, fontSize, paddingValue);

            if (!_infoTextSizeCache.TryGetValue(cacheKey, out Size measuredSize))
            {
                var measureTextBlock = new TextBlock
                {
                    Text = text,
                    FontSize = fontSize,
                    FontWeight = FontWeights.Bold,
                    Padding = new Thickness(paddingValue),
                    IsHitTestVisible = false
                };

                measureTextBlock.Measure(new Size(double.PositiveInfinity, double.PositiveInfinity));
                measuredSize = measureTextBlock.DesiredSize;
                _infoTextSizeCache[cacheKey] = measuredSize;
            }

            var textBlock = new TextBlock
            {
                Text = text,
                Foreground = brush,
                FontSize = fontSize,
                FontWeight = FontWeights.Bold,
                Background = InfoTextBackgroundBrush,
                Padding = new Thickness(paddingValue),
                IsHitTestVisible = false
            };

            double offsetX = centerAlign ? -measuredSize.Width / 2 : 0;
            double offsetY = centerAlign ? -measuredSize.Height / 2 : 0;

            Canvas.SetLeft(textBlock, pos.X + offsetX);
            Canvas.SetTop(textBlock, pos.Y + offsetY);
            overlayCanvas.Children.Add(textBlock);
        }
    }
}
