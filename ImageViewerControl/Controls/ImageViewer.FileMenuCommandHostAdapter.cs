using System;
using System.Threading.Tasks;

namespace ImageViewer.Controls
{
    internal sealed class ImageViewerFileMenuCommandHostAdapter : IImageViewerFileMenuCommandHost
    {
        private readonly Func<Task> _showOpenImageDialogAsync;
        private readonly ImageViewerSessionController _sessionController;
        private readonly ImageViewerRoiPersistenceController _roiPersistenceController;
        private readonly Action _updateContextMenuState;

        public ImageViewerFileMenuCommandHostAdapter(
            Func<Task> showOpenImageDialogAsync,
            ImageViewerSessionController sessionController,
            ImageViewerRoiPersistenceController roiPersistenceController,
            Action updateContextMenuState)
        {
            _showOpenImageDialogAsync = showOpenImageDialogAsync ?? throw new ArgumentNullException(nameof(showOpenImageDialogAsync));
            _sessionController = sessionController ?? throw new ArgumentNullException(nameof(sessionController));
            _roiPersistenceController = roiPersistenceController ?? throw new ArgumentNullException(nameof(roiPersistenceController));
            _updateContextMenuState = updateContextMenuState ?? throw new ArgumentNullException(nameof(updateContextMenuState));
        }

        public Task ShowOpenImageDialogAsync() => _showOpenImageDialogAsync();

        public Task OpenRecentProjectAsync(string filePath) => _sessionController.OpenRecentProjectAsync(filePath);

        public Task SaveRoisAsync() => _roiPersistenceController.SaveRoisAsync();

        public Task LoadRoisAsync() => _roiPersistenceController.LoadRoisAsync();

        public Task SaveSessionAsync() => _sessionController.SaveSessionAsync();

        public Task LoadSessionAsync() => _sessionController.LoadProjectAsync();

        public Task ExportProjectPackageAsync() => _sessionController.ExportProjectPackageAsync();

        public void ToggleAutoSave() => _sessionController.ToggleAutoSave();

        public void UpdateContextMenuState() => _updateContextMenuState();
    }
}