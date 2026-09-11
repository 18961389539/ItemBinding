using System.Windows;
using System.Windows.Controls;
using ImageViewer.Localization;

namespace ImageViewer.Dialogs
{
    internal sealed class ReadOnlyTextDialog : Window
    {
        public ReadOnlyTextDialog(string title, string text)
        {
            Title = title;
            Width = 420;
            Height = 360;
            WindowStartupLocation = WindowStartupLocation.CenterOwner;

            var textBox = new TextBox
            {
                Text = text,
                IsReadOnly = true,
                TextWrapping = TextWrapping.Wrap,
                AcceptsReturn = true,
                VerticalScrollBarVisibility = ScrollBarVisibility.Auto,
                Margin = new Thickness(12)
            };

            var closeButton = new Button
            {
                Content = UiText.Get("DialogButtonClose"),
                IsCancel = true,
                Width = 72,
                HorizontalAlignment = HorizontalAlignment.Right,
                Margin = new Thickness(12, 0, 12, 12)
            };
            closeButton.Click += (_, _) => Close();

            var buttonHost = new Border { Child = closeButton };
            DockPanel.SetDock(buttonHost, Dock.Bottom);

            var panel = new DockPanel();
            panel.Children.Add(buttonHost);
            panel.Children.Add(textBox);
            Content = panel;
        }
    }
}
