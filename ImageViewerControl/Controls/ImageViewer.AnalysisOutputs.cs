using System;
using System.Windows.Media.Effects;
using ImageViewer.Services;

namespace ImageViewer.Controls
{
    internal sealed record ImageViewerHistogramOutput(int[] Histogram, int HistogramBinCount);

    internal sealed record ImageViewerProfileOutput(byte[] ProfileData);

    internal sealed record ImageViewerPseudoColorMenuState(PseudoColorPalette SelectedPalette);

    internal sealed record ImageViewerRenderedImagePlan(
        ImageViewerRenderFrame RenderFrame,
        Effect? Effect,
        bool EnableGpuRendering)
    {
        public bool IsShaderPseudoColorActive => Effect != null;
    }

    internal sealed record ImageViewerRenderStatus(
        int? SourcePixelWidth,
        int? SourcePixelHeight,
        bool EnableImagePyramid,
        int PyramidLevelCount,
        TimeSpan PyramidBuildDuration,
        bool AutoSelectPyramidLevel,
        bool EnableTiledRendering,
        bool PrefetchAdjacentTiles,
        int TileCacheMaximumMegabytes,
        int TilePrefetchRadius,
        ImageViewerRenderFrame LastRenderFrame,
        bool EnableGpuRendering,
        PseudoColorPalette PseudoColorPalette,
        bool IsShaderPseudoColorActive,
        bool EnableAsyncAnalysis,
        bool PauseRealtimeHistogram,
        TimeSpan LastHistogramDuration,
        bool PauseRealtimeProfile,
        TimeSpan LastProfileDuration);
}