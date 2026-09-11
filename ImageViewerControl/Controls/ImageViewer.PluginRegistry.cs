using System;
using System.Collections.Generic;
using System.Linq;
using ImageViewer.Models;
using ImageViewer.Plugins;

namespace ImageViewer.Controls
{
    public partial class ImageViewer
    {
        public RoiPluginRegistry PluginRegistry
        {
            get => _hostState.PluginRegistry;
            set
            {
                ArgumentNullException.ThrowIfNull(value);
                if (ReferenceEquals(_hostState.PluginRegistry, value))
                {
                    return;
                }

                _hostState.PluginRegistry = value;
                RefreshRoiDrawingMenuItems();
                DrawRois();
            }
        }

        public IReadOnlyList<RoiToolDescriptor> AvailableDrawingTools => PluginRegistry.GetDrawingTools().ToArray();
    }
}