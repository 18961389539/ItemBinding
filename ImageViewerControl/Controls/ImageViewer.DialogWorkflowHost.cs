using System;
using System.Windows.Media;
using ImageViewer.Models;
using ImageViewer.Plugins;
using ImageViewer.ViewModels;

namespace ImageViewer.Controls
{
    internal sealed class ImageViewerDialogWorkflowHostAdapter : IImageViewerDialogWorkflowHost
    {
        private readonly ImageViewer _owner;
        private readonly RoiSelectionStateController _roiSelectionStateController;
        private readonly ViewportController _viewportController;
        private readonly Action _drawRois;
        private readonly Action _drawSelectedRoiLayer;
        private readonly Func<RoiBase, RoiBase, RoiBase, IUndoRedoCommand?> _createStateCommand;
        private readonly Func<CaliperMeasureRoi, bool> _tryApplyCaliperDetection;
        private readonly Func<LineCaliperMeasureRoi, bool> _tryApplyLineCaliperDetection;
        private readonly Func<CircularCaliperMeasureRoi, bool> _tryApplyCircularCaliperDetection;
        private readonly Action<string, string, Exception> _showNonCriticalError;

        public ImageViewerDialogWorkflowHostAdapter(
            ImageViewer owner,
            RoiSelectionStateController roiSelectionStateController,
            ViewportController viewportController,
            Action drawRois,
            Action drawSelectedRoiLayer,
            Func<RoiBase, RoiBase, RoiBase, IUndoRedoCommand?> createStateCommand,
            Func<CaliperMeasureRoi, bool> tryApplyCaliperDetection,
            Func<LineCaliperMeasureRoi, bool> tryApplyLineCaliperDetection,
            Func<CircularCaliperMeasureRoi, bool> tryApplyCircularCaliperDetection,
            Action<string, string, Exception> showNonCriticalError)
        {
            _owner = owner ?? throw new ArgumentNullException(nameof(owner));
            _roiSelectionStateController = roiSelectionStateController ?? throw new ArgumentNullException(nameof(roiSelectionStateController));
            _viewportController = viewportController ?? throw new ArgumentNullException(nameof(viewportController));
            _drawRois = drawRois ?? throw new ArgumentNullException(nameof(drawRois));
            _drawSelectedRoiLayer = drawSelectedRoiLayer ?? throw new ArgumentNullException(nameof(drawSelectedRoiLayer));
            _createStateCommand = createStateCommand ?? throw new ArgumentNullException(nameof(createStateCommand));
            _tryApplyCaliperDetection = tryApplyCaliperDetection ?? throw new ArgumentNullException(nameof(tryApplyCaliperDetection));
            _tryApplyLineCaliperDetection = tryApplyLineCaliperDetection ?? throw new ArgumentNullException(nameof(tryApplyLineCaliperDetection));
            _tryApplyCircularCaliperDetection = tryApplyCircularCaliperDetection ?? throw new ArgumentNullException(nameof(tryApplyCircularCaliperDetection));
            _showNonCriticalError = showNonCriticalError ?? throw new ArgumentNullException(nameof(showNonCriticalError));
        }

        public RoiPluginRegistry PluginRegistry => _owner.PluginRegistry;

        public double PixelSize
        {
            get => _owner.PixelSize;
            set => _owner.PixelSize = value;
        }

        public string PhysicalUnit
        {
            get => _owner.PhysicalUnit;
            set => _owner.PhysicalUnit = value;
        }

        public int ImageLoadRetryCount => _owner.RuntimeOptions.ImageLoadRetryCount;

        public int ImageLoadRetryDelayMilliseconds => _owner.RuntimeOptions.ImageLoadRetryDelayMilliseconds;

        public void DrawRois() => _drawRois();

        public void DrawSelectedRoiLayer() => _drawSelectedRoiLayer();

        public void HandleRoiEdited(RoiBase roi) => _roiSelectionStateController.HandleRoiEdited(roi);

        public IUndoRedoCommand? CreateStateCommand(RoiBase roi, RoiBase oldState, RoiBase newState) => _createStateCommand(roi, oldState, newState);

        public void ExecuteUndoRedoCommand(IUndoRedoCommand command) => _owner.ViewerState.UndoRedo.Execute(command);

        public bool TryApplyCaliperDetection(CaliperMeasureRoi roi) => _tryApplyCaliperDetection(roi);

        public bool TryApplyLineCaliperDetection(LineCaliperMeasureRoi roi) => _tryApplyLineCaliperDetection(roi);

        public bool TryApplyCircularCaliperDetection(CircularCaliperMeasureRoi roi) => _tryApplyCircularCaliperDetection(roi);

        public void SetImage(ImageSource source) => _owner.SetImage(source);

        public void SetImageLoadState(bool isLoading, string statusText, double progress, bool canRetry) => _owner.SetImageLoadState(isLoading, statusText, progress, canRetry);

        public void FitToView() => _viewportController.FitToView();

        public void ShowNonCriticalError(string title, string message, Exception ex) => _showNonCriticalError(title, message, ex);
    }
}