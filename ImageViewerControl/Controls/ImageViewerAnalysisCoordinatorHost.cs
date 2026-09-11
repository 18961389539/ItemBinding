using System.Windows;
using System.Windows.Media;
using System;
using ImageViewer.Abstractions;
using ImageViewer.Services;

namespace ImageViewer.Controls
{
    internal sealed class ImageViewerAnalysisCoordinatorHost : IImageViewerAnalysisHost
    {
        private readonly ImageViewer _owner;
        private readonly ImageViewerAnalysisState _analysisState;
        private readonly Func<Point> _translationProvider;
        private readonly Func<Size> _viewportSizeProvider;

        public ImageViewerAnalysisCoordinatorHost(
            ImageViewer owner,
            ImageViewerAnalysisState analysisState,
            Func<Point> translationProvider,
            Func<Size> viewportSizeProvider)
        {
            _owner = owner ?? throw new ArgumentNullException(nameof(owner));
            _analysisState = analysisState ?? throw new ArgumentNullException(nameof(analysisState));
            _translationProvider = translationProvider ?? throw new ArgumentNullException(nameof(translationProvider));
            _viewportSizeProvider = viewportSizeProvider ?? throw new ArgumentNullException(nameof(viewportSizeProvider));
        }

        public IImageViewerRenderService RenderService => _owner.RuntimeServices.RenderService;
        public ImageSource? ImageSource => _owner.ImageSource;
        public bool EnableGpuRendering => _owner.EnableGpuRendering;
        public bool PreferShaderPseudoColor => _owner.PreferShaderPseudoColor;
        public bool AllowCpuPseudoColorFallback => _owner.AllowCpuPseudoColorFallback;
        public PseudoColorPalette PseudoColorPalette => _owner.PseudoColorPalette;
        public bool EnableImagePyramid => _owner.EnableImagePyramid;
        public bool AutoSelectPyramidLevel => _owner.AutoSelectPyramidLevel;
        public bool EnableTiledRendering => _owner.EnableTiledRendering;
        public bool PrefetchAdjacentTiles => _owner.PrefetchAdjacentTiles;
        public int TileCacheMaximumMegabytes => _owner.TileCacheMaximumMegabytes;
        public int TilePrefetchRadius => _owner.TilePrefetchRadius;
        public double Scale => _owner.Scale;
        public Point Translation => _translationProvider();
        public Size ViewportSize => _viewportSizeProvider();
        public ImageViewerAnalysisState AnalysisState => _analysisState;
        public int HistogramBinCount => ImageViewerAnalysisCoordinator.HistogramBinCount;
        public bool ShowHistogram => _owner.ShowHistogram;
        public bool ShowProfile => _owner.ShowProfile;
        public bool EnableAsyncAnalysis => _owner.EnableAsyncAnalysis;
        public bool PauseRealtimeHistogram => _owner.PauseRealtimeHistogram;
        public bool PauseRealtimeProfile => _owner.PauseRealtimeProfile;
    }
}
