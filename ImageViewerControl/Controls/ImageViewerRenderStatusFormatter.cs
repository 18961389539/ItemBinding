using ImageViewer.Services;
using ImageViewer.Localization;

namespace ImageViewer.Controls
{
    internal static class ImageViewerRenderStatusFormatter
    {
        public static string Format(ImageViewerRenderStatus status)
        {
            string sourceSize = status.SourcePixelWidth.HasValue && status.SourcePixelHeight.HasValue
                ? $"{status.SourcePixelWidth.Value} x {status.SourcePixelHeight.Value}"
                : UiText.Get("RenderStatusNotAvailable");
            string renderMode = status.IsShaderPseudoColorActive
                ? UiText.Get("RenderStatusModeShaderEffect")
                : (status.PseudoColorPalette == PseudoColorPalette.None ? UiText.Get("RenderStatusModeOriginal") : UiText.Get("RenderStatusModeCpuFallback"));

            string pyramidState = status.EnableImagePyramid
                ? UiText.FormatInvariant("RenderStatusPyramidOn", status.PyramidLevelCount, status.PyramidBuildDuration.TotalMilliseconds)
                : UiText.Get("RenderStatusPyramidOff");

            return UiText.FormatInvariant("RenderStatusLineImage", sourceSize) + "\n" +
                   UiText.FormatInvariant("RenderStatusLinePyramid", pyramidState) + "\n" +
                   UiText.FormatInvariant("RenderStatusLineAutoLevel", status.AutoSelectPyramidLevel) + "\n" +
                   UiText.FormatInvariant("RenderStatusLineTiledRendering", status.EnableTiledRendering) + "\n" +
                   UiText.FormatInvariant("RenderStatusLinePrefetchAdjacentTiles", status.PrefetchAdjacentTiles) + "\n" +
                     UiText.FormatInvariant("RenderStatusLineTileCacheLimit", status.TileCacheMaximumMegabytes) + "\n" +
                     UiText.FormatInvariant("RenderStatusLineTilePrefetchRadius", status.TilePrefetchRadius) + "\n" +
                   UiText.FormatInvariant("RenderStatusLineFrame", status.LastRenderFrame.Left, status.LastRenderFrame.Top, status.LastRenderFrame.Width, status.LastRenderFrame.Height, status.LastRenderFrame.ScaleFactor, status.LastRenderFrame.IsTiled) + "\n" +
                   UiText.FormatInvariant("RenderStatusLineGpuCache", status.EnableGpuRendering) + "\n" +
                   UiText.FormatInvariant("RenderStatusLinePseudoColor", status.PseudoColorPalette, renderMode) + "\n" +
                   UiText.FormatInvariant("RenderStatusLineAsyncAnalysis", status.EnableAsyncAnalysis) + "\n" +
                   UiText.FormatInvariant("RenderStatusLineHistogram", status.PauseRealtimeHistogram, status.LastHistogramDuration.TotalMilliseconds) + "\n" +
                   UiText.FormatInvariant("RenderStatusLineProfile", status.PauseRealtimeProfile, status.LastProfileDuration.TotalMilliseconds);
        }
    }
}