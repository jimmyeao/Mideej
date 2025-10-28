using System.Globalization;
using System.Windows.Data;
using System.Windows.Media;

namespace Mideej.Converters;

/// <summary>
/// Converts mute state to color
/// </summary>
public class MuteColorConverter : IValueConverter
{
    public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        if (value is bool isMuted && isMuted)
        {
            return new SolidColorBrush(Color.FromRgb(239, 68, 68)); // Red when muted
        }
        return new SolidColorBrush(Colors.Transparent); // Transparent when not muted
    }

    public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        throw new NotImplementedException();
    }
}
