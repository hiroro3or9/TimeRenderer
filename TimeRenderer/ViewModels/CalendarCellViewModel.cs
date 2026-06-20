using System;
using System.Collections.Generic;

using TimeRenderer.Models;

namespace TimeRenderer.ViewModels;

public class CalendarCellViewModel(DateTime date, bool isCurrentMonth, bool isToday, IReadOnlyList<ScheduleItem> dailyItems)
{
    public DateTime Date { get; } = date;
    public string DayText => Date.Day.ToString();
    public DayOfWeek DayOfWeek => Date.DayOfWeek;
    public bool IsCurrentMonth { get; } = isCurrentMonth;
    public bool IsToday { get; } = isToday;
    public IReadOnlyList<ScheduleItem> DailyItems { get; } = dailyItems;
}
