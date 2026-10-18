using System.Globalization;
using System.Windows;
using System.Windows.Data;
using System.Windows.Media;

namespace SahandGearMaker.App;

/// <summary>مبدل بول → براش (رنگ متن فعال/غیرفعال در فهرست فرمت‌ها).</summary>
public sealed class BoolToBrushConverter : IValueConverter
{
    public Brush TrueBrush { get; set; } = Brushes.Black;
    public Brush FalseBrush { get; set; } = Brushes.Gray;

    public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
        => value is true ? TrueBrush : FalseBrush;

    public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture)
        => throw new NotSupportedException();
}

/// <summary>مبدل بول → Visibility با جهت قابل‌تنظیم (نمایش نشانگر «—» فقط برای فرمت غیرفعال).</summary>
public sealed class BoolToVisibilityConverter : IValueConverter
{
    public Visibility WhenTrue { get; set; } = Visibility.Visible;
    public Visibility WhenFalse { get; set; } = Visibility.Collapsed;

    public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
        => value is true ? WhenTrue : WhenFalse;

    public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture)
        => throw new NotSupportedException();
}
