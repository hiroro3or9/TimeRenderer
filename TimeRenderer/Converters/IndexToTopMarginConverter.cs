using System;
using System.Globalization;
using System.Windows.Data;

namespace TimeRenderer.Converters;

public class IndexToTopMarginConverter : IValueConverter
{
    public double ItemHeight { get; set; } = 24.0;
    public double MarginTop { get; set; } = 2.0;

    public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
    {
        if (value is int index)
        {
            return (index * ItemHeight) + MarginTop;
        }
        return MarginTop;
    }

    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
    {
        throw new NotImplementedException();
    }
}
