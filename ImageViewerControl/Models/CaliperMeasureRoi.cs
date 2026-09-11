using System;
using System.Runtime.CompilerServices;
using System.Windows;
using ImageViewer.Utils;

namespace ImageViewer.Models
{
    public class CaliperMeasureRoi : LineMeasureRoi, ICaliperInfoSource
    {
        private Point _caliperCenter;
        private double _caliperAngleDegrees;
        private bool _hasExplicitCaliperRegion;
        private int _caliperCount = 9;
        private int _caliperSearchRange = 24;
        private int _caliperSamplingHalfWidth = 3;
        private double _caliperRegionLength;
        private double _caliperMinimumGradient = 8;
        private int _minimumValidCalipers = 4;
        private double _caliperOutlierThreshold = 2.5;
        private CaliperEdgePolarity _caliperEdgePolarity = CaliperEdgePolarity.Any;

        private DualEdgeCaliperDetectionDisplayState DetectionDisplayState => DualEdgeCaliperDetectionDisplayStateStore.GetOrCreate(this);

        public override string RoiTypeName => nameof(CaliperMeasureRoi);

        public Point CaliperCenter
        {
            get => _caliperCenter;
            set => SetProperty(ref _caliperCenter, value);
        }

        public double CaliperAngleDegrees
        {
            get => _caliperAngleDegrees;
            set => SetProperty(ref _caliperAngleDegrees, value);
        }

        public bool HasExplicitCaliperRegion
        {
            get => _hasExplicitCaliperRegion;
            set => SetProperty(ref _hasExplicitCaliperRegion, value);
        }

        public bool HasDetectedEdges
        {
            get => DetectionDisplayState.HasDetection;
            set => SetDetectionDisplayStateValue(DetectionDisplayState.HasDetection, value, static (state, v) => state.HasDetection = v);
        }

        public Point Edge1Start
        {
            get => DetectionDisplayState.Edge1Start;
            set => SetDetectionDisplayStateValue(DetectionDisplayState.Edge1Start, value, static (state, v) => state.Edge1Start = v);
        }

        public Point Edge1End
        {
            get => DetectionDisplayState.Edge1End;
            set => SetDetectionDisplayStateValue(DetectionDisplayState.Edge1End, value, static (state, v) => state.Edge1End = v);
        }

        public Point Edge2Start
        {
            get => DetectionDisplayState.Edge2Start;
            set => SetDetectionDisplayStateValue(DetectionDisplayState.Edge2Start, value, static (state, v) => state.Edge2Start = v);
        }

        public Point Edge2End
        {
            get => DetectionDisplayState.Edge2End;
            set => SetDetectionDisplayStateValue(DetectionDisplayState.Edge2End, value, static (state, v) => state.Edge2End = v);
        }

        public LineSegmentOverlay[] RegionSegments
        {
            get => DetectionDisplayState.RegionSegments;
            set => SetDetectionDisplayStateValue(DetectionDisplayState.RegionSegments, value, static (state, v) => state.RegionSegments = v);
        }

        public LineSegmentOverlay[] CaliperBars
        {
            get => DetectionDisplayState.CaliperBars;
            set => SetDetectionDisplayStateValue(DetectionDisplayState.CaliperBars, value, static (state, v) => state.CaliperBars = v);
        }

        public LineSegmentOverlay[] InvalidCaliperMarkers
        {
            get => DetectionDisplayState.InvalidCaliperMarkers;
            set => SetDetectionDisplayStateValue(DetectionDisplayState.InvalidCaliperMarkers, value, static (state, v) => state.InvalidCaliperMarkers = v);
        }

        public LineSegmentOverlay[] Edge1Markers
        {
            get => DetectionDisplayState.Edge1Markers;
            set => SetDetectionDisplayStateValue(DetectionDisplayState.Edge1Markers, value, static (state, v) => state.Edge1Markers = v);
        }

        public LineSegmentOverlay[] Edge2Markers
        {
            get => DetectionDisplayState.Edge2Markers;
            set => SetDetectionDisplayStateValue(DetectionDisplayState.Edge2Markers, value, static (state, v) => state.Edge2Markers = v);
        }

        public LineSegmentOverlay[] RejectedEdge1Markers
        {
            get => DetectionDisplayState.RejectedEdge1Markers;
            set => SetDetectionDisplayStateValue(DetectionDisplayState.RejectedEdge1Markers, value, static (state, v) => state.RejectedEdge1Markers = v);
        }

        public LineSegmentOverlay[] RejectedEdge2Markers
        {
            get => DetectionDisplayState.RejectedEdge2Markers;
            set => SetDetectionDisplayStateValue(DetectionDisplayState.RejectedEdge2Markers, value, static (state, v) => state.RejectedEdge2Markers = v);
        }

        public CaliperScoreOverlay[] ScoreOverlays
        {
            get => DetectionDisplayState.ScoreOverlays;
            set => SetDetectionDisplayStateValue(DetectionDisplayState.ScoreOverlays, value, static (state, v) => state.ScoreOverlays = v);
        }

        public Point[] Edge1Points
        {
            get => DetectionDisplayState.Edge1Points;
            set => SetDetectionDisplayStateValue(DetectionDisplayState.Edge1Points, value, static (state, v) => state.Edge1Points = v);
        }

        public Point[] Edge2Points
        {
            get => DetectionDisplayState.Edge2Points;
            set => SetDetectionDisplayStateValue(DetectionDisplayState.Edge2Points, value, static (state, v) => state.Edge2Points = v);
        }

        public int CaliperCount
        {
            get => _caliperCount;
            set => SetProperty(ref _caliperCount, Math.Max(3, value));
        }

        public int CaliperSearchRange
        {
            get => _caliperSearchRange;
            set => SetProperty(ref _caliperSearchRange, Math.Max(1, value));
        }

        public int CaliperSamplingHalfWidth
        {
            get => _caliperSamplingHalfWidth;
            set => SetProperty(ref _caliperSamplingHalfWidth, Math.Max(0, value));
        }

        public double CaliperRegionLength
        {
            get => _caliperRegionLength;
            set => SetProperty(ref _caliperRegionLength, Math.Max(0, value));
        }

        public double CaliperMinimumGradient
        {
            get => _caliperMinimumGradient;
            set => SetProperty(ref _caliperMinimumGradient, Math.Max(0, value));
        }

        public int MinimumValidCalipers
        {
            get => _minimumValidCalipers;
            set => SetProperty(ref _minimumValidCalipers, Math.Max(2, value));
        }

        public double CaliperOutlierThreshold
        {
            get => _caliperOutlierThreshold;
            set => SetProperty(ref _caliperOutlierThreshold, Math.Max(0, value));
        }

        public CaliperEdgePolarity CaliperEdgePolarity
        {
            get => _caliperEdgePolarity;
            set => SetProperty(ref _caliperEdgePolarity, value);
        }

        public double Edge1AverageScore
        {
            get => DetectionDisplayState.Edge1AverageScore;
            set => SetDetectionDisplayStateValue(DetectionDisplayState.Edge1AverageScore, value, static (state, v) => state.Edge1AverageScore = v);
        }

        public double Edge2AverageScore
        {
            get => DetectionDisplayState.Edge2AverageScore;
            set => SetDetectionDisplayStateValue(DetectionDisplayState.Edge2AverageScore, value, static (state, v) => state.Edge2AverageScore = v);
        }

        public double Edge1ResidualRms
        {
            get => DetectionDisplayState.Edge1ResidualRms;
            set => SetDetectionDisplayStateValue(DetectionDisplayState.Edge1ResidualRms, value, static (state, v) => state.Edge1ResidualRms = v);
        }

        public double Edge2ResidualRms
        {
            get => DetectionDisplayState.Edge2ResidualRms;
            set => SetDetectionDisplayStateValue(DetectionDisplayState.Edge2ResidualRms, value, static (state, v) => state.Edge2ResidualRms = v);
        }

        public double Edge1ResidualMax
        {
            get => DetectionDisplayState.Edge1ResidualMax;
            set => SetDetectionDisplayStateValue(DetectionDisplayState.Edge1ResidualMax, value, static (state, v) => state.Edge1ResidualMax = v);
        }

        public double Edge2ResidualMax
        {
            get => DetectionDisplayState.Edge2ResidualMax;
            set => SetDetectionDisplayStateValue(DetectionDisplayState.Edge2ResidualMax, value, static (state, v) => state.Edge2ResidualMax = v);
        }

        public int ValidCaliperCount
        {
            get => DetectionDisplayState.ValidCaliperCount;
            set => SetDetectionDisplayStateValue(DetectionDisplayState.ValidCaliperCount, Math.Max(0, value), static (state, v) => state.ValidCaliperCount = v);
        }

        public double Edge1AngleDegrees
        {
            get => DetectionDisplayState.Edge1AngleDegrees;
            set => SetDetectionDisplayStateValue(DetectionDisplayState.Edge1AngleDegrees, value, static (state, v) => state.Edge1AngleDegrees = v);
        }

        public double Edge2AngleDegrees
        {
            get => DetectionDisplayState.Edge2AngleDegrees;
            set => SetDetectionDisplayStateValue(DetectionDisplayState.Edge2AngleDegrees, value, static (state, v) => state.Edge2AngleDegrees = v);
        }

        public double ParallelismErrorDegrees
        {
            get => DetectionDisplayState.ParallelismErrorDegrees;
            set => SetDetectionDisplayStateValue(DetectionDisplayState.ParallelismErrorDegrees, value, static (state, v) => state.ParallelismErrorDegrees = v);
        }

        public double Confidence
        {
            get => DetectionDisplayState.Confidence;
            set => SetDetectionDisplayStateValue(DetectionDisplayState.Confidence, Math.Clamp(value, 0, 1), static (state, v) => state.Confidence = v);
        }

        public void SetDetectedEdges(Point edge1Start, Point edge1End, Point edge2Start, Point edge2End)
        {
            Edge1Start = edge1Start;
            Edge1End = edge1End;
            Edge2Start = edge2Start;
            Edge2End = edge2End;
            HasDetectedEdges = true;
        }

        public void ClearDetectedEdges()
        {
            this.ClearDetection();
        }

        public void SyncCaliperRegionFromMeasurementLine(bool updateSearchRange = true)
        {
            Vector measurementVector = P2 - P1;
            double distance = measurementVector.Length;
            if (distance <= 0)
            {
                return;
            }

            CaliperCenter = new Point((P1.X + P2.X) / 2, (P1.Y + P2.Y) / 2);
            CaliperAngleDegrees = Math.Atan2(measurementVector.Y, measurementVector.X) * 180 / Math.PI;
            if (updateSearchRange)
            {
                CaliperSearchRange = Math.Max(1, (int)Math.Ceiling(distance / 2 + 4));
            }

            HasExplicitCaliperRegion = true;
        }

        public void EnsureCaliperRegion()
        {
            if (!HasExplicitCaliperRegion)
            {
                SyncCaliperRegionFromMeasurementLine();
            }
        }

        public void MoveCaliperRegion(double dx, double dy)
        {
            if (!HasExplicitCaliperRegion)
            {
                return;
            }

            CaliperCenter = new Point(CaliperCenter.X + dx, CaliperCenter.Y + dy);
        }

        public Vector GetCaliperMeasurementDirection()
        {
            double radians = CaliperAngleDegrees * Math.PI / 180.0;
            Vector direction = new(Math.Cos(radians), Math.Sin(radians));
            if (direction.LengthSquared < 1e-6)
            {
                direction = P2 - P1;
            }

            if (direction.LengthSquared < 1e-6)
            {
                direction = new Vector(0, 1);
            }

            direction.Normalize();
            return direction;
        }

        public double GetResolvedCaliperRegionLength()
        {
            return CaliperRegionLength > 0
                ? Math.Max(6, CaliperRegionLength)
                : Math.Max(10, Math.Min(48, GeometryUtils.Distance(P1, P2) * 0.1));
        }

        public void SetCaliperVisualization(
            LineSegmentOverlay[] regionSegments,
            LineSegmentOverlay[] caliperBars,
            LineSegmentOverlay[] invalidCaliperMarkers,
            LineSegmentOverlay[] edge1Markers,
            LineSegmentOverlay[] edge2Markers,
            LineSegmentOverlay[] rejectedEdge1Markers,
            LineSegmentOverlay[] rejectedEdge2Markers,
            CaliperScoreOverlay[] scoreOverlays,
            Point[] edge1Points,
            Point[] edge2Points)
        {
            RegionSegments = [..regionSegments];
            CaliperBars = [..caliperBars];
            InvalidCaliperMarkers = [..invalidCaliperMarkers];
            Edge1Markers = [..edge1Markers];
            Edge2Markers = [..edge2Markers];
            RejectedEdge1Markers = [..rejectedEdge1Markers];
            RejectedEdge2Markers = [..rejectedEdge2Markers];
            ScoreOverlays = [..scoreOverlays];
            Edge1Points = [..edge1Points];
            Edge2Points = [..edge2Points];
        }

        public override RoiBase Clone()
        {
            var clone = new CaliperMeasureRoi();
            clone.ApplyFrom(this);
            return clone;
        }

        public override void ApplyFrom(RoiBase source)
        {
            if (source is not CaliperMeasureRoi caliper)
            {
                throw new ArgumentException($"Cannot apply state from {source.GetType().Name} to {nameof(CaliperMeasureRoi)}.", nameof(source));
            }

            base.ApplyFrom(source);
            this.CopyStateFrom(caliper);
        }

        bool ICaliperInfoSource.HasDetection => HasDetectedEdges;

        double ICaliperInfoSource.AverageScore => (Edge1AverageScore + Edge2AverageScore) / 2;

        double ICaliperInfoSource.ResidualRms => Math.Max(Edge1ResidualRms, Edge2ResidualRms);

        double ICaliperInfoSource.ResidualMax => Math.Max(Edge1ResidualMax, Edge2ResidualMax);

        private void SetDetectionDisplayStateValue<T>(T currentValue, T newValue, Action<DualEdgeCaliperDetectionDisplayState, T> assign, [CallerMemberName] string? propertyName = null)
        {
            if (System.Collections.Generic.EqualityComparer<T>.Default.Equals(currentValue, newValue))
            {
                return;
            }

            assign(DetectionDisplayState, newValue);
            OnPropertyChanged(propertyName);
        }
    }
}
