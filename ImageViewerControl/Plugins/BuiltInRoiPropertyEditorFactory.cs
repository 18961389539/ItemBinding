using System;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Data;
using System.Windows.Media;
using ImageViewer.Localization;
using ImageViewer.Models;

namespace ImageViewer.Plugins
{
    internal static class BuiltInRoiPropertyEditorFactory
    {
        public static FrameworkElement? CreateEditor(RoiBase roi)
        {
            ArgumentNullException.ThrowIfNull(roi);

            return roi switch
            {
                BlobAnalysisRoi typed => CreateEditor(typed),
                RotatedRect typed => CreateEditor(typed),
                ArcCaliperMeasureRoi typed => CreateEditor(typed),
                EllipseRoi typed => CreateEditor(typed),
                CircleRoi typed => CreateEditor(typed),
                RingRoi typed => CreateEditor(typed),
                PolygonRoi typed => CreateEditor(typed),
                PolylineRoi typed => CreateEditor(typed),
                PointAnnotationRoi typed => CreateEditor(typed),
                TextAnnotationRoi typed => CreateEditor(typed),
                LineMeasureRoi typed => CreateEditor(typed),
                AngleMeasureRoi typed => CreateEditor(typed),
                ArcMeasureRoi typed => CreateEditor(typed),
                PointToLineDistanceRoi typed => CreateEditor(typed),
                PointToCircleDistanceRoi typed => CreateEditor(typed),
                ParallelismMeasureRoi typed => CreateEditor(typed),
                PerpendicularityMeasureRoi typed => CreateEditor(typed),
                ConcentricityMeasureRoi typed => CreateEditor(typed),
                _ => null
            };
        }

        public static FrameworkElement CreateEditor(RotatedRect roi) => CreatePanel(roi, nameof(RotatedRect.Width), nameof(RotatedRect.Height), nameof(RotatedRect.Angle), nameof(RotatedRect.Center));

        public static FrameworkElement CreateEditor(BlobAnalysisRoi roi)
        {
            StackPanel panel = CreatePanel(roi, nameof(RotatedRect.Width), nameof(RotatedRect.Height), nameof(RotatedRect.Angle), nameof(RotatedRect.Center));
            AddCheckBox(panel, UiText.Get("EditorUseOtsu"), nameof(BlobAnalysisRoi.UseOtsu));
            AddSlider(panel, UiText.Get("EditorManualThreshold"), nameof(BlobAnalysisRoi.ManualThreshold), 0, 255, true);
            AddCheckBox(panel, UiText.Get("EditorDetectDark"), nameof(BlobAnalysisRoi.DetectDark));
            AddSlider(panel, UiText.Get("EditorMinArea"), nameof(BlobAnalysisRoi.MinArea), 1, 10000, true);
            return panel;
        }

        public static FrameworkElement CreateEditor(EllipseRoi roi) => CreatePanel(roi, nameof(EllipseRoi.RadiusX), nameof(EllipseRoi.RadiusY), nameof(EllipseRoi.Angle), nameof(EllipseRoi.Center));

        public static FrameworkElement CreateEditor(CircleRoi roi) => CreatePanel(roi, nameof(CircleRoi.Radius), readOnlyPointPath: nameof(CircleRoi.Center));

        public static FrameworkElement CreateEditor(RingRoi roi)
        {
            var panel = CreateBasePanel(roi);
            AddSlider(panel, UiText.Get("EditorInnerRadius"), nameof(RingRoi.InnerRadius), 0, 500);
            AddSlider(panel, UiText.Get("EditorOuterRadius"), nameof(RingRoi.OuterRadius), 0, 500);
            AddReadOnlyText(panel, UiText.Get("EditorCenter"), nameof(RingRoi.Center));
            return panel;
        }

        public static FrameworkElement CreateEditor(ArcCaliperMeasureRoi roi)
        {
            var panel = CreateBasePanel(roi);
            AddSlider(panel, UiText.Get("EditorRadius"), nameof(ArcCaliperMeasureRoi.Radius), 1, 500);
            AddSlider(panel, UiText.Get("EditorStartAngle"), nameof(ArcCaliperMeasureRoi.StartAngle), -360, 360);
            AddSlider(panel, UiText.Get("EditorSweepAngle"), nameof(ArcCaliperMeasureRoi.SweepAngle), -360, 360);
            AddSlider(panel, UiText.Get("EditorCaliperCount"), nameof(ArcCaliperMeasureRoi.CaliperCount), 6, 128, true);
            AddSlider(panel, UiText.Get("EditorSearchRange"), nameof(ArcCaliperMeasureRoi.CaliperSearchRange), 1, 100, true);
            AddSlider(panel, UiText.Get("EditorSamplingHalfWidth"), nameof(ArcCaliperMeasureRoi.CaliperSamplingHalfWidth), 0, 20, true);
            AddSlider(panel, UiText.Get("EditorMinimumValidCalipers"), nameof(ArcCaliperMeasureRoi.MinimumValidCalipers), 3, 128, true);
            AddSlider(panel, UiText.Get("EditorMinimumGradient"), nameof(ArcCaliperMeasureRoi.CaliperMinimumGradient), 0, 255);
            AddSlider(panel, UiText.Get("EditorOutlierThreshold"), nameof(ArcCaliperMeasureRoi.CaliperOutlierThreshold), 0, 20);
            AddEnumComboBox<CaliperEdgePolarity>(panel, UiText.Get("EditorEdgePolarity"), nameof(ArcCaliperMeasureRoi.CaliperEdgePolarity));
            AddReadOnlyText(panel, UiText.Get("EditorCenter"), nameof(ArcCaliperMeasureRoi.Center));
            return panel;
        }

        public static FrameworkElement CreateEditor(PolygonRoi roi) => CreatePanel(roi, readOnlyText: UiText.FormatInvariant("EditorVertices", roi.Points.Count));

        public static FrameworkElement CreateEditor(PolylineRoi roi) => CreatePanel(roi, readOnlyText: UiText.FormatInvariant("EditorPoints", roi.Points.Count, roi.IsFreehand ? UiText.Get("CommonYes") : UiText.Get("CommonNo")));

        public static FrameworkElement CreateEditor(PointAnnotationRoi roi) => CreatePanel(roi, readOnlyPointPath: nameof(PointAnnotationRoi.Position));

        public static FrameworkElement CreateEditor(TextAnnotationRoi roi) => CreatePanel(roi, readOnlyPointPath: nameof(TextAnnotationRoi.Position), includeLabel: true);

        public static FrameworkElement CreateEditor(LineMeasureRoi roi) => CreatePanel(roi, readOnlyText: UiText.FormatInvariant("EditorLineMeasureDetails", roi.P1.X, roi.P1.Y, roi.P2.X, roi.P2.Y));

        public static FrameworkElement CreateEditor(AngleMeasureRoi roi) => CreatePanel(roi, readOnlyText: UiText.FormatInvariant("EditorAngleDetails", roi.P1.X, roi.P1.Y, roi.Vertex.X, roi.Vertex.Y, roi.P2.X, roi.P2.Y));

        public static FrameworkElement CreateEditor(ArcMeasureRoi roi)
        {
            string text = UiText.FormatInvariant("EditorArcDetailsBase", roi.StartPoint.X, roi.StartPoint.Y, roi.EndPoint.X, roi.EndPoint.Y, roi.ArcPoint.X, roi.ArcPoint.Y);
            if (roi.IsValid)
            {
                text += UiText.FormatInvariant("EditorArcDetailsRadius", roi.Radius);
                text += UiText.FormatInvariant("EditorArcDetailsLength", roi.ArcLength);
                text += UiText.FormatInvariant("EditorArcDetailsCentralAngle", roi.CentralAngle);
            }
            return CreatePanel(roi, readOnlyText: text);
        }

        public static FrameworkElement CreateEditor(PointToLineDistanceRoi roi)
        {
            return CreatePanel(roi, readOnlyText: UiText.FormatInvariant("EditorPointToLineDetails", roi.Point.X, roi.Point.Y, roi.LineP1.X, roi.LineP1.Y, roi.LineP2.X, roi.LineP2.Y, roi.Distance));
        }

        public static FrameworkElement CreateEditor(PointToCircleDistanceRoi roi)
        {
            return CreatePanel(roi, readOnlyText: UiText.FormatInvariant("EditorPointToCircleDetails", roi.Point.X, roi.Point.Y, roi.Center.X, roi.Center.Y, roi.Radius, roi.DistanceToCircle, roi.DistanceToCenter));
        }

        public static FrameworkElement CreateEditor(ParallelismMeasureRoi roi)
        {
            return CreatePanel(roi, readOnlyText: UiText.FormatInvariant("EditorParallelismDetails", roi.Line1P1.X, roi.Line1P1.Y, roi.Line1P2.X, roi.Line1P2.Y, roi.Line2P1.X, roi.Line2P1.Y, roi.Line2P2.X, roi.Line2P2.Y, roi.AngleDifference, roi.AverageDistance));
        }

        public static FrameworkElement CreateEditor(PerpendicularityMeasureRoi roi)
        {
            return CreatePanel(roi, readOnlyText: UiText.FormatInvariant("EditorPerpendicularityDetails", roi.Line1P1.X, roi.Line1P1.Y, roi.Line1P2.X, roi.Line1P2.Y, roi.Line2P1.X, roi.Line2P1.Y, roi.Line2P2.X, roi.Line2P2.Y, roi.AngleBetweenLines, roi.PerpendicularityError));
        }

        public static FrameworkElement CreateEditor(ConcentricityMeasureRoi roi)
        {
            return CreatePanel(roi, readOnlyText: UiText.FormatInvariant("EditorConcentricityDetails", roi.Center1.X, roi.Center1.Y, roi.Radius1, roi.Center2.X, roi.Center2.Y, roi.Radius2, roi.CenterDistance));
        }

        private static StackPanel CreateBasePanel(RoiBase roi, bool includeLabel = true)
        {
            var panel = new StackPanel
            {
                Margin = new Thickness(0),
                DataContext = roi,
                MinWidth = 260
            };

            panel.Children.Add(CreateHeader(roi.DisplayTypeName));
            if (includeLabel)
            {
                AddTextEditor(panel, UiText.Get("EditorLabel"), nameof(RoiBase.Label));
            }

            AddSlider(panel, UiText.Get("EditorStrokeThickness"), nameof(RoiBase.StrokeThickness), 1, 12);
            AddCheckBox(panel, UiText.Get("EditorVisible"), nameof(RoiBase.IsVisible));
            AddCheckBox(panel, UiText.Get("EditorLocked"), nameof(RoiBase.IsLocked));
            return panel;
        }

        private static StackPanel CreatePanel(RoiBase roi, string? metricPath1 = null, string? metricPath2 = null, string? metricPath3 = null, string? readOnlyPointPath = null, string? readOnlyText = null, bool includeLabel = true)
        {
            StackPanel panel = CreateBasePanel(roi, includeLabel);

            if (metricPath1 != null)
            {
                AddSlider(panel, GetPropertyLabel(metricPath1), metricPath1, 0, 500);
            }

            if (metricPath2 != null)
            {
                AddSlider(panel, GetPropertyLabel(metricPath2), metricPath2, 0, 500);
            }

            if (metricPath3 != null)
            {
                AddSlider(panel, GetPropertyLabel(metricPath3), metricPath3, -180, 180);
            }

            if (readOnlyPointPath != null)
            {
                AddReadOnlyText(panel, GetPropertyLabel(readOnlyPointPath), readOnlyPointPath);
            }

            if (readOnlyText != null)
            {
                panel.Children.Add(CreateLabel(UiText.Get("EditorDetailsLabel")));
                panel.Children.Add(new TextBlock
                {
                    Text = readOnlyText,
                    Foreground = Brushes.WhiteSmoke,
                    TextWrapping = TextWrapping.Wrap
                });
            }

            return panel;
        }

        private static string GetPropertyLabel(string propertyPath)
        {
            return propertyPath switch
            {
                nameof(RotatedRect.Width) => UiText.Get("EditorWidth"),
                nameof(RotatedRect.Height) => UiText.Get("EditorHeight"),
                nameof(RotatedRect.Angle) => UiText.Get("EditorAngle"),
                nameof(RotatedRect.Center) => UiText.Get("EditorCenter"),
                nameof(EllipseRoi.RadiusX) => UiText.Get("EditorRadiusX"),
                nameof(EllipseRoi.RadiusY) => UiText.Get("EditorRadiusY"),
                nameof(CircleRoi.Radius) => UiText.Get("EditorRadius"),
                nameof(PointAnnotationRoi.Position) => UiText.Get("EditorPosition"),
                _ => propertyPath
            };
        }

        private static TextBlock CreateHeader(string text) => new()
        {
            Text = text,
            FontWeight = FontWeights.Bold,
            Foreground = Brushes.White,
            Margin = new Thickness(0, 0, 0, 8)
        };

        private static TextBlock CreateLabel(string text) => new()
        {
            Text = text,
            Foreground = Brushes.White,
            Margin = new Thickness(0, 6, 0, 2)
        };

        private static void AddTextEditor(StackPanel panel, string header, string propertyPath)
        {
            panel.Children.Add(CreateLabel(header));
            var textBox = new TextBox();
            textBox.SetBinding(TextBox.TextProperty, CreateTwoWayBinding(propertyPath));
            panel.Children.Add(textBox);
        }

        private static void AddSlider(StackPanel panel, string header, string propertyPath, double minimum, double maximum, bool isSnapToTickEnabled = false)
        {
            panel.Children.Add(CreateLabel(header));
            var slider = new Slider
            {
                Minimum = minimum,
                Maximum = maximum,
                TickFrequency = 1,
                IsSnapToTickEnabled = isSnapToTickEnabled
            };
            slider.SetBinding(RangeBase.ValueProperty, CreateTwoWayBinding(propertyPath));
            panel.Children.Add(slider);
        }

        private static void AddEnumComboBox<TEnum>(StackPanel panel, string header, string propertyPath) where TEnum : struct, Enum
        {
            panel.Children.Add(CreateLabel(header));
            var comboBox = new ComboBox
            {
                ItemsSource = Enum.GetValues<TEnum>()
            };
            comboBox.SetBinding(Selector.SelectedItemProperty, CreateTwoWayBinding(propertyPath));
            panel.Children.Add(comboBox);
        }


        private static void AddCheckBox(StackPanel panel, string header, string propertyPath)
        {
            var checkBox = new CheckBox
            {
                Content = header,
                Foreground = Brushes.White,
                Margin = new Thickness(0, 6, 0, 0)
            };
            checkBox.SetBinding(ToggleButton.IsCheckedProperty, CreateTwoWayBinding(propertyPath));
            panel.Children.Add(checkBox);
        }

        private static void AddReadOnlyText(StackPanel panel, string header, string propertyPath)
        {
            panel.Children.Add(CreateLabel(header));
            var textBlock = new TextBlock
            {
                Foreground = Brushes.WhiteSmoke,
                TextWrapping = TextWrapping.Wrap
            };
            textBlock.SetBinding(TextBlock.TextProperty, new Binding(propertyPath));
            panel.Children.Add(textBlock);
        }

        private static Binding CreateTwoWayBinding(string propertyPath) => new(propertyPath)
        {
            Mode = BindingMode.TwoWay,
            UpdateSourceTrigger = UpdateSourceTrigger.PropertyChanged
        };
    }
}
