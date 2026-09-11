using System.Windows;

namespace ImageViewer.Models
{
    internal sealed class SingleEdgeCaliperDetectionDisplayState
    {
        public bool HasDetection { get; set; }

        public LineSegmentOverlay[] RegionSegments { get; set; } = [];

        public LineSegmentOverlay[] CaliperBars { get; set; } = [];

        public LineSegmentOverlay[] InvalidCaliperMarkers { get; set; } = [];

        public LineSegmentOverlay[] EdgeMarkers { get; set; } = [];

        public LineSegmentOverlay[] RejectedEdgeMarkers { get; set; } = [];

        public CaliperScoreOverlay[] ScoreOverlays { get; set; } = [];

        public Point[] EdgePoints { get; set; } = [];

        public double AverageScore { get; set; }

        public double ResidualRms { get; set; }

        public double ResidualMax { get; set; }

        public int ValidCaliperCount { get; set; }

        public double Confidence { get; set; }

        public static SingleEdgeCaliperDetectionDisplayState Empty => new();

        public static SingleEdgeCaliperDetectionDisplayState Capture(ISingleEdgeCaliperDetectionDisplayStateOwner roi)
        {
            return new SingleEdgeCaliperDetectionDisplayState
            {
                HasDetection = roi.HasDetection,
                RegionSegments = [.. roi.RegionSegments],
                CaliperBars = [.. roi.CaliperBars],
                InvalidCaliperMarkers = [.. roi.InvalidCaliperMarkers],
                EdgeMarkers = [.. roi.EdgeMarkers],
                RejectedEdgeMarkers = [.. roi.RejectedEdgeMarkers],
                ScoreOverlays = [.. roi.ScoreOverlays],
                EdgePoints = [.. roi.EdgePoints],
                AverageScore = roi.AverageScore,
                ResidualRms = roi.ResidualRms,
                ResidualMax = roi.ResidualMax,
                ValidCaliperCount = roi.ValidCaliperCount,
                Confidence = roi.Confidence
            };
        }

        public static SingleEdgeCaliperDetectionDisplayState CreateDetected(
            LineSegmentOverlay[] regionSegments,
            LineSegmentOverlay[] caliperBars,
            LineSegmentOverlay[] invalidCaliperMarkers,
            LineSegmentOverlay[] edgeMarkers,
            LineSegmentOverlay[] rejectedEdgeMarkers,
            CaliperScoreOverlay[] scoreOverlays,
            Point[] edgePoints)
        {
            return new SingleEdgeCaliperDetectionDisplayState
            {
                HasDetection = true,
                RegionSegments = [.. regionSegments],
                CaliperBars = [.. caliperBars],
                InvalidCaliperMarkers = [.. invalidCaliperMarkers],
                EdgeMarkers = [.. edgeMarkers],
                RejectedEdgeMarkers = [.. rejectedEdgeMarkers],
                ScoreOverlays = [.. scoreOverlays],
                EdgePoints = [.. edgePoints]
            };
        }

        public static SingleEdgeCaliperDetectionDisplayState CreateDetected(ISingleEdgeCaliperDetectionResult detectionResult)
        {
            ArgumentNullException.ThrowIfNull(detectionResult);

            return new SingleEdgeCaliperDetectionDisplayState
            {
                HasDetection = true,
                RegionSegments = [.. detectionResult.RegionSegments],
                CaliperBars = [.. detectionResult.CaliperBars],
                InvalidCaliperMarkers = [.. detectionResult.InvalidCaliperMarkers],
                EdgeMarkers = [.. detectionResult.EdgeMarkers],
                RejectedEdgeMarkers = [.. detectionResult.RejectedEdgeMarkers],
                ScoreOverlays = [.. detectionResult.ScoreOverlays],
                EdgePoints = [.. detectionResult.EdgePoints],
                AverageScore = detectionResult.AverageScore,
                ResidualRms = detectionResult.ResidualRms,
                ResidualMax = detectionResult.ResidualMax,
                ValidCaliperCount = detectionResult.ValidCaliperCount,
                Confidence = detectionResult.Confidence
            };
        }

        public SingleEdgeCaliperDetectionDisplayState Clone()
        {
            return Capture(new DetectionDisplayStateAdapter(this));
        }

        public void ApplyTo(ISingleEdgeCaliperDetectionDisplayStateOwner roi)
        {
            roi.HasDetection = HasDetection;
            roi.RegionSegments = [.. RegionSegments];
            roi.CaliperBars = [.. CaliperBars];
            roi.InvalidCaliperMarkers = [.. InvalidCaliperMarkers];
            roi.EdgeMarkers = [.. EdgeMarkers];
            roi.RejectedEdgeMarkers = [.. RejectedEdgeMarkers];
            roi.ScoreOverlays = [.. ScoreOverlays];
            roi.EdgePoints = [.. EdgePoints];
            roi.AverageScore = AverageScore;
            roi.ResidualRms = ResidualRms;
            roi.ResidualMax = ResidualMax;
            roi.ValidCaliperCount = ValidCaliperCount;
            roi.Confidence = Confidence;
        }

        private sealed class DetectionDisplayStateAdapter : ISingleEdgeCaliperDetectionDisplayStateOwner
        {
            private readonly SingleEdgeCaliperDetectionDisplayState _state;

            public DetectionDisplayStateAdapter(SingleEdgeCaliperDetectionDisplayState state)
            {
                _state = state;
            }

            public bool HasDetection { get => _state.HasDetection; set => _state.HasDetection = value; }
            public LineSegmentOverlay[] RegionSegments { get => _state.RegionSegments; set => _state.RegionSegments = value; }
            public LineSegmentOverlay[] CaliperBars { get => _state.CaliperBars; set => _state.CaliperBars = value; }
            public LineSegmentOverlay[] InvalidCaliperMarkers { get => _state.InvalidCaliperMarkers; set => _state.InvalidCaliperMarkers = value; }
            public LineSegmentOverlay[] EdgeMarkers { get => _state.EdgeMarkers; set => _state.EdgeMarkers = value; }
            public LineSegmentOverlay[] RejectedEdgeMarkers { get => _state.RejectedEdgeMarkers; set => _state.RejectedEdgeMarkers = value; }
            public CaliperScoreOverlay[] ScoreOverlays { get => _state.ScoreOverlays; set => _state.ScoreOverlays = value; }
            public Point[] EdgePoints { get => _state.EdgePoints; set => _state.EdgePoints = value; }
            public double AverageScore { get => _state.AverageScore; set => _state.AverageScore = value; }
            public double ResidualRms { get => _state.ResidualRms; set => _state.ResidualRms = value; }
            public double ResidualMax { get => _state.ResidualMax; set => _state.ResidualMax = value; }
            public int ValidCaliperCount { get => _state.ValidCaliperCount; set => _state.ValidCaliperCount = value; }
            public double Confidence { get => _state.Confidence; set => _state.Confidence = value; }
        }
    }
}
