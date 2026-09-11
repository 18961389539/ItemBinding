using System;
using System.Windows;
using ImageViewer.Models;
using ImageViewer.Services;

namespace ImageViewer.Rendering
{
    internal static class CaliperDetectionDisplayStateFactory
    {
        public static DualEdgeCaliperDetectionDisplayState Create(CaliperMeasureRoi roi, LineMeasureGradientDetectionResult detectionResult)
        {
            ArgumentNullException.ThrowIfNull(roi);

            Vector measurementDirection = roi.GetCaliperMeasurementDirection();
            Vector markerDirection = new(-measurementDirection.Y, measurementDirection.X);
            double markerHalfLength = Math.Max(roi.CaliperSamplingHalfWidth + 1, 3);

            return new DualEdgeCaliperDetectionDisplayState
            {
                HasDetection = true,
                Edge1Start = detectionResult.FittedEdge1.Start,
                Edge1End = detectionResult.FittedEdge1.End,
                Edge2Start = detectionResult.FittedEdge2.Start,
                Edge2End = detectionResult.FittedEdge2.End,
                RegionSegments = CaliperOverlayGeometryHelper.BuildDualEdgeCaliperRegionSegments(roi),
                CaliperBars = CaliperOverlayGeometryHelper.BuildDualEdgeCaliperBars(roi),
                InvalidCaliperMarkers = CaliperOverlayGeometryHelper.BuildLinearMarkers(detectionResult.InvalidCaliperCenters, markerDirection, markerHalfLength),
                Edge1Markers = CaliperOverlayGeometryHelper.BuildLinearMarkers(detectionResult.Edge1Points, markerDirection, markerHalfLength),
                Edge2Markers = CaliperOverlayGeometryHelper.BuildLinearMarkers(detectionResult.Edge2Points, markerDirection, markerHalfLength),
                RejectedEdge1Markers = CaliperOverlayGeometryHelper.BuildLinearMarkers(detectionResult.RejectedEdge1Points, markerDirection, markerHalfLength),
                RejectedEdge2Markers = CaliperOverlayGeometryHelper.BuildLinearMarkers(detectionResult.RejectedEdge2Points, markerDirection, markerHalfLength),
                ScoreOverlays = CaliperOverlayGeometryHelper.BuildDualEdgeScoreOverlays(
                    detectionResult.InvalidCaliperCenters,
                    detectionResult.Edge1Points,
                    detectionResult.Edge2Points,
                    detectionResult.Edge1Scores,
                    detectionResult.Edge2Scores,
                    detectionResult.RejectedEdge1Points,
                    detectionResult.RejectedEdge2Points,
                    measurementDirection),
                Edge1Points = [.. detectionResult.Edge1Points],
                Edge2Points = [.. detectionResult.Edge2Points],
                Edge1AverageScore = detectionResult.Edge1AverageScore,
                Edge2AverageScore = detectionResult.Edge2AverageScore,
                Edge1ResidualRms = detectionResult.Edge1ResidualRms,
                Edge2ResidualRms = detectionResult.Edge2ResidualRms,
                Edge1ResidualMax = detectionResult.Edge1ResidualMax,
                Edge2ResidualMax = detectionResult.Edge2ResidualMax,
                ValidCaliperCount = detectionResult.ValidCaliperCount,
                Edge1AngleDegrees = detectionResult.Edge1AngleDegrees,
                Edge2AngleDegrees = detectionResult.Edge2AngleDegrees,
                ParallelismErrorDegrees = detectionResult.ParallelismErrorDegrees,
                Confidence = detectionResult.Confidence
            };
        }

        public static SingleEdgeCaliperDetectionDisplayState Create(LineCaliperMeasureRoi roi, LineCaliperDetectionResult detectionResult)
        {
            ArgumentNullException.ThrowIfNull(roi);

            Vector lineDirection = detectionResult.ReferenceP2 - detectionResult.ReferenceP1;
            if (lineDirection.LengthSquared < 1e-6)
            {
                lineDirection = new Vector(1, 0);
            }

            lineDirection.Normalize();
            Vector measurementDirection = new(-lineDirection.Y, lineDirection.X);
            double markerHalfLength = Math.Max(roi.CaliperSamplingHalfWidth + 1, 3);

            return new SingleEdgeCaliperDetectionDisplayState
            {
                HasDetection = true,
                RegionSegments = CaliperOverlayGeometryHelper.BuildLineCaliperRegionSegments(detectionResult.ReferenceP1, detectionResult.ReferenceP2, roi.CaliperSearchRange),
                CaliperBars = CaliperOverlayGeometryHelper.BuildLineCaliperBars(detectionResult.ReferenceP1, detectionResult.ReferenceP2, roi.CaliperSearchRange, roi.CaliperCount),
                InvalidCaliperMarkers = CaliperOverlayGeometryHelper.BuildLinearMarkers(detectionResult.InvalidSamplePoints, lineDirection, markerHalfLength),
                EdgeMarkers = CaliperOverlayGeometryHelper.BuildLinearMarkers(detectionResult.EdgePoints, lineDirection, markerHalfLength),
                RejectedEdgeMarkers = CaliperOverlayGeometryHelper.BuildLinearMarkers(detectionResult.RejectedEdgePoints, lineDirection, markerHalfLength),
                ScoreOverlays = CaliperOverlayGeometryHelper.BuildLinearScoreOverlays(
                    detectionResult.InvalidSamplePoints,
                    detectionResult.EdgePoints,
                    detectionResult.EdgeScores,
                    detectionResult.RejectedEdgePoints,
                    measurementDirection),
                EdgePoints = [.. detectionResult.EdgePoints],
                AverageScore = detectionResult.AverageScore,
                ResidualRms = detectionResult.ResidualRms,
                ResidualMax = detectionResult.ResidualMax,
                ValidCaliperCount = detectionResult.ValidCaliperCount,
                Confidence = detectionResult.Confidence
            };
        }

        public static SingleEdgeCaliperDetectionDisplayState Create(CircularCaliperMeasureRoi roi, CircularCaliperDetectionResult detectionResult)
        {
            ArgumentNullException.ThrowIfNull(roi);

            double markerHalfLength = Math.Max(roi.CaliperSamplingHalfWidth + 1, 3);
            LineSegmentOverlay[] regionSegments = roi is ArcCaliperMeasureRoi arcCaliper
                ? CaliperOverlayGeometryHelper.BuildArcCaliperRegionSegments(detectionResult.ReferenceCenter, detectionResult.ReferenceRadius, arcCaliper.CaliperSearchRange, arcCaliper.CaliperCount, arcCaliper.StartAngle, arcCaliper.SweepAngle)
                : CaliperOverlayGeometryHelper.BuildCircularCaliperRegionSegments(detectionResult.ReferenceCenter, detectionResult.ReferenceRadius, roi.CaliperSearchRange, roi.CaliperCount);
            LineSegmentOverlay[] caliperBars = roi is ArcCaliperMeasureRoi arcRoi
                ? CaliperOverlayGeometryHelper.BuildArcCaliperBars(detectionResult.ReferenceCenter, detectionResult.ReferenceRadius, arcRoi.CaliperSearchRange, arcRoi.CaliperCount, arcRoi.StartAngle, arcRoi.SweepAngle)
                : CaliperOverlayGeometryHelper.BuildCircularCaliperBars(detectionResult.ReferenceCenter, detectionResult.ReferenceRadius, roi.CaliperSearchRange, roi.CaliperCount);

            return new SingleEdgeCaliperDetectionDisplayState
            {
                HasDetection = true,
                RegionSegments = regionSegments,
                CaliperBars = caliperBars,
                InvalidCaliperMarkers = CaliperOverlayGeometryHelper.BuildCircularMarkers(detectionResult.InvalidSamplePoints, detectionResult.ReferenceCenter, markerHalfLength),
                EdgeMarkers = CaliperOverlayGeometryHelper.BuildCircularMarkers(detectionResult.EdgePoints, detectionResult.ReferenceCenter, markerHalfLength),
                RejectedEdgeMarkers = CaliperOverlayGeometryHelper.BuildCircularMarkers(detectionResult.RejectedEdgePoints, detectionResult.ReferenceCenter, markerHalfLength),
                ScoreOverlays = CaliperOverlayGeometryHelper.BuildCircularScoreOverlays(
                    detectionResult.InvalidSamplePoints,
                    detectionResult.EdgePoints,
                    detectionResult.EdgeScores,
                    detectionResult.RejectedEdgePoints,
                    detectionResult.ReferenceCenter),
                EdgePoints = [.. detectionResult.EdgePoints],
                AverageScore = detectionResult.AverageScore,
                ResidualRms = detectionResult.ResidualRms,
                ResidualMax = detectionResult.ResidualMax,
                ValidCaliperCount = detectionResult.ValidCaliperCount,
                Confidence = detectionResult.Confidence
            };
        }
    }
}
