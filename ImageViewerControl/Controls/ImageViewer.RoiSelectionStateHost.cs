using System.Windows;
using System.Windows.Controls;
using System;
using ImageViewer.Models;

namespace ImageViewer.Controls
{
    internal sealed class ImageViewerRoiSelectionStateHost : IRoiSelectionStateHost
    {
        private readonly ImageViewer _owner;
        private readonly ContentControl _propertyEditorHost;
        private readonly FrameworkElement _propertyPanel;
        private readonly Action<CaliperMeasureRoi> _applyCaliperDetection;
        private readonly Action<LineCaliperMeasureRoi> _applyLineCaliperDetection;
        private readonly Action<CircularCaliperMeasureRoi> _applyCircularCaliperDetection;
        private readonly Action _drawRois;

        public ImageViewerRoiSelectionStateHost(
            ImageViewer owner,
            ContentControl propertyEditorHost,
            FrameworkElement propertyPanel,
            Action<CaliperMeasureRoi> applyCaliperDetection,
            Action<LineCaliperMeasureRoi> applyLineCaliperDetection,
            Action<CircularCaliperMeasureRoi> applyCircularCaliperDetection,
            Action drawRois)
        {
            _owner = owner ?? throw new ArgumentNullException(nameof(owner));
            _propertyEditorHost = propertyEditorHost ?? throw new ArgumentNullException(nameof(propertyEditorHost));
            _propertyPanel = propertyPanel ?? throw new ArgumentNullException(nameof(propertyPanel));
            _applyCaliperDetection = applyCaliperDetection ?? throw new ArgumentNullException(nameof(applyCaliperDetection));
            _applyLineCaliperDetection = applyLineCaliperDetection ?? throw new ArgumentNullException(nameof(applyLineCaliperDetection));
            _applyCircularCaliperDetection = applyCircularCaliperDetection ?? throw new ArgumentNullException(nameof(applyCircularCaliperDetection));
            _drawRois = drawRois ?? throw new ArgumentNullException(nameof(drawRois));
        }

        public RoiBase? SelectedRoi => _owner.ViewerState.SelectedRoi;

        public FrameworkElement? CreatePropertyEditor(RoiBase roi)
        {
            return _owner.PluginRegistry.FindByRoi(roi)?.CreatePropertyEditor(roi);
        }

        public void ApplyPropertyPanel(FrameworkElement? editor, bool isVisible)
        {
            _propertyEditorHost.Content = editor;
            _propertyPanel.Visibility = isVisible ? Visibility.Visible : Visibility.Collapsed;
        }

        public void ApplyCaliperDetection(CaliperMeasureRoi roi)
        {
            _applyCaliperDetection(roi);
        }

        public void ApplyLineCaliperDetection(LineCaliperMeasureRoi roi)
        {
            _applyLineCaliperDetection(roi);
        }

        public void ApplyCircularCaliperDetection(CircularCaliperMeasureRoi roi)
        {
            _applyCircularCaliperDetection(roi);
        }

        public void DrawRois()
        {
            _drawRois();
        }
    }
}