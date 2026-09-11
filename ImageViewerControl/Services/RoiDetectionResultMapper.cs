using ImageViewer.Models;
using ImageViewer.Rendering;

namespace ImageViewer.Services
{
    internal static class RoiDetectionResultMapper
    {
        public static void Apply(CaliperMeasureRoi line, LineMeasureGradientDetectionResult detectionResult)
        {
            line.P1 = detectionResult.DetectedP1;
            line.P2 = detectionResult.DetectedP2;
            line.SetDetectionVisualization(CaliperDetectionDisplayStateFactory.Create(line, detectionResult));
        }

        public static void Apply(LineCaliperMeasureRoi line, LineCaliperDetectionResult detectionResult)
        {
            line.P1 = detectionResult.DetectedP1;
            line.P2 = detectionResult.DetectedP2;
            line.SetDetectionVisualization(CaliperDetectionDisplayStateFactory.Create(line, detectionResult));
            line.AngleDegrees = detectionResult.AngleDegrees;
        }

        public static void Apply(CircularCaliperMeasureRoi caliper, CircularCaliperDetectionResult detectionResult)
        {
            caliper.Center = detectionResult.DetectedCenter;
            caliper.Radius = detectionResult.DetectedRadius;
            caliper.SetDetectionVisualization(CaliperDetectionDisplayStateFactory.Create(caliper, detectionResult));
        }
    }
}
