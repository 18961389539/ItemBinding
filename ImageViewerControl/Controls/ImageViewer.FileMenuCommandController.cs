using System;
using System.Threading.Tasks;

namespace ImageViewer.Controls
{
    internal interface IImageViewerFileMenuCommandHost
    {
        Task ShowOpenImageDialogAsync();

        Task OpenRecentProjectAsync(string filePath);

        Task SaveRoisAsync();

        Task LoadRoisAsync();

        Task SaveSessionAsync();

        Task LoadSessionAsync();

        Task ExportProjectPackageAsync();

        void ToggleAutoSave();

        void UpdateContextMenuState();
    }

    internal sealed class ImageViewerFileMenuCommandController
    {
        private readonly IImageViewerFileMenuCommandHost _host;

        public ImageViewerFileMenuCommandController(IImageViewerFileMenuCommandHost host)
        {
            _host = host;
        }

        public async Task ExecuteAsync(ImageViewerFileMenuCommand command)
        {
            switch (command)
            {
                case ImageViewerFileMenuCommand.OpenImage:
                    await _host.ShowOpenImageDialogAsync();
                    break;
                case ImageViewerFileMenuCommand.SaveRois:
                    await _host.SaveRoisAsync();
                    break;
                case ImageViewerFileMenuCommand.LoadRois:
                    await _host.LoadRoisAsync();
                    break;
                case ImageViewerFileMenuCommand.SaveSession:
                    await _host.SaveSessionAsync();
                    break;
                case ImageViewerFileMenuCommand.LoadSession:
                    await _host.LoadSessionAsync();
                    break;
                case ImageViewerFileMenuCommand.ExportProjectPackage:
                    await _host.ExportProjectPackageAsync();
                    break;
                case ImageViewerFileMenuCommand.ToggleAutoSave:
                    _host.ToggleAutoSave();
                    break;
                default:
                    throw new ArgumentOutOfRangeException(nameof(command), command, null);
            }

            _host.UpdateContextMenuState();
        }

        public async Task OpenRecentProjectAsync(string filePath)
        {
            if (string.IsNullOrWhiteSpace(filePath))
            {
                return;
            }

            await _host.OpenRecentProjectAsync(filePath);
            _host.UpdateContextMenuState();
        }
    }
}