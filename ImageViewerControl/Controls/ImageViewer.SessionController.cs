using System;
using System.IO;
using System.Threading.Tasks;
using ImageViewer.Abstractions;
using ImageViewer.Localization;
using ImageViewer.Models;
using ImageViewer.Plugins;
using ImageViewer.Services;
using System.Windows.Threading;

namespace ImageViewer.Controls
{
    internal interface IImageViewerSessionControllerHost
    {
        Dispatcher Dispatcher { get; }

        string? ShowSaveSessionDialog();

        string? ShowOpenSessionDialog();

        string? ShowSaveProjectPackageDialog();

        IImageViewerSessionService SessionService { get; }

        IImageViewerRecentProjectService RecentProjectService { get; }

        IImageViewerProjectPackageService ProjectPackageService { get; }

        RoiPluginRegistry PluginRegistry { get; }

        IReadOnlyList<RoiBase> AllRois { get; }

        double PixelSize { get; set; }

        string PhysicalUnit { get; set; }

        ImageViewerViewportState CurrentViewportState { get; }

        bool HasContent { get; }

        string? TryGetCurrentImagePath();

        void LoadImageFromFile(string filePath, bool fitToView);

        void ReplaceAllRois(IReadOnlyList<RoiBase> rois);

        void ApplyViewportState(ImageViewerViewportState state);

        void DrawRois();

        void ShowNonCriticalError(string title, string message, Exception ex);

        void LogNonCriticalError(string context, Exception ex);

        void ShowWarning(string title, string message);

        void UpdateContextMenuState();
    }

    internal sealed class ImageViewerSessionController : IDisposable
    {
        private const string SessionProjectKind = "session";
        private const string PackageProjectKind = "package";
        private readonly IImageViewerSessionControllerHost _host;
        private readonly ImageViewerRecentProjectCatalog _recentProjectCatalog;
        private readonly ImageViewerAutoSaveController _autoSaveController;
        private string? _currentProjectPath;

        public ImageViewerSessionController(
            IImageViewerSessionControllerHost host,
            IImageViewerPeriodicTaskSchedulerFactory periodicTaskSchedulerFactory,
            IImageViewerSessionStoragePolicy sessionStoragePolicy)
        {
            _host = host ?? throw new ArgumentNullException(nameof(host));
            ArgumentNullException.ThrowIfNull(periodicTaskSchedulerFactory);
            ArgumentNullException.ThrowIfNull(sessionStoragePolicy);

            _recentProjectCatalog = new ImageViewerRecentProjectCatalog(_host.RecentProjectService, sessionStoragePolicy.RecentProjectsFilePath);
            _autoSaveController = new ImageViewerAutoSaveController(_host, periodicTaskSchedulerFactory, sessionStoragePolicy);
        }

        public bool IsAutoSaveEnabled => _autoSaveController.IsEnabled;

        public async Task SaveSessionAsync()
        {
            string? filePath = _host.ShowSaveSessionDialog();
            if (string.IsNullOrWhiteSpace(filePath))
            {
                return;
            }

            try
            {
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
                SetCurrentProject(filePath, SessionProjectKind);
            }
            catch (Exception ex)
            {
                _host.ShowNonCriticalError(UiText.Get("ErrorSaveSessionTitle"), UiText.Get("ErrorSaveSessionMessage"), ex);
            }
        }

        public async Task LoadProjectAsync()
        {
            string? filePath = _host.ShowOpenSessionDialog();
            if (string.IsNullOrWhiteSpace(filePath))
            {
                return;
            }

            await OpenProjectAsync(filePath);
        }

        public async Task ExportProjectPackageAsync()
        {
            string? filePath = _host.ShowSaveProjectPackageDialog();
            if (string.IsNullOrWhiteSpace(filePath))
            {
                return;
            }

            try
            {
                ImageViewerViewportState viewportState = _host.CurrentViewportState;
                await _host.ProjectPackageService.ExportAsync(
                    filePath,
                    _host.TryGetCurrentImagePath(),
                    _host.AllRois,
                    _host.PixelSize,
                    _host.PhysicalUnit,
                    viewportState.Scale,
                    viewportState.TranslateX,
                    viewportState.TranslateY,
                    _host.PluginRegistry);
                SetCurrentProject(filePath, PackageProjectKind);
            }
            catch (Exception ex)
            {
                _host.ShowNonCriticalError(UiText.Get("ErrorExportProjectPackageTitle"), UiText.Get("ErrorExportProjectPackageMessage"), ex);
            }
        }

        public IReadOnlyList<ImageViewerDynamicMenuItem> GetRecentProjectMenuItems()
        {
            return _recentProjectCatalog.GetMenuItems();
        }

        public Task OpenRecentProjectAsync(string filePath)
        {
            return OpenProjectAsync(filePath);
        }

        public void ToggleAutoSave()
        {
            _autoSaveController.Toggle();
        }

        public void Dispose()
        {
            _autoSaveController.Dispose();
        }

        public async Task OpenProjectAsync(string filePath)
        {
            if (!File.Exists(filePath))
            {
                _recentProjectCatalog.RemoveMissing(filePath);
                _host.ShowWarning(UiText.Get("WarningFileMissingTitle"), UiText.Get("WarningRecentProjectRemoved"));
                _host.UpdateContextMenuState();
                return;
            }

            try
            {
                ImageViewerSessionData session = string.Equals(Path.GetExtension(filePath), ".ivpkg", StringComparison.OrdinalIgnoreCase)
                    ? await _host.ProjectPackageService.LoadAsync(filePath, _host.PluginRegistry)
                    : await _host.SessionService.LoadFromFileAsync(filePath, _host.PluginRegistry);

                ApplySession(session);
                SetCurrentProject(filePath, string.Equals(Path.GetExtension(filePath), ".ivpkg", StringComparison.OrdinalIgnoreCase) ? PackageProjectKind : SessionProjectKind);
            }
            catch (Exception ex)
            {
                _host.ShowNonCriticalError(UiText.Get("ErrorLoadProjectTitle"), UiText.Get("ErrorLoadProjectMessage"), ex);
            }
        }

        private void ApplySession(ImageViewerSessionData session)
        {
            if (!string.IsNullOrWhiteSpace(session.ImagePath) && File.Exists(session.ImagePath))
            {
                _host.LoadImageFromFile(session.ImagePath, fitToView: false);
            }

            _host.ReplaceAllRois(session.Rois);
            _host.PixelSize = session.PixelSize;
            _host.PhysicalUnit = session.PhysicalUnit;
            _host.ApplyViewportState(new ImageViewerViewportState(session.Scale, session.TranslateX, session.TranslateY));
            _host.DrawRois();
            _host.UpdateContextMenuState();
        }

        private void SetCurrentProject(string filePath, string projectKind)
        {
            _currentProjectPath = Path.GetFullPath(filePath);
            _autoSaveController.SetCurrentProject(_currentProjectPath);
            _recentProjectCatalog.Remember(_currentProjectPath, projectKind);
            _host.UpdateContextMenuState();
        }
    }
}