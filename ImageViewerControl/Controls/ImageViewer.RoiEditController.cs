using System;
using System.Windows.Media;
using ImageViewer.Models;
using ImageViewer.ViewModels;

namespace ImageViewer.Controls
{
    internal sealed class RoiEditController
    {
        private readonly ImageViewerViewModel _viewModel;
        private readonly ImageViewerDialogWorkflowService _dialogWorkflowService;
        private readonly Action<RoiBase?> _tryRefreshCaliperDetection;
        private readonly Action _drawRois;
        private readonly Action<string, Exception> _logNonCriticalError;

        public RoiEditController(
            ImageViewerViewModel viewModel,
            ImageViewerDialogWorkflowService dialogWorkflowService,
            Action<RoiBase?> tryRefreshCaliperDetection,
            Action drawRois,
            Action<string, Exception> logNonCriticalError)
        {
            _viewModel = viewModel ?? throw new ArgumentNullException(nameof(viewModel));
            _dialogWorkflowService = dialogWorkflowService ?? throw new ArgumentNullException(nameof(dialogWorkflowService));
            _tryRefreshCaliperDetection = tryRefreshCaliperDetection ?? throw new ArgumentNullException(nameof(tryRefreshCaliperDetection));
            _drawRois = drawRois ?? throw new ArgumentNullException(nameof(drawRois));
            _logNonCriticalError = logNonCriticalError ?? throw new ArgumentNullException(nameof(logNonCriticalError));
        }

        public bool RemoveSelectedRoi()
        {
            if (_viewModel.SelectedRoi == null)
            {
                return false;
            }

            _viewModel.UndoRedo.Execute(new RemoveRoiCommand(_viewModel.SelectedRoi, _viewModel));
            return true;
        }

        public void Undo()
        {
            _viewModel.UndoRedo.Undo();
            _tryRefreshCaliperDetection(_viewModel.SelectedRoi);
            _drawRois();
        }

        public void Redo()
        {
            _viewModel.UndoRedo.Redo();
            _tryRefreshCaliperDetection(_viewModel.SelectedRoi);
            _drawRois();
        }

        public void DeleteSelected()
        {
            if (RemoveSelectedRoi())
            {
                _drawRois();
            }
        }

        public void ClearAll()
        {
            _viewModel.UndoRedo.Execute(new ClearAllRoisCommand(_viewModel));
            _drawRois();
        }

        public void SetSelectedLabel()
        {
            if (_viewModel.SelectedRoi is not RoiBase roi)
            {
                return;
            }

            string? label = _dialogWorkflowService.ShowRoiLabelDialog(roi);
            if (label == null)
            {
                return;
            }

            _viewModel.UndoRedo.Execute(new RoiLabelCommand(roi, label));
            _drawRois();
        }

        public void SetSelectedColor(string colorStr)
        {
            if (_viewModel.SelectedRoi is not RoiBase roi)
            {
                return;
            }

            try
            {
                Color color = (Color)ColorConverter.ConvertFromString(colorStr);
                _viewModel.UndoRedo.Execute(new RoiColorCommand(roi, color));
                _drawRois();
            }
            catch (Exception ex)
            {
                _logNonCriticalError("Failed to set ROI color", ex);
            }
        }
    }
}