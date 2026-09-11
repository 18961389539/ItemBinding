using System;
using System.Globalization;
using System.Windows;
using System.Windows.Data;

namespace MainAPP.Converters;

public class NullToVisibilityConverter : IValueConverter
{
    public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
    {
        return value != null ? Visibility.Visible : Visibility.Collapsed;
    }

    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
    {
        // 不支持反向转换
        throw new NotSupportedException("NullToVisibilityConverter does not support ConvertBack");
    }
}