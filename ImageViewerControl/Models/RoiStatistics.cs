namespace ImageViewer.Models
{
    public sealed class RoiStatistics
    {
        public int PixelCount { get; init; }
        public double Mean { get; init; }
        public byte Min { get; init; }
        public byte Max { get; init; }
        public double StandardDeviation { get; init; }
    }
}
