using System;
using System.Collections.Generic;
using System.Windows.Threading;
using ImageViewer.Abstractions;
using ImageViewer.Models;
using ImageViewer.Plugins;

namespace ImageViewer.Controls
{
    internal sealed class ImageViewerSessionControllerHostAdapter : IImageViewerSessionControllerHost
    {
        private readonly ImageViewer _owner;
        private readonly ImageViewerDialogWorkflowService _dialogWorkflowService;
        private readonly ViewportController _viewportController;
        private readonly Action _drawRois;
        private readonly Action<string, string, Exception> _showNonCriticalError;
        private readonly Action<string, Exception> _logNonCriticalError;
        private readonly Action _updateContextMenuState;

        public ImageViewerSessionControllerHostAdapter(
            ImageViewer owner,
            ImageViewerDialogWorkflowService dialogWorkflowService,
            ViewportController viewportController,
            Action drawRois,
            Action<string, string, Exception> showNonCriticalError,
            Action<string, Exception> logNonCriticalError,
            Action updateContextMenuState)
        {
            _owner = owner ?? throw new ArgumentNullException(nameof(owner));
            _dialogWorkflowService = dialogWorkflowService ?? throw new ArgumentNullException(nameof(dialogWorkflowService));
            _viewportController = viewportController ?? throw new ArgumentNullException(nameof(viewportController));
            _drawRois = drawRois ?? throw new ArgumentNullException(nameof(drawRois));
            _showNonCriticalError = showNonCriticalError ?? throw new ArgumentNullException(nameof(showNonCriticalError));
            _logNonCriticalError = logNonCriticalError ?? throw new ArgumentNullException(nameof(logNonCriticalError));
            _updateContextMenuState = updateContextMenuState ?? throw new ArgumentNullException(nameof(updateContextMenuState));
        }

        public Dispatcher Dispatcher => _owner.Dispatcher;

        public string? ShowSaveSessionDialog() => _dialogWorkflowService.ShowSaveSessionDialog();

        public string? ShowOpenSessionDialog() => _dialogWorkflowService.ShowOpenSessionDialog();

        public string? ShowSaveProjectPackageDialog() => _dialogWorkflowService.ShowSaveProjectPackageDialog();

        public IImageViewerSessionService SessionService => _owner.RuntimeServices.SessionService;

        public IImageViewerRecentProjectService RecentProjectService => _owner.RuntimeServices.RecentProjectService;

        public IImageViewerProjectPackageService ProjectPackageService => _owner.RuntimeServices.ProjectPackageService;

        public RoiPluginRegistry PluginRegistry => _owner.PluginRegistry;

        public IReadOnlyList<RoiBase> AllRois => _owner.ViewerState.AllRois;

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

        public ImageViewerViewportState CurrentViewportState => _viewportController.CurrentState;

        public bool HasContent => _owner.ImageSource != null || _owner.ViewerState.AllRois.Count > 0;

        public string? TryGetCurrentImagePath() => _viewportController.TryGetCurrentImagePath();

        public void LoadImageFromFile(string filePath, bool fitToView) => _viewportController.LoadImageFromFile(filePath, fitToView);

        public void ReplaceAllRois(IReadOnlyList<RoiBase> rois) => _owner.ViewerState.ReplaceAllRois(rois);

        public void ApplyViewportState(ImageViewerViewportState state) => _viewportController.ApplyViewportState(state);

        public void DrawRois() => _drawRois();

        public void ShowNonCriticalError(string title, string message, Exception ex) => _showNonCriticalError(title, message, ex);

        public void LogNonCriticalError(string context, Exception ex) => _logNonCriticalError(context, ex);

        public void ShowWarning(string title, string message) => _dialogWorkflowService.ShowWarning(title, message);

        public void UpdateContextMenuState() => _updateContextMenuState();
    }
}