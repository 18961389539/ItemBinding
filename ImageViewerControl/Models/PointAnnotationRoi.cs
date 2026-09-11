using System.Windows;
using ImageViewer.Common;

namespace ImageViewer.Models
{
    public class PointAnnotationRoi : RoiBase
    {
        private Point _position;

        public PointAnnotationRoi()
        {
            StrokeColor = System.Windows.Media.Colors.DeepSkyBlue;
        }

        public Point Position
        {
            get => _position;
            set => SetProperty(ref _position, value);
        }

        public override RoiBase Clone()
        {
            return new PointAnnotationRoi
            {
                Position = Position,
                Label = Label,
                StrokeColor = StrokeColor,
                StrokeThickness = StrokeThickness,
                IsVisible = IsVisible,
                IsLocked = IsLocked
            };
        }

        public override void ApplyFrom(RoiBase source)
        {
            if (source is not PointAnnotationRoi point)
            {
                throw new ArgumentException($"Cannot apply state from {source.GetType().Name} to {nameof(PointAnnotationRoi)}.", nameof(source));
            }

            Position = point.Position;
            ApplyCommonState(point);
        }
    }
}
