using System;
using System.Globalization;
using System.Windows.Data;

namespace MainAPP.Converters;

/// <summary>
/// 非空 → true；null → false。用于按钮 IsEnabled 与选中状态联动。
/// </summary>
public class NullToBoolConverter : IValueConverter
{
    public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
        => value != null;

    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
        => throw new NotSupportedException("NullToBoolConverter does not support ConvertBack");
}
