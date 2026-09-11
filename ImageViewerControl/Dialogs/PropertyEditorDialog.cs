using System.Windows;
using System.Windows.Controls;

namespace ImageViewer.Dialogs
{
    internal sealed class PropertyEditorDialog : Window
    {
        public PropertyEditorDialog(string title, FrameworkElement editor)
        {
            Title = title;
            Width = 360;
            Height = 520;
            WindowStartupLocation = WindowStartupLocation.CenterOwner;
            Background = new System.Windows.Media.SolidColorBrush(System.Windows.Media.Color.FromRgb(37, 37, 38));

            Content = new ScrollViewer
            {
                VerticalScrollBarVisibility = ScrollBarVisibility.Auto,
                Content = new Border
                {
                    Background = new System.Windows.Media.SolidColorBrush(System.Windows.Media.Color.FromRgb(37, 37, 38)),
                    Padding = new Thickness(12),
                    Child = editor
                }
            };
        }
    }
}
