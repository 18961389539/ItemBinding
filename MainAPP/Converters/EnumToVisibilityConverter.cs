using System;
using System.Globalization;
using System.Windows;
using System.Windows.Data;

namespace MainAPP.Converters;

/// <summary>枚举值转 Visibility。ConverterParameter 指定要匹配的枚举名称。</summary>
public class EnumToVisibilityConverter : IValueConverter
{
    public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
    {
        if (value == null || parameter == null) return Visibility.Collapsed;
        return value.ToString() == parameter.ToString() ? Visibility.Visible : Visibility.Collapsed;
    }

    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
        => throw new NotSupportedException("EnumToVisibilityConverter does not support ConvertBack");
}
