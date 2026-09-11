using System;
using ImageViewer.Abstractions;
using ImageViewer.Plugins;
using ImageViewer.Services;
using ImageViewer.ViewModels;

namespace ImageViewer.Controls
{
    internal sealed class ImageViewerPluginRuntimeState
    {
        private RoiPluginRegistry _pluginRegistry;

        public ImageViewerPluginRuntimeState(
            RoiPluginRegistry pluginRegistry,
            ISelectedRoiDetectionService selectedRoiDetectionService)
        {
            _pluginRegistry = pluginRegistry ?? throw new ArgumentNullException(nameof(pluginRegistry));
            ArgumentNullException.ThrowIfNull(selectedRoiDetectionService);

            ViewModel = new ImageViewerViewModel(_pluginRegistry, selectedRoiDetectionService);
            RoiInteraction = CreateRoiInteraction(_pluginRegistry);
            RoiRenderer = CreateRoiRenderer(_pluginRegistry);
        }

        public RoiPluginRegistry PluginRegistry
        {
            get => _pluginRegistry;
            set => ApplyPluginRegistry(value);
        }

        public ImageViewerViewModel ViewModel { get; }

        public RoiInteractionService RoiInteraction { get; private set; }

        public Rendering.RoiRenderService RoiRenderer { get; private set; }

        private void ApplyPluginRegistry(RoiPluginRegistry pluginRegistry)
        {
            ArgumentNullException.ThrowIfNull(pluginRegistry);
            if (ReferenceEquals(_pluginRegistry, pluginRegistry))
            {
                return;
            }

            _pluginRegistry = pluginRegistry;
            ViewModel.PluginRegistry = pluginRegistry;
            RoiInteraction = CreateRoiInteraction(pluginRegistry);
            RoiRenderer = CreateRoiRenderer(pluginRegistry);
        }

        private static RoiInteractionService CreateRoiInteraction(RoiPluginRegistry pluginRegistry)
        {
            return new RoiInteractionService(pluginRegistry);
        }

        private static Rendering.RoiRenderService CreateRoiRenderer(RoiPluginRegistry pluginRegistry)
        {
            return new Rendering.RoiRenderService(pluginRegistry);
        }
    }
}