using System;
using ImageViewer.Localization;
using ImageViewer.Models;
using ImageViewer.Services;

namespace ImageViewer.Controls
{
    internal sealed class ImageViewerAnalysisCommandHostAdapter : IImageViewerAnalysisCommandHost
    {
        private readonly ImageViewer _owner;
        private readonly ImageViewerAnalysisCoordinator _analysisController;
        private readonly ImageViewerAnalysisState _analysisState;
        private readonly ImageViewerDialogWorkflowService _dialogWorkflowService;
        private readonly Action _updateRenderedImage;
        private readonly Action _rebuildPyramidIfNeeded;
        private readonly Func<string> _buildRenderStatusSummary;

        public ImageViewerAnalysisCommandHostAdapter(
            ImageViewer owner,
            ImageViewerAnalysisCoordinator analysisController,
            ImageViewerAnalysisState analysisState,
            ImageViewerDialogWorkflowService dialogWorkflowService,
            Action updateRenderedImage,
            Action rebuildPyramidIfNeeded,
            Func<string> buildRenderStatusSummary)
        {
            _owner = owner ?? throw new ArgumentNullException(nameof(owner));
            _analysisController = analysisController ?? throw new ArgumentNullException(nameof(analysisController));
            _analysisState = analysisState ?? throw new ArgumentNullException(nameof(analysisState));
            _dialogWorkflowService = dialogWorkflowService ?? throw new ArgumentNullException(nameof(dialogWorkflowService));
            _updateRenderedImage = updateRenderedImage ?? throw new ArgumentNullException(nameof(updateRenderedImage));
            _rebuildPyramidIfNeeded = rebuildPyramidIfNeeded ?? throw new ArgumentNullException(nameof(rebuildPyramidIfNeeded));
            _buildRenderStatusSummary = buildRenderStatusSummary ?? throw new ArgumentNullException(nameof(buildRenderStatusSummary));
        }

        public bool EnableAsyncAnalysis
        {
            get => _owner.EnableAsyncAnalysis;
            set => _owner.EnableAsyncAnalysis = value;
        }

        public bool PauseRealtimeHistogram
        {
            get => _owner.PauseRealtimeHistogram;
            set => _owner.PauseRealtimeHistogram = value;
        }

        public bool PauseRealtimeProfile
        {
            get => _owner.PauseRealtimeProfile;
            set => _owner.PauseRealtimeProfile = value;
        }

        public bool EnableImagePyramid
        {
            get => _owner.EnableImagePyramid;
            set => _owner.EnableImagePyramid = value;
        }

        public bool AutoSelectPyramidLevel
        {
            get => _owner.AutoSelectPyramidLevel;
            set => _owner.AutoSelectPyramidLevel = value;
        }

        public bool EnableTiledRendering
        {
            get => _owner.EnableTiledRendering;
            set => _owner.EnableTiledRendering = value;
        }

        public bool PrefetchAdjacentTiles
        {
            get => _owner.PrefetchAdjacentTiles;
            set => _owner.PrefetchAdjacentTiles = value;
        }

        public int TileCacheMaximumMegabytes
        {
            get => _owner.TileCacheMaximumMegabytes;
            set => _owner.TileCacheMaximumMegabytes = value;
        }

        public int TilePrefetchRadius
        {
            get => _owner.TilePrefetchRadius;
            set => _owner.TilePrefetchRadius = value;
        }

        public bool EnableGpuRendering
        {
            get => _owner.EnableGpuRendering;
            set => _owner.EnableGpuRendering = value;
        }

        public bool PreferShaderPseudoColor
        {
            get => _owner.PreferShaderPseudoColor;
            set => _owner.PreferShaderPseudoColor = value;
        }

        public bool AllowCpuPseudoColorFallback
        {
            get => _owner.AllowCpuPseudoColorFallback;
            set => _owner.AllowCpuPseudoColorFallback = value;
        }

        public void UpdateRenderedImage() => _updateRenderedImage();

        public void RefreshAnalysis() => _analysisController.HandleRefreshAnalysisRequested();

        public void ClearAnalysisCache() => _analysisController.HandleClearAnalysisCacheRequested();

        public void ResetPyramidToBaseLevel()
        {
            _analysisController.ClearRenderCache();
            _analysisState.ResetPyramidToBaseLevel();
        }

        public void RebuildPyramidIfNeeded() => _rebuildPyramidIfNeeded();

        public void SetPseudoColorPalette(PseudoColorPalette palette) => _owner.PseudoColorPalette = palette;

        public void ShowRenderStatus() => _dialogWorkflowService.ShowReadOnlyText(UiText.Get("DialogRenderStatusTitle"), _buildRenderStatusSummary());
    }
}