using System;

namespace ImageViewer.Controls
{
    internal sealed class ImageViewerViewCommandHostAdapter : IImageViewerViewCommandHost
    {
        private readonly ImageViewer _owner;
        private readonly ViewportController _viewportController;

        public ImageViewerViewCommandHostAdapter(ImageViewer owner, ViewportController viewportController)
        {
            _owner = owner ?? throw new ArgumentNullException(nameof(owner));
            _viewportController = viewportController ?? throw new ArgumentNullException(nameof(viewportController));
        }

        public bool ShowPixelGrid
        {
            get => _owner.ShowPixelGrid;
            set => _owner.ShowPixelGrid = value;
        }

        public bool ShowCrosshair
        {
            get => _owner.ShowCrosshair;
            set => _owner.ShowCrosshair = value;
        }

        public bool ShowCaliperScores
        {
            get => _owner.ShowCaliperScores;
            set => _owner.ShowCaliperScores = value;
        }

        public bool ShowInfoPanel
        {
            get => _owner.ShowInfoPanel;
            set => _owner.ShowInfoPanel = value;
        }

        public bool ShowHistogram
        {
            get => _owner.ShowHistogram;
            set => _owner.ShowHistogram = value;
        }

        public bool ShowProfile
        {
            get => _owner.ShowProfile;
            set => _owner.ShowProfile = value;
        }

        public bool ShowScaleBar
        {
            get => _owner.ShowScaleBar;
            set => _owner.ShowScaleBar = value;
        }

        public bool ShowRoiList
        {
            get => _owner.ShowRoiList;
            set => _owner.ShowRoiList = value;
        }

        public bool ShowSnapGrid
        {
            get => _owner.ShowSnapGrid;
            set => _owner.ShowSnapGrid = value;
        }

        public bool EnableSnapToGrid
        {
            get => _owner.EnableSnapToGrid;
            set => _owner.EnableSnapToGrid = value;
        }

        public void FitToView() => _viewportController.FitToView();

        public void ResetView() => _viewportController.ResetView();

        public void ShowFullImage() => _viewportController.ShowFullImage();

        public void SetActualSize() => _viewportController.SetActualSize();

        public void ZoomToSelection() => _viewportController.ZoomToSelection();
    }
}