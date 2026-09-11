using System;
using System.Windows;
using System.Windows.Input;
using ImageViewer.Models;
using ImageViewer.ViewModels;

namespace ImageViewer.Controls
{
    internal sealed class EditInteractionFlow : IImageViewerEditInteractionFlow
    {
        private readonly IImageViewerEditInteractionHost _host;
        private readonly ImageViewerInteractionManipulationState _state;

        public EditInteractionFlow(IImageViewerEditInteractionHost host)
        {
            _host = host ?? throw new ArgumentNullException(nameof(host));
            _state = host.ManipulationState;
        }

        public bool HandleKeyInput(Key key, bool isCtrlPressed, bool isShiftPressed)
        {
            var viewModel = _host.ViewModel;

            if (key == Key.Delete)
            {
                if (_host.RemoveSelectedRoi())
                {
                    _host.DrawRois();
                    return true;
                }

                return false;
            }

            if (isCtrlPressed && key == Key.Z)
            {
                viewModel.UndoRedo.Undo();
                _host.TryRefreshCaliperDetection(viewModel.SelectedRoi);
                _host.DrawRois();
                return true;
            }

            if (isCtrlPressed && key == Key.Y)
            {
                viewModel.UndoRedo.Redo();
                _host.TryRefreshCaliperDetection(viewModel.SelectedRoi);
                _host.DrawRois();
                return true;
            }

            if (viewModel.SelectedRoi != null && (key == Key.Up || key == Key.Down || key == Key.Left || key == Key.Right))
            {
                double step = isShiftPressed ? 10 : 1;
                double dx = 0;
                double dy = 0;

                if (key == Key.Up)
                {
                    dy = -step;
                }
                else if (key == Key.Down)
                {
                    dy = step;
                }
                else if (key == Key.Left)
                {
                    dx = -step;
                }
                else if (key == Key.Right)
                {
                    dx = step;
                }

                MoveRoi(viewModel.SelectedRoi, dx, dy);
                _host.TryRefreshCaliperDetection(viewModel.SelectedRoi);
                _host.DrawRois();
                return true;
            }

            if (key == Key.Escape)
            {
                _host.ExitCurrentMode();
                return true;
            }

            return false;
        }

        public bool TryBeginEdit(Point imagePosition, bool isRightButtonPressed)
        {
            var viewModel = _host.ViewModel;
            ResizeHandle handle = _host.GetHandleAt(imagePosition);
            if (handle != ResizeHandle.None)
            {
                _state.OriginalRoiState = viewModel.SelectedRoi?.Clone();
                _state.ActiveHandle = handle;
                _state.LastMousePosition = imagePosition;
                _host.CaptureRootMouse();
                return true;
            }

            int pointIndex = _host.GetPolygonPointIndexAt(imagePosition);
            if (pointIndex != -1)
            {
                _state.OriginalRoiState = viewModel.SelectedRoi?.Clone();
                if (isRightButtonPressed && viewModel.SelectedRoi is PolygonRoi removablePolygon && removablePolygon.Points.Count > 3)
                {
                    removablePolygon.Points.RemoveAt(pointIndex);
                    if (_state.OriginalRoiState is PolygonRoi oldPolygon)
                    {
                        viewModel.UndoRedo.Execute(new RoiStateCommand(removablePolygon, oldPolygon, removablePolygon.Clone()));
                    }

                    _host.DrawRois();
                    return true;
                }

                _state.ActivePolygonPointIndex = pointIndex;
                _state.LastMousePosition = imagePosition;
                _host.CaptureRootMouse();
                return true;
            }

            int segmentIndex = _host.GetPolygonSegmentAt(imagePosition);
            if (segmentIndex != -1 && viewModel.SelectedRoi is PolygonRoi segmentPolygon)
            {
                PolygonRoi? oldState = segmentPolygon.Clone() as PolygonRoi;
                segmentPolygon.Points.Insert(segmentIndex + 1, imagePosition);
                PolygonRoi? newState = segmentPolygon.Clone() as PolygonRoi;

                if (oldState != null && newState != null)
                {
                    viewModel.UndoRedo.Execute(new RoiStateCommand(segmentPolygon, oldState, newState));
                }

                _state.ActivePolygonPointIndex = segmentIndex + 1;
                _state.LastMousePosition = imagePosition;
                _host.DrawRois();
                _host.CaptureRootMouse();
                return true;
            }

            RoiBase? hitRoi = _host.HitTest(imagePosition);
            if (hitRoi == null)
            {
                return false;
            }

            viewModel.SelectedRoi = hitRoi;
            if (hitRoi.IsLocked)
            {
                _host.DrawRois();
                return true;
            }

            _state.OriginalRoiState = hitRoi.Clone();
            _state.IsRoiDragging = true;
            _state.LastMousePosition = imagePosition;
            _host.CaptureRootMouse();
            _host.DrawRois();
            return true;
        }

        public bool TryHandleActiveEditMove(Point imagePosition)
        {
            var viewModel = _host.ViewModel;
            if (_state.ActiveHandle != ResizeHandle.None && viewModel.SelectedRoi != null)
            {
                double dx = imagePosition.X - _state.LastMousePosition.X;
                double dy = imagePosition.Y - _state.LastMousePosition.Y;
                _host.ResizeRoi(viewModel.SelectedRoi, _state.ActiveHandle, dx, dy, imagePosition);
                _state.LastMousePosition = imagePosition;
                _host.DrawSelectedRoiLayer();
                return true;
            }

            if (_state.ActivePolygonPointIndex != -1 && viewModel.SelectedRoi is PolygonRoi polygon)
            {
                polygon.Points[_state.ActivePolygonPointIndex] = imagePosition;
                _state.LastMousePosition = imagePosition;
                _host.DrawSelectedRoiLayer();
                return true;
            }

            if (_state.IsRoiDragging && viewModel.SelectedRoi != null)
            {
                double dx = imagePosition.X - _state.LastMousePosition.X;
                double dy = imagePosition.Y - _state.LastMousePosition.Y;
                MoveRoi(viewModel.SelectedRoi, dx, dy);
                _state.LastMousePosition = imagePosition;
                _host.DrawSelectedRoiLayer();
                return true;
            }

            return false;
        }

        public void CompleteEdit()
        {
            var viewModel = _host.ViewModel;
            bool hadInteractiveManipulation = _state.HasActiveRoiManipulation;

            if (_state.OriginalRoiState != null && viewModel.SelectedRoi is RoiBase selectedRoi)
            {
                if (hadInteractiveManipulation)
                {
                    _host.TryRefreshCaliperDetection(selectedRoi);
                }

                RoiBase newRoiState = selectedRoi.Clone();
                IUndoRedoCommand? command = _host.CreateStateCommand(selectedRoi, _state.OriginalRoiState, newRoiState);
                if (command != null)
                {
                    viewModel.UndoRedo.Execute(command);
                }
            }

            ResetManipulationState();

            if (hadInteractiveManipulation)
            {
                _host.DrawRois();
                _host.UpdateInfoPanel(force: true);
            }
        }

        public void ResetManipulationState()
        {
            _state.ResetRoiManipulation();
            if (_host.IsRootMouseCaptured && !_state.IsCanvasDragging)
            {
                _host.ReleaseRootMouse();
            }
        }

        private void MoveRoi(RoiBase roi, double dx, double dy)
        {
            _host.MoveRoi(roi, dx, dy);
        }
    }
}