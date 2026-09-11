using System;
using System.IO;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Media.Imaging;
using ImageViewer.Localization;
using ImageViewer.Models;
using ImageViewer.Services;
using ImageViewer.ViewModels;

namespace ImageViewer.Controls
{
    internal sealed class ImageViewerFeatureMenuCommandHostAdapter : IImageViewerFeatureMenuCommandHost
    {
        private readonly ImageViewer _owner;
        private readonly FrameworkElement _renderRoot;
        private readonly ImageViewerDialogWorkflowService _dialogWorkflowService;
        private readonly Func<BitmapSource?> _getAnalysisBitmapSource;
        private readonly Func<RoiBase, RoiBase, RoiBase, IUndoRedoCommand?> _createStateCommand;
        private readonly Action<IUndoRedoCommand> _executeUndoRedoCommand;
        private readonly Action _drawRois;
        private readonly Action<string, string, Exception> _showNonCriticalError;
        private readonly Action _updateContextMenuState;

        public ImageViewerFeatureMenuCommandHostAdapter(
            ImageViewer owner,
            FrameworkElement renderRoot,
            ImageViewerDialogWorkflowService dialogWorkflowService,
            Func<BitmapSource?> getAnalysisBitmapSource,
            Func<RoiBase, RoiBase, RoiBase, IUndoRedoCommand?> createStateCommand,
            Action<IUndoRedoCommand> executeUndoRedoCommand,
            Action drawRois,
            Action<string, string, Exception> showNonCriticalError,
            Action updateContextMenuState)
        {
            _owner = owner ?? throw new ArgumentNullException(nameof(owner));
            _renderRoot = renderRoot ?? throw new ArgumentNullException(nameof(renderRoot));
            _dialogWorkflowService = dialogWorkflowService ?? throw new ArgumentNullException(nameof(dialogWorkflowService));
            _getAnalysisBitmapSource = getAnalysisBitmapSource ?? throw new ArgumentNullException(nameof(getAnalysisBitmapSource));
            _createStateCommand = createStateCommand ?? throw new ArgumentNullException(nameof(createStateCommand));
            _executeUndoRedoCommand = executeUndoRedoCommand ?? throw new ArgumentNullException(nameof(executeUndoRedoCommand));
            _drawRois = drawRois ?? throw new ArgumentNullException(nameof(drawRois));
            _showNonCriticalError = showNonCriticalError ?? throw new ArgumentNullException(nameof(showNonCriticalError));
            _updateContextMenuState = updateContextMenuState ?? throw new ArgumentNullException(nameof(updateContextMenuState));
        }

        public void RunGradientDetection()
        {
            if (_getAnalysisBitmapSource() is not BitmapSource bitmap || _owner.ViewerState.SelectedRoi is not RoiBase selectedRoi)
            {
                return;
            }

            RoiBase oldState = selectedRoi.Clone();
            RoiBase? detectedRoi = selectedRoi switch
            {
                CaliperMeasureRoi line when ImageAnalysisService.TryDetectLineMeasureEdges(bitmap, line, out LineMeasureGradientDetectionResult lineDetectionResult) => CreateDetectedLineMeasureRoi(line, lineDetectionResult),
                CircularCaliperMeasureRoi circular when ImageAnalysisService.TryDetectCircularCaliperEdges(bitmap, circular, out CircularCaliperDetectionResult circularDetectionResult) => CreateDetectedCircularCaliperRoi(circular, circularDetectionResult),
                _ => null
            };

            if (detectedRoi == null)
            {
                return;
            }

            IUndoRedoCommand? command = _createStateCommand(selectedRoi, oldState, detectedRoi);
            if (command == null)
            {
                return;
            }

            _executeUndoRedoCommand(command);
            _drawRois();
        }

        public async Task ExportSnapshotAsync()
        {
            string? filePath = _dialogWorkflowService.ShowSaveSnapshotDialog();
            if (string.IsNullOrWhiteSpace(filePath))
            {
                return;
            }

            try
            {
                _renderRoot.UpdateLayout();
                var bitmap = new RenderTargetBitmap(
                    Math.Max(1, (int)Math.Ceiling(_renderRoot.ActualWidth)),
                    Math.Max(1, (int)Math.Ceiling(_renderRoot.ActualHeight)),
                    96,
                    96,
                    System.Windows.Media.PixelFormats.Pbgra32);
                bitmap.Render(_renderRoot);

                var encoder = new PngBitmapEncoder();
                encoder.Frames.Add(BitmapFrame.Create(bitmap));
                await using var stream = File.Create(filePath);
                await Task.Run(() => encoder.Save(stream));
            }
            catch (Exception ex)
            {
                _showNonCriticalError(UiText.Get("ErrorExportPngTitle"), UiText.Get("ErrorExportPngMessage"), ex);
            }
        }

        public async Task ExportAnalysisCsvAsync()
        {
            string? filePath = _dialogWorkflowService.ShowSaveAnalysisCsvDialog();
            if (string.IsNullOrWhiteSpace(filePath))
            {
                return;
            }

            try
            {
                await RoiAnalysisExportService.SaveCsvAsync(filePath, _owner.ViewerState.AllRois, _getAnalysisBitmapSource(), _owner.PixelSize, _owner.PhysicalUnit);
            }
            catch (Exception ex)
            {
                _showNonCriticalError(UiText.Get("ErrorExportAnalysisTitle"), UiText.Get("ErrorExportAnalysisMessage"), ex);
            }
        }

        public void ShowAnalysisSummary()
        {
            string summary = RoiAnalysisExportService.BuildSummary(_owner.ViewerState.AllRois, _getAnalysisBitmapSource(), _owner.PixelSize, _owner.PhysicalUnit);
            _dialogWorkflowService.ShowReadOnlyText(UiText.Get("DialogAnalysisSummaryTitle"), summary);
        }

        public void UpdateContextMenuState() => _updateContextMenuState();

        private static CaliperMeasureRoi CreateDetectedLineMeasureRoi(CaliperMeasureRoi source, LineMeasureGradientDetectionResult detectionResult)
        {
            var detected = (CaliperMeasureRoi)source.Clone();
            RoiDetectionResultMapper.Apply(detected, detectionResult);
            return detected;
        }

        private static CircularCaliperMeasureRoi CreateDetectedCircularCaliperRoi(CircularCaliperMeasureRoi source, CircularCaliperDetectionResult detectionResult)
        {
            var detected = (CircularCaliperMeasureRoi)source.Clone();
            RoiDetectionResultMapper.Apply(detected, detectionResult);
            return detected;
        }
    }
}