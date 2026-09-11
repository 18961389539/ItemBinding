using System;

namespace ImageViewer.Controls
{
    public partial class ImageViewer
    {
        private sealed class ControllerFactory
        {
            private readonly CoreControllerAssembler _coreAssembler;
            private readonly AnalysisCompositionAssembler _analysisAssembler;
            private readonly SessionCompositionAssembler _sessionAssembler;
            private readonly InteractionCompositionAssembler _interactionAssembler;
            private readonly CommandControllerAssembler _commandAssembler;

            public ControllerFactory(ImageViewer owner, ImageViewerDependencies dependencies)
            {
                ArgumentNullException.ThrowIfNull(owner);
                ArgumentNullException.ThrowIfNull(dependencies);

                _coreAssembler = new CoreControllerAssembler(owner, dependencies);
                _analysisAssembler = new AnalysisCompositionAssembler(owner);
                _sessionAssembler = new SessionCompositionAssembler(owner);
                _interactionAssembler = new InteractionCompositionAssembler(owner);
                _commandAssembler = new CommandControllerAssembler(owner);
            }

            public IImageViewStateController CreateImageViewStateController() => _coreAssembler.CreateImageViewStateController();

            public RoiSelectionStateController CreateRoiSelectionStateController() => _coreAssembler.CreateRoiSelectionStateController();

            public ViewModelController CreateViewModelController(RoiSelectionStateController roiSelectionStateController)
                => _coreAssembler.CreateViewModelController(roiSelectionStateController);

            public ViewportController CreateViewportController() => _coreAssembler.CreateViewportController();

            public ImageViewerDialogWorkflowService CreateDialogWorkflowService(
                RoiSelectionStateController roiSelectionStateController,
                ViewportController viewportController)
                => _coreAssembler.CreateDialogWorkflowService(roiSelectionStateController, viewportController);

            public ImageViewerAnalysisComposition CreateAnalysisComposition(ImageViewerDialogWorkflowService dialogWorkflowService)
                => _analysisAssembler.CreateAnalysisComposition(dialogWorkflowService);

            public ImageViewerSessionComposition CreateSessionComposition(
                ImageViewerDialogWorkflowService dialogWorkflowService,
                ViewportController viewportController)
                => _sessionAssembler.CreateSessionComposition(dialogWorkflowService, viewportController);

            public ImageSourceController CreateImageSourceController(
                ImageViewerDialogWorkflowService dialogWorkflowService,
                IImageViewStateController imageViewStateController)
                => _coreAssembler.CreateImageSourceController(dialogWorkflowService, imageViewStateController);

            public RoiEditController CreateRoiEditController(ImageViewerDialogWorkflowService dialogWorkflowService)
                => _coreAssembler.CreateRoiEditController(dialogWorkflowService);

            public CalibrationController CreateCalibrationController(ImageViewerDialogWorkflowService dialogWorkflowService)
                => _coreAssembler.CreateCalibrationController(dialogWorkflowService);

            public DroppedContentController CreateDroppedContentController(
                ViewportController viewportController,
                ImageViewerSessionController sessionController)
                => _coreAssembler.CreateDroppedContentController(viewportController, sessionController);

            public ImageViewerInteractionComposition CreateInteractionComposition(
                ViewportController viewportController,
                RoiEditController roiEditController,
                ImageViewerSessionController sessionController,
                ImageViewerAnalysisCoordinator analysisController)
                => _interactionAssembler.CreateInteractionComposition(viewportController, roiEditController, sessionController, analysisController);

            public ImageViewerFeatureMenuCommandController CreateFeatureMenuCommandController(ImageViewerDialogWorkflowService dialogWorkflowService)
                => _commandAssembler.CreateFeatureMenuCommandController(dialogWorkflowService);

            public ImageViewerRoiMenuCommandController CreateRoiMenuCommandController(
                ImageViewerDialogWorkflowService dialogWorkflowService,
                RoiEditController roiEditController,
                CalibrationController calibrationController)
                => _commandAssembler.CreateRoiMenuCommandController(dialogWorkflowService, roiEditController, calibrationController);

            public ImageViewerViewCommandController CreateViewCommandController(ViewportController viewportController)
                => _commandAssembler.CreateViewCommandController(viewportController);

            public ImageViewerModeCommandController CreateModeCommandController() => _commandAssembler.CreateModeCommandController();

            public ExternalImageSourceBindingController CreateExternalImageSourceBindingController() => _coreAssembler.CreateExternalImageSourceBindingController();
        }
    }
}