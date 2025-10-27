using System.Globalization;
using System.Windows.Data;
using System.Windows.Media;

namespace Mideej.Converters;

/// <summary>
/// Converts solo state to color
/// </summary>
public class SoloColorConverter : IValueConverter
{
    public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        if (value is bool isSoloed && isSoloed)
        {
            return Color.FromRgb(245, 158, 11); // Amber/yellow when soloed
        }
        return Color.FromRgb(42, 42, 60); // Default surface color
    }

    public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        throw new NotImplementedException();
    }
}
