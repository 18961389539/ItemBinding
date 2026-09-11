using System.Windows;
using ImageViewer.Abstractions;
using ImageViewer.Localization;
using Microsoft.Win32;

namespace ImageViewer.Dialogs
{
    public sealed class ImageViewerFileDialogService : IImageViewerFileDialogService
    {
        public string? ShowOpenImageDialog(Window? owner)
        {
            var dialog = new OpenFileDialog
            {
                Title = UiText.Get("FileDialogOpenImageTitle"),
                Filter = UiText.Get("FileDialogOpenImageFilter")
            };

            return dialog.ShowDialog(owner) == true ? dialog.FileName : null;
        }

        public string? ShowSaveRoiDialog(Window? owner)
        {
            var dialog = new SaveFileDialog
            {
                Title = UiText.Get("FileDialogSaveRoiTitle"),
                Filter = UiText.Get("FileDialogRoiFilter"),
                DefaultExt = ".json"
            };

            return dialog.ShowDialog(owner) == true ? dialog.FileName : null;
        }

        public string? ShowOpenRoiDialog(Window? owner)
        {
            var dialog = new OpenFileDialog
            {
                Title = UiText.Get("FileDialogOpenRoiTitle"),
                Filter = UiText.Get("FileDialogRoiFilter")
            };

            return dialog.ShowDialog(owner) == true ? dialog.FileName : null;
        }

        public string? ShowSaveSessionDialog(Window? owner)
        {
            var dialog = new SaveFileDialog
            {
                Title = UiText.Get("FileDialogSaveSessionTitle"),
                Filter = UiText.Get("FileDialogSessionFilter"),
                DefaultExt = ".ivsession"
            };

            return dialog.ShowDialog(owner) == true ? dialog.FileName : null;
        }

        public string? ShowOpenSessionDialog(Window? owner)
        {
            var dialog = new OpenFileDialog
            {
                Title = UiText.Get("FileDialogOpenSessionTitle"),
                Filter = UiText.Get("FileDialogOpenSessionFilter")
            };

            return dialog.ShowDialog(owner) == true ? dialog.FileName : null;
        }

        public string? ShowSaveSnapshotDialog(Window? owner)
        {
            var dialog = new SaveFileDialog
            {
                Title = UiText.Get("FileDialogSaveSnapshotTitle"),
                Filter = UiText.Get("FileDialogPngFilter"),
                DefaultExt = ".png"
            };

            return dialog.ShowDialog(owner) == true ? dialog.FileName : null;
        }

        public string? ShowSaveAnalysisCsvDialog(Window? owner)
        {
            var dialog = new SaveFileDialog
            {
                Title = UiText.Get("FileDialogSaveAnalysisCsvTitle"),
                Filter = UiText.Get("FileDialogCsvFilter"),
                DefaultExt = ".csv"
            };

            return dialog.ShowDialog(owner) == true ? dialog.FileName : null;
        }

        public string? ShowSaveProjectPackageDialog(Window? owner)
        {
            var dialog = new SaveFileDialog
            {
                Title = UiText.Get("FileDialogSaveProjectPackageTitle"),
                Filter = UiText.Get("FileDialogProjectPackageFilter"),
                DefaultExt = ".ivpkg"
            };

            return dialog.ShowDialog(owner) == true ? dialog.FileName : null;
        }
    }
}
