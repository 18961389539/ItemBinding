using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using ImageViewer.Models;
using ImageViewer.Plugins;
using ImageViewer.Services;

namespace ImageViewer.Abstractions
{
    public interface IImageViewerProjectPackageService
    {
        Task ExportAsync(string packagePath, string? imagePath, IEnumerable<RoiBase> rois, double pixelSize, string? physicalUnit, double scale, double translateX, double translateY, RoiPluginRegistry? pluginRegistry = null, CancellationToken cancellationToken = default);

        Task<ImageViewerSessionData> LoadAsync(string packagePath, RoiPluginRegistry? pluginRegistry = null, CancellationToken cancellationToken = default);
    }
}
