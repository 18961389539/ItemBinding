using System;
using System.Windows;

namespace ImageViewer.Controls
{
    internal sealed class ImageViewerCompositionHooks
    {
        public ImageViewerCompositionHooks(
            Func<Func<Window?>, ImageViewerRuntimeServices, IImageViewerDialogWorkflowAdapter> dialogWorkflowAdapterFactory,
            Func<IImageViewerDialogWorkflowHost, IImageViewerDialogWorkflowAdapter, ImageViewerDialogWorkflowService> dialogWorkflowServiceFactory,
            Func<ImageViewer, ImageViewerDependencies, ImageViewerControlComposition> controlCompositionFactory)
        {
            DialogWorkflowAdapterFactory = dialogWorkflowAdapterFactory ?? throw new ArgumentNullException(nameof(dialogWorkflowAdapterFactory));
            DialogWorkflowServiceFactory = dialogWorkflowServiceFactory ?? throw new ArgumentNullException(nameof(dialogWorkflowServiceFactory));
            ControlCompositionFactory = controlCompositionFactory ?? throw new ArgumentNullException(nameof(controlCompositionFactory));
        }

        public Func<Func<Window?>, ImageViewerRuntimeServices, IImageViewerDialogWorkflowAdapter> DialogWorkflowAdapterFactory { get; }

        public Func<IImageViewerDialogWorkflowHost, IImageViewerDialogWorkflowAdapter, ImageViewerDialogWorkflowService> DialogWorkflowServiceFactory { get; }

        public Func<ImageViewer, ImageViewerDependencies, ImageViewerControlComposition> ControlCompositionFactory { get; set; }

        public static ImageViewerCompositionHooks CreateDefault()
        {
            return new ImageViewerCompositionHooks(
                (ownerWindowProvider, runtimeServices) => new WpfImageViewerDialogWorkflowAdapter(ownerWindowProvider, runtimeServices.DialogService, runtimeServices.FileDialogService),
                (host, adapter) => new ImageViewerDialogWorkflowService(host, adapter),
                (owner, dependencies) => ImageViewerControlCompositionRoot.Create(owner, dependencies));
        }
    }
}