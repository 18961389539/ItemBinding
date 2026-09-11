using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using ImageViewer.Abstractions;

namespace ImageViewer.Services
{
    public sealed class ImageViewerRecentProjectService : IImageViewerRecentProjectService
    {
        private static readonly JsonSerializerOptions SerializerOptions = new()
        {
            WriteIndented = true,
            PropertyNameCaseInsensitive = true
        };

        public IReadOnlyList<RecentImageViewerProject> Load(string filePath, int maxCount = 10)
        {
            ArgumentException.ThrowIfNullOrWhiteSpace(filePath);

            if (!File.Exists(filePath))
            {
                return [];
            }

            var items = JsonSerializer.Deserialize<List<RecentImageViewerProject>>(File.ReadAllText(filePath), SerializerOptions) ?? [];
            return items
                .Where(item => !string.IsNullOrWhiteSpace(item.Path))
                .OrderByDescending(item => item.LastOpenedUtc)
                .Take(Math.Max(1, maxCount))
                .ToArray();
        }

        public void Save(string filePath, IEnumerable<RecentImageViewerProject> items)
        {
            ArgumentException.ThrowIfNullOrWhiteSpace(filePath);
            ArgumentNullException.ThrowIfNull(items);

            string? directory = Path.GetDirectoryName(Path.GetFullPath(filePath));
            if (!string.IsNullOrWhiteSpace(directory))
            {
                Directory.CreateDirectory(directory);
            }

            File.WriteAllText(filePath, JsonSerializer.Serialize(items, SerializerOptions));
        }

        public IReadOnlyList<RecentImageViewerProject> Touch(IEnumerable<RecentImageViewerProject> items, string filePath, string projectKind, int maxCount = 10)
        {
            ArgumentNullException.ThrowIfNull(items);
            ArgumentException.ThrowIfNullOrWhiteSpace(filePath);
            ArgumentException.ThrowIfNullOrWhiteSpace(projectKind);

            var normalizedPath = Path.GetFullPath(filePath);
            var updated = items
                .Where(item => !string.Equals(Path.GetFullPath(item.Path), normalizedPath, StringComparison.OrdinalIgnoreCase))
                .Prepend(new RecentImageViewerProject(Path.GetFileNameWithoutExtension(normalizedPath), normalizedPath, projectKind, DateTimeOffset.UtcNow))
                .OrderByDescending(item => item.LastOpenedUtc)
                .Take(Math.Max(1, maxCount))
                .ToArray();

            return updated;
        }
    }

    public sealed record RecentImageViewerProject(string DisplayName, string Path, string ProjectKind, DateTimeOffset LastOpenedUtc);
}
