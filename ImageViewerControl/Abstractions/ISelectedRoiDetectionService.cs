using System.Windows.Media;
using ImageViewer.Models;
using ImageViewer.Services;

namespace ImageViewer.Abstractions
{
    public interface ISelectedRoiDetectionService
    {
        bool TryDetectSelectedLineCaliperEdges(ImageSource? imageSource, RoiBase? selectedRoi, out LineCaliperDetectionResult result);
    }
}
