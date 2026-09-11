using System;

namespace ImageViewer.Controls
{
    internal sealed class ImageViewerViewCommandController
    {
        private readonly IImageViewerViewCommandHost _host;

        public ImageViewerViewCommandController(IImageViewerViewCommandHost host)
        {
            _host = host;
        }

        public void Execute(ImageViewerViewCommand command)
        {
            switch (command)
            {
                case ImageViewerViewCommand.TogglePixelGrid:
                    _host.ShowPixelGrid = !_host.ShowPixelGrid;
                    break;
                case ImageViewerViewCommand.ToggleCrosshair:
                    _host.ShowCrosshair = !_host.ShowCrosshair;
                    break;
                case ImageViewerViewCommand.ToggleCaliperScores:
                    _host.ShowCaliperScores = !_host.ShowCaliperScores;
                    break;
                case ImageViewerViewCommand.ToggleInfoPanel:
                    _host.ShowInfoPanel = !_host.ShowInfoPanel;
                    break;
                case ImageViewerViewCommand.ToggleHistogram:
                    _host.ShowHistogram = !_host.ShowHistogram;
                    break;
                case ImageViewerViewCommand.ToggleProfile:
                    _host.ShowProfile = !_host.ShowProfile;
                    break;
                case ImageViewerViewCommand.ToggleScaleBar:
                    _host.ShowScaleBar = !_host.ShowScaleBar;
                    break;
                case ImageViewerViewCommand.ToggleRoiList:
                    _host.ShowRoiList = !_host.ShowRoiList;
                    break;
                case ImageViewerViewCommand.ToggleSnapGrid:
                    _host.ShowSnapGrid = !_host.ShowSnapGrid;
                    break;
                case ImageViewerViewCommand.ToggleSnapToGrid:
                    _host.EnableSnapToGrid = !_host.EnableSnapToGrid;
                    break;
                case ImageViewerViewCommand.FitToView:
                    _host.FitToView();
                    break;
                case ImageViewerViewCommand.ActualSize:
                    _host.SetActualSize();
                    break;
                case ImageViewerViewCommand.ZoomToSelection:
                    _host.ZoomToSelection();
                    break;
                case ImageViewerViewCommand.ResetView:
                    _host.ResetView();
                    break;
                case ImageViewerViewCommand.ShowFullImage:
                    _host.ShowFullImage();
                    break;
                default:
                    throw new ArgumentOutOfRangeException(nameof(command), command, null);
            }
        }
    }
}