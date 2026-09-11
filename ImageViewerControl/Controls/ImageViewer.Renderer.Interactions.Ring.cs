using System.Windows;
using System.Windows.Input;
using ImageViewer.Models;
using ImageViewer.Services;
using ImageViewer.Utils;
using ImageViewer.ViewModels;

namespace ImageViewer.Controls
{
    public partial class ImageViewer
    {
        private void OnRingMouseDown(object sender, MouseButtonEventArgs e)
        {
            HandleRingMouseDown(e);
        }

        private void OnRingMouseMove(object sender, MouseEventArgs e)
        {
            HandleRingMouseMove(e);
        }

        private void OnRingMouseUp(object sender, MouseButtonEventArgs e)
        {
            EndRingDrawing();
        }

        private void EndRingDrawing()
        {
            var vm = ViewModel;
            if (_currentRing == null)
            {
                return;
            }

            ReleaseRootGridMouseIfCaptured();

            if (_ringDrawStep == 0)
            {
                CompleteRingFirstStage();
                DrawRois();
                return;
            }

            if (RoiGeometryService.IsRingDrawable(_currentRing.OuterRadius, MinimumDrawableSize))
            {
                _currentRing.InnerRadius = RoiGeometryService.ClampRingInnerRadius(
                    _currentRing.InnerRadius,
                    _currentRing.OuterRadius,
                    MinimumDrawableSize);
                vm.UndoRedo.Execute(new AddRoiCommand(_currentRing, vm));
            }

            ResetRingDrawing();
        }

        private void HandleRingMouseDown(MouseButtonEventArgs e)
        {
            if (e.Handled)
            {
                return;
            }

            if (e.RightButton == MouseButtonState.Pressed)
            {
                ResetRingDrawing();
                return;
            }

            if (e.LeftButton != MouseButtonState.Pressed)
            {
                return;
            }

            if (_currentRing == null && TryBeginMouseDrawing(e, out Point position))
            {
                BeginRingDrawing(position);
            }
        }

        private void HandleRingMouseMove(MouseEventArgs e)
        {
            if (_currentRing == null || !rootGrid.IsMouseCaptured)
            {
                return;
            }

            UpdateRingDrawing(GetSnappedMousePosition(e));
        }

        private void BeginRingDrawing(Point position)
        {
            _startPoint = position;
            _currentRing = new RingRoi { Center = position, InnerRadius = 0, OuterRadius = 0 };
            _ringDrawStep = 0;
        }

        private void UpdateRingDrawing(Point currentPos)
        {
            if (_currentRing == null)
            {
                return;
            }

            double radius = GeometryUtils.Distance(_currentRing.Center, currentPos);
            if (_ringDrawStep == 0)
            {
                _currentRing.OuterRadius = radius;
            }
            else
            {
                _currentRing.InnerRadius = RoiGeometryService.ClampRingInnerRadius(
                    radius,
                    _currentRing.OuterRadius,
                    MinimumDrawableSize);
            }

            DrawRois();
        }

        private void CompleteRingFirstStage()
        {
            if (_currentRing == null)
            {
                return;
            }

            if (!RoiGeometryService.IsRingDrawable(_currentRing.OuterRadius, MinimumDrawableSize))
            {
                _currentRing = null;
                _ringDrawStep = 0;
                LeaveInteractionMode();
                return;
            }

            _ringDrawStep = 1;
            _currentRing.InnerRadius = _currentRing.OuterRadius / 2;
        }

        private void ResetRingDrawing()
        {
            _currentRing = null;
            _ringDrawStep = 0;
            LeaveInteractionMode();
            DrawRois();
        }
    }
}
