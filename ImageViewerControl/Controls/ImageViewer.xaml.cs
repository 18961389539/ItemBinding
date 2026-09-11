using System;
using System.Threading.Tasks;
using System.Windows.Controls;
using System.Windows.Media;
using ImageViewer.Services;

namespace ImageViewer.Controls
{
    public partial class ImageViewer : UserControl, IDisposable
    {
        /// <summary>
        /// ImageViewer 控件：主要入口类
        /// Chinese: 该类实现了图像查看器控件的 UI 互动绑定、依赖属性以及键盘/鼠标事件的初始化。
        /// English: Main ImageViewer control partial class that wires up dependency properties and input handlers.
        /// </summary>
        private readonly ImageViewerInteractionManipulationState _interactionManipulationState = new();
        private readonly ImageViewerHostState _hostState;
        private const double MinScale = 0.1;
        private const double MaxScale = 100;
        private readonly ImageViewerControlComposition _controlComposition;
        private readonly ImageViewerAnalysisState _analysisState = new();
        private readonly IImageViewerLatestTaskScheduler _infoPanelStatisticsScheduler;
        private readonly ImageViewerLifetime _lifetime;

        public ImageViewer()
            : this(ImageViewerHost.CreateDefault().Dependencies)
        {
        }

        public ImageViewer(ImageViewerDependencies dependencies)
        {
            ImageViewerBootstrapState bootstrapState = CreateBootstrapState(dependencies);
            _hostState = bootstrapState.HostState;
            _viewportOverlayRefreshScheduler = bootstrapState.ViewportOverlayRefreshScheduler;
            _analysisRefreshScheduler = bootstrapState.AnalysisRefreshScheduler;
            _infoPanelStatisticsScheduler = bootstrapState.InfoPanelStatisticsScheduler;
            _controlComposition = dependencies.CreateControlComposition(this);
            _lifetime = new ImageViewerLifetime(CreateLifetimeRegistrations(_controlComposition));
            CompleteBootstrap();
        }

        private void ResetView() => _viewCommandController.Execute(ImageViewerViewCommand.ResetView);

        public void SetImage(ImageSource source)
        {
            ImageSource = source;
        }

        internal void SetImageLoadState(bool isLoading, string statusText, double progress, bool canRetry)
        {
            IsImageLoading = isLoading;
            ImageLoadStatusText = statusText;
            ImageLoadProgress = Math.Clamp(progress, 0, 100);
            CanRetryImageLoad = canRetry;
        }

        public Task RetryLastImageLoadAsync() => _controlComposition.DialogWorkflowService.RetryLastImageLoadAsync();

        public void Dispose()
        {
            _lifetime.Dispose();
            GC.SuppressFinalize(this);
        }
    }
}
