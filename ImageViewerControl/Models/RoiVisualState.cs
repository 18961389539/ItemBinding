using System.Windows.Media;

namespace ImageViewer.Models
{
    internal sealed class RoiVisualState
    {
        public Color StrokeColor { get; set; } = Colors.Cyan;

        public double StrokeThickness { get; set; } = 2.0;

        public bool IsSelected { get; set; }

        public bool IsVisible { get; set; } = true;

        public bool IsLocked { get; set; }

        public static RoiVisualState Capture(RoiBase roi)
        {
            ArgumentNullException.ThrowIfNull(roi);

            return new RoiVisualState
            {
                StrokeColor = roi.StrokeColor,
                StrokeThickness = roi.StrokeThickness,
                IsSelected = roi.IsSelected,
                IsVisible = roi.IsVisible,
                IsLocked = roi.IsLocked
            };
        }

        public void ApplyTo(RoiBase roi, bool includeSelection = true)
        {
            ArgumentNullException.ThrowIfNull(roi);

            roi.StrokeColor = StrokeColor;
            roi.StrokeThickness = StrokeThickness;
            roi.IsVisible = IsVisible;
            roi.IsLocked = IsLocked;

            if (includeSelection)
            {
                roi.IsSelected = IsSelected;
            }
        }
    }
}