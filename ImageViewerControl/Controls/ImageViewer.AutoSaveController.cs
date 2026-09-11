using System;
using System.IO;
using System.Threading.Tasks;
using System.Windows.Threading;

namespace ImageViewer.Controls
{
    internal sealed class ImageViewerAutoSaveController : IDisposable
    {
        private readonly IImageViewerSessionControllerHost _host;
        private readonly IImageViewerPeriodicTaskScheduler _autoSaveScheduler;
        private readonly string _autoSaveDirectory;
        private string? _currentProjectPath;

        public ImageViewerAutoSaveController(
            IImageViewerSessionControllerHost host,
            IImageViewerPeriodicTaskSchedulerFactory periodicTaskSchedulerFactory,
            IImageViewerSessionStoragePolicy sessionStoragePolicy)
        {
            _host = host ?? throw new ArgumentNullException(nameof(host));
            ArgumentNullException.ThrowIfNull(periodicTaskSchedulerFactory);
            ArgumentNullException.ThrowIfNull(sessionStoragePolicy);

            _autoSaveDirectory = sessionStoragePolicy.AutoSaveDirectory;
            _autoSaveScheduler = periodicTaskSchedulerFactory.Create(
                AutoSaveAsync,
                _host.Dispatcher,
                DispatcherPriority.Background,
                sessionStoragePolicy.AutoSaveInterval);
            _autoSaveScheduler.Start();
        }

        public bool IsEnabled { get; private set; } = true;

        public void SetCurrentProject(string? filePath)
        {
            _currentProjectPath = string.IsNullOrWhiteSpace(filePath)
                ? null
                : Path.GetFullPath(filePath);
        }

        public void Toggle()
        {
            IsEnabled = !IsEnabled;
        }

        public void Dispose()
        {
            _autoSaveScheduler.StopScheduling();
            _autoSaveScheduler.Dispose();
        }

        private async Task AutoSaveAsync()
        {
            if (!IsEnabled || !_host.HasContent)
            {
                return;
            }

            try
            {
                Directory.CreateDirectory(_autoSaveDirectory);
                string filePath = Path.Combine(_autoSaveDirectory, $"{GetAutoSaveFileName()}.ivsession");
                ImageViewerViewportState viewportState = _host.CurrentViewportState;
                await _host.SessionService.SaveToFileAsync(
                    filePath,
                    _host.TryGetCurrentImagePath(),
                    _host.AllRois,
                    _host.PixelSize,
                    _host.PhysicalUnit,
                    viewportState.Scale,
                    viewportState.TranslateX,
                    viewportState.TranslateY,
                    _host.PluginRegistry);
            }
            catch (Exception ex)
            {
                _host.LogNonCriticalError("Auto save failed", ex);
            }
        }

        private string GetAutoSaveFileName()
        {
            string baseName = Path.GetFileNameWithoutExtension(_currentProjectPath) ?? "autosave";
            foreach (char invalidChar in Path.GetInvalidFileNameChars())
            {
                baseName = baseName.Replace(invalidChar, '_');
            }

            return string.IsNullOrWhiteSpace(baseName) ? "autosave" : baseName;
        }
    }
}