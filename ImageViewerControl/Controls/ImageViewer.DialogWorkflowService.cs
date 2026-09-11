using System;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using ImageViewer.Abstractions;
using ImageViewer.Localization;
using ImageViewer.Models;
using ImageViewer.Plugins;
using ImageViewer.Services;
using ImageViewer.Utils;
using ImageViewer.ViewModels;

namespace ImageViewer.Controls
{
    internal interface IImageViewerDialogWorkflowHost
    {
        RoiPluginRegistry PluginRegistry { get; }

        double PixelSize { get; set; }

        string PhysicalUnit { get; set; }

        int ImageLoadRetryCount { get; }

        int ImageLoadRetryDelayMilliseconds { get; }

        void DrawRois();

        void DrawSelectedRoiLayer();

        void HandleRoiEdited(RoiBase roi);

        IUndoRedoCommand? CreateStateCommand(RoiBase roi, RoiBase oldState, RoiBase newState);

        void ExecuteUndoRedoCommand(IUndoRedoCommand command);

        bool TryApplyCaliperDetection(CaliperMeasureRoi roi);

        bool TryApplyLineCaliperDetection(LineCaliperMeasureRoi roi);

        bool TryApplyCircularCaliperDetection(CircularCaliperMeasureRoi roi);

        void SetImage(ImageSource source);

        void SetImageLoadState(bool isLoading, string statusText, double progress, bool canRetry);

        void FitToView();

        void ShowNonCriticalError(string title, string message, Exception ex);
    }

    internal sealed class ImageViewerDialogWorkflowService
    {
        private readonly IImageViewerDialogWorkflowHost _host;
        private readonly IImageViewerDialogWorkflowAdapter _adapter;
        private string? _lastFailedImagePath;

        public ImageViewerDialogWorkflowService(IImageViewerDialogWorkflowHost host, IImageViewerDialogWorkflowAdapter adapter)
        {
            _host = host;
            _adapter = adapter;
        }

        public async Task OpenImageAsync()
        {
            string? filePath = _adapter.ShowOpenImageDialog();
            if (string.IsNullOrWhiteSpace(filePath))
            {
                return;
            }

            await OpenImageFromPathAsync(filePath);
        }

        public async Task RetryLastImageLoadAsync()
        {
            if (string.IsNullOrWhiteSpace(_lastFailedImagePath))
            {
                return;
            }

            await OpenImageFromPathAsync(_lastFailedImagePath);
        }

        private async Task OpenImageFromPathAsync(string filePath)
        {
            int retryCount = Math.Max(0, _host.ImageLoadRetryCount);
            int retryDelayMilliseconds = Math.Max(0, _host.ImageLoadRetryDelayMilliseconds);
            Exception? lastException = null;

            _host.SetImageLoadState(true, UiText.Get("ImageLoadStatusStarting"), 5, false);

            for (int attempt = 0; attempt <= retryCount; attempt++)
            {
                try
                {
                    if (attempt > 0)
                    {
                        _host.SetImageLoadState(true, UiText.Get("ImageLoadStatusRetrying"), 15 + attempt * 10, false);
                        if (retryDelayMilliseconds > 0)
                        {
                            await Task.Delay(retryDelayMilliseconds);
                        }
                    }

                    _host.SetImageLoadState(true, UiText.Get("ImageLoadStatusDecoding"), 45, false);
                    BitmapImage bitmap = await Task.Run(() => CreateBitmapFromFile(filePath));
                    _host.SetImageLoadState(true, UiText.Get("ImageLoadStatusApplying"), 85, false);
                    _host.SetImage(bitmap);
                    _host.FitToView();
                    _host.SetImageLoadState(false, UiText.Get("ImageLoadStatusReady"), 100, false);
                    _lastFailedImagePath = null;
                    return;
                }
                catch (Exception ex)
                {
                    lastException = ex;
                    if (attempt < retryCount)
                    {
                        continue;
                    }
                }
            }

            _lastFailedImagePath = filePath;
            _host.SetImageLoadState(true, UiText.Get("ImageLoadStatusFailed"), 0, true);
            _host.ShowNonCriticalError(UiText.Get("ErrorOpenImageTitle"), UiText.Get("ErrorOpenImageMessage"), lastException ?? new InvalidOperationException("Image load failed."));
        }

        public string? ShowTextInput(string message, string defaultValue)
        {
            return _adapter.ShowTextInput(message, defaultValue);
        }

        public string? ShowSaveRoiDialog()
        {
            return _adapter.ShowSaveRoiDialog();
        }

        public string? ShowOpenRoiDialog()
        {
            return _adapter.ShowOpenRoiDialog();
        }

        public string? ShowSaveSessionDialog()
        {
            return _adapter.ShowSaveSessionDialog();
        }

        public string? ShowOpenSessionDialog()
        {
            return _adapter.ShowOpenSessionDialog();
        }

        public string? ShowSaveProjectPackageDialog()
        {
            return _adapter.ShowSaveProjectPackageDialog();
        }

        public string? ShowSaveSnapshotDialog()
        {
            return _adapter.ShowSaveSnapshotDialog();
        }

        public string? ShowSaveAnalysisCsvDialog()
        {
            return _adapter.ShowSaveAnalysisCsvDialog();
        }

        public void ShowReadOnlyText(string title, string text)
        {
            _adapter.ShowReadOnlyText(title, text);
        }

        public void ShowWarning(string title, string message)
        {
            _adapter.ShowWarning(title, message);
        }

        public void ShowRoiProperties(RoiBase roi)
        {
            ArgumentNullException.ThrowIfNull(roi);

            if (roi is CaliperMeasureRoi or LineCaliperMeasureRoi or CircularCaliperMeasureRoi)
            {
                ShowCaliperSettings(roi);
                return;
            }

            FrameworkElement? editor = _host.PluginRegistry.FindByRoi(roi)?.CreatePropertyEditor(roi);
            if (editor == null)
            {
                return;
            }

            _adapter.ShowPropertyEditor(UiText.Format("DialogEditRoiTitle", roi.DisplayTypeName), editor);
            _host.HandleRoiEdited(roi);
        }

        public void ShowCaliperSettings(RoiBase roi)
        {
            switch (roi)
            {
                case LineCaliperMeasureRoi lineCaliper:
                    ShowCaliperSettingsDialogCore(lineCaliper, _adapter.ShowLineCaliperSettingsDialog, CreateConfiguredLineCaliperState);
                    return;
                case CircularCaliperMeasureRoi circularCaliper:
                    ShowCaliperSettingsDialogCore(circularCaliper, _adapter.ShowCircularCaliperSettingsDialog, CreateConfiguredCircularCaliperState);
                    return;
                case CaliperMeasureRoi line:
                    ShowCaliperSettingsDialogCore(line, _adapter.ShowLineMeasureCaliperSettingsDialog, CreateConfiguredCaliperState);
                    return;
            }
        }

        public string? ShowRoiLabelDialog(RoiBase roi)
        {
            ArgumentNullException.ThrowIfNull(roi);
            return ShowTextInput(UiText.Get("DialogRoiLabelPrompt"), roi.Label);
        }

        public void CalibrateSelectedRoi(RoiBase? selectedRoi)
        {
            Point p1;
            Point p2;

            switch (selectedRoi)
            {
                case CaliperMeasureRoi caliper:
                    p1 = caliper.P1;
                    p2 = caliper.P2;
                    break;
                case LineMeasureRoi line:
                    p1 = line.P1;
                    p2 = line.P2;
                    break;
                default:
                    return;
            }

            double pixelDistance = GeometryUtils.Distance(p1, p2);
            if (pixelDistance <= 0)
            {
                return;
            }

            var calibration = _adapter.ShowCalibrationDialog(_host.PhysicalUnit);
            if (calibration != null)
            {
                _host.PixelSize = calibration.Value.Length / pixelDistance;
                _host.PhysicalUnit = calibration.Value.Unit;
            }
        }

        private void ShowCaliperSettingsDialogCore<TCaliper>(
            TCaliper roi,
            Func<TCaliper, Action<TCaliper>?, TCaliper?> showDialog,
            Func<TCaliper, TCaliper, TCaliper> createConfiguredState)
            where TCaliper : RoiBase
        {
            var originalState = (TCaliper)roi.Clone();
            TCaliper? configuredState = showDialog(
                roi,
                preview => PreviewConfiguredCaliper(roi, preview, createConfiguredState));
            if (configuredState == null)
            {
                roi.ApplyFrom(originalState);
                _host.DrawRois();
                return;
            }

            RoiBase newState = createConfiguredState(originalState, configuredState);
            roi.ApplyFrom(originalState);

            IUndoRedoCommand? command = _host.CreateStateCommand(roi, originalState, newState);
            if (command != null)
            {
                _host.ExecuteUndoRedoCommand(command);
            }

            _host.DrawRois();
        }

        private void PreviewConfiguredCaliper<TCaliper>(
            TCaliper targetCaliper,
            TCaliper configuredCaliper,
            Func<TCaliper, TCaliper, TCaliper> createConfiguredState)
            where TCaliper : RoiBase
        {
            TCaliper previewState = createConfiguredState(targetCaliper, configuredCaliper);
            targetCaliper.ApplyFrom(previewState);
            _host.DrawSelectedRoiLayer();
        }

        private CaliperMeasureRoi CreateConfiguredCaliperState(CaliperMeasureRoi geometrySource, CaliperMeasureRoi configuredLine)
        {
            var state = (CaliperMeasureRoi)configuredLine.Clone();
            CopyLineGeometry(geometrySource, state);
            CopyCommonRoiState(geometrySource, state);
            state.ClearDetectedEdges();
            _host.TryApplyCaliperDetection(state);
            return state;
        }

        private CircularCaliperMeasureRoi CreateConfiguredCircularCaliperState(CircularCaliperMeasureRoi geometrySource, CircularCaliperMeasureRoi configuredCaliper)
        {
            return CreateConfiguredSingleEdgeCaliperState(
                geometrySource,
                configuredCaliper,
                static (source, target) => CopyCircleGeometry(source, target),
                static roi => roi.ClearDetectedEdges(),
                _host.TryApplyCircularCaliperDetection);
        }

        private LineCaliperMeasureRoi CreateConfiguredLineCaliperState(LineCaliperMeasureRoi geometrySource, LineCaliperMeasureRoi configuredLine)
        {
            return CreateConfiguredSingleEdgeCaliperState(
                geometrySource,
                configuredLine,
                static (source, target) => CopyLineGeometry(source, target),
                static roi => roi.ClearDetectedLine(),
                _host.TryApplyLineCaliperDetection);
        }

        private static TCaliper CreateConfiguredSingleEdgeCaliperState<TCaliper>(
            TCaliper geometrySource,
            TCaliper configuredCaliper,
            Action<TCaliper, TCaliper> copyGeometry,
            Action<TCaliper> clearDetection,
            Func<TCaliper, bool> tryApplyDetection)
            where TCaliper : RoiBase, ISingleEdgeCaliperRoi
        {
            var state = (TCaliper)configuredCaliper.Clone();
            copyGeometry(geometrySource, state);
            CopyCommonRoiState(geometrySource, state);
            clearDetection(state);
            tryApplyDetection(state);
            return state;
        }

        private static void CopyCommonRoiState(RoiBase source, RoiBase target)
        {
            target.Label = source.Label;
            RoiVisualState.Capture(source).ApplyTo(target, includeSelection: false);
        }

        private static void CopyLineGeometry(LineMeasureRoi source, LineMeasureRoi target)
        {
            target.P1 = source.P1;
            target.P2 = source.P2;
        }

        private static void CopyCircleGeometry(CircleRoi source, CircleRoi target)
        {
            target.Center = source.Center;
            target.Radius = source.Radius;
        }

        private static BitmapImage CreateBitmapFromFile(string filePath)
        {
            var bitmap = new BitmapImage();
            bitmap.BeginInit();
            bitmap.CacheOption = BitmapCacheOption.OnLoad;
            bitmap.UriSource = new Uri(filePath, UriKind.Absolute);
            bitmap.EndInit();
            bitmap.Freeze();
            return bitmap;
        }
    }
}