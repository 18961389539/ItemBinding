using System;
using System.Windows.Threading;

namespace ImageViewer.Controls
{
    public partial class ImageViewer
    {
        private ImageViewerBootstrapState CreateBootstrapState(ImageViewerDependencies dependencies)
        {
            ArgumentNullException.ThrowIfNull(dependencies);

            ImageViewerHostState hostState = new(dependencies.PluginRegistry, dependencies.RuntimeServices, dependencies.HostServices);
            IImageViewerDeferredRefreshScheduler viewportOverlayRefreshScheduler = hostState.HostServices.RefreshSchedulerFactory.CreateDeferred(
                RefreshViewportOverlay,
                Dispatcher,
                DispatcherPriority.Render,
                TimeSpan.FromMilliseconds(16));
            IImageViewerForcedRefreshScheduler analysisRefreshScheduler = hostState.HostServices.RefreshSchedulerFactory.CreateForced(
                RefreshAnalysisPanels,
                Dispatcher,
                DispatcherPriority.Background,
                TimeSpan.FromMilliseconds(48));
            IImageViewerLatestTaskScheduler infoPanelStatisticsScheduler = hostState.HostServices.LatestTaskSchedulerFactory.Create();

            InitializeComponent();
            mainContextMenu.DataContext = this;

            return new ImageViewerBootstrapState(
                hostState,
                viewportOverlayRefreshScheduler,
                analysisRefreshScheduler,
                infoPanelStatisticsScheduler);
        }

        private void CompleteBootstrap()
        {
            _lifetime.Attach();
            UpdateRenderedImage();
            RefreshRoiDrawingMenuItems();
            ApplyMenuItemContentAlignment(mainContextMenu);
            UpdateContextMenuState();
        }

        private readonly record struct ImageViewerBootstrapState(
            ImageViewerHostState HostState,
            IImageViewerDeferredRefreshScheduler ViewportOverlayRefreshScheduler,
            IImageViewerForcedRefreshScheduler AnalysisRefreshScheduler,
            IImageViewerLatestTaskScheduler InfoPanelStatisticsScheduler);
    }
}