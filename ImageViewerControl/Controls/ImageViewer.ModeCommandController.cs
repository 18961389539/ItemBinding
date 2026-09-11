using System;

namespace ImageViewer.Controls
{
    internal sealed class ImageViewerModeCommandController
    {
        private readonly IImageViewerModeCommandHost _host;

        public ImageViewerModeCommandController(IImageViewerModeCommandHost host)
        {
            _host = host;
        }

        public void Execute(ImageViewerModeCommand command)
        {
            switch (command)
            {
                case ImageViewerModeCommand.Rectangle:
                    _host.StartRectangleMode();
                    break;
                case ImageViewerModeCommand.Ellipse:
                    _host.StartEllipseMode();
                    break;
                case ImageViewerModeCommand.Circle:
                    _host.StartCircleMode();
                    break;
                case ImageViewerModeCommand.Polygon:
                    _host.StartPolygonMode();
                    break;
                case ImageViewerModeCommand.Polyline:
                    _host.StartPolylineMode();
                    break;
                case ImageViewerModeCommand.Freehand:
                    _host.StartFreehandMode();
                    break;
                case ImageViewerModeCommand.PointAnnotation:
                    _host.StartPointAnnotationMode();
                    break;
                case ImageViewerModeCommand.TextAnnotation:
                    _host.StartTextAnnotationMode();
                    break;
                case ImageViewerModeCommand.LineMeasure:
                    _host.StartLineMeasureMode();
                    break;
                case ImageViewerModeCommand.AngleMeasure:
                    _host.StartAngleMeasureMode();
                    break;
                default:
                    throw new ArgumentOutOfRangeException(nameof(command), command, null);
            }
        }
    }
}