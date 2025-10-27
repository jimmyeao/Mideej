using System.Globalization;
using System.Windows.Data;
using System.Windows.Media;

namespace Mideej.Converters;

/// <summary>
/// Converts connection status to color
/// </summary>
public class ConnectionStatusConverter : IValueConverter
{
    public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        if (value is bool isConnected && isConnected)
        {
            return Color.FromRgb(16, 185, 129); // Green when connected
        }
        return Color.FromRgb(239, 68, 68); // Red when disconnected
    }

    public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        throw new NotImplementedException();
    }
}
