using System.Globalization;
using System.Windows;
using System.Windows.Data;
using System.Windows.Media;

namespace TrayStats.Helpers;

/// <summary>
/// Converts a percentage (0-100) to a Width value proportional to the parent container.
/// Uses a reference width of ~150px for compact bars.
/// </summary>
public class PercentToWidthConverter : IValueConverter
{
    public double MaxWidth { get; set; } = 150;

    public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
    {
        if (value is float f)
            return Math.Max(0, Math.Min(MaxWidth, f / 100.0 * MaxWidth));
        if (value is double d)
            return Math.Max(0, Math.Min(MaxWidth, d / 100.0 * MaxWidth));
        return 0.0;
    }

    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
        => throw new NotSupportedException();
}

/// <summary>
/// Same as PercentToWidth but for larger bars (e.g. RAM detail).
/// </summary>
public class PercentToWidthLargeConverter : IValueConverter
{
    public double MaxWidth { get; set; } = 300;

    public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
    {
        if (value is float f)
            return Math.Max(0, Math.Min(MaxWidth, f / 100.0 * MaxWidth));
        if (value is double d)
            return Math.Max(0, Math.Min(MaxWidth, d / 100.0 * MaxWidth));
        return 0.0;
    }

    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
        => throw new NotSupportedException();
}

public class BoolToVisibilityConverter : IValueConverter
{
    public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
    {
        if (value is bool b && b)
            return Visibility.Visible;
        return Visibility.Collapsed;
    }

    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
        => throw new NotSupportedException();
}

/// <summary>
/// Formats a sensor value with a unit suffix, showing "N/A" when the value is 0
/// (meaning the sensor isn't available). Pass the format string as the parameter,
/// e.g. "{0:F0}°C" or "{0:F0} RPM".
/// </summary>
public class SensorValueConverter : IValueConverter
{
    public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
    {
        double v = 0;
        if (value is float f) v = f;
        else if (value is double d) v = d;

        if (v == 0)
            return "N/A";

        string fmt = parameter as string ?? "{0:F0}";
        return string.Format(fmt, v);
    }

    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
        => throw new NotSupportedException();
}

public class WeatherTemperatureConverter : IValueConverter
{
    public static bool UseFahrenheit { get; set; }

    public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
    {
        double v = 0;
        if (value is float f) v = f;
        else if (value is double d) v = d;

        if (v == 0 && parameter?.ToString() != "no_na")
            return "N/A";

        bool useF = UseFahrenheit;
        string fmt = parameter as string ?? "{0:F0}";
        char unit = useF ? 'F' : 'C';

        double displayValue = useF ? v * 9.0 / 5.0 + 32 : v;
        return string.Format(fmt, displayValue) + (char)0x00B0 + unit;
    }

    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
        => throw new NotSupportedException();
}

/// <summary>
/// Maps a battery percentage (0-100) to a gradient color:
///   0-20%  red -> orange
///  20-50%  orange -> yellow
///  50-100% yellow -> green
/// </summary>
public class BatteryLevelToBrushConverter : IValueConverter
{
    private static readonly Color Red    = Color.FromRgb(0xF4, 0x43, 0x36);
    private static readonly Color Orange = Color.FromRgb(0xFF, 0x98, 0x00);
    private static readonly Color Yellow = Color.FromRgb(0xFF, 0xC1, 0x07);
    private static readonly Color Green  = Color.FromRgb(0x4C, 0xAF, 0x50);

    public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
    {
        double pct = 0;
        if (value is float f) pct = f;
        else if (value is double d) pct = d;

        pct = Math.Clamp(pct, 0, 100);

        Color c;
        if (pct <= 20)
            c = Lerp(Red, Orange, pct / 20.0);
        else if (pct <= 50)
            c = Lerp(Orange, Yellow, (pct - 20) / 30.0);
        else
            c = Lerp(Yellow, Green, (pct - 50) / 50.0);

        var brush = new SolidColorBrush(c);
        brush.Freeze();
        return brush;
    }

    private static Color Lerp(Color a, Color b, double t)
    {
        t = Math.Clamp(t, 0, 1);
        return Color.FromRgb(
            (byte)(a.R + (b.R - a.R) * t),
            (byte)(a.G + (b.G - a.G) * t),
            (byte)(a.B + (b.B - a.B) * t));
    }

    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
        => throw new NotSupportedException();
}
