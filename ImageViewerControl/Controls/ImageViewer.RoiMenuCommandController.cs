using System;

namespace ImageViewer.Controls
{
    internal interface IImageViewerRoiMenuCommandHost
    {
        void Undo();

        void Redo();

        void DeleteSelected();

        void ClearAll();

        void EditSelectedProperties();

        void SetSelectedLabel();

        void SetSelectedColor(string colorName);

        void CalibrateSelectedRoi();

        void EditSelectedCaliperSettings();

        void UpdateContextMenuState();
    }

    internal sealed class ImageViewerRoiMenuCommandController
    {
        private readonly IImageViewerRoiMenuCommandHost _host;

        public ImageViewerRoiMenuCommandController(IImageViewerRoiMenuCommandHost host)
        {
            _host = host;
        }

        public void Execute(ImageViewerRoiMenuCommand command)
        {
            switch (command)
            {
                case ImageViewerRoiMenuCommand.Undo:
                    _host.Undo();
                    break;
                case ImageViewerRoiMenuCommand.Redo:
                    _host.Redo();
                    break;
                case ImageViewerRoiMenuCommand.DeleteSelected:
                    _host.DeleteSelected();
                    break;
                case ImageViewerRoiMenuCommand.ClearAll:
                    _host.ClearAll();
                    break;
                case ImageViewerRoiMenuCommand.EditProperties:
                    _host.EditSelectedProperties();
                    break;
                case ImageViewerRoiMenuCommand.SetLabel:
                    _host.SetSelectedLabel();
                    break;
                case ImageViewerRoiMenuCommand.SetColorCyan:
                    _host.SetSelectedColor("Cyan");
                    break;
                case ImageViewerRoiMenuCommand.SetColorRed:
                    _host.SetSelectedColor("Red");
                    break;
                case ImageViewerRoiMenuCommand.SetColorGreen:
                    _host.SetSelectedColor("Green");
                    break;
                case ImageViewerRoiMenuCommand.SetColorYellow:
                    _host.SetSelectedColor("Yellow");
                    break;
                case ImageViewerRoiMenuCommand.SetColorMagenta:
                    _host.SetSelectedColor("Magenta");
                    break;
                case ImageViewerRoiMenuCommand.CalibratePixels:
                    _host.CalibrateSelectedRoi();
                    break;
                case ImageViewerRoiMenuCommand.EditCaliperSettings:
                    _host.EditSelectedCaliperSettings();
                    break;
                default:
                    throw new ArgumentOutOfRangeException(nameof(command), command, null);
            }

            _host.UpdateContextMenuState();
        }
    }
}