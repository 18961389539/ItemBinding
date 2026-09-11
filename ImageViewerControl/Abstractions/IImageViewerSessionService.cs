using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using ImageViewer.Models;
using ImageViewer.Plugins;
using ImageViewer.Services;

namespace ImageViewer.Abstractions
{
    public interface IImageViewerSessionService
    {
        void SaveToFile(string filePath, string? imagePath, IEnumerable<RoiBase> rois, double pixelSize, string? physicalUnit, double scale, double translateX, double translateY, RoiPluginRegistry? pluginRegistry = null);

        Task SaveToFileAsync(string filePath, string? imagePath, IEnumerable<RoiBase> rois, double pixelSize, string? physicalUnit, double scale, double translateX, double translateY, RoiPluginRegistry? pluginRegistry = null, CancellationToken cancellationToken = default);

        string SerializeSession(string? sessionName, string? imagePath, IEnumerable<RoiBase> rois, double pixelSize, string? physicalUnit, double scale, double translateX, double translateY, RoiPluginRegistry? pluginRegistry = null);

        ImageViewerSessionData LoadFromFile(string filePath, RoiPluginRegistry? pluginRegistry = null);

        Task<ImageViewerSessionData> LoadFromFileAsync(string filePath, RoiPluginRegistry? pluginRegistry = null, CancellationToken cancellationToken = default);

        ImageViewerSessionData LoadFromJson(string sessionJson, string? sessionBaseDirectory = null, RoiPluginRegistry? pluginRegistry = null);
    }
}
