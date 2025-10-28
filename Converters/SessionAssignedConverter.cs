using System.Globalization;
using System.Windows.Data;

namespace Mideej.Converters;

/// <summary>
/// Converter to check if a session ID is in the already-mapped set
/// </summary>
public class SessionAssignedConverter : IValueConverter
{
    public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        if (value is string sessionId && parameter is HashSet<string> mappedIds)
        {
            return mappedIds.Contains(sessionId);
        }
        return false;
    }

    public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        throw new NotImplementedException();
    }
}
