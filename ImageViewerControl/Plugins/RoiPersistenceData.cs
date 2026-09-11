using System.Collections.Generic;
using System.Text.Json.Serialization;

namespace ImageViewer.Plugins
{
    public sealed class RoiPersistenceData
    {
        public string Type { get; set; } = string.Empty;

        [JsonIgnore]
        public RoiPersistenceCommonData Common { get; } = new();

        [JsonIgnore]
        public RoiPersistenceGeometryData Geometry { get; } = new();

        [JsonIgnore]
        public RoiPersistenceMeasurementData Measurement { get; } = new();

        [JsonIgnore]
        public RoiPersistenceOptionsData Options { get; } = new();

        public string? Label
        {
            get => Common.Label;
            set => Common.Label = value;
        }

        public string StrokeColor
        {
            get => Common.StrokeColor;
            set => Common.StrokeColor = string.IsNullOrWhiteSpace(value) ? "#FF00FFFF" : value;
        }

        public double StrokeThickness
        {
            get => Common.StrokeThickness;
            set => Common.StrokeThickness = value;
        }

        public bool IsVisible
        {
            get => Common.IsVisible;
            set => Common.IsVisible = value;
        }

        public bool IsLocked
        {
            get => Common.IsLocked;
            set => Common.IsLocked = value;
        }

        public RoiPersistencePoint? Center
        {
            get => Geometry.Center;
            set => Geometry.Center = value;
        }

        public RoiPersistencePoint? Position
        {
            get => Geometry.Position;
            set => Geometry.Position = value;
        }

        public RoiPersistencePoint? P1
        {
            get => Geometry.P1;
            set => Geometry.P1 = value;
        }

        public RoiPersistencePoint? P2
        {
            get => Geometry.P2;
            set => Geometry.P2 = value;
        }

        public RoiPersistencePoint? P3
        {
            get => Geometry.P3;
            set => Geometry.P3 = value;
        }

        public RoiPersistencePoint? Vertex
        {
            get => Geometry.Vertex;
            set => Geometry.Vertex = value;
        }

        public List<RoiPersistencePoint>? Points
        {
            get => Geometry.Points;
            set => Geometry.Points = value;
        }

        public double Width
        {
            get => Geometry.Width;
            set => Geometry.Width = value;
        }

        public double Height
        {
            get => Geometry.Height;
            set => Geometry.Height = value;
        }

        public double RadiusX
        {
            get => Geometry.RadiusX;
            set => Geometry.RadiusX = value;
        }

        public double RadiusY
        {
            get => Geometry.RadiusY;
            set => Geometry.RadiusY = value;
        }

        public double Radius
        {
            get => Geometry.Radius;
            set => Geometry.Radius = value;
        }

        public double Radius2
        {
            get => Geometry.Radius2;
            set => Geometry.Radius2 = value;
        }

        public double Angle
        {
            get => Geometry.Angle;
            set => Geometry.Angle = value;
        }

        public int CaliperCount
        {
            get => Measurement.CaliperCount;
            set => Measurement.CaliperCount = value;
        }

        public int CaliperSearchRange
        {
            get => Measurement.CaliperSearchRange;
            set => Measurement.CaliperSearchRange = value;
        }

        public int CaliperSamplingHalfWidth
        {
            get => Measurement.CaliperSamplingHalfWidth;
            set => Measurement.CaliperSamplingHalfWidth = value;
        }

        public int MinimumValidCalipers
        {
            get => Measurement.MinimumValidCalipers;
            set => Measurement.MinimumValidCalipers = value;
        }

        public double CaliperMinimumGradient
        {
            get => Measurement.CaliperMinimumGradient;
            set => Measurement.CaliperMinimumGradient = value;
        }

        public double CaliperOutlierThreshold
        {
            get => Measurement.CaliperOutlierThreshold;
            set => Measurement.CaliperOutlierThreshold = value;
        }

        public string? CaliperEdgePolarity
        {
            get => Measurement.CaliperEdgePolarity;
            set => Measurement.CaliperEdgePolarity = value;
        }

        public bool IsClosed
        {
            get => Options.IsClosed;
            set => Options.IsClosed = value;
        }

        public bool IsFreehand
        {
            get => Options.IsFreehand;
            set => Options.IsFreehand = value;
        }

        public bool UseOtsu
        {
            get => Options.UseOtsu;
            set => Options.UseOtsu = value;
        }

        public int ManualThreshold
        {
            get => Options.ManualThreshold;
            set => Options.ManualThreshold = value;
        }

        public bool DetectDark
        {
            get => Options.DetectDark;
            set => Options.DetectDark = value;
        }

        public int MinArea
        {
            get => Options.MinArea;
            set => Options.MinArea = value;
        }
    }

    public sealed class RoiPersistenceCommonData
    {
        public string? Label { get; set; }
        public string StrokeColor { get; set; } = "#FF00FFFF";
        public double StrokeThickness { get; set; } = 2.0;
        public bool IsVisible { get; set; } = true;
        public bool IsLocked { get; set; }
    }

    public sealed class RoiPersistenceGeometryData
    {
        public RoiPersistencePoint? Center { get; set; }
        public RoiPersistencePoint? Position { get; set; }
        public RoiPersistencePoint? P1 { get; set; }
        public RoiPersistencePoint? P2 { get; set; }
        public RoiPersistencePoint? P3 { get; set; }
        public RoiPersistencePoint? Vertex { get; set; }
        public List<RoiPersistencePoint>? Points { get; set; }
        public double Width { get; set; }
        public double Height { get; set; }
        public double RadiusX { get; set; }
        public double RadiusY { get; set; }
        public double Radius { get; set; }
        public double Radius2 { get; set; }
        public double Angle { get; set; }
    }

    public sealed class RoiPersistenceMeasurementData
    {
        public int CaliperCount { get; set; }
        public int CaliperSearchRange { get; set; }
        public int CaliperSamplingHalfWidth { get; set; }
        public int MinimumValidCalipers { get; set; }
        public double CaliperMinimumGradient { get; set; }
        public double CaliperOutlierThreshold { get; set; }
        public string? CaliperEdgePolarity { get; set; }
    }

    public sealed class RoiPersistenceOptionsData
    {
        public bool IsClosed { get; set; }
        public bool IsFreehand { get; set; }
        public bool UseOtsu { get; set; }
        public int ManualThreshold { get; set; }
        public bool DetectDark { get; set; }
        public int MinArea { get; set; }
    }

    public sealed class RoiPersistencePoint
    {
        public double X { get; set; }
        public double Y { get; set; }
    }
}
