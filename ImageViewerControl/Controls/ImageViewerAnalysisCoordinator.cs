using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Effects;
using System.Windows.Media.Imaging;
using ImageViewer.Abstractions;
using ImageViewer.Models;
using ImageViewer.Services;

namespace ImageViewer.Controls
{
    internal sealed record ImageViewerAnalysisMenuState(
        bool EnableImagePyramidChecked,
        bool AutoSelectPyramidLevelChecked,
        bool AutoSelectPyramidLevelEnabled,
        bool EnableTiledRenderingChecked,
        bool PrefetchAdjacentTilesChecked,
        bool PrefetchAdjacentTilesEnabled,
        bool EnableAsyncAnalysisChecked,
        bool PauseRealtimeHistogramChecked,
        bool PauseRealtimeHistogramEnabled,
        bool PauseRealtimeProfileChecked,
        bool PauseRealtimeProfileEnabled,
        bool EnableRefreshAnalysis,
        bool PreferShaderPseudoColorChecked,
        bool AllowCpuPseudoColorFallbackChecked,
        bool EnableClearPyramidCache,
        bool EnableClearAnalysisCache,
        bool EnableShowRenderStatus,
        bool GpuRenderingChecked,
        bool PseudoColorNoneChecked,
        bool PseudoColorHotChecked,
        bool PseudoColorJetChecked,
        bool PseudoColorViridisChecked)
    {
        public static ImageViewerAnalysisMenuState Empty { get; } =
            new(
                EnableImagePyramidChecked: false,
                AutoSelectPyramidLevelChecked: false,
                AutoSelectPyramidLevelEnabled: false,
                EnableTiledRenderingChecked: false,
                PrefetchAdjacentTilesChecked: false,
                PrefetchAdjacentTilesEnabled: false,
                EnableAsyncAnalysisChecked: false,
                PauseRealtimeHistogramChecked: false,
                PauseRealtimeHistogramEnabled: false,
                PauseRealtimeProfileChecked: false,
                PauseRealtimeProfileEnabled: false,
                EnableRefreshAnalysis: false,
                PreferShaderPseudoColorChecked: false,
                AllowCpuPseudoColorFallbackChecked: false,
                EnableClearPyramidCache: false,
                EnableClearAnalysisCache: false,
                EnableShowRenderStatus: false,
                GpuRenderingChecked: false,
                PseudoColorNoneChecked: true,
                PseudoColorHotChecked: false,
                PseudoColorJetChecked: false,
                PseudoColorViridisChecked: false);
    }

    internal interface IImageViewerAnalysisHost
    {
        ImageViewerAnalysisState AnalysisState { get; }
        IImageViewerRenderService RenderService { get; }
        ImageSource? ImageSource { get; }
        bool EnableGpuRendering { get; }
        bool PreferShaderPseudoColor { get; }
        bool AllowCpuPseudoColorFallback { get; }
        PseudoColorPalette PseudoColorPalette { get; }
        bool EnableImagePyramid { get; }
        bool AutoSelectPyramidLevel { get; }
        bool EnableTiledRendering { get; }
        bool PrefetchAdjacentTiles { get; }
        int TileCacheMaximumMegabytes { get; }
        int TilePrefetchRadius { get; }
        double Scale { get; }
        Point Translation { get; }
        Size ViewportSize { get; }
        int HistogramBinCount { get; }
        bool ShowHistogram { get; }
        bool ShowProfile { get; }
        bool EnableAsyncAnalysis { get; }
        bool PauseRealtimeHistogram { get; }
        bool PauseRealtimeProfile { get; }
    }

    internal sealed class ImageViewerAnalysisCoordinator
    {
        internal const int HistogramBinCount = 256;

        private readonly IImageViewerAnalysisHost _host;
        private readonly IImageViewerAnalysisUiFacade _uiFacade;
        private readonly IImageViewerProfileTargetResolver _profileTargetResolver;
        private readonly IImageViewerAnalysisErrorSink _errorSink;

        public ImageViewerAnalysisCoordinator(
            IImageViewerAnalysisHost host,
            IImageViewerAnalysisUiFacade uiFacade,
            IImageViewerProfileTargetResolver profileTargetResolver,
            IImageViewerAnalysisErrorSink errorSink)
        {
            _host = host;
            _uiFacade = uiFacade;
            _profileTargetResolver = profileTargetResolver;
            _errorSink = errorSink;
        }

        public void UpdateRenderedImage()
        {
            ImageViewerRenderedImagePlan renderPlan = BuildRenderedImagePlan();
            _uiFacade.ApplyRenderedImagePlan(renderPlan);
            _host.AnalysisState.IsShaderPseudoColorActive = renderPlan.IsShaderPseudoColorActive;
            _host.AnalysisState.LastRenderFrame = renderPlan.RenderFrame;
        }

        public void ClearRenderCache()
        {
            _host.RenderService.ClearTileCache();
        }

        private ImageViewerRenderedImagePlan BuildRenderedImagePlan()
        {
            Effect? pseudoColorEffect = _host.PreferShaderPseudoColor ? _host.RenderService.CreatePseudoColorEffect(_host.PseudoColorPalette) : null;
            PseudoColorPalette cpuPseudoColorPalette = pseudoColorEffect == null && _host.AllowCpuPseudoColorFallback ? _host.PseudoColorPalette : PseudoColorPalette.None;

            if (_host.AnalysisState.AnalysisBitmapSource == null)
            {
                double imageWidth = _host.ImageSource is BitmapSource bitmap ? bitmap.PixelWidth : _host.ImageSource?.Width ?? 0;
                double imageHeight = _host.ImageSource is BitmapSource bitmapSource ? bitmapSource.PixelHeight : _host.ImageSource?.Height ?? 0;
                ImageSource? source = pseudoColorEffect == null ? _host.RenderService.BuildDisplaySource(_host.ImageSource, cpuPseudoColorPalette) : _host.ImageSource;
                return new ImageViewerRenderedImagePlan(
                    new ImageViewerRenderFrame(source, 0, 0, imageWidth, imageHeight, 1.0, false),
                    pseudoColorEffect,
                    _host.EnableGpuRendering);
            }

            IReadOnlyList<ImagePyramidLevel> activePyramidLevels = _host.EnableImagePyramid
                ? _host.AnalysisState.PyramidLevels
                : [new ImagePyramidLevel(_host.AnalysisState.AnalysisBitmapSource, 1.0)];

            ImageViewerRenderFrame frame = _host.RenderService.BuildRenderFrame(
                _host.AnalysisState.AnalysisBitmapSource,
                activePyramidLevels,
                _host.ViewportSize,
                _host.Scale,
                _host.Translation,
                cpuPseudoColorPalette,
                _host.EnableTiledRendering,
                _host.AutoSelectPyramidLevel,
                _host.PrefetchAdjacentTiles,
                _host.TileCacheMaximumMegabytes,
                _host.TilePrefetchRadius);

            return new ImageViewerRenderedImagePlan(frame, pseudoColorEffect, _host.EnableGpuRendering);
        }

        public BitmapSource? GetAnalysisBitmapSource()
        {
            return _host.AnalysisState.AnalysisBitmapSource;
        }

        public async Task PrepareAnalysisResourcesAsync(ImageSource? source)
        {
            _host.RenderService.ClearTileCache();
            _host.AnalysisState.ResetForSource(_host.RenderService.GetAnalysisBitmap(source));
            UpdateRenderedImage();

            if (_host.AnalysisState.AnalysisBitmapSource == null || !_host.EnableImagePyramid)
            {
                await RefreshAnalysisDisplaysAsync();
                return;
            }

            var cancellationTokenSource = new CancellationTokenSource();
            _host.AnalysisState.PyramidBuildCancellationTokenSource = cancellationTokenSource;
            var stopwatch = Stopwatch.StartNew();

            try
            {
                IReadOnlyList<ImagePyramidLevel> pyramidLevels = await _host.RenderService.BuildPyramidAsync(_host.AnalysisState.AnalysisBitmapSource, cancellationTokenSource.Token);
                if (!cancellationTokenSource.IsCancellationRequested)
                {
                    _host.AnalysisState.SetPyramidLevels(pyramidLevels, stopwatch.Elapsed);
                    UpdateRenderedImage();
                    await RefreshAnalysisDisplaysAsync();
                }
            }
            catch (OperationCanceledException)
            {
            }
        }

        public void HandleAsyncAnalysisChanged()
        {
            _ = RefreshAnalysisDisplaysAsync(force: true);
        }

        public void HandleRealtimeHistogramPauseChanged()
        {
            if (!_host.PauseRealtimeHistogram)
            {
                _ = UpdateHistogram(force: true);
            }
        }

        public void HandleRealtimeProfilePauseChanged()
        {
            if (!_host.PauseRealtimeProfile)
            {
                _ = UpdateProfile(force: true);
            }
        }

        public void HandleRefreshAnalysisRequested()
        {
            _ = RefreshAnalysisDisplaysAsync(force: true);
        }

        public void HandleClearAnalysisCacheRequested()
        {
            ClearAnalysisCaches();
            _ = RefreshAnalysisDisplaysAsync(force: true);
        }

        public void HandlePseudoColorPaletteChanged()
        {
            UpdateRenderedImage();
            _ = UpdateHistogram();
            _ = UpdateProfile();
        }

        public void HandleRenderingOptionChanged()
        {
            UpdateRenderedImage();
        }

        public void HandleHistogramVisibilityChanged(bool isVisible)
        {
            if (isVisible)
            {
                _uiFacade.SetHistogramPanelVisibility(true);
                _ = UpdateHistogram();
                return;
            }

            _host.AnalysisState.ClearHistogramWork();
            _uiFacade.PresentHistogram(null);
            _uiFacade.SetHistogramPanelVisibility(false);
        }

        public void HandleProfileVisibilityChanged(bool isVisible)
        {
            if (isVisible)
            {
                _uiFacade.SetProfilePanelVisibility(true);
                _ = UpdateProfile();
                return;
            }

            _host.AnalysisState.ClearProfileWork();
            _uiFacade.PresentProfile(null);
            _uiFacade.SetProfilePanelVisibility(false);
        }

        public Task RefreshAnalysisDisplays(bool force = false)
        {
            return RefreshAnalysisDisplaysAsync(force);
        }

        private async Task RefreshAnalysisDisplaysAsync(bool force = false)
        {
            if (_host.ShowHistogram)
            {
                await UpdateHistogram(force);
            }

            if (_host.ShowProfile)
            {
                await UpdateProfile(force);
            }
        }

        public async Task UpdateHistogram(bool force = false)
        {
            _host.AnalysisState.HistogramUpdateCancellationTokenSource?.Cancel();
            _host.AnalysisState.HistogramUpdateCancellationTokenSource?.Dispose();

            if (!_host.ShowHistogram || _host.AnalysisState.AnalysisBitmapSource is not BitmapSource bitmap)
            {
                _uiFacade.PresentHistogram(null);
                return;
            }

            if (_host.PauseRealtimeHistogram && !force)
            {
                return;
            }

            if (!_host.EnableAsyncAnalysis)
            {
                try
                {
                    var stopwatch = Stopwatch.StartNew();
                    int[] histogram = ImageAnalysisService.CreateHistogram(bitmap, _host.HistogramBinCount);
                    _host.AnalysisState.LastHistogramDuration = stopwatch.Elapsed;
                    _uiFacade.PresentHistogram(new ImageViewerHistogramOutput(histogram, _host.HistogramBinCount));
                }
                catch (Exception ex)
                {
                    _errorSink.LogNonCriticalError("Failed to update histogram", ex);
                }

                return;
            }

            var cancellationTokenSource = new CancellationTokenSource();
            _host.AnalysisState.HistogramUpdateCancellationTokenSource = cancellationTokenSource;

            try
            {
                await Task.Delay(120, cancellationTokenSource.Token);
                var stopwatch = Stopwatch.StartNew();
                int[]? histogram = await _host.RenderService.CreateHistogramAsync(bitmap, _host.HistogramBinCount, cancellationTokenSource.Token);
                if (cancellationTokenSource.IsCancellationRequested || histogram == null)
                {
                    return;
                }

                _host.AnalysisState.LastHistogramDuration = stopwatch.Elapsed;
                _uiFacade.PresentHistogram(new ImageViewerHistogramOutput(histogram, _host.HistogramBinCount));
            }
            catch (OperationCanceledException)
            {
            }
            catch (Exception ex)
            {
                _errorSink.LogNonCriticalError("Failed to update histogram", ex);
            }
        }

        public async Task UpdateProfile(bool force = false)
        {
            if (!_host.ShowProfile)
            {
                return;
            }

            LineMeasureRoi? targetLine = _profileTargetResolver.GetProfileTargetLine();
            if (targetLine == null)
            {
                _uiFacade.PresentProfile(null);
                return;
            }

            if (_host.AnalysisState.AnalysisBitmapSource is not BitmapSource bitmap)
            {
                _uiFacade.PresentProfile(null);
                return;
            }

            if (_host.PauseRealtimeProfile && !force)
            {
                return;
            }

            if (!_host.EnableAsyncAnalysis)
            {
                try
                {
                    var stopwatch = Stopwatch.StartNew();
                    byte[] profileData = ImageAnalysisService.CreateProfile(bitmap, targetLine.P1, targetLine.P2);
                    _host.AnalysisState.LastProfileDuration = stopwatch.Elapsed;
                    _uiFacade.PresentProfile(new ImageViewerProfileOutput(profileData));
                }
                catch (Exception ex)
                {
                    _errorSink.LogNonCriticalError("Failed to update profile", ex);
                }

                return;
            }

            _host.AnalysisState.ProfileUpdateCancellationTokenSource?.Cancel();
            _host.AnalysisState.ProfileUpdateCancellationTokenSource?.Dispose();
            var cancellationTokenSource = new CancellationTokenSource();
            _host.AnalysisState.ProfileUpdateCancellationTokenSource = cancellationTokenSource;

            try
            {
                await Task.Delay(120, cancellationTokenSource.Token);
                var stopwatch = Stopwatch.StartNew();
                byte[]? profileData = await _host.RenderService.CreateProfileAsync(new ImageViewerAnalysisRequest(bitmap, targetLine.P1, targetLine.P2), cancellationTokenSource.Token);
                if (cancellationTokenSource.IsCancellationRequested || profileData == null)
                {
                    return;
                }

                _host.AnalysisState.LastProfileDuration = stopwatch.Elapsed;
                _uiFacade.PresentProfile(new ImageViewerProfileOutput(profileData));
            }
            catch (OperationCanceledException)
            {
            }
            catch (Exception ex)
            {
                _errorSink.LogNonCriticalError("Failed to update profile", ex);
            }
        }

        public void RebuildPyramidIfNeeded()
        {
            if (_host.ImageSource == null)
            {
                UpdateRenderedImage();
                return;
            }

            _ = PrepareAnalysisResourcesAsync(_host.ImageSource);
        }

        public void ClearAnalysisCaches()
        {
            _host.AnalysisState.ClearAnalysisCaches();
            _uiFacade.PresentHistogram(null);
            _uiFacade.PresentProfile(null);
        }

        public ImageViewerRenderStatus BuildRenderStatus()
        {
            BitmapSource? analysisBitmap = _host.AnalysisState.AnalysisBitmapSource;

            return new ImageViewerRenderStatus(
                analysisBitmap?.PixelWidth,
                analysisBitmap?.PixelHeight,
                _host.EnableImagePyramid,
                _host.AnalysisState.PyramidLevels.Count,
                _host.AnalysisState.LastPyramidBuildDuration,
                _host.AutoSelectPyramidLevel,
                _host.EnableTiledRendering,
                _host.PrefetchAdjacentTiles,
                _host.TileCacheMaximumMegabytes,
                _host.TilePrefetchRadius,
                _host.AnalysisState.LastRenderFrame,
                _host.EnableGpuRendering,
                _host.PseudoColorPalette,
                _host.AnalysisState.IsShaderPseudoColorActive,
                _host.EnableAsyncAnalysis,
                _host.PauseRealtimeHistogram,
                _host.AnalysisState.LastHistogramDuration,
                _host.PauseRealtimeProfile,
                _host.AnalysisState.LastProfileDuration);
        }

        public string BuildRenderStatusSummary()
        {
            return ImageViewerRenderStatusFormatter.Format(BuildRenderStatus());
        }

        public void UpdatePseudoColorMenuState()
        {
            _uiFacade.ApplyPseudoColorMenuState(BuildPseudoColorMenuState());
        }

        public ImageViewerPseudoColorMenuState BuildPseudoColorMenuState()
        {
            return new ImageViewerPseudoColorMenuState(_host.PseudoColorPalette);
        }

        public ImageViewerAnalysisMenuState BuildMenuState()
        {
            bool hasAnalysisBitmap = _host.AnalysisState.AnalysisBitmapSource != null;
            return new ImageViewerAnalysisMenuState(
                _host.EnableImagePyramid,
                _host.AutoSelectPyramidLevel,
                _host.EnableImagePyramid,
                _host.EnableTiledRendering,
                _host.PrefetchAdjacentTiles,
                _host.EnableTiledRendering,
                _host.EnableAsyncAnalysis,
                _host.PauseRealtimeHistogram,
                _host.ShowHistogram,
                _host.PauseRealtimeProfile,
                _host.ShowProfile,
                hasAnalysisBitmap,
                _host.PreferShaderPseudoColor,
                _host.AllowCpuPseudoColorFallback,
                hasAnalysisBitmap,
                _host.ShowHistogram || _host.ShowProfile,
                hasAnalysisBitmap || _host.ImageSource != null,
                _host.EnableGpuRendering,
                _host.PseudoColorPalette == PseudoColorPalette.None,
                _host.PseudoColorPalette == PseudoColorPalette.Hot,
                _host.PseudoColorPalette == PseudoColorPalette.Jet,
                _host.PseudoColorPalette == PseudoColorPalette.Viridis);
        }

    }
}
