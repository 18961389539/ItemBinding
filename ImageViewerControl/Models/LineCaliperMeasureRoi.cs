using System;
using System.Runtime.CompilerServices;
using System.Windows;

namespace ImageViewer.Models
{
    public class LineCaliperMeasureRoi : LineMeasureRoi, ISingleEdgeCaliperRoi, ISingleEdgeCaliperDetectionDisplayStateOwner, ICaliperInfoSource
    {
        private int _caliperCount = 16;
        private int _caliperSearchRange = 18;
        private int _caliperSamplingHalfWidth = 2;
        private double _caliperMinimumGradient = 8;
        private int _minimumValidCalipers = 8;
        private double _caliperOutlierThreshold = 2.5;
        private CaliperEdgePolarity _caliperEdgePolarity = CaliperEdgePolarity.Any;
        private double _angleDegrees;

        private SingleEdgeCaliperDetectionDisplayState DetectionDisplayState => SingleEdgeCaliperDetectionDisplayStateStore.GetOrCreate(this);

        public override string RoiTypeName => nameof(LineCaliperMeasureRoi);

        public bool HasDetectedLine
        {
            get => DetectionDisplayState.HasDetection;
            set => SetDetectionDisplayStateValue(DetectionDisplayState.HasDetection, value, static (state, v) => state.HasDetection = v);
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

        public LineSegmentOverlay[] EdgeMarkers
        {
            get => DetectionDisplayState.EdgeMarkers;
            set => SetDetectionDisplayStateValue(DetectionDisplayState.EdgeMarkers, value, static (state, v) => state.EdgeMarkers = v);
        }

        public LineSegmentOverlay[] RejectedEdgeMarkers
        {
            get => DetectionDisplayState.RejectedEdgeMarkers;
            set => SetDetectionDisplayStateValue(DetectionDisplayState.RejectedEdgeMarkers, value, static (state, v) => state.RejectedEdgeMarkers = v);
        }

        public CaliperScoreOverlay[] ScoreOverlays
        {
            get => DetectionDisplayState.ScoreOverlays;
            set => SetDetectionDisplayStateValue(DetectionDisplayState.ScoreOverlays, value, static (state, v) => state.ScoreOverlays = v);
        }

        public Point[] EdgePoints
        {
            get => DetectionDisplayState.EdgePoints;
            set => SetDetectionDisplayStateValue(DetectionDisplayState.EdgePoints, value, static (state, v) => state.EdgePoints = v);
        }

        public int CaliperCount
        {
            get => _caliperCount;
            set => SetProperty(ref _caliperCount, Math.Max(6, value));
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

        public double CaliperMinimumGradient
        {
            get => _caliperMinimumGradient;
            set => SetProperty(ref _caliperMinimumGradient, Math.Max(0, value));
        }

        public int MinimumValidCalipers
        {
            get => _minimumValidCalipers;
            set => SetProperty(ref _minimumValidCalipers, Math.Max(3, value));
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

        public double AverageScore
        {
            get => DetectionDisplayState.AverageScore;
            set => SetDetectionDisplayStateValue(DetectionDisplayState.AverageScore, value, static (state, v) => state.AverageScore = v);
        }

        public double ResidualRms
        {
            get => DetectionDisplayState.ResidualRms;
            set => SetDetectionDisplayStateValue(DetectionDisplayState.ResidualRms, value, static (state, v) => state.ResidualRms = v);
        }

        public double ResidualMax
        {
            get => DetectionDisplayState.ResidualMax;
            set => SetDetectionDisplayStateValue(DetectionDisplayState.ResidualMax, value, static (state, v) => state.ResidualMax = v);
        }

        public int ValidCaliperCount
        {
            get => DetectionDisplayState.ValidCaliperCount;
            set => SetDetectionDisplayStateValue(DetectionDisplayState.ValidCaliperCount, Math.Max(0, value), static (state, v) => state.ValidCaliperCount = v);
        }

        public double AngleDegrees
        {
            get => _angleDegrees;
            set => SetProperty(ref _angleDegrees, value);
        }

        public double Confidence
        {
            get => DetectionDisplayState.Confidence;
            set => SetDetectionDisplayStateValue(DetectionDisplayState.Confidence, Math.Clamp(value, 0, 1), static (state, v) => state.Confidence = v);
        }

        public void ClearDetectedLine()
        {
            this.ClearDetection();
            AngleDegrees = 0;
        }

        public void SetCaliperVisualization(
            LineSegmentOverlay[] regionSegments,
            LineSegmentOverlay[] caliperBars,
            LineSegmentOverlay[] invalidCaliperMarkers,
            LineSegmentOverlay[] edgeMarkers,
            LineSegmentOverlay[] rejectedEdgeMarkers,
            CaliperScoreOverlay[] scoreOverlays,
            Point[] edgePoints)
        {
            this.SetDetectionVisualization(
                SingleEdgeCaliperDetectionDisplayState.CreateDetected(
                    regionSegments,
                    caliperBars,
                    invalidCaliperMarkers,
                    edgeMarkers,
                    rejectedEdgeMarkers,
                    scoreOverlays,
                    edgePoints));
        }

        public override RoiBase Clone()
        {
            var clone = new LineCaliperMeasureRoi();
            clone.ApplyFrom(this);
            return clone;
        }

        public override void ApplyFrom(RoiBase source)
        {
            if (source is not LineCaliperMeasureRoi lineCaliper)
            {
                throw new ArgumentException($"Cannot apply state from {source.GetType().Name} to {nameof(LineCaliperMeasureRoi)}.", nameof(source));
            }

            base.ApplyFrom(source);
            this.CopyStateFrom(lineCaliper);
            AngleDegrees = lineCaliper.AngleDegrees;
        }

        bool ISingleEdgeCaliperDetectionDisplayStateOwner.HasDetection
        {
            get => SingleEdgeCaliperDetectionStateHelper.GetHasDetection(this);
            set => SingleEdgeCaliperDetectionStateHelper.SetHasDetection(this, value);
        }

        bool ICaliperInfoSource.HasDetection => SingleEdgeCaliperDetectionStateHelper.GetHasDetection(this);

        private void SetDetectionDisplayStateValue<T>(T currentValue, T newValue, Action<SingleEdgeCaliperDetectionDisplayState, T> assign, [CallerMemberName] string? propertyName = null)
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
