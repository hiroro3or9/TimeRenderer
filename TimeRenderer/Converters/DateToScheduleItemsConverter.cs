using System;
using System.Collections.Generic;
using System.Globalization;
using System.Windows.Data;
using TimeRenderer.Models;

namespace TimeRenderer.Converters;

public class DateToScheduleItemsConverter : IMultiValueConverter
{
    public object Convert(object[] values, Type targetType, object parameter, CultureInfo culture)
    {
        if (values.Length < 2 || values[0] is not DateTime cellDate || values[1] is not IReadOnlyDictionary<DateTime, List<ScheduleItem>> dailyItems)
            return new List<ScheduleItem>();

        if (dailyItems.TryGetValue(cellDate.Date, out var items))
        {
            return items;
        }

        return new List<ScheduleItem>();
    }

    public object[] ConvertBack(object value, Type[] targetTypes, object parameter, CultureInfo culture)
    {
        throw new NotImplementedException();
    }
}
