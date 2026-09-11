using System;
using System.Collections.Generic;
using System.IO;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;

namespace ImageViewer.Plugins
{
    public sealed class RoiPluginDiscoveryOptions
    {
        private static readonly JsonSerializerOptions SerializerOptions = new()
        {
            WriteIndented = true,
            PropertyNameCaseInsensitive = true
        };

        public string? PluginDirectoryPath { get; set; }

        public List<string> DisabledAssemblyNames { get; set; } = new();

        public List<string> DisabledModuleTypeNames { get; set; } = new();

        public List<string> DisabledPluginTypeKeys { get; set; } = new();

        public List<string> UnloadedPluginTypeKeys { get; set; } = new();

        public static RoiPluginDiscoveryOptions Load(string filePath)
        {
            ArgumentException.ThrowIfNullOrWhiteSpace(filePath);

            if (!File.Exists(filePath))
            {
                return new RoiPluginDiscoveryOptions();
            }

            string json = File.ReadAllText(filePath);
            return JsonSerializer.Deserialize<RoiPluginDiscoveryOptions>(json, SerializerOptions) ?? new RoiPluginDiscoveryOptions();
        }

        public static async Task<RoiPluginDiscoveryOptions> LoadAsync(string filePath, CancellationToken cancellationToken = default)
        {
            ArgumentException.ThrowIfNullOrWhiteSpace(filePath);

            if (!File.Exists(filePath))
            {
                return new RoiPluginDiscoveryOptions();
            }

            string json = await File.ReadAllTextAsync(filePath, cancellationToken).ConfigureAwait(false);
            return JsonSerializer.Deserialize<RoiPluginDiscoveryOptions>(json, SerializerOptions) ?? new RoiPluginDiscoveryOptions();
        }

        public string ResolvePluginDirectory(string baseDirectory)
        {
            ArgumentException.ThrowIfNullOrWhiteSpace(baseDirectory);

            if (string.IsNullOrWhiteSpace(PluginDirectoryPath))
            {
                return baseDirectory;
            }

            return Path.IsPathRooted(PluginDirectoryPath)
                ? PluginDirectoryPath
                : Path.GetFullPath(Path.Combine(baseDirectory, PluginDirectoryPath));
        }
    }
}
