using System.Collections.Generic;
using System.Windows;
using System.Windows.Threading;
using ImageViewer.Abstractions;
using ImageViewer.Models;
using ImageViewer.Plugins;
using ImageViewer.Services;

namespace ImageViewer.Controls
{
    internal interface IImageViewerViewCommandHost
    {
        bool ShowPixelGrid { get; set; }
        bool ShowCrosshair { get; set; }
        bool ShowCaliperScores { get; set; }
        bool ShowInfoPanel { get; set; }
        bool ShowHistogram { get; set; }
        bool ShowProfile { get; set; }
        bool ShowScaleBar { get; set; }
        bool ShowRoiList { get; set; }
        bool ShowSnapGrid { get; set; }
        bool EnableSnapToGrid { get; set; }

        void FitToView();
        void ResetView();
        void ShowFullImage();
        void SetActualSize();
        void ZoomToSelection();
    }

    internal interface IImageViewerModeCommandHost
    {
        void StartRectangleMode();
        void StartEllipseMode();
        void StartCircleMode();
        void StartPolygonMode();
        void StartPolylineMode();
        void StartFreehandMode();
        void StartPointAnnotationMode();
        void StartTextAnnotationMode();
        void StartLineMeasureMode();
        void StartAngleMeasureMode();
    }

    internal interface IImageViewerAnalysisCommandHost
    {
        bool EnableAsyncAnalysis { get; set; }
        bool PauseRealtimeHistogram { get; set; }
        bool PauseRealtimeProfile { get; set; }
        bool EnableImagePyramid { get; set; }
        bool AutoSelectPyramidLevel { get; set; }
        bool EnableTiledRendering { get; set; }
        bool PrefetchAdjacentTiles { get; set; }
        bool EnableGpuRendering { get; set; }
        bool PreferShaderPseudoColor { get; set; }
        bool AllowCpuPseudoColorFallback { get; set; }

        void UpdateRenderedImage();
        void RefreshAnalysis();
        void ClearAnalysisCache();
        void ResetPyramidToBaseLevel();
        void RebuildPyramidIfNeeded();
        void SetPseudoColorPalette(PseudoColorPalette palette);
        void ShowRenderStatus();
    }

}