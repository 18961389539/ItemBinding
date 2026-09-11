using System;
using System.Windows;

namespace ImageViewer.Controls
{
    public partial class ImageViewer
    {
        private sealed class CoreControllerAssembler
        {
            private readonly ImageViewer _owner;
            private readonly ImageViewerDependencies _dependencies;

            public CoreControllerAssembler(ImageViewer owner, ImageViewerDependencies dependencies)
            {
                _owner = owner ?? throw new ArgumentNullException(nameof(owner));
                _dependencies = dependencies ?? throw new ArgumentNullException(nameof(dependencies));
            }

            public ImageViewStateController CreateImageViewStateController()
            {
                return new ImageViewStateController(
                    new ImageViewerImageViewStateHost(
                        _owner,
                        _owner.imageContainer,
                        _owner.overlayCanvas,
                        _owner.snapGridCanvas,
                        _owner.pixelGridCanvas,
                        _owner.scaleBarCanvas,
                        _owner.scaleBarLine,
                        _owner.scaleBarText,
                        _owner.crosshairH,
                        _owner.crosshairV,
                        _owner.infoPanel,
                        _owner.roiListPanel,
                        _owner.scaleTransform,
                        _owner.UpdateRenderedImage,
                        _owner.UpdateCrosshair,
                        () => _owner.DrawRois(),
                        _owner.UpdatePixelGrid,
                        () => _owner.RequestViewportOverlayRefresh(),
                        _owner.FormatLength),
                    MinScale);
            }

            public RoiSelectionStateController CreateRoiSelectionStateController()
            {
                return new RoiSelectionStateController(
                    new ImageViewerRoiSelectionStateHost(
                        _owner,
                        _owner.roiPropertyEditorHost,
                        _owner.roiPropertyPanel,
                        roi => _owner.TryApplyCaliperDetection(roi),
                        roi => _owner.TryApplyLineCaliperDetection(roi),
                        roi => _owner.TryApplyCircularCaliperDetection(roi),
                        () => _owner.DrawRois()));
            }

            public ViewModelController CreateViewModelController(RoiSelectionStateController roiSelectionStateController)
            {
                ArgumentNullException.ThrowIfNull(roiSelectionStateController);

                return new ViewModelController(
                    _owner.ViewModel,
                    _owner.UpdateContextMenuState,
                    roiSelectionStateController.HandleSelectedRoiChanged,
                    () => _owner.DrawRois());
            }

            public ViewportController CreateViewportController()
            {
                return new ViewportController(
                    new ImageViewerViewportHost(
                        _owner,
                        () => new Size(_owner.rootGrid.ActualWidth, _owner.rootGrid.ActualHeight),
                        () => new ImageViewerViewportState(_owner.Scale, _owner.translateTransform.X, _owner.translateTransform.Y),
                        state =>
                        {
                            _owner.Scale = state.Scale;
                            _owner.translateTransform.X = state.TranslateX;
                            _owner.translateTransform.Y = state.TranslateY;
                        },
                        () => _owner.BeginViewportOverlayBatch(),
                        immediate => _owner.EndViewportOverlayBatch(immediate),
                        _owner.UpdatePixelGrid,
                        () => _owner.UpdateProfile(),
                        () => _owner.UpdateInfoPanel(),
                        _owner.UpdateRenderedImage,
                        () => _owner.RequestViewportOverlayRefresh()),
                    ViewportPadding,
                    MinScale,
                    MaxScale);
            }

            public ImageViewerDialogWorkflowService CreateDialogWorkflowService(
                RoiSelectionStateController roiSelectionStateController,
                ViewportController viewportController)
            {
                ArgumentNullException.ThrowIfNull(roiSelectionStateController);
                ArgumentNullException.ThrowIfNull(viewportController);

                IImageViewerDialogWorkflowAdapter dialogWorkflowAdapter = _dependencies.CreateDialogWorkflowAdapter(() => Window.GetWindow(_owner));
                return _dependencies.CreateDialogWorkflowService(
                    new ImageViewerDialogWorkflowHostAdapter(
                        _owner,
                        roiSelectionStateController,
                        viewportController,
                        () => _owner.DrawRois(),
                        _owner.DrawSelectedRoiLayer,
                        static (roi, oldState, newState) => ImageViewer.CreateStateCommand(roi, oldState, newState),
                        roi => _owner.TryApplyCaliperDetection(roi),
                        roi => _owner.TryApplyLineCaliperDetection(roi),
                        roi => _owner.TryApplyCircularCaliperDetection(roi),
                        _owner.ShowNonCriticalError),
                    dialogWorkflowAdapter);
            }

            public ImageSourceController CreateImageSourceController(
                ImageViewerDialogWorkflowService dialogWorkflowService,
                IImageViewStateController imageViewStateController)
            {
                ArgumentNullException.ThrowIfNull(dialogWorkflowService);
                ArgumentNullException.ThrowIfNull(imageViewStateController);

                return new ImageSourceController(
                    dialogWorkflowService,
                    () => _owner.IsLoaded,
                    () => _owner.ImageSource,
                    source => _owner.PrepareAnalysisResourcesAsync(source),
                    imageViewStateController,
                    _owner.RefreshAllCaliperDetections,
                    () => _owner.DrawRois());
            }

            public RoiEditController CreateRoiEditController(ImageViewerDialogWorkflowService dialogWorkflowService)
            {
                ArgumentNullException.ThrowIfNull(dialogWorkflowService);

                return new RoiEditController(
                    _owner.ViewModel,
                    dialogWorkflowService,
                    roi => _owner.TryRefreshCaliperDetection(roi),
                    () => _owner.DrawRois(),
                    _owner.LogNonCriticalError);
            }

            public CalibrationController CreateCalibrationController(ImageViewerDialogWorkflowService dialogWorkflowService)
            {
                ArgumentNullException.ThrowIfNull(dialogWorkflowService);
                return new CalibrationController(dialogWorkflowService, () => _owner.ViewerState.SelectedRoi);
            }

            public DroppedContentController CreateDroppedContentController(
                ViewportController viewportController,
                ImageViewerSessionController sessionController)
            {
                ArgumentNullException.ThrowIfNull(viewportController);
                ArgumentNullException.ThrowIfNull(sessionController);

                return new DroppedContentController(viewportController, sessionController, () => _owner._roiSelectionStateController.RefreshPropertyPanel());
            }

            public ExternalImageSourceBindingController CreateExternalImageSourceBindingController()
            {
                return new ExternalImageSourceBindingController(
                    _owner,
                    () => _owner.ImageSource,
                    source => _owner.SetCurrentValue(ImageSourceProperty, source));
            }
        }
    }
}