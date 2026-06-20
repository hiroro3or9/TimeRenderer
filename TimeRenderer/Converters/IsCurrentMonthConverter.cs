using System;
using System.Globalization;
using System.Windows.Data;

namespace TimeRenderer.Converters;

public class IsCurrentMonthConverter : IMultiValueConverter
{
    public object Convert(object[] values, Type targetType, object parameter, CultureInfo culture)
    {
        if (values.Length < 2 || values[0] is not DateTime cellDate || values[1] is not DateTime currentDate)
        {
            if (parameter as string == "OPACITY") return 1.0;
            return true;
        }

        bool isCurrentMonth = cellDate.Year == currentDate.Year && cellDate.Month == currentDate.Month;
        
        if (parameter as string == "OPACITY")
            return isCurrentMonth ? 1.0 : 0.4;

        if (parameter as string == "INVERT")
            return !isCurrentMonth;
            
        return isCurrentMonth;
    }

    public object[] ConvertBack(object value, Type[] targetTypes, object parameter, CultureInfo culture)
    {
        throw new NotImplementedException();
    }
}
