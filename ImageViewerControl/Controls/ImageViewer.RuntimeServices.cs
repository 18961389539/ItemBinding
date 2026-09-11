using System;
using ImageViewer.Abstractions;

namespace ImageViewer.Controls
{
    public sealed class ImageViewerRuntimeServices
    {
        public ImageViewerRuntimeServices(
            IImageViewerDialogService dialogService,
            IImageViewerFileDialogService fileDialogService,
            IImageViewerLogger logger,
            IImageViewerViewportService viewportService,
            IImageViewerSessionService sessionService,
            IImageViewerRecentProjectService recentProjectService,
            IImageViewerProjectPackageService projectPackageService,
            IImageViewerRenderService renderService,
            ISelectedRoiDetectionService selectedRoiDetectionService)
        {
            DialogService = dialogService ?? throw new ArgumentNullException(nameof(dialogService));
            FileDialogService = fileDialogService ?? throw new ArgumentNullException(nameof(fileDialogService));
            Logger = logger ?? throw new ArgumentNullException(nameof(logger));
            ViewportService = viewportService ?? throw new ArgumentNullException(nameof(viewportService));
            SessionService = sessionService ?? throw new ArgumentNullException(nameof(sessionService));
            RecentProjectService = recentProjectService ?? throw new ArgumentNullException(nameof(recentProjectService));
            ProjectPackageService = projectPackageService ?? throw new ArgumentNullException(nameof(projectPackageService));
            RenderService = renderService ?? throw new ArgumentNullException(nameof(renderService));
            SelectedRoiDetectionService = selectedRoiDetectionService ?? throw new ArgumentNullException(nameof(selectedRoiDetectionService));
        }

        public IImageViewerDialogService DialogService { get; }

        public IImageViewerFileDialogService FileDialogService { get; }

        public IImageViewerLogger Logger { get; }

        public IImageViewerViewportService ViewportService { get; }

        public IImageViewerSessionService SessionService { get; }

        public IImageViewerRecentProjectService RecentProjectService { get; }

        public IImageViewerProjectPackageService ProjectPackageService { get; }

        public IImageViewerRenderService RenderService { get; }

        public ISelectedRoiDetectionService SelectedRoiDetectionService { get; }
    }
}