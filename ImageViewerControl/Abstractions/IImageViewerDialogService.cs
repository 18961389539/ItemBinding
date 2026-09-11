using System.Windows;
using ImageViewer.Models;

namespace ImageViewer.Abstractions
{
    public interface IImageViewerDialogService
    {
        string? ShowLabelDialog(Window? owner, string message, string defaultValue);

        (double Length, string Unit)? ShowCalibrationDialog(Window? owner, string currentUnit);

        CaliperMeasureRoi? ShowLineMeasureCaliperSettingsDialog(Window? owner, CaliperMeasureRoi roi, Action<CaliperMeasureRoi>? previewAction = null);

        LineCaliperMeasureRoi? ShowLineCaliperSettingsDialog(Window? owner, LineCaliperMeasureRoi roi, Action<LineCaliperMeasureRoi>? previewAction = null);

        CircularCaliperMeasureRoi? ShowCircularCaliperSettingsDialog(Window? owner, CircularCaliperMeasureRoi roi, Action<CircularCaliperMeasureRoi>? previewAction = null);

        void ShowPropertyEditorDialog(Window? owner, string title, FrameworkElement editor);

        void ShowReadOnlyTextDialog(Window? owner, string title, string text);

        void ShowWarning(Window? owner, string title, string message);
    }
}
