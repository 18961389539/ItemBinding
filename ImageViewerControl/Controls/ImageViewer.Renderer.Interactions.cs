using System;
using System.Windows;
using System.Windows.Input;
using System.Windows.Media;
using ImageViewer.Dialogs;
using ImageViewer.Localization;
using ImageViewer.Models;
using ImageViewer.Utils;
using ImageViewer.ViewModels;

namespace ImageViewer.Controls
{
    public partial class ImageViewer
    {
        private void OnExternalPlacementMouseDown(object sender, MouseButtonEventArgs e)
        {
            if (e.Handled || e.ChangedButton != MouseButton.Left || _externalPlacementFactory == null)
            {
                return;
            }

            Point position = e.GetPosition(imageContainer);
            var roi = _externalPlacementFactory(position);
            if (roi == null)
            {
                return;
            }

            CommitRoiAndExitCurrentMode(roi);
            e.Handled = true;
        }

        private void OnRoiMouseDown(object sender, MouseButtonEventArgs e)
        {
            if (!TryBeginMouseDrawing(e, ref _currentRect, startPoint => _interactionMode == InteractionMode.DrawBlobAnalysis
                ? new BlobAnalysisRoi
                {
                    Center = startPoint,
                    Width = 0,
                    Height = 0
                }
                : new RotatedRect
                {
                    Center = startPoint,
                    Width = 0,
                    Height = 0
                }, out _startPoint))
            {
                return;
            }
        }

        private void OnRoiMouseMove(object sender, MouseEventArgs e)
        {
            UpdateMouseDrawing(_currentRect, e, (rect, currentPos) =>
            {
                rect.Width = Math.Abs(currentPos.X - _startPoint.X);
                rect.Height = Math.Abs(currentPos.Y - _startPoint.Y);
                rect.Center = GetMidPoint(_startPoint, currentPos);
            });
        }

        private void OnRoiMouseUp(object sender, MouseButtonEventArgs e)
        {
            EndRoiDrawing();
        }

        private void EndRoiDrawing()
        {
            CompleteSingleRoiInteraction(
                ref _currentRect,
                rect => rect.Width > MinimumDrawableSize && rect.Height > MinimumDrawableSize,
                rect =>
                {
                    if (rect is BlobAnalysisRoi blob)
                    {
                        TryApplyBlobAnalysis(blob);
                    }
                });
        }

        public void StartEllipseRoiMode()
        {
            ExitCurrentMode();
            EnterInteractionMode(InteractionMode.DrawEllipse, Cursors.Cross);
        }

        private void OnEllipseMouseDown(object sender, MouseButtonEventArgs e)
        {
            if (!TryBeginMouseDrawing(e, ref _currentEllipse, startPoint => new EllipseRoi
            {
                Center = startPoint,
                RadiusX = 0,
                RadiusY = 0
            }, out _startPoint))
            {
                return;
            }
        }

        private void OnEllipseMouseMove(object sender, MouseEventArgs e)
        {
            UpdateMouseDrawing(_currentEllipse, e, (ellipse, currentPos) =>
            {
                ellipse.RadiusX = Math.Abs(currentPos.X - _startPoint.X) / 2;
                ellipse.RadiusY = Math.Abs(currentPos.Y - _startPoint.Y) / 2;
                ellipse.Center = GetMidPoint(_startPoint, currentPos);
            });
        }

        private void OnEllipseMouseUp(object sender, MouseButtonEventArgs e)
        {
            EndEllipseDrawing();
        }

        private void EndEllipseDrawing()
        {
            CompleteSingleRoiInteraction(
                ref _currentEllipse,
                ellipse => ellipse.RadiusX > MinimumDrawableSize && ellipse.RadiusY > MinimumDrawableSize);
        }

        private void OnCircleMouseDown(object sender, MouseButtonEventArgs e)
        {
            if (!TryBeginMouseDrawing(e, ref _currentCircle, startPoint => new CircleRoi { Center = startPoint, Radius = 0 }, out _startPoint))
            {
                return;
            }
        }

        private void OnCircleMouseMove(object sender, MouseEventArgs e)
        {
            UpdateMouseDrawing(_currentCircle, e, (circle, currentPos) => circle.Radius = GeometryUtils.Distance(_startPoint, currentPos));
        }

        private void OnCircleMouseUp(object sender, MouseButtonEventArgs e)
        {
            EndCircleDrawing();
        }

        private void EndCircleDrawing()
        {
            CompleteSingleRoiInteraction(
                ref _currentCircle,
                circle => circle.Radius > MinimumDrawableSize);
        }

        private void OnCircularCaliperMouseDown(object sender, MouseButtonEventArgs e)
        {
            if (!TryBeginMouseDrawing(e, ref _currentCircularCaliper, startPoint => _isArcCaliperMode
                ? new ArcCaliperMeasureRoi { Center = startPoint, Radius = 0 }
                : new CircularCaliperMeasureRoi { Center = startPoint, Radius = 0 }, out _startPoint))
            {
                return;
            }
        }

        private void OnCircularCaliperMouseMove(object sender, MouseEventArgs e)
        {
            UpdateMouseDrawing(_currentCircularCaliper, e, UpdateCircularCaliperDrawing);
        }

        private void OnCircularCaliperMouseUp(object sender, MouseButtonEventArgs e)
        {
            EndCircularCaliperDrawing();
        }

        private void EndCircularCaliperDrawing()
        {
            CompleteSingleRoiInteraction(
                ref _currentCircularCaliper,
                circularCaliper => circularCaliper.Radius > MinimumDrawableSize,
                ApplyCircularCaliperDetection,
                () => _isArcCaliperMode = false);
        }

        private void OnPointAnnotationMouseDown(object sender, MouseButtonEventArgs e)
        {
            if (!IsPrimaryMouseButtonDown(e))
            {
                return;
            }

            Point pos = GetSnappedMousePosition(e);
            var roi = new PointAnnotationRoi { Position = pos, Label = $"P ({pos.X:F0},{pos.Y:F0})" };
            CommitRoiAndLeaveInteraction(roi);
        }

        private void OnTextAnnotationMouseDown(object sender, MouseButtonEventArgs e)
        {
            if (!IsPrimaryMouseButtonDown(e))
            {
                return;
            }

            Point pos = GetSnappedMousePosition(e);
            var text = _dialogWorkflowService.ShowTextInput(UiText.Get("DialogTextAnnotationPrompt"), UiText.Get("DialogTextAnnotationDefault"));
            if (!string.IsNullOrWhiteSpace(text))
            {
                var roi = new TextAnnotationRoi { Position = pos, Label = text.Trim() };
                CommitRoiAndLeaveInteraction(roi);
            }
        }

        private void OnLostMouseCapture(object sender, MouseEventArgs e)
        {
            _interactionController.HandleLostMouseCapture();

            if (_currentRect != null) EndRoiDrawing();
            if (_currentEllipse != null) EndEllipseDrawing();
            if (_currentCircle != null) EndCircleDrawing();
            if (_currentRing != null) EndRingDrawing();
            if (_currentCircularCaliper != null) EndCircularCaliperDrawing();
            if (_currentLineCaliperMeasure != null) EndLineCaliperDrawing();
            if (_isFreehandPolylineMode && _currentPolyline != null) EndPolylineDrawing();
        }
    }
}
