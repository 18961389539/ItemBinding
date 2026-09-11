using System.Windows;

namespace ImageViewer.Models
{
    public readonly record struct DetectedLineSegment(Point Start, Point End);
}
