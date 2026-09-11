using System;
using System.Threading.Tasks;
using System.Windows;
using ImageViewer.Utils;

namespace ImageViewer.Controls
{
    internal sealed class DroppedContentController
    {
        private readonly ViewportController _viewportController;
        private readonly ImageViewerSessionController _sessionController;
        private readonly Action _refreshSelectedRoiPropertyPanel;

        public DroppedContentController(
            ViewportController viewportController,
            ImageViewerSessionController sessionController,
            Action refreshSelectedRoiPropertyPanel)
        {
            _viewportController = viewportController ?? throw new ArgumentNullException(nameof(viewportController));
            _sessionController = sessionController ?? throw new ArgumentNullException(nameof(sessionController));
            _refreshSelectedRoiPropertyPanel = refreshSelectedRoiPropertyPanel ?? throw new ArgumentNullException(nameof(refreshSelectedRoiPropertyPanel));
        }

        public static void HandleDragOver(DragEventArgs e)
        {
            if (e.Data.GetDataPresent(DataFormats.FileDrop) && e.Data.GetData(DataFormats.FileDrop) is string[] paths && DroppedFileHelper.TryGetOpenablePath(paths, out _, out _))
            {
                e.Effects = DragDropEffects.Copy;
                e.Handled = true;
                return;
            }

            e.Effects = DragDropEffects.None;
            e.Handled = true;
        }

        public async Task HandleDropAsync(DragEventArgs e)
        {
            if (e.Data.GetDataPresent(DataFormats.FileDrop) && e.Data.GetData(DataFormats.FileDrop) is string[] paths && DroppedFileHelper.TryGetOpenablePath(paths, out string path, out DroppedFileKind kind))
            {
                await OpenDroppedPathAsync(path, kind);
            }
        }

        private async Task OpenDroppedPathAsync(string path, DroppedFileKind kind)
        {
            switch (kind)
            {
                case DroppedFileKind.Image:
                    _viewportController.LoadImageFromFile(path, fitToView: true);
                    break;
                case DroppedFileKind.Session:
                case DroppedFileKind.ProjectPackage:
                    await _sessionController.OpenProjectAsync(path);
                    break;
            }

            _refreshSelectedRoiPropertyPanel();
        }
    }
}