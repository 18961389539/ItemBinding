using System;

namespace ImageViewer.Controls
{
    public partial class ImageViewer
    {
        private sealed class SessionCompositionAssembler
        {
            private readonly ImageViewer _owner;

            public SessionCompositionAssembler(ImageViewer owner)
            {
                _owner = owner ?? throw new ArgumentNullException(nameof(owner));
            }

            public ImageViewerSessionComposition CreateSessionComposition(
                ImageViewerDialogWorkflowService dialogWorkflowService,
                ViewportController viewportController)
            {
                ArgumentNullException.ThrowIfNull(dialogWorkflowService);
                ArgumentNullException.ThrowIfNull(viewportController);

                var sessionController = new ImageViewerSessionController(
                    new ImageViewerSessionControllerHostAdapter(
                        _owner,
                        dialogWorkflowService,
                        viewportController,
                        () => _owner.DrawRois(),
                        _owner.ShowNonCriticalError,
                        _owner.LogNonCriticalError,
                        _owner.UpdateContextMenuState),
                    _owner.HostServices.PeriodicTaskSchedulerFactory,
                    _owner.HostServices.SessionStoragePolicy);

                ImageViewerRoiPersistenceController roiPersistenceController = CreateRoiPersistenceController(dialogWorkflowService);
                ImageViewerFileMenuCommandController fileMenuCommandController = CreateFileMenuCommandController(dialogWorkflowService, sessionController, roiPersistenceController);

                return new ImageViewerSessionComposition(sessionController, roiPersistenceController, fileMenuCommandController);
            }

            private ImageViewerRoiPersistenceController CreateRoiPersistenceController(ImageViewerDialogWorkflowService dialogWorkflowService)
            {
                ArgumentNullException.ThrowIfNull(dialogWorkflowService);

                return new ImageViewerRoiPersistenceController(
                    new ImageViewerRoiPersistenceControllerHostAdapter(
                        _owner,
                        dialogWorkflowService,
                        _owner.RefreshAllCaliperDetections,
                        () => _owner.DrawRois(),
                        () => _owner._roiSelectionStateController.RefreshPropertyPanel(),
                        _owner.ShowNonCriticalError));
            }

            private ImageViewerFileMenuCommandController CreateFileMenuCommandController(
                ImageViewerDialogWorkflowService dialogWorkflowService,
                ImageViewerSessionController sessionController,
                ImageViewerRoiPersistenceController roiPersistenceController)
            {
                ArgumentNullException.ThrowIfNull(dialogWorkflowService);
                ArgumentNullException.ThrowIfNull(sessionController);
                ArgumentNullException.ThrowIfNull(roiPersistenceController);

                return new ImageViewerFileMenuCommandController(
                    new ImageViewerFileMenuCommandHostAdapter(
                        dialogWorkflowService.OpenImageAsync,
                        sessionController,
                        roiPersistenceController,
                        _owner.UpdateContextMenuState));
            }
        }
    }
}