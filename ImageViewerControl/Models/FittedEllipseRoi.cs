using System;

namespace ImageViewer.Models
{
    public class FittedEllipseRoi : EllipseRoi
    {
        private int _sourcePointCount;

        public override string RoiTypeName => nameof(FittedEllipseRoi);

        public int SourcePointCount
        {
            get => _sourcePointCount;
            set => SetProperty(ref _sourcePointCount, Math.Max(0, value));
        }

        public override RoiBase Clone()
        {
            return new FittedEllipseRoi
            {
                Center = Center,
                RadiusX = RadiusX,
                RadiusY = RadiusY,
                Angle = Angle,
                SourcePointCount = SourcePointCount,
                Label = Label,
                StrokeColor = StrokeColor,
                StrokeThickness = StrokeThickness,
                IsVisible = IsVisible,
                IsLocked = IsLocked
            };
        }

        public override void ApplyFrom(RoiBase source)
        {
            if (source is not FittedEllipseRoi ellipse)
            {
                throw new ArgumentException($"Cannot apply state from {source.GetType().Name} to {nameof(FittedEllipseRoi)}.", nameof(source));
            }

            base.ApplyFrom(source);
            SourcePointCount = ellipse.SourcePointCount;
        }
    }
}
