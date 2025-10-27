using System.Globalization;
using System.Windows.Data;

namespace Mideej.Converters;

/// <summary>
/// Converts volume (0-1) to a height value for VU meters
/// </summary>
public class VolumeToHeightConverter : IValueConverter
{
    public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        if (value is float floatValue && parameter is double maxHeight)
        {
            return floatValue * maxHeight;
        }
        if (value is float floatValue2)
        {
            return floatValue2 * 100; // Default height
        }
        return 0.0;
    }

    public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        throw new NotImplementedException();
    }
}
