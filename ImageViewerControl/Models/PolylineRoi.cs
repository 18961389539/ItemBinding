using System.Collections.ObjectModel;
using System.Windows;
using ImageViewer.Common;

namespace ImageViewer.Models
{
    public class PolylineRoi : RoiBase
    {
        private ObservableCollection<Point> _points = new();
        private bool _isFreehand;

        public PolylineRoi()
        {
            StrokeColor = System.Windows.Media.Colors.LightGreen;
        }

        public ObservableCollection<Point> Points
        {
            get => _points;
            set => SetProperty(ref _points, value);
        }

        public bool IsFreehand
        {
            get => _isFreehand;
            set => SetProperty(ref _isFreehand, value);
        }

        public override RoiBase Clone()
        {
            return new PolylineRoi
            {
                Points = new ObservableCollection<Point>(Points),
                IsFreehand = IsFreehand,
                Label = Label,
                StrokeColor = StrokeColor,
                StrokeThickness = StrokeThickness,
                IsVisible = IsVisible,
                IsLocked = IsLocked
            };
        }

        public override void ApplyFrom(RoiBase source)
        {
            if (source is not PolylineRoi polyline)
            {
                throw new ArgumentException($"Cannot apply state from {source.GetType().Name} to {nameof(PolylineRoi)}.", nameof(source));
            }

            Points = new ObservableCollection<Point>(polyline.Points);
            IsFreehand = polyline.IsFreehand;
            ApplyCommonState(polyline);
        }
    }
}
