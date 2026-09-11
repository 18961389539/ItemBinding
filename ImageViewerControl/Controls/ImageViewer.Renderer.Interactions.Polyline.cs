using System.Linq;
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
        private void OnPolylineMouseDown(object sender, MouseButtonEventArgs e)
        {
            HandlePolylineMouseDown(e);
        }

        private void OnPolylineMouseMove(object sender, MouseEventArgs e)
        {
            HandlePolylineMouseMove(e);
        }

        private void OnPolylineMouseUp(object sender, MouseButtonEventArgs e)
        {
            HandlePolylineMouseUp(e);
        }

        private void EndPolylineDrawing()
        {
            if (_currentPolyline == null)
            {
                return;
            }

            var vm = ViewModel;
            if (_currentPolyline.Points.Count >= 2)
            {
                vm.UndoRedo.Execute(new AddRoiCommand(_currentPolyline, vm));
            }

            ResetPolylineDrawing();
        }

        private void HandlePolylineMouseDown(MouseButtonEventArgs e)
        {
            if (e.Handled || _currentPolyline == null)
            {
                return;
            }

            if (!_isFreehandPolylineMode && (e.RightButton == MouseButtonState.Pressed || e.ClickCount >= 2))
            {
                EndPolylineDrawing();
                return;
            }

            if (e.LeftButton != MouseButtonState.Pressed)
            {
                return;
            }

            if (!TryCaptureRootGridMouse())
            {
                return;
            }

            Point pos = GetSnappedMousePosition(e);
            if (_isFreehandPolylineMode)
            {
                _currentPolyline.Points.Clear();
            }

            _currentPolyline.Points.Add(pos);
            DrawRois();
        }

        private void HandlePolylineMouseMove(MouseEventArgs e)
        {
            if (_currentPolyline == null)
            {
                return;
            }

            Point pos = GetSnappedMousePosition(e);
            if (_isFreehandPolylineMode)
            {
                if (RoiGeometryService.ShouldAppendFreehandPolylinePoint(_currentPolyline.Points, pos))
                {
                    _currentPolyline.Points.Add(pos);
                    DrawRois();
                }

                return;
            }

            if (_currentPolyline.Points.Count > 0)
            {
                _polylinePreviewPoint = pos;
                DrawRois();
            }
        }

        private void HandlePolylineMouseUp(MouseButtonEventArgs e)
        {
            if (_isFreehandPolylineMode && _currentPolyline != null)
            {
                EndPolylineDrawing();
            }
        }

        private void ResetPolylineDrawing()
        {
            ReleaseRootGridMouseIfCaptured();
            _currentPolyline = null;
            _isFreehandPolylineMode = false;
            _polylinePreviewPoint = null;
            LeaveInteractionMode();
            DrawRois();
        }
    }
}
