using System.Windows.Media;
using System.Windows.Media.Imaging;
using ImageViewer.Abstractions;
using ImageViewer.Models;

namespace ImageViewer.Services
{
    public sealed class SelectedRoiDetectionService : ISelectedRoiDetectionService
    {
        public static SelectedRoiDetectionService Default { get; } = new();

        public bool TryDetectSelectedLineCaliperEdges(ImageSource? imageSource, RoiBase? selectedRoi, out LineCaliperDetectionResult result)
        {
            result = default;
            if (selectedRoi is not LineCaliperMeasureRoi lineCaliper)
            {
                return false;
            }

            if (imageSource is not BitmapSource bitmap)
            {
                return false;
            }

            if (!ImageAnalysisService.TryDetectLineCaliperEdges(bitmap, lineCaliper, out result))
            {
                lineCaliper.ClearDetectedLine();
                return false;
            }

            RoiDetectionResultMapper.Apply(lineCaliper, result);
            return true;
        }
    }
}
