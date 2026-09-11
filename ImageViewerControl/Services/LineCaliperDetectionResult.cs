using System.Windows;
using ImageViewer.Models;

namespace ImageViewer.Services
{
    public readonly record struct LineCaliperDetectionResult(
        Point ReferenceP1,
        Point ReferenceP2,
        Point DetectedP1,
        Point DetectedP2,
        Point[] InvalidSamplePoints,
        Point[] EdgePoints,
        Point[] RejectedEdgePoints,
        double[] EdgeScores,
        DetectedLineSegment FittedLine,
        double AverageScore,
        double ResidualRms,
        double ResidualMax,
        int ValidCaliperCount,
        double AngleDegrees,
        double Confidence);
}
