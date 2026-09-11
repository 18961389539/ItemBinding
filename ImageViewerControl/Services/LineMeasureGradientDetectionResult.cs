using System.Windows;
using ImageViewer.Models;

namespace ImageViewer.Services
{
    public readonly record struct LineMeasureGradientDetectionResult(
        Point DetectedP1,
        Point DetectedP2,
        Point[] InvalidCaliperCenters,
        Point[] Edge1Points,
        Point[] Edge2Points,
        Point[] RejectedEdge1Points,
        Point[] RejectedEdge2Points,
        double[] Edge1Scores,
        double[] Edge2Scores,
        DetectedLineSegment FittedEdge1,
        DetectedLineSegment FittedEdge2,
        double Edge1AverageScore,
        double Edge2AverageScore,
        double Edge1ResidualRms,
        double Edge2ResidualRms,
        double Edge1ResidualMax,
        double Edge2ResidualMax,
        int ValidCaliperCount,
        double Edge1AngleDegrees,
        double Edge2AngleDegrees,
        double ParallelismErrorDegrees,
        double Confidence);
}
