using System;
using System.Globalization;
using System.Windows;
using System.Windows.Data;

namespace MainAPP.Converters;

/// <summary>
/// 反向 NullToVisibility 转换器：值为 null 时返回 Visible，非 null 时返回 Collapsed。
/// 用于占位提示文本等需要在未选中时显示、选中时隐藏的场景。
/// </summary>
public class InverseNullToVisibilityConverter : IValueConverter
{
    public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
    {
        return value == null ? Visibility.Visible : Visibility.Collapsed;
    }

    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
    {
        // 不支持反向转换
        throw new NotSupportedException("InverseNullToVisibilityConverter does not support ConvertBack");
    }
}
