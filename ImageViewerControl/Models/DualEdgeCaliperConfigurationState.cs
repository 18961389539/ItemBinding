namespace ImageViewer.Models
{
    internal sealed record DualEdgeCaliperConfigurationState(
        System.Windows.Point CaliperCenter,
        double CaliperAngleDegrees,
        bool HasExplicitCaliperRegion,
        int CaliperCount,
        int CaliperSearchRange,
        int CaliperSamplingHalfWidth,
        double CaliperRegionLength,
        double CaliperMinimumGradient,
        int MinimumValidCalipers,
        double CaliperOutlierThreshold,
        CaliperEdgePolarity CaliperEdgePolarity)
    {
        public static DualEdgeCaliperConfigurationState Capture(CaliperMeasureRoi roi)
        {
            return new DualEdgeCaliperConfigurationState(
                roi.CaliperCenter,
                roi.CaliperAngleDegrees,
                roi.HasExplicitCaliperRegion,
                roi.CaliperCount,
                roi.CaliperSearchRange,
                roi.CaliperSamplingHalfWidth,
                roi.CaliperRegionLength,
                roi.CaliperMinimumGradient,
                roi.MinimumValidCalipers,
                roi.CaliperOutlierThreshold,
                roi.CaliperEdgePolarity);
        }

        public void ApplyTo(CaliperMeasureRoi roi)
        {
            roi.CaliperCenter = CaliperCenter;
            roi.CaliperAngleDegrees = CaliperAngleDegrees;
            roi.HasExplicitCaliperRegion = HasExplicitCaliperRegion;
            roi.CaliperCount = CaliperCount;
            roi.CaliperSearchRange = CaliperSearchRange;
            roi.CaliperSamplingHalfWidth = CaliperSamplingHalfWidth;
            roi.CaliperRegionLength = CaliperRegionLength;
            roi.CaliperMinimumGradient = CaliperMinimumGradient;
            roi.MinimumValidCalipers = MinimumValidCalipers;
            roi.CaliperOutlierThreshold = CaliperOutlierThreshold;
            roi.CaliperEdgePolarity = CaliperEdgePolarity;
        }
    }
}
