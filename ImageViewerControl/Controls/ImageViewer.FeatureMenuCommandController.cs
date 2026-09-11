using System;
using System.Threading.Tasks;

namespace ImageViewer.Controls
{
    internal interface IImageViewerFeatureMenuCommandHost
    {
        void RunGradientDetection();

        Task ExportSnapshotAsync();

        Task ExportAnalysisCsvAsync();

        void ShowAnalysisSummary();

        void UpdateContextMenuState();
    }

    internal sealed class ImageViewerFeatureMenuCommandController
    {
        private readonly IImageViewerFeatureMenuCommandHost _host;

        public ImageViewerFeatureMenuCommandController(IImageViewerFeatureMenuCommandHost host)
        {
            _host = host ?? throw new ArgumentNullException(nameof(host));
        }

        public async Task ExecuteAsync(ImageViewerFeatureMenuCommand command)
        {
            switch (command)
            {
                case ImageViewerFeatureMenuCommand.GradientDetect:
                    _host.RunGradientDetection();
                    break;
                case ImageViewerFeatureMenuCommand.ExportSnapshot:
                    await _host.ExportSnapshotAsync();
                    break;
                case ImageViewerFeatureMenuCommand.ExportAnalysisCsv:
                    await _host.ExportAnalysisCsvAsync();
                    break;
                case ImageViewerFeatureMenuCommand.ShowAnalysisSummary:
                    _host.ShowAnalysisSummary();
                    break;
                default:
                    throw new ArgumentOutOfRangeException(nameof(command), command, null);
            }

            _host.UpdateContextMenuState();
        }
    }
}