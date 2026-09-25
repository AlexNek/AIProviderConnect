using System.Globalization;
using System.Windows.Data;
using System.Windows.Media;

namespace ScraperTool.Converters;

/// <summary>
/// Converts validation state display string to border color.
/// </summary>
public sealed class ValidationStateToBorderConverter : IValueConverter
{
    public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
    {
        if (value is not string display || string.IsNullOrEmpty(display))
            return Brushes.Transparent;

        if (display.Contains("failed", StringComparison.OrdinalIgnoreCase))
            return new SolidColorBrush(Color.FromRgb(0xF4, 0x43, 0x36)); // Red

        if (display.Contains("verified", StringComparison.OrdinalIgnoreCase))
            return new SolidColorBrush(Color.FromRgb(0x9C, 0x27, 0xB0)); // Purple

        if (display.Contains("manual", StringComparison.OrdinalIgnoreCase))
            return new SolidColorBrush(Color.FromRgb(0x21, 0x96, 0xF3)); // Blue

        if (display.Contains("auto", StringComparison.OrdinalIgnoreCase))
            return new SolidColorBrush(Color.FromRgb(0x4C, 0xAF, 0x50)); // Green

        return Brushes.Transparent;
    }

    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
    {
        throw new NotImplementedException();
    }
}
