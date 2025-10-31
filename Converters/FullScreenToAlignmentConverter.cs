using System;
using System.Globalization;
using System.Windows;
using System.Windows.Data;

namespace Mideej.Converters;

public class FullScreenToAlignmentConverter : IValueConverter
{
    public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
    {
        if (value is bool isFullScreen && isFullScreen)
        {
            return HorizontalAlignment.Center;
        }
        return HorizontalAlignment.Left;
    }

    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
    {
        throw new NotImplementedException();
    }
}
