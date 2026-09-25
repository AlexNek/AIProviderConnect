using System.Globalization;
using System.Windows.Data;
using System.Windows.Media;

using ScraperTool.Models;

namespace ScraperTool.Converters;

/// <summary>
/// Converts ValidationLevel to a foreground color for text.
/// </summary>
public sealed class ValidationLevelToTextColorConverter : IValueConverter
{
    public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
    {
        if (value is not ValidationLevel level)
            return Brushes.Transparent;

        return level switch
            {
                ValidationLevel.ValidationError => new SolidColorBrush(
                    Color.FromRgb(0xD3, 0x2F, 0x2F)), // Red
                ValidationLevel.AutoValidated => new SolidColorBrush(
                    Color.FromRgb(0x38, 0x8E, 0x3C)), // Green
                ValidationLevel.ManuallyValidated => new SolidColorBrush(
                    Color.FromRgb(0x19, 0x76, 0xD2)), // Blue
                ValidationLevel.VerifiedByOwner => new SolidColorBrush(
                    Color.FromRgb(0x7B, 0x1F, 0xA2)), // Purple
                _ => Brushes.Transparent
            };
    }

    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
    {
        throw new NotImplementedException();
    }
}
