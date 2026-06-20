using System;
using System.Collections.Generic;
using System.Globalization;
using System.Windows;
using System.Windows.Data;
using TimeRenderer.Models;

namespace TimeRenderer.Converters;

public class DateToSprintTimelinePositionConverter : IMultiValueConverter
{
    private const string ParameterWidth = "WIDTH";
    private const double HiddenOutPosition = -10000.0;

    public object Convert(object[] values, Type targetType, object parameter, CultureInfo culture)
    {
        if (values.Length < 4 ||
            values[0] is not DateTime startTime ||
            values[1] is not DateTime endTime ||
            values[2] is not IReadOnlyList<SprintInfo> sprints ||
            sprints.Count == 0 ||
            values[3] is not double actualWidth)
        {
            return 0.0;
        }

        bool isWidth = parameter as string == ParameterWidth;

        var totalStart = sprints[0].StartDate.Date;
        var totalEnd = sprints[^1].EndDate.Date;

        if (endTime.Date < totalStart || startTime.Date > totalEnd)
        {
            return isWidth ? 0.0 : HiddenOutPosition;
        }

        var displayStart = startTime.Date < totalStart ? totalStart : startTime.Date;
        var displayEnd = endTime.Date > totalEnd ? totalEnd : endTime.Date;

        double sprintWidth = actualWidth / sprints.Count;

        double startPosition = GetPositionForDate(displayStart, sprints, sprintWidth);
        double endPosition = GetPositionForDate(displayEnd.AddDays(1), sprints, sprintWidth);

        if (isWidth)
        {
            return Math.Max(0.0, endPosition - startPosition);
        }
        else
        {
            return new System.Windows.Thickness(startPosition, 0, 0, 0);
        }
    }

    private static double GetPositionForDate(DateTime date, IReadOnlyList<SprintInfo> sprints, double sprintWidth)
    {
        for (int i = 0; i < sprints.Count; i++)
        {
            var sprint = sprints[i];
            if (date >= sprint.StartDate && date <= sprint.EndDate.AddDays(1))
            {
                double totalDays = (sprint.EndDate - sprint.StartDate).TotalDays + 1;
                double currentDayOffset = (date - sprint.StartDate).TotalDays;
                double ratio = currentDayOffset / totalDays;
                
                return (i * sprintWidth) + (ratio * sprintWidth);
            }
        }
        
        if (date < sprints[0].StartDate) return 0.0;
        return sprints.Count * sprintWidth;
    }

    public object[] ConvertBack(object value, Type[] targetTypes, object parameter, CultureInfo culture)
        => throw new NotImplementedException();
}
