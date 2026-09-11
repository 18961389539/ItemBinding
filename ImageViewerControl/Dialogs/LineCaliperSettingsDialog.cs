using System;
using System.Globalization;
using System.Windows;
using System.Windows.Controls;
using ImageViewer.Localization;
using ImageViewer.Models;

namespace ImageViewer.Dialogs
{
    internal sealed class LineCaliperSettingsDialog : Window
    {
        private readonly LineCaliperMeasureRoi _model;
        private readonly Action<LineCaliperMeasureRoi>? _previewAction;
        private readonly TextBox _caliperCountTextBox;
        private readonly TextBox _searchRangeTextBox;
        private readonly TextBox _samplingHalfWidthTextBox;
        private readonly TextBox _minimumGradientTextBox;
        private readonly TextBox _minimumValidCalipersTextBox;
        private readonly TextBox _outlierThresholdTextBox;
        private readonly ComboBox _polarityComboBox;
        private readonly CheckBox _livePreviewCheckBox;

        public LineCaliperSettingsDialog(LineCaliperMeasureRoi model, Action<LineCaliperMeasureRoi>? previewAction)
        {
            _model = model;
            _previewAction = previewAction;
            Title = UiText.Get("LineCaliperDialogTitle");
            Width = 360;
            Height = 450;
            WindowStartupLocation = WindowStartupLocation.CenterOwner;
            ResizeMode = ResizeMode.NoResize;

            var panel = new StackPanel { Margin = new Thickness(12) };
            panel.Children.Add(new TextBlock
            {
                Text = UiText.Get("LineCaliperDialogHeading"),
                FontWeight = FontWeights.Bold,
                Margin = new Thickness(0, 0, 0, 10)
            });

            var primaryPanel = new StackPanel();
            panel.Children.Add(primaryPanel);
            _caliperCountTextBox = AddField(primaryPanel, UiText.Get("CaliperFieldCount"), _model.CaliperCount.ToString(CultureInfo.InvariantCulture));
            _searchRangeTextBox = AddField(primaryPanel, UiText.Get("CaliperFieldSearchRange"), _model.CaliperSearchRange.ToString(CultureInfo.InvariantCulture));

            var advancedPanel = new StackPanel { Margin = new Thickness(0, 4, 0, 0) };
            _samplingHalfWidthTextBox = AddField(advancedPanel, UiText.Get("CaliperFieldSamplingHalfWidth"), _model.CaliperSamplingHalfWidth.ToString(CultureInfo.InvariantCulture));
            _minimumGradientTextBox = AddField(advancedPanel, UiText.Get("CaliperFieldMinimumGradient"), _model.CaliperMinimumGradient.ToString(CultureInfo.InvariantCulture));
            _minimumValidCalipersTextBox = AddField(advancedPanel, UiText.Get("CaliperFieldMinimumValid"), _model.MinimumValidCalipers.ToString(CultureInfo.InvariantCulture));
            _outlierThresholdTextBox = AddField(advancedPanel, UiText.Get("CaliperFieldOutlierThreshold"), _model.CaliperOutlierThreshold.ToString(CultureInfo.InvariantCulture));

            advancedPanel.Children.Add(new TextBlock { Text = UiText.Get("CaliperFieldPolarity"), Margin = new Thickness(0, 8, 0, 2) });
            _polarityComboBox = new ComboBox
            {
                ItemsSource = Enum.GetValues<CaliperEdgePolarity>(),
                SelectedItem = _model.CaliperEdgePolarity,
                Margin = new Thickness(0, 0, 0, 4)
            };
            advancedPanel.Children.Add(_polarityComboBox);
            panel.Children.Add(new Expander
            {
                Header = UiText.Get("DialogAdvancedParameters"),
                IsExpanded = false,
                Margin = new Thickness(0, 6, 0, 0),
                Content = advancedPanel
            });

            AttachPreviewHandler(_caliperCountTextBox);
            AttachPreviewHandler(_searchRangeTextBox);
            AttachPreviewHandler(_samplingHalfWidthTextBox);
            AttachPreviewHandler(_minimumGradientTextBox);
            AttachPreviewHandler(_minimumValidCalipersTextBox);
            AttachPreviewHandler(_outlierThresholdTextBox);
            _polarityComboBox.SelectionChanged += (_, _) =>
            {
                if (_livePreviewCheckBox?.IsChecked == true)
                {
                    PreviewCurrentValues();
                }
            };

            panel.Children.Add(new TextBlock
            {
                Text = UiText.Get("LineCaliperDialogDescription"),
                TextWrapping = TextWrapping.Wrap,
                Opacity = 0.8,
                Margin = new Thickness(0, 8, 0, 12)
            });

            _livePreviewCheckBox = new CheckBox
            {
                Content = UiText.Get("DialogLivePreview"),
                IsChecked = true,
                Margin = new Thickness(0, 0, 0, 8)
            };
            _livePreviewCheckBox.Checked += (_, _) => PreviewCurrentValues();
            panel.Children.Add(_livePreviewCheckBox);

            var buttonPanel = new StackPanel
            {
                Orientation = Orientation.Horizontal,
                HorizontalAlignment = HorizontalAlignment.Right
            };
            var previewButton = new Button { Content = UiText.Get("DialogPreviewNow"), Width = 82, Margin = new Thickness(0, 0, 8, 0) };
            previewButton.Click += (_, _) => PreviewCurrentValues();
            var okButton = new Button { Content = UiText.Get("DialogButtonOk"), Width = 72, Margin = new Thickness(0, 0, 8, 0), IsDefault = true };
            okButton.Click += OnOkClick;
            var cancelButton = new Button { Content = UiText.Get("DialogButtonCancel"), Width = 72, IsCancel = true };
            cancelButton.Click += (_, _) => Close();
            buttonPanel.Children.Add(previewButton);
            buttonPanel.Children.Add(okButton);
            buttonPanel.Children.Add(cancelButton);
            panel.Children.Add(buttonPanel);

            Content = new ScrollViewer
            {
                VerticalScrollBarVisibility = ScrollBarVisibility.Auto,
                Content = panel
            };
        }

        public LineCaliperMeasureRoi? Result { get; private set; }

        private void OnOkClick(object? sender, RoutedEventArgs e)
        {
            if (!TryApplyInputs(out string errorMessage))
            {
                MessageBox.Show(this, errorMessage, UiText.Get("LineCaliperDialogTitle"), MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }

            PreviewCurrentValues();
            Result = _model;
            DialogResult = true;
            Close();
        }

        private void PreviewCurrentValues()
        {
            if (!TryApplyInputs(out _))
            {
                return;
            }

            _previewAction?.Invoke((LineCaliperMeasureRoi)_model.Clone());
        }

        private bool TryApplyInputs(out string errorMessage)
        {
            errorMessage = UiText.Get("LineCaliperDialogInvalidMessage");
            if (!TryParsePositiveInt(_caliperCountTextBox.Text, out int caliperCount, minValue: 6) ||
                !TryParsePositiveInt(_searchRangeTextBox.Text, out int searchRange, minValue: 1) ||
                !TryParseNonNegativeInt(_samplingHalfWidthTextBox.Text, out int samplingHalfWidth) ||
                !TryParseNonNegativeDouble(_minimumGradientTextBox.Text, out double minimumGradient) ||
                !TryParsePositiveInt(_minimumValidCalipersTextBox.Text, out int minimumValidCalipers, minValue: 3) ||
                !TryParseNonNegativeDouble(_outlierThresholdTextBox.Text, out double outlierThreshold) ||
                _polarityComboBox.SelectedItem is not CaliperEdgePolarity polarity)
            {
                return false;
            }

            _model.CaliperCount = caliperCount;
            _model.CaliperSearchRange = searchRange;
            _model.CaliperSamplingHalfWidth = samplingHalfWidth;
            _model.CaliperMinimumGradient = minimumGradient;
            _model.MinimumValidCalipers = minimumValidCalipers;
            _model.CaliperOutlierThreshold = outlierThreshold;
            _model.CaliperEdgePolarity = polarity;
            return true;
        }

        private static TextBox AddField(Panel panel, string label, string value)
        {
            panel.Children.Add(new TextBlock { Text = label, Margin = new Thickness(0, 6, 0, 2) });
            var textBox = new TextBox { Text = value };
            panel.Children.Add(textBox);
            return textBox;
        }

        private void AttachPreviewHandler(TextBox textBox)
        {
            textBox.TextChanged += (_, _) =>
            {
                if (_livePreviewCheckBox?.IsChecked == true)
                {
                    PreviewCurrentValues();
                }
            };
        }

        private static bool TryParsePositiveInt(string text, out int value, int minValue)
        {
            return int.TryParse(text, NumberStyles.Integer, CultureInfo.InvariantCulture, out value) && value >= minValue;
        }

        private static bool TryParseNonNegativeInt(string text, out int value)
        {
            return int.TryParse(text, NumberStyles.Integer, CultureInfo.InvariantCulture, out value) && value >= 0;
        }

        private static bool TryParseNonNegativeDouble(string text, out double value)
        {
            return double.TryParse(text, NumberStyles.Float | NumberStyles.AllowThousands, CultureInfo.InvariantCulture, out value) && value >= 0;
        }
    }
}
