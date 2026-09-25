using System.Globalization;
using System.Windows.Data;
using System.Windows.Media;

namespace ScraperTool.Converters;

/// <summary>
/// Converts validation state display string to background color (checks if string contains keywords).
/// </summary>
public sealed class ValidationStateToColorConverter : IValueConverter
{
    public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
    {
        if (value is not string display || string.IsNullOrEmpty(display))
            return Brushes.Transparent;

        if (display.Contains("failed", StringComparison.OrdinalIgnoreCase))
            return new SolidColorBrush(Color.FromArgb(0x20, 0xF4, 0x43, 0x36)); // Red tint

        if (display.Contains("verified", StringComparison.OrdinalIgnoreCase))
            return new SolidColorBrush(Color.FromArgb(0x20, 0x9C, 0x27, 0xB0)); // Purple tint

        if (display.Contains("manual", StringComparison.OrdinalIgnoreCase))
            return new SolidColorBrush(Color.FromArgb(0x20, 0x21, 0x96, 0xF3)); // Blue tint

        if (display.Contains("auto", StringComparison.OrdinalIgnoreCase))
            return new SolidColorBrush(Color.FromArgb(0x20, 0x4C, 0xAF, 0x50)); // Green tint

        return Brushes.Transparent;
    }

    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
    {
        throw new NotImplementedException();
    }
}
