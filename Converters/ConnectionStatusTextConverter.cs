using System.Globalization;
using System.Windows.Data;

namespace Mideej.Converters;

/// <summary>
/// Converts connection status to text
/// </summary>
public class ConnectionStatusTextConverter : IValueConverter
{
    public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        if (value is bool isConnected && isConnected)
        {
            return "MIDI Connected";
        }
        return "MIDI Disconnected";
    }

    public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        throw new NotImplementedException();
    }
}
