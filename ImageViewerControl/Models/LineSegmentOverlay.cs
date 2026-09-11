using System.Windows;

namespace ImageViewer.Models
{
    public readonly record struct LineSegmentOverlay(Point Start, Point End);
}
