using System;
using System.Windows;
using ImageViewer.Common;

namespace ImageViewer.Models
{
    public class PointToLineDistanceRoi : RoiBase
    {
        private Point _point;
        private Point _lineP1;
        private Point _lineP2;

        public PointToLineDistanceRoi()
        {
            StrokeColor = System.Windows.Media.Colors.LimeGreen;
        }

        public override string RoiTypeName => "PointToLineDistance";

        public Point Point
        {
            get => _point;
            set => SetProperty(ref _point, value);
        }

        public Point LineP1
        {
            get => _lineP1;
            set => SetProperty(ref _lineP1, value);
        }

        public Point LineP2
        {
            get => _lineP2;
            set => SetProperty(ref _lineP2, value);
        }

        public double Distance
        {
            get
            {
                double dx = LineP2.X - LineP1.X;
                double dy = LineP2.Y - LineP1.Y;
                double lineLengthSquared = dx * dx + dy * dy;

                if (lineLengthSquared < 1e-10)
                {
                    return Math.Sqrt((Point.X - LineP1.X) * (Point.X - LineP1.X) +
                                     (Point.Y - LineP1.Y) * (Point.Y - LineP1.Y));
                }

                double t = ((Point.X - LineP1.X) * dx + (Point.Y - LineP1.Y) * dy) / lineLengthSquared;
                t = Math.Max(0, Math.Min(1, t));

                double projX = LineP1.X + t * dx;
                double projY = LineP1.Y + t * dy;

                return Math.Sqrt((Point.X - projX) * (Point.X - projX) +
                                 (Point.Y - projY) * (Point.Y - projY));
            }
        }

        public Point FootPoint
        {
            get
            {
                double dx = LineP2.X - LineP1.X;
                double dy = LineP2.Y - LineP1.Y;
                double lineLengthSquared = dx * dx + dy * dy;

                if (lineLengthSquared < 1e-10)
                {
                    return LineP1;
                }

                double t = ((Point.X - LineP1.X) * dx + (Point.Y - LineP1.Y) * dy) / lineLengthSquared;
                t = Math.Max(0, Math.Min(1, t));

                return new Point(LineP1.X + t * dx, LineP1.Y + t * dy);
            }
        }

        public override RoiBase Clone()
        {
            return new PointToLineDistanceRoi
            {
                Point = Point,
                LineP1 = LineP1,
                LineP2 = LineP2,
                Label = Label,
                StrokeColor = StrokeColor,
                StrokeThickness = StrokeThickness,
                IsVisible = IsVisible,
                IsLocked = IsLocked
            };
        }

        public override void ApplyFrom(RoiBase source)
        {
            if (source is not PointToLineDistanceRoi roi)
            {
                throw new ArgumentException($"Cannot apply state from {source.GetType().Name} to {nameof(PointToLineDistanceRoi)}.", nameof(source));
            }

            Point = roi.Point;
            LineP1 = roi.LineP1;
            LineP2 = roi.LineP2;
            ApplyCommonState(roi);
        }
    }
}
