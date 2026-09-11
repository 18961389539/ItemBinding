using System;
using ImageViewer.Models;

namespace ImageViewer.Controls
{
    internal sealed class ImageViewerRoiMenuCommandHostAdapter : IImageViewerRoiMenuCommandHost
    {
        private readonly Func<RoiBase?> _getSelectedRoi;
        private readonly RoiEditController _roiEditController;
        private readonly CalibrationController _calibrationController;
        private readonly Action<RoiBase> _showRoiProperties;
        private readonly Action<RoiBase> _showCaliperSettings;
        private readonly Action _updateContextMenuState;

        public ImageViewerRoiMenuCommandHostAdapter(
            Func<RoiBase?> getSelectedRoi,
            RoiEditController roiEditController,
            CalibrationController calibrationController,
            Action<RoiBase> showRoiProperties,
            Action<RoiBase> showCaliperSettings,
            Action updateContextMenuState)
        {
            _getSelectedRoi = getSelectedRoi ?? throw new ArgumentNullException(nameof(getSelectedRoi));
            _roiEditController = roiEditController ?? throw new ArgumentNullException(nameof(roiEditController));
            _calibrationController = calibrationController ?? throw new ArgumentNullException(nameof(calibrationController));
            _showRoiProperties = showRoiProperties ?? throw new ArgumentNullException(nameof(showRoiProperties));
            _showCaliperSettings = showCaliperSettings ?? throw new ArgumentNullException(nameof(showCaliperSettings));
            _updateContextMenuState = updateContextMenuState ?? throw new ArgumentNullException(nameof(updateContextMenuState));
        }

        public void Undo() => _roiEditController.Undo();

        public void Redo() => _roiEditController.Redo();

        public void DeleteSelected() => _roiEditController.DeleteSelected();

        public void ClearAll() => _roiEditController.ClearAll();

        public void EditSelectedProperties()
        {
            if (_getSelectedRoi() is RoiBase roi)
            {
                _showRoiProperties(roi);
            }
        }

        public void SetSelectedLabel() => _roiEditController.SetSelectedLabel();

        public void SetSelectedColor(string colorName) => _roiEditController.SetSelectedColor(colorName);

        public void CalibrateSelectedRoi() => _calibrationController.CalibrateSelectedRoi();

        public void EditSelectedCaliperSettings()
        {
            if (_getSelectedRoi() is RoiBase roi)
            {
                _showCaliperSettings(roi);
            }
        }

        public void UpdateContextMenuState() => _updateContextMenuState();
    }
}