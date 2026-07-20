using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Windows.Data;
using TimeRenderer.ViewModels;

namespace TimeRenderer.Converters;

public class DateToVisibleDaysConverter : IMultiValueConverter
{
    public object Convert(object[] values, Type targetType, object parameter, CultureInfo culture)
    {
        if (values.Length < ConverterIndices.DateToVisibleDays.RequiredCount || values[ConverterIndices.DateToVisibleDays.CurrentDate] is not DateTime date)
            return (List<DateTime>)[];

        var mode = values[ConverterIndices.DateToVisibleDays.ViewMode] is ViewMode m ? m : ViewMode.Day;
        var enabledDays = values[ConverterIndices.DateToVisibleDays.EnabledDays] as IEnumerable<DayOfWeek> 
            ?? [DayOfWeek.Monday, DayOfWeek.Tuesday, DayOfWeek.Wednesday, DayOfWeek.Thursday, DayOfWeek.Friday, DayOfWeek.Saturday, DayOfWeek.Sunday];

        if (mode == ViewMode.Day)
        {
            return (List<DateTime>)[date.Date];
        }
        else
        {
            var start = date.GetStartOfWeek();
            var days = new List<DateTime>();
            for (int i = 0; i < 7; i++)
            {
                var d = start.AddDays(i);
                if (enabledDays.Contains(d.DayOfWeek))
                {
                    days.Add(d);
                }
            }
            return days;
        }
    }

    public object[] ConvertBack(object value, Type[] targetTypes, object parameter, CultureInfo culture)
    {
        throw new NotImplementedException();
    }
}
