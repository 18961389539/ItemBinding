using System.Windows;

namespace ImageViewer.Services
{
    public readonly record struct BlobFeature(
        int Label,
        int Area,
        Point Centroid,
        Rect BoundingBox
    );
}
