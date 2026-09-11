using System;
using System.IO;

namespace ImageViewer.Controls
{
    public interface IImageViewerSessionStoragePolicy
    {
        string RecentProjectsFilePath { get; }

        string AutoSaveDirectory { get; }

        string ProjectPackageCacheDirectory { get; }

        TimeSpan AutoSaveInterval { get; }
    }

    public sealed class LocalAppDataImageViewerSessionStoragePolicy : IImageViewerSessionStoragePolicy
    {
        public LocalAppDataImageViewerSessionStoragePolicy()
            : this(
                Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "ImageViewer"),
                TimeSpan.FromSeconds(30))
        {
        }

        public LocalAppDataImageViewerSessionStoragePolicy(string storageRoot, TimeSpan autoSaveInterval)
        {
            ArgumentException.ThrowIfNullOrWhiteSpace(storageRoot);

            RecentProjectsFilePath = Path.Combine(storageRoot, "recent-projects.json");
            AutoSaveDirectory = Path.Combine(storageRoot, "Autosave");
            ProjectPackageCacheDirectory = Path.Combine(storageRoot, "PackageCache");
            AutoSaveInterval = autoSaveInterval <= TimeSpan.Zero
                ? throw new ArgumentOutOfRangeException(nameof(autoSaveInterval))
                : autoSaveInterval;
        }

        public string RecentProjectsFilePath { get; }

        public string AutoSaveDirectory { get; }

        public string ProjectPackageCacheDirectory { get; }

        public TimeSpan AutoSaveInterval { get; }
    }
}