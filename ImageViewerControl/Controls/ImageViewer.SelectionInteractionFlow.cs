using System;
using System.Windows;
using ImageViewer.Models;

namespace ImageViewer.Controls
{
    internal sealed class SelectionInteractionFlow : IImageViewerSelectionInteractionFlow
    {
        private readonly IImageViewerSelectionInteractionHost _host;

        public SelectionInteractionFlow(IImageViewerSelectionInteractionHost host)
        {
            _host = host ?? throw new ArgumentNullException(nameof(host));
        }

        public bool HandleRightClick(Point imagePosition)
        {
            if (_host.IsToolInteractionActive)
            {
                _host.ExitCurrentMode();
                return true;
            }

            RoiBase? hitRoi = _host.HitTest(imagePosition);
            if (hitRoi == null)
            {
                return false;
            }

            _host.SelectedRoi = hitRoi;
            _host.DrawRois();
            _host.ShowRoiProperties(hitRoi);
            return true;
        }

        public void ClearSelection()
        {
            _host.SelectedRoi = null;
        }
    }
}