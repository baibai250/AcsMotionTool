using System.Globalization;
using System.Windows;
using System.Windows.Data;

namespace AcsMotionTool.App.Infrastructure;

/// <summary>布尔取反转换器(例如“模拟器模式时禁用 IP 输入”)</summary>
public sealed class InverseBooleanConverter : IValueConverter
{
    public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
        => value is bool flag ? !flag : true;

    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
        => value is bool flag ? !flag : false;
}

/// <summary>布尔到 Visibility 的转换器(true = Visible)</summary>
public sealed class BooleanToVisibilityConverter : IValueConverter
{
    public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
        => value is bool flag && flag ? Visibility.Visible : Visibility.Collapsed;

    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
        => value is Visibility visibility && visibility == Visibility.Visible;
}

/// <summary>字符串非空到 Visibility 的转换器(用于“报警为空时不显示”这类场景)</summary>
public sealed class NonEmptyStringToVisibilityConverter : IValueConverter
{
    public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
        => string.IsNullOrWhiteSpace(value as string) ? Visibility.Collapsed : Visibility.Visible;

    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
        => Binding.DoNothing;
}

/// <summary>布尔取反到 Visibility(true 时隐藏，用于“忙时隐藏按钮”)</summary>
public sealed class InverseBooleanToVisibilityConverter : IValueConverter
{
    public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
        => value is bool flag && flag ? Visibility.Collapsed : Visibility.Visible;

    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
        => Binding.DoNothing;
}

/// <summary>整数等于参数转换器(用于 RadioButton 绑定到 int 枚举型属性)</summary>
public sealed class IntEqualsConverter : IValueConverter
{
    public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
        => value is int current && int.TryParse(parameter?.ToString(), out var expected) && current == expected;

    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
        => value is true && int.TryParse(parameter?.ToString(), out var expected) ? expected : Binding.DoNothing;
}
