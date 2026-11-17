using System.Globalization;
using System.Windows;
using System.Windows.Data;
using System.Windows.Media;
using IndustrialDuckPanic.ViewModels;

namespace IndustrialDuckPanic.Controls;

public sealed class InverseBoolToVisibility : IValueConverter
{
    public object Convert(object value, Type t, object p, CultureInfo c) => value is true ? Visibility.Collapsed : Visibility.Visible;
    public object ConvertBack(object value, Type t, object p, CultureInfo c) => throw new NotSupportedException();
}

public sealed class InverseBool : IValueConverter
{
    public object Convert(object value, Type t, object p, CultureInfo c) => value is not true;
    public object ConvertBack(object value, Type t, object p, CultureInfo c) => value is not true;
}

public sealed class NullToCollapsed : IValueConverter
{
    public object Convert(object? value, Type t, object p, CultureInfo c) =>
        value is null || value is string { Length: 0 } ? Visibility.Collapsed : Visibility.Visible;
    public object ConvertBack(object value, Type t, object p, CultureInfo c) => throw new NotSupportedException();
}

/// <summary>Green for OK, red for error, grey for unknown. ConverterParameter "color" returns a Color.</summary>
public sealed class HealthToBrush : IValueConverter
{
    public static readonly Color Ok = Color.FromRgb(0x2E, 0xCC, 0x71);
    public static readonly Color Error = Color.FromRgb(0xE0, 0x26, 0x2E);
    public static readonly Color Unknown = Color.FromRgb(0x7A, 0x7F, 0x87);

    public object Convert(object value, Type t, object p, CultureInfo c)
    {
        var color = value switch { Health.Ok => Ok, Health.Error => Error, _ => Unknown };
        return p as string == "color" ? color : new SolidColorBrush(color);
    }

    public object ConvertBack(object value, Type t, object p, CultureInfo c) => throw new NotSupportedException();
}
