using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using ImageViewer.Models;
using ImageViewer.Plugins;

namespace ImageViewer.Services
{
    public static class RoiPersistenceService
    {
        private const int CurrentDocumentVersion = 1;
        private static readonly JsonSerializerOptions SerializerOptions = new()
        {
            WriteIndented = true,
            PropertyNameCaseInsensitive = true
        };

        public static void SaveToFile(string filePath, IEnumerable<RoiBase> rois, double pixelSize, string? physicalUnit, RoiPluginRegistry? pluginRegistry = null)
        {
            ArgumentException.ThrowIfNullOrWhiteSpace(filePath);
            ArgumentNullException.ThrowIfNull(rois);
            ArgumentNullException.ThrowIfNull(pluginRegistry);

            File.WriteAllText(filePath, Serialize(rois, pixelSize, physicalUnit, pluginRegistry));
        }

        public static Task SaveToFileAsync(string filePath, IEnumerable<RoiBase> rois, double pixelSize, string? physicalUnit, RoiPluginRegistry? pluginRegistry = null, CancellationToken cancellationToken = default)
        {
            ArgumentException.ThrowIfNullOrWhiteSpace(filePath);
            ArgumentNullException.ThrowIfNull(rois);
            ArgumentNullException.ThrowIfNull(pluginRegistry);

            return File.WriteAllTextAsync(filePath, Serialize(rois, pixelSize, physicalUnit, pluginRegistry), cancellationToken);
        }

        public static string Serialize(IEnumerable<RoiBase> rois, double pixelSize, string? physicalUnit, RoiPluginRegistry? pluginRegistry = null)
        {
            ArgumentNullException.ThrowIfNull(rois);
            var roiPlugins = pluginRegistry ?? throw new ArgumentNullException(nameof(pluginRegistry));

            var document = new RoiDocument
            {
                Version = CurrentDocumentVersion,
                PixelSize = pixelSize,
                PhysicalUnit = string.IsNullOrWhiteSpace(physicalUnit) ? "px" : physicalUnit,
                Items = rois.Select(roi => CreateItem(roi, roiPlugins)).ToList()
            };

            return JsonSerializer.Serialize(document, SerializerOptions);
        }

        public static (IReadOnlyList<RoiBase> Rois, double PixelSize, string PhysicalUnit) LoadFromFile(string filePath, RoiPluginRegistry? pluginRegistry = null)
        {
            ArgumentException.ThrowIfNullOrWhiteSpace(filePath);
            ArgumentNullException.ThrowIfNull(pluginRegistry);

            return Deserialize(File.ReadAllText(filePath), pluginRegistry);
        }

        public static async Task<(IReadOnlyList<RoiBase> Rois, double PixelSize, string PhysicalUnit)> LoadFromFileAsync(string filePath, RoiPluginRegistry? pluginRegistry = null, CancellationToken cancellationToken = default)
        {
            ArgumentException.ThrowIfNullOrWhiteSpace(filePath);
            ArgumentNullException.ThrowIfNull(pluginRegistry);

            return Deserialize(await File.ReadAllTextAsync(filePath, cancellationToken).ConfigureAwait(false), pluginRegistry);
        }

        public static (IReadOnlyList<RoiBase> Rois, double PixelSize, string PhysicalUnit) Deserialize(string json, RoiPluginRegistry? pluginRegistry = null)
        {
            ArgumentNullException.ThrowIfNull(json);
            var roiPlugins = pluginRegistry ?? throw new ArgumentNullException(nameof(pluginRegistry));

            var document = JsonSerializer.Deserialize<RoiDocument>(json, SerializerOptions) ?? new RoiDocument();
            var rois = document.Items
                .Select(item => CreateRoi(item, roiPlugins))
                .Where(roi => roi != null)
                .Cast<RoiBase>()
                .ToList();

            return (
                rois,
                document.PixelSize <= 0 ? 1.0 : document.PixelSize,
                string.IsNullOrWhiteSpace(document.PhysicalUnit) ? "px" : document.PhysicalUnit);
        }

        private static RoiPersistenceData CreateItem(RoiBase roi, RoiPluginRegistry roiPlugins)
        {
            var plugin = roiPlugins.FindByRoi(roi)
                ?? throw new InvalidOperationException($"No ROI plugin registered for type '{roi.GetType().FullName}'.");

            var item = new RoiPersistenceData();
            item.PopulateCommonState(roi, plugin.TypeKey);
            plugin.PopulatePersistenceData(roi, item);
            return item;
        }

        private static RoiBase? CreateRoi(RoiPersistenceData item, RoiPluginRegistry roiPlugins)
        {
            var plugin = ResolvePlugin(item, roiPlugins);
            if (plugin == null)
            {
                return null;
            }

            var roi = plugin.CreateRoi(item);
            roi.ApplyCommonState(item);
            return roi;
        }

        private static IRoiPlugin? ResolvePlugin(RoiPersistenceData item, RoiPluginRegistry roiPlugins)
        {
            if (string.IsNullOrWhiteSpace(item.Type))
            {
                return null;
            }

            return roiPlugins.FindByTypeKey(item.Type)
                ?? roiPlugins.Plugins.FirstOrDefault(plugin => string.Equals(plugin.RoiType.Name, item.Type, StringComparison.OrdinalIgnoreCase));
        }

        private sealed class RoiDocument
        {
            public int Version { get; set; } = CurrentDocumentVersion;
            public double PixelSize { get; set; } = 1.0;
            public string PhysicalUnit { get; set; } = "px";
            public List<RoiPersistenceData> Items { get; set; } = new();
        }
    }
}
