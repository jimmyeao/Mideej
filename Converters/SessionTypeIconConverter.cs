using System.Globalization;
using System.Windows.Data;
using Mideej.Models;

namespace Mideej.Converters;

/// <summary>
/// Converts AudioSessionType to an icon
/// </summary>
public class SessionTypeIconConverter : IValueConverter
{
    public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        if (value is AudioSessionType sessionType)
        {
            return sessionType switch
            {
                AudioSessionType.Application => "🎵",
                AudioSessionType.Output => "🔊",
                AudioSessionType.Input => "🎤",
                AudioSessionType.SystemSounds => "🔔",
                _ => "🎛"
            };
        }
        return "🎛";
    }

    public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        throw new NotImplementedException();
    }
}
