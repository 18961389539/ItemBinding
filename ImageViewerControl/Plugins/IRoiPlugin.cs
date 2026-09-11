using System;
using System.Collections.Generic;
using System.Windows;
using System.Windows.Media.Imaging;
using ImageViewer.Abstractions;
using ImageViewer.Models;
using ImageViewer.Rendering;
using ImageViewer.ViewModels;

namespace ImageViewer.Plugins
{
    public interface IRoiPlugin
    {
        string TypeKey { get; }

        Type RoiType { get; }

        int HitTestOrder { get; }

        IReadOnlyList<RoiToolDescriptor> DrawingTools { get; }

        IRoiBehavior Behavior { get; }

        IRoiRenderer Renderer { get; }

        IEnumerable<RoiBase> GetRois(ImageViewerViewModel viewModel);

        void ClearCollection(ImageViewerViewModel viewModel);

        bool AddToCollection(ImageViewerViewModel viewModel, RoiBase roi);

        bool RemoveFromCollection(ImageViewerViewModel viewModel, RoiBase roi);

        RoiBase CreateRoi(RoiPersistenceData data);

        void PopulatePersistenceData(RoiBase roi, RoiPersistenceData data);

        IReadOnlyList<string> BuildInfoLines(RoiBase roi, BitmapSource? bitmap, double pixelSize, string? physicalUnit);

        FrameworkElement? CreatePropertyEditor(RoiBase roi);
    }
}
