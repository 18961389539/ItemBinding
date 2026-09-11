using System;
using System.Collections.Generic;
using System.Threading;
using System.Windows.Media.Imaging;
using ImageViewer.Services;

namespace ImageViewer.Controls
{
    internal sealed class ImageViewerAnalysisState
    {
        private static readonly ImageViewerRenderFrame EmptyRenderFrame = new(null, 0, 0, 0, 0, 1.0, false);

        public BitmapSource? AnalysisBitmapSource { get; set; }

        public IReadOnlyList<ImagePyramidLevel> PyramidLevels { get; set; } = [];

        public CancellationTokenSource? PyramidBuildCancellationTokenSource { get; set; }

        public CancellationTokenSource? HistogramUpdateCancellationTokenSource { get; set; }

        public CancellationTokenSource? ProfileUpdateCancellationTokenSource { get; set; }

        public ImageViewerRenderFrame LastRenderFrame { get; set; } = new(null, 0, 0, 0, 0, 1.0, false);

        public TimeSpan LastPyramidBuildDuration { get; set; } = TimeSpan.Zero;

        public TimeSpan LastHistogramDuration { get; set; } = TimeSpan.Zero;

        public TimeSpan LastProfileDuration { get; set; } = TimeSpan.Zero;

        public bool IsShaderPseudoColorActive { get; set; }

        public void ResetForSource(BitmapSource? analysisBitmapSource)
        {
            ClearPyramidBuildWork();
            ClearAnalysisCaches();
            AnalysisBitmapSource = analysisBitmapSource;
            PyramidLevels = analysisBitmapSource != null ? [new ImagePyramidLevel(analysisBitmapSource, 1.0)] : [];
            LastPyramidBuildDuration = TimeSpan.Zero;
            LastRenderFrame = EmptyRenderFrame;
            IsShaderPseudoColorActive = false;
        }

        public void SetPyramidLevels(IReadOnlyList<ImagePyramidLevel> pyramidLevels, TimeSpan buildDuration)
        {
            PyramidLevels = pyramidLevels;
            LastPyramidBuildDuration = buildDuration;
        }

        public void ResetPyramidToBaseLevel()
        {
            ClearPyramidBuildWork();
            PyramidLevels = AnalysisBitmapSource != null ? [new ImagePyramidLevel(AnalysisBitmapSource, 1.0)] : [];
            LastPyramidBuildDuration = TimeSpan.Zero;
        }

        public void ClearAnalysisCaches()
        {
            ClearHistogramWork();
            ClearProfileWork();
        }

        public void ClearPyramidBuildWork()
        {
            PyramidBuildCancellationTokenSource?.Cancel();
            PyramidBuildCancellationTokenSource?.Dispose();
            PyramidBuildCancellationTokenSource = null;
        }

        public void ClearHistogramWork()
        {
            HistogramUpdateCancellationTokenSource?.Cancel();
            HistogramUpdateCancellationTokenSource?.Dispose();
            HistogramUpdateCancellationTokenSource = null;
            LastHistogramDuration = TimeSpan.Zero;
        }

        public void ClearProfileWork()
        {
            ProfileUpdateCancellationTokenSource?.Cancel();
            ProfileUpdateCancellationTokenSource?.Dispose();
            ProfileUpdateCancellationTokenSource = null;
            LastProfileDuration = TimeSpan.Zero;
        }

        public void DisposeAnalysisWork()
        {
            ClearPyramidBuildWork();
            ClearAnalysisCaches();
        }
    }
}
