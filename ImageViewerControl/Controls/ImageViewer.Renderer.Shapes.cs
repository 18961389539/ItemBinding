using System;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Shapes;
using ImageViewer.Models;
using ImageViewer.Rendering;
using ImageViewer.Utils;
using ImageViewer.ViewModels;

namespace ImageViewer.Controls
{
    public partial class ImageViewer
    {
        private void UpdatePixelGrid()
        {
            if (!ShowPixelGrid || Scale < PixelGridScaleThreshold || !ImageViewerImageSourceUtilities.TryGetSourceImageSize(ImageSource, out Size imageSize))
            {
                if (_pixelGridPath != null)
                {
                    _pixelGridPath.Visibility = Visibility.Collapsed;
                }

                return;
            }

            _pixelGridPath ??= new Path
            {
                Stroke = Brushes.Gray,
                IsHitTestVisible = false
            };

            if (!pixelGridCanvas.Children.Contains(_pixelGridPath))
            {
                pixelGridCanvas.Children.Clear();
                pixelGridCanvas.Children.Add(_pixelGridPath);
            }

            double width = imageSize.Width;
            double height = imageSize.Height;
            bool needsRebuild = !ReferenceEquals(_cachedPixelGridImageSource, ImageSource)
                || _cachedPixelGridWidth != width
                || _cachedPixelGridHeight != height
                || _pixelGridPath.Data == null;

            if (needsRebuild)
            {
                var geometry = new StreamGeometry();
                using (var ctx = geometry.Open())
                {
                    for (int x = 0; x <= (int)Math.Ceiling(width); x++)
                    {
                        ctx.BeginFigure(new Point(x, 0), false, false);
                        ctx.LineTo(new Point(x, height), true, false);
                    }

                    for (int y = 0; y <= (int)Math.Ceiling(height); y++)
                    {
                        ctx.BeginFigure(new Point(0, y), false, false);
                        ctx.LineTo(new Point(width, y), true, false);
                    }
                }

                geometry.Freeze();
                _pixelGridPath.Data = geometry;
                _cachedPixelGridImageSource = ImageSource;
                _cachedPixelGridWidth = width;
                _cachedPixelGridHeight = height;
            }

            _pixelGridPath.StrokeThickness = 1 / Scale;
            _pixelGridPath.Visibility = Visibility.Visible;
        }

        private void DrawRois(bool immediate = false, bool forceAnalysis = false)
        {
            RequestViewportOverlayRefresh(immediate);
            RequestAnalysisRefresh(forceAnalysis, immediate: immediate && forceAnalysis);
        }

        private void RefreshViewportOverlay()
        {
            var vm = ViewModel;
            ScreenOverlayCanvas.Children.Clear();
            DrawCommittedRois(vm);
            DrawSelectedRoi(vm);
            DrawActiveRois();
        }

        private void DrawSelectedRoiLayer()
        {
            DrawRois(immediate: true, forceAnalysis: true);
        }

        private void DrawCommittedRois(ImageViewerViewModel vm)
        {
            committedOverlayCanvas.Children.Clear();
            var context = CreateRoiRenderContext(committedOverlayCanvas);
            RoiRenderer.RenderCommitted(vm.AllRois, context, vm.SelectedRoi);
        }

        private void DrawSelectedRoi(ImageViewerViewModel vm)
        {
            selectionOverlayCanvas.Children.Clear();
            var context = CreateRoiRenderContext(selectionOverlayCanvas);
            RoiRenderer.RenderSelected(vm.SelectedRoi, context);
        }

        private void DrawActiveRois()
        {
            activeOverlayCanvas.Children.Clear();
            var context = CreateRoiRenderContext(activeOverlayCanvas);
            RoiRenderer.RenderActive(EnumerateActiveRois(), context);

            if (_currentPolygon != null && _currentPolygon.Points.Count > 0 && !_currentPolygon.IsClosed)
            {
                string info = $"Points: {_currentPolygon.Points.Count}";
                Point lastPoint = _currentPolygon.Points.Last();
                context.DrawInfoText(info, new Point(lastPoint.X, lastPoint.Y - InfoTextOffset / Scale), Brushes.Orange);

                if (_polygonPreviewPoint is Point currentPos)
                {
                    if (_currentPolygon.Points.Count > 0)
                    {
                        Point polygonLastPoint = _currentPolygon.Points.Last();
                        var previewLine = new Line
                        {
                            X1 = polygonLastPoint.X,
                            Y1 = polygonLastPoint.Y,
                            X2 = currentPos.X,
                            Y2 = currentPos.Y,
                            Stroke = Brushes.Orange,
                            StrokeThickness = 2 / Scale,
                            StrokeDashArray = new DoubleCollection { 2, 2 },
                            IsHitTestVisible = false
                        };
                        activeOverlayCanvas.Children.Add(previewLine);
                    }

                    Point firstPoint = _currentPolygon.Points.First();
                    var closingLine = new Line
                    {
                        X1 = currentPos.X,
                        Y1 = currentPos.Y,
                        X2 = firstPoint.X,
                        Y2 = firstPoint.Y,
                        Stroke = _isPolygonCloseCandidate ? Brushes.Yellow : Brushes.Orange,
                        StrokeThickness = 1 / Scale,
                        StrokeDashArray = new DoubleCollection { 4, 4 },
                        IsHitTestVisible = false
                    };
                    activeOverlayCanvas.Children.Add(closingLine);

                    if (_isPolygonCloseCandidate)
                    {
                        double highlightSize = (HandleSize + PolygonCloseHighlightPadding) / Scale;
                        var closeHandle = new Ellipse
                        {
                            Width = highlightSize,
                            Height = highlightSize,
                            Fill = new SolidColorBrush(Color.FromArgb(160, 255, 215, 0)),
                            Stroke = Brushes.Yellow,
                            StrokeThickness = 2 / Scale,
                            IsHitTestVisible = false
                        };

                        Canvas.SetLeft(closeHandle, firstPoint.X - highlightSize / 2);
                        Canvas.SetTop(closeHandle, firstPoint.Y - highlightSize / 2);
                        activeOverlayCanvas.Children.Add(closeHandle);
                    }
                }
            }

            if (_currentPolyline != null && _currentPolyline.Points.Count > 0 && !_isFreehandPolylineMode && _polylinePreviewPoint is Point polylinePreviewPoint)
            {
                Point lastPoint = _currentPolyline.Points.Last();
                var previewLine = new Line
                {
                    X1 = lastPoint.X,
                    Y1 = lastPoint.Y,
                    X2 = polylinePreviewPoint.X,
                    Y2 = polylinePreviewPoint.Y,
                    Stroke = Brushes.Orange,
                    StrokeThickness = 2 / Scale,
                    StrokeDashArray = new DoubleCollection { 2, 2 },
                    IsHitTestVisible = false
                };
                activeOverlayCanvas.Children.Add(previewLine);
            }
        }

    }
}
