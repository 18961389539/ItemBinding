using System;
using System.Windows;
using ImageViewer.Common;

namespace ImageViewer.Models
{
    public class PerpendicularityMeasureRoi : RoiBase
    {
        private Point _line1P1;
        private Point _line1P2;
        private Point _line2P1;
        private Point _line2P2;

        public PerpendicularityMeasureRoi()
        {
            StrokeColor = System.Windows.Media.Colors.Orchid;
        }

        public override string RoiTypeName => "Perpendicularity";

        public Point Line1P1
        {
            get => _line1P1;
            set => SetProperty(ref _line1P1, value);
        }

        public Point Line1P2
        {
            get => _line1P2;
            set => SetProperty(ref _line1P2, value);
        }

        public Point Line2P1
        {
            get => _line2P1;
            set => SetProperty(ref _line2P1, value);
        }

        public Point Line2P2
        {
            get => _line2P2;
            set => SetProperty(ref _line2P2, value);
        }

        public double Line1Angle
        {
            get
            {
                double dx = Line1P2.X - Line1P1.X;
                double dy = Line1P2.Y - Line1P1.Y;
                return Math.Atan2(dy, dx) * 180.0 / Math.PI;
            }
        }

        public double Line2Angle
        {
            get
            {
                double dx = Line2P2.X - Line2P1.X;
                double dy = Line2P2.Y - Line2P1.Y;
                return Math.Atan2(dy, dx) * 180.0 / Math.PI;
            }
        }

        public double AngleDifference
        {
            get
            {
                double diff = Math.Abs(Line1Angle - Line2Angle);
                diff = diff % 180;
                if (diff > 90) diff = 180 - diff;
                return diff;
            }
        }

        public double PerpendicularityError => Math.Abs(90 - AngleDifference);

        public bool IsPerpendicular => PerpendicularityError < 1.0;

        public double AngleBetweenLines => AngleDifference;

        public Point? IntersectionPoint
        {
            get
            {
                double x1 = Line1P1.X, y1 = Line1P1.Y;
                double x2 = Line1P2.X, y2 = Line1P2.Y;
                double x3 = Line2P1.X, y3 = Line2P1.Y;
                double x4 = Line2P2.X, y4 = Line2P2.Y;

                double denom = (x1 - x2) * (y3 - y4) - (y1 - y2) * (x3 - x4);
                if (Math.Abs(denom) < 1e-10)
                {
                    return null;
                }

                double t = ((x1 - x3) * (y3 - y4) - (y1 - y3) * (x3 - x4)) / denom;

                double ix = x1 + t * (x2 - x1);
                double iy = y1 + t * (y2 - y1);

                return new Point(ix, iy);
            }
        }

        public override RoiBase Clone()
        {
            return new PerpendicularityMeasureRoi
            {
                Line1P1 = Line1P1,
                Line1P2 = Line1P2,
                Line2P1 = Line2P1,
                Line2P2 = Line2P2,
                Label = Label,
                StrokeColor = StrokeColor,
                StrokeThickness = StrokeThickness,
                IsVisible = IsVisible,
                IsLocked = IsLocked
            };
        }

        public override void ApplyFrom(RoiBase source)
        {
            if (source is not PerpendicularityMeasureRoi roi)
            {
                throw new ArgumentException($"Cannot apply state from {source.GetType().Name} to {nameof(PerpendicularityMeasureRoi)}.", nameof(source));
            }

            Line1P1 = roi.Line1P1;
            Line1P2 = roi.Line1P2;
            Line2P1 = roi.Line2P1;
            Line2P2 = roi.Line2P2;
            ApplyCommonState(roi);
        }
    }
}
