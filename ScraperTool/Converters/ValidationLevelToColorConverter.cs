using System.Globalization;
using System.Windows.Data;
using System.Windows.Media;

using ScraperTool.Models;

namespace ScraperTool.Converters;

/// <summary>
/// Converts ValidationLevel to a brush color for visual indication.
/// </summary>
public sealed class ValidationLevelToColorConverter : IValueConverter
{
    public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
    {
        if (value is not ValidationLevel level)
            return Brushes.Transparent;

        return level switch
            {
                ValidationLevel.ValidationError => new SolidColorBrush(
                    Color.FromArgb(0x20, 0xF4, 0x43, 0x36)), // Red tint
                ValidationLevel.AutoValidated => new SolidColorBrush(
                    Color.FromArgb(0x20, 0x4C, 0xAF, 0x50)), // Green tint
                ValidationLevel.ManuallyValidated => new SolidColorBrush(
                    Color.FromArgb(0x20, 0x21, 0x96, 0xF3)), // Blue tint
                ValidationLevel.VerifiedByOwner => new SolidColorBrush(
                    Color.FromArgb(0x20, 0x9C, 0x27, 0xB0)), // Purple tint
                _ => Brushes.Transparent
            };
    }

    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
    {
        throw new NotImplementedException();
    }
}
