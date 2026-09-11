using System.Windows;

namespace ImageViewer.Models
{
    public enum CaliperOverlayStatus
    {
        Valid,
        Rejected,
        Invalid
    }

    public readonly record struct CaliperScoreOverlay(Point Position, string Text, CaliperOverlayStatus Status);
}
