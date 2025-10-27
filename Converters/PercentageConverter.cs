using System.Globalization;
using System.Windows.Data;

namespace Mideej.Converters;

/// <summary>
/// Converts a float value (0-1) to percentage string
/// </summary>
public class PercentageConverter : IValueConverter
{
    public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        if (value is float floatValue)
        {
            return $"{(int)(floatValue * 100)}%";
        }
        return "0%";
    }

    public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        if (value is string stringValue)
        {
            stringValue = stringValue.TrimEnd('%');
            if (float.TryParse(stringValue, out float result))
            {
                return result / 100f;
            }
        }
        return 0f;
    }
}
