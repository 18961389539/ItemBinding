namespace ImageViewer.Controls
{
    internal enum ImageViewerViewCommand
    {
        TogglePixelGrid,
        ToggleCrosshair,
        ToggleCaliperScores,
        ToggleInfoPanel,
        ToggleHistogram,
        ToggleProfile,
        ToggleScaleBar,
        ToggleRoiList,
        ToggleSnapGrid,
        ToggleSnapToGrid,
        FitToView,
        ActualSize,
        ZoomToSelection,
        ResetView,
        ShowFullImage
    }

    internal enum ImageViewerModeCommand
    {
        Rectangle,
        Ellipse,
        Circle,
        Polygon,
        Polyline,
        Freehand,
        PointAnnotation,
        TextAnnotation,
        LineMeasure,
        AngleMeasure
    }

    internal enum ImageViewerAnalysisCommand
    {
        ToggleAsyncAnalysis,
        TogglePauseRealtimeHistogram,
        TogglePauseRealtimeProfile,
        RefreshAnalysis,
        ToggleImagePyramid,
        ToggleAutoSelectPyramidLevel,
        ToggleTiledRendering,
        TogglePrefetchAdjacentTiles,
        ToggleGpuRendering,
        ClearPyramidCache,
        ClearAnalysisCache,
        TogglePreferShaderPseudoColor,
        ToggleAllowCpuPseudoColorFallback,
        SetPseudoColorPaletteNone,
        SetPseudoColorPaletteHot,
        SetPseudoColorPaletteJet,
        SetPseudoColorPaletteViridis,
        ShowRenderStatus
    }

    internal enum ImageViewerRoiMenuCommand
    {
        Undo,
        Redo,
        DeleteSelected,
        ClearAll,
        EditProperties,
        SetLabel,
        SetColorCyan,
        SetColorRed,
        SetColorGreen,
        SetColorYellow,
        SetColorMagenta,
        CalibratePixels,
        EditCaliperSettings
    }

    internal enum ImageViewerFileMenuCommand
    {
        OpenImage,
        SaveRois,
        LoadRois,
        SaveSession,
        LoadSession,
        ExportProjectPackage,
        ToggleAutoSave
    }

    internal enum ImageViewerFeatureMenuCommand
    {
        GradientDetect,
        ExportSnapshot,
        ExportAnalysisCsv,
        ShowAnalysisSummary
    }
}