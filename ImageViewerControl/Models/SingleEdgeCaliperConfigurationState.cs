namespace ImageViewer.Models
{
    internal sealed record SingleEdgeCaliperConfigurationState(
        int CaliperCount,
        int CaliperSearchRange,
        int CaliperSamplingHalfWidth,
        double CaliperMinimumGradient,
        int MinimumValidCalipers,
        double CaliperOutlierThreshold,
        CaliperEdgePolarity CaliperEdgePolarity)
    {
        public static SingleEdgeCaliperConfigurationState Capture(ISingleEdgeCaliperRoi roi)
        {
            return new SingleEdgeCaliperConfigurationState(
                roi.CaliperCount,
                roi.CaliperSearchRange,
                roi.CaliperSamplingHalfWidth,
                roi.CaliperMinimumGradient,
                roi.MinimumValidCalipers,
                roi.CaliperOutlierThreshold,
                roi.CaliperEdgePolarity);
        }

        public void ApplyTo(ISingleEdgeCaliperRoi roi)
        {
            roi.CaliperCount = CaliperCount;
            roi.CaliperSearchRange = CaliperSearchRange;
            roi.CaliperSamplingHalfWidth = CaliperSamplingHalfWidth;
            roi.CaliperMinimumGradient = CaliperMinimumGradient;
            roi.MinimumValidCalipers = MinimumValidCalipers;
            roi.CaliperOutlierThreshold = CaliperOutlierThreshold;
            roi.CaliperEdgePolarity = CaliperEdgePolarity;
        }
    }
}
