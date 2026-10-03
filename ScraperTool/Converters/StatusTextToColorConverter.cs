using System.Globalization;
using System.Windows.Data;
using System.Windows.Media;

namespace ScraperTool.Converters;

/// <summary>
/// Returns red brush when the bound string starts with "Error:", otherwise a neutral gray.
/// </summary>
public sealed class StatusTextToColorConverter : IValueConverter
{
    public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
    {
        if (value is string s && s.StartsWith("Error:", System.StringComparison.Ordinal))
            return new SolidColorBrush(Color.FromRgb(0xD3, 0x2F, 0x2F));
        return new SolidColorBrush(Color.FromRgb(0x55, 0x55, 0x55));
    }

    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
    {
        throw new NotImplementedException();
    }
}
