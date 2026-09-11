using System.Windows;
using System.Windows.Controls;
using ImageViewer.Localization;

namespace ImageViewer.Dialogs
{
    internal sealed class CalibrationDialog : Window
    {
        private readonly TextBox _lengthTextBox;
        private readonly TextBox _unitTextBox;

        public CalibrationDialog(string currentUnit)
        {
            Title = UiText.Get("CalibrationDialogTitle");
            Width = 320;
            Height = 180;
            WindowStartupLocation = WindowStartupLocation.CenterOwner;
            ResizeMode = ResizeMode.NoResize;

            var stackPanel = new StackPanel { Margin = new Thickness(10) };
            stackPanel.Children.Add(new TextBlock { Text = UiText.Get("CalibrationDialogPrompt"), Margin = new Thickness(0, 0, 0, 10) });

            _lengthTextBox = new TextBox { Text = "1.0", Margin = new Thickness(0, 0, 0, 8) };
            _unitTextBox = new TextBox { Text = string.IsNullOrWhiteSpace(currentUnit) ? UiText.Get("CalibrationDefaultUnit") : currentUnit };

            stackPanel.Children.Add(new TextBlock { Text = UiText.Get("CalibrationDialogActualLengthLabel"), Margin = new Thickness(0, 0, 0, 2) });
            stackPanel.Children.Add(_lengthTextBox);
            stackPanel.Children.Add(new TextBlock { Text = UiText.Get("CalibrationDialogUnitLabel"), Margin = new Thickness(0, 8, 0, 2) });
            stackPanel.Children.Add(_unitTextBox);

            var buttonPanel = new StackPanel
            {
                Orientation = Orientation.Horizontal,
                HorizontalAlignment = HorizontalAlignment.Right,
                Margin = new Thickness(0, 12, 0, 0)
            };
            var okButton = new Button { Content = UiText.Get("DialogButtonOk"), Width = 60, IsDefault = true, Margin = new Thickness(0, 0, 10, 0) };
            okButton.Click += OnOkClick;
            var cancelButton = new Button { Content = UiText.Get("DialogButtonCancel"), Width = 60, IsCancel = true };
            cancelButton.Click += (s, e) => { DialogResult = false; Close(); };

            buttonPanel.Children.Add(okButton);
            buttonPanel.Children.Add(cancelButton);
            stackPanel.Children.Add(buttonPanel);

            Content = stackPanel;
            Loaded += (_, _) =>
            {
                _lengthTextBox.Focus();
                _lengthTextBox.SelectAll();
            };
        }

        public double Length { get; private set; }

        public string Unit { get; private set; } = "mm";

        private void OnOkClick(object? sender, RoutedEventArgs e)
        {
            if (!double.TryParse(_lengthTextBox.Text, out double length) || length <= 0)
            {
                MessageBox.Show(this, UiText.Get("CalibrationDialogInvalidLengthMessage"), UiText.Get("CalibrationDialogWarningTitle"), MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }

            Length = length;
            Unit = string.IsNullOrWhiteSpace(_unitTextBox.Text) ? UiText.Get("CalibrationDefaultUnit") : _unitTextBox.Text.Trim();
            DialogResult = true;
            Close();
        }
    }
}
