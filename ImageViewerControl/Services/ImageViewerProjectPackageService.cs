using System;
using System.Collections.Generic;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using ImageViewer.Abstractions;
using ImageViewer.Controls;
using ImageViewer.Models;
using ImageViewer.Plugins;

namespace ImageViewer.Services
{
    public sealed class ImageViewerProjectPackageService : IImageViewerProjectPackageService
    {
        private const string SessionEntryName = "session.ivsession";
        private const string AssetFolderName = "assets";
        private readonly IImageViewerSessionService _sessionService;
        private readonly IImageViewerSessionStoragePolicy _sessionStoragePolicy;

        public ImageViewerProjectPackageService(IImageViewerSessionService sessionService, IImageViewerSessionStoragePolicy sessionStoragePolicy)
        {
            _sessionService = sessionService ?? throw new ArgumentNullException(nameof(sessionService));
            _sessionStoragePolicy = sessionStoragePolicy ?? throw new ArgumentNullException(nameof(sessionStoragePolicy));
        }

        public async Task ExportAsync(string packagePath, string? imagePath, IEnumerable<RoiBase> rois, double pixelSize, string? physicalUnit, double scale, double translateX, double translateY, RoiPluginRegistry? pluginRegistry = null, CancellationToken cancellationToken = default)
        {
            ArgumentException.ThrowIfNullOrWhiteSpace(packagePath);
            ArgumentNullException.ThrowIfNull(rois);
            ArgumentNullException.ThrowIfNull(pluginRegistry);

            string fullPackagePath = Path.GetFullPath(packagePath);
            string? directory = Path.GetDirectoryName(fullPackagePath);
            if (!string.IsNullOrWhiteSpace(directory))
            {
                Directory.CreateDirectory(directory);
            }

            string? packagedImagePath = null;
            if (!string.IsNullOrWhiteSpace(imagePath) && File.Exists(imagePath))
            {
                packagedImagePath = $"{AssetFolderName}/{Path.GetFileName(imagePath)}";
            }

            string sessionJson = _sessionService.SerializeSession(
                Path.GetFileNameWithoutExtension(fullPackagePath),
                packagedImagePath ?? imagePath,
                rois,
                pixelSize,
                physicalUnit,
                scale,
                translateX,
                translateY,
                pluginRegistry);

            if (File.Exists(fullPackagePath))
            {
                File.Delete(fullPackagePath);
            }

            using var archive = ZipFile.Open(fullPackagePath, ZipArchiveMode.Create);
            var sessionEntry = archive.CreateEntry(SessionEntryName, CompressionLevel.Optimal);
            await using (var sessionStream = sessionEntry.Open())
            await using (var writer = new StreamWriter(sessionStream))
            {
                cancellationToken.ThrowIfCancellationRequested();
                await writer.WriteAsync(sessionJson).ConfigureAwait(false);
            }

            if (!string.IsNullOrWhiteSpace(packagedImagePath) && !string.IsNullOrWhiteSpace(imagePath))
            {
                archive.CreateEntryFromFile(imagePath, packagedImagePath, CompressionLevel.Optimal);
            }
        }

        public async Task<ImageViewerSessionData> LoadAsync(string packagePath, RoiPluginRegistry? pluginRegistry = null, CancellationToken cancellationToken = default)
        {
            ArgumentException.ThrowIfNullOrWhiteSpace(packagePath);
            ArgumentNullException.ThrowIfNull(pluginRegistry);

            string fullPackagePath = Path.GetFullPath(packagePath);
            using var archive = ZipFile.OpenRead(fullPackagePath);
            var sessionEntry = archive.GetEntry(SessionEntryName) ?? throw new InvalidDataException("The project package does not contain a session file.");

            string cacheRoot = GetPackageCacheDirectory(fullPackagePath);
            Directory.CreateDirectory(cacheRoot);
            ExtractArchiveEntries(archive, cacheRoot, cancellationToken);

            using var sessionStream = sessionEntry.Open();
            using var reader = new StreamReader(sessionStream);
            string sessionJson = await reader.ReadToEndAsync(cancellationToken).ConfigureAwait(false);
            return _sessionService.LoadFromJson(sessionJson, cacheRoot, pluginRegistry);
        }

        private static void ExtractArchiveEntries(ZipArchive archive, string destinationDirectory, CancellationToken cancellationToken)
        {
            string rootPath = Path.GetFullPath(destinationDirectory);
            if (!rootPath.EndsWith(Path.DirectorySeparatorChar))
            {
                rootPath += Path.DirectorySeparatorChar;
            }

            foreach (var entry in archive.Entries.Where(entry => !string.Equals(entry.FullName, SessionEntryName, StringComparison.OrdinalIgnoreCase)))
            {
                cancellationToken.ThrowIfCancellationRequested();

                if (string.IsNullOrWhiteSpace(entry.FullName))
                {
                    continue;
                }

                string destinationPath = Path.GetFullPath(Path.Combine(destinationDirectory, entry.FullName.Replace('/', Path.DirectorySeparatorChar)));
                if (!destinationPath.StartsWith(rootPath, StringComparison.OrdinalIgnoreCase))
                {
                    throw new InvalidDataException($"Unsafe archive entry path '{entry.FullName}'.");
                }

                string? destinationFolder = Path.GetDirectoryName(destinationPath);
                if (!string.IsNullOrWhiteSpace(destinationFolder))
                {
                    Directory.CreateDirectory(destinationFolder);
                }

                if (entry.FullName.EndsWith('/') || entry.FullName.EndsWith('\\'))
                {
                    Directory.CreateDirectory(destinationPath);
                    continue;
                }

                entry.ExtractToFile(destinationPath, true);
            }
        }

        private string GetPackageCacheDirectory(string packagePath)
        {
            string stamp = Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(System.Text.Encoding.UTF8.GetBytes(packagePath)))[..12];
            return Path.Combine(
                _sessionStoragePolicy.ProjectPackageCacheDirectory,
                $"{Path.GetFileNameWithoutExtension(packagePath)}-{stamp}");
        }
    }
}
