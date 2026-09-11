using System;
using System.Collections.Generic;
using System.IO;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using ImageViewer.Abstractions;
using ImageViewer.Models;
using ImageViewer.Plugins;

namespace ImageViewer.Services
{
    public sealed class ImageViewerSessionService : IImageViewerSessionService
    {
        private static readonly JsonSerializerOptions SerializerOptions = new()
        {
            WriteIndented = true,
            PropertyNameCaseInsensitive = true
        };

        public void SaveToFile(string filePath, string? imagePath, IEnumerable<RoiBase> rois, double pixelSize, string? physicalUnit, double scale, double translateX, double translateY, RoiPluginRegistry? pluginRegistry = null)
        {
            ArgumentException.ThrowIfNullOrWhiteSpace(filePath);
            ArgumentNullException.ThrowIfNull(rois);
            ArgumentNullException.ThrowIfNull(pluginRegistry);

            EnsureDirectory(filePath);
            File.WriteAllText(filePath, SerializeSession(Path.GetFileNameWithoutExtension(filePath), imagePath, rois, pixelSize, physicalUnit, scale, translateX, translateY, pluginRegistry));
        }

        public Task SaveToFileAsync(string filePath, string? imagePath, IEnumerable<RoiBase> rois, double pixelSize, string? physicalUnit, double scale, double translateX, double translateY, RoiPluginRegistry? pluginRegistry = null, CancellationToken cancellationToken = default)
        {
            ArgumentException.ThrowIfNullOrWhiteSpace(filePath);
            ArgumentNullException.ThrowIfNull(rois);
            ArgumentNullException.ThrowIfNull(pluginRegistry);

            EnsureDirectory(filePath);
            return File.WriteAllTextAsync(filePath, SerializeSession(Path.GetFileNameWithoutExtension(filePath), imagePath, rois, pixelSize, physicalUnit, scale, translateX, translateY, pluginRegistry), cancellationToken);
        }

        public string SerializeSession(string? sessionName, string? imagePath, IEnumerable<RoiBase> rois, double pixelSize, string? physicalUnit, double scale, double translateX, double translateY, RoiPluginRegistry? pluginRegistry = null)
        {
            ArgumentNullException.ThrowIfNull(rois);
            ArgumentNullException.ThrowIfNull(pluginRegistry);

            var session = new ImageViewerSessionDocument
            {
                SessionName = sessionName,
                SavedAtUtc = DateTimeOffset.UtcNow,
                ImagePath = imagePath,
                RoiDocumentJson = RoiPersistenceService.Serialize(rois, pixelSize, physicalUnit, pluginRegistry),
                Scale = scale,
                TranslateX = translateX,
                TranslateY = translateY
            };

            return JsonSerializer.Serialize(session, SerializerOptions);
        }

        public ImageViewerSessionData LoadFromFile(string filePath, RoiPluginRegistry? pluginRegistry = null)
        {
            ArgumentException.ThrowIfNullOrWhiteSpace(filePath);
            ArgumentNullException.ThrowIfNull(pluginRegistry);

            return LoadFromJson(File.ReadAllText(filePath), Path.GetDirectoryName(Path.GetFullPath(filePath)), pluginRegistry);
        }

        public async Task<ImageViewerSessionData> LoadFromFileAsync(string filePath, RoiPluginRegistry? pluginRegistry = null, CancellationToken cancellationToken = default)
        {
            ArgumentException.ThrowIfNullOrWhiteSpace(filePath);
            ArgumentNullException.ThrowIfNull(pluginRegistry);

            return LoadFromJson(await File.ReadAllTextAsync(filePath, cancellationToken).ConfigureAwait(false), Path.GetDirectoryName(Path.GetFullPath(filePath)), pluginRegistry);
        }

        public ImageViewerSessionData LoadFromJson(string sessionJson, string? sessionBaseDirectory = null, RoiPluginRegistry? pluginRegistry = null)
        {
            ArgumentNullException.ThrowIfNull(pluginRegistry);
            var session = JsonSerializer.Deserialize<ImageViewerSessionDocument>(sessionJson, SerializerOptions)
                ?? new ImageViewerSessionDocument();
            var roiData = RoiPersistenceService.Deserialize(session.RoiDocumentJson ?? string.Empty, pluginRegistry);
            return new ImageViewerSessionData(session.SessionName, session.SavedAtUtc, ResolveImagePath(session.ImagePath, sessionBaseDirectory), roiData.Rois, roiData.PixelSize, roiData.PhysicalUnit, session.Scale, session.TranslateX, session.TranslateY);
        }

        private static string? ResolveImagePath(string? imagePath, string? sessionBaseDirectory)
        {
            if (string.IsNullOrWhiteSpace(imagePath) || string.IsNullOrWhiteSpace(sessionBaseDirectory) || Path.IsPathRooted(imagePath))
            {
                return imagePath;
            }

            return Path.GetFullPath(Path.Combine(sessionBaseDirectory, imagePath));
        }

        private static void EnsureDirectory(string filePath)
        {
            string? directory = Path.GetDirectoryName(Path.GetFullPath(filePath));
            if (!string.IsNullOrWhiteSpace(directory))
            {
                Directory.CreateDirectory(directory);
            }
        }

        private sealed class ImageViewerSessionDocument
        {
            public string? SessionName { get; set; }
            public DateTimeOffset SavedAtUtc { get; set; }
            public string? ImagePath { get; set; }
            public string RoiDocumentJson { get; set; } = string.Empty;
            public double Scale { get; set; } = 1.0;
            public double TranslateX { get; set; }
            public double TranslateY { get; set; }
        }
    }

    public sealed record ImageViewerSessionData(
        string? SessionName,
        DateTimeOffset SavedAtUtc,
        string? ImagePath,
        IReadOnlyList<RoiBase> Rois,
        double PixelSize,
        string PhysicalUnit,
        double Scale,
        double TranslateX,
        double TranslateY);
}
