using System.Windows;

namespace ImageViewer.Abstractions
{
    public interface IImageViewerFileDialogService
    {
        string? ShowOpenImageDialog(Window? owner);

        string? ShowSaveRoiDialog(Window? owner);

        string? ShowOpenRoiDialog(Window? owner);

        string? ShowSaveSessionDialog(Window? owner);

        string? ShowOpenSessionDialog(Window? owner);

        string? ShowSaveSnapshotDialog(Window? owner);

        string? ShowSaveAnalysisCsvDialog(Window? owner);

        string? ShowSaveProjectPackageDialog(Window? owner);
    }
}
