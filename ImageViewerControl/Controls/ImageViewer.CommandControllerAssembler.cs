using System;

namespace ImageViewer.Controls
{
    public partial class ImageViewer
    {
        private sealed class CommandControllerAssembler
        {
            private readonly ImageViewer _owner;

            public CommandControllerAssembler(ImageViewer owner)
            {
                _owner = owner ?? throw new ArgumentNullException(nameof(owner));
            }

            public ImageViewerFeatureMenuCommandController CreateFeatureMenuCommandController(ImageViewerDialogWorkflowService dialogWorkflowService)
            {
                ArgumentNullException.ThrowIfNull(dialogWorkflowService);

                return new ImageViewerFeatureMenuCommandController(
                    new ImageViewerFeatureMenuCommandHostAdapter(
                        _owner,
                        _owner.rootGrid,
                        dialogWorkflowService,
                        () => _owner.GetAnalysisBitmapSource(),
                        static (roi, oldState, newState) => ImageViewer.CreateStateCommand(roi, oldState, newState),
                        command => _owner.ViewerState.UndoRedo.Execute(command),
                        () => _owner.DrawRois(),
                        _owner.ShowNonCriticalError,
                        _owner.UpdateContextMenuState));
            }

            public ImageViewerRoiMenuCommandController CreateRoiMenuCommandController(
                ImageViewerDialogWorkflowService dialogWorkflowService,
                RoiEditController roiEditController,
                CalibrationController calibrationController)
            {
                ArgumentNullException.ThrowIfNull(dialogWorkflowService);
                ArgumentNullException.ThrowIfNull(roiEditController);
                ArgumentNullException.ThrowIfNull(calibrationController);

                return new ImageViewerRoiMenuCommandController(
                    new ImageViewerRoiMenuCommandHostAdapter(
                        () => _owner.ViewerState.SelectedRoi,
                        roiEditController,
                        calibrationController,
                        dialogWorkflowService.ShowRoiProperties,
                        dialogWorkflowService.ShowCaliperSettings,
                        _owner.UpdateContextMenuState));
            }

            public ImageViewerViewCommandController CreateViewCommandController(ViewportController viewportController)
            {
                ArgumentNullException.ThrowIfNull(viewportController);
                return new ImageViewerViewCommandController(new ImageViewerViewCommandHostAdapter(_owner, viewportController));
            }

            public ImageViewerModeCommandController CreateModeCommandController()
            {
                return new ImageViewerModeCommandController(new ImageViewerModeCommandHostAdapter(_owner));
            }
        }
    }
}