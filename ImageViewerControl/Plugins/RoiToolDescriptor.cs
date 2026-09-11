using System;
using System.Windows;
using System.Windows.Controls;
using ImageViewer.Controls;

namespace ImageViewer.Plugins
{
    public sealed class RoiToolDescriptor
    {
        public RoiToolDescriptor(string header, Action<ImageViewer.Controls.ImageViewer> activate, int menuOrder = 0, Func<FrameworkElement>? createIcon = null, bool isMeasurement = false)
        {
            ArgumentException.ThrowIfNullOrWhiteSpace(header);
            ArgumentNullException.ThrowIfNull(activate);

            Header = header;
            Activate = activate;
            MenuOrder = menuOrder;
            CreateIcon = createIcon;
            IsMeasurement = isMeasurement;
        }

        public string Header { get; }

        public int MenuOrder { get; }

        public Func<FrameworkElement>? CreateIcon { get; }

        public Action<ImageViewer.Controls.ImageViewer> Activate { get; }

        public bool IsMeasurement { get; }
    }
}
