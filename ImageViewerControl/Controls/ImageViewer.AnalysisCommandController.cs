using System;
using ImageViewer.Models;
using ImageViewer.Services;

namespace ImageViewer.Controls
{
    internal sealed class ImageViewerAnalysisCommandController
    {
        private readonly IImageViewerAnalysisCommandHost _host;

        public ImageViewerAnalysisCommandController(IImageViewerAnalysisCommandHost host)
        {
            _host = host;
        }

        public void Execute(ImageViewerAnalysisCommand command)
        {
            switch (command)
            {
                case ImageViewerAnalysisCommand.ToggleAsyncAnalysis:
                    _host.EnableAsyncAnalysis = !_host.EnableAsyncAnalysis;
                    break;
                case ImageViewerAnalysisCommand.TogglePauseRealtimeHistogram:
                    _host.PauseRealtimeHistogram = !_host.PauseRealtimeHistogram;
                    break;
                case ImageViewerAnalysisCommand.TogglePauseRealtimeProfile:
                    _host.PauseRealtimeProfile = !_host.PauseRealtimeProfile;
                    break;
                case ImageViewerAnalysisCommand.RefreshAnalysis:
                    _host.RefreshAnalysis();
                    break;
                case ImageViewerAnalysisCommand.ToggleImagePyramid:
                    _host.EnableImagePyramid = !_host.EnableImagePyramid;
                    break;
                case ImageViewerAnalysisCommand.ToggleAutoSelectPyramidLevel:
                    _host.AutoSelectPyramidLevel = !_host.AutoSelectPyramidLevel;
                    break;
                case ImageViewerAnalysisCommand.ToggleTiledRendering:
                    _host.EnableTiledRendering = !_host.EnableTiledRendering;
                    break;
                case ImageViewerAnalysisCommand.TogglePrefetchAdjacentTiles:
                    _host.PrefetchAdjacentTiles = !_host.PrefetchAdjacentTiles;
                    break;
                case ImageViewerAnalysisCommand.ToggleGpuRendering:
                    _host.EnableGpuRendering = !_host.EnableGpuRendering;
                    _host.UpdateRenderedImage();
                    break;
                case ImageViewerAnalysisCommand.ClearPyramidCache:
                    _host.ResetPyramidToBaseLevel();
                    _host.RebuildPyramidIfNeeded();
                    break;
                case ImageViewerAnalysisCommand.ClearAnalysisCache:
                    _host.ClearAnalysisCache();
                    break;
                case ImageViewerAnalysisCommand.TogglePreferShaderPseudoColor:
                    _host.PreferShaderPseudoColor = !_host.PreferShaderPseudoColor;
                    break;
                case ImageViewerAnalysisCommand.ToggleAllowCpuPseudoColorFallback:
                    _host.AllowCpuPseudoColorFallback = !_host.AllowCpuPseudoColorFallback;
                    break;
                case ImageViewerAnalysisCommand.SetPseudoColorPaletteNone:
                    _host.SetPseudoColorPalette(PseudoColorPalette.None);
                    break;
                case ImageViewerAnalysisCommand.SetPseudoColorPaletteHot:
                    _host.SetPseudoColorPalette(PseudoColorPalette.Hot);
                    break;
                case ImageViewerAnalysisCommand.SetPseudoColorPaletteJet:
                    _host.SetPseudoColorPalette(PseudoColorPalette.Jet);
                    break;
                case ImageViewerAnalysisCommand.SetPseudoColorPaletteViridis:
                    _host.SetPseudoColorPalette(PseudoColorPalette.Viridis);
                    break;
                case ImageViewerAnalysisCommand.ShowRenderStatus:
                    _host.ShowRenderStatus();
                    break;
                default:
                    throw new ArgumentOutOfRangeException(nameof(command), command, null);
            }
        }
    }
}