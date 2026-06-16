using System;
using System.Linq;

namespace TimeRenderer;

public partial class MainViewModel
{
    private int _displayStartHour = 0;
    public int DisplayStartHour
    {
        get => _displayStartHour;
        set
        {
            var clamped = Math.Clamp(value, 0, _displayEndHour - 1);
            if (SetProperty(ref _displayStartHour, clamped))
            {
                OnPropertyChanged(nameof(ScheduleGridHeight));
                InitializeTimeLabels();
                SaveSettings();
            }
        }
    }

    private int _displayEndHour = 24;
    public int DisplayEndHour
    {
        get => _displayEndHour;
        set
        {
            var clamped = Math.Clamp(value, _displayStartHour + 1, 24);
            if (SetProperty(ref _displayEndHour, clamped))
            {
                OnPropertyChanged(nameof(ScheduleGridHeight));
                InitializeTimeLabels();
                SaveSettings();
            }
        }
    }

    public double ScheduleGridHeight => (_displayEndHour - _displayStartHour) * 60.0;

    private double _allDayPanelHeight = 30;
    public double AllDayPanelHeight
    {
        get => _allDayPanelHeight;
        set => SetProperty(ref _allDayPanelHeight, value);
    }

    private void InitializeTimeLabels()
    {
        var labels = new List<string>();
        for (int i = _displayStartHour; i <= _displayEndHour; i++)
        {
            labels.Add($"{i}:00");
        }
        TimeLabels = labels;
    }

    private void UpdateVisibleDays()
    {
        var days = new List<DateTime>();
        if (CurrentViewMode == ViewMode.Day)
        {
            days.Add(CurrentDate);
        }
        else if (CurrentViewMode == ViewMode.Week)
        {
            var start = CurrentWeekStart;
            for (int i = 0; i < 7; i++)
            {
                days.Add(start.AddDays(i));
            }
        }
        else // Month
        {
            // 月初の1日を取得
            var firstDayOfMonth = new DateTime(CurrentDate.Year, CurrentDate.Month, 1);
            // その月を含む週の月曜日を取得（カレンダーの左上）
            var diff = (7 + (firstDayOfMonth.DayOfWeek - DayOfWeek.Monday)) % 7;
            var start = firstDayOfMonth.AddDays(-1 * diff).Date;
            
            // 6週間(42日)分を追加
            for (int i = 0; i < 42; i++)
            {
                days.Add(start.AddDays(i));
            }
        }
        VisibleDays = days;
        UpdateCalendarCells();
    }

    private void RecalculateLayout()
    {
        var newStandardItems = new List<ScheduleItem>();
        var newAllDayItems = new List<ScheduleItem>();

        foreach (var item in ScheduleItems)
        {
            if (item.IsAllDay)
            {
                newAllDayItems.Add(item);
            }
            else
            {
                newStandardItems.Add(item);
            }
        }

        var allDayGrouped = newAllDayItems.GroupBy(x => x.StartTime.Date);
        int maxStackIndex = 0;

        foreach (var group in allDayGrouped)
        {
            int index = 0;
            foreach (var item in group.OrderBy(x => x.Title))
            {
                item.ColumnIndex = index;
                index++;
            }
            if (index > maxStackIndex) maxStackIndex = index;
        }

        AllDayPanelHeight = Math.Max(30, (maxStackIndex * 24) + 6);

        var grouped = newStandardItems.GroupBy(x => x.StartTime.Date);
        foreach (var group in grouped)
        {
            var sortedItems = group.OrderBy(x => x.StartTime).ThenByDescending(x => x.EndTime).ToList();
            Helpers.ScheduleLayoutHelper.CalculateClustersAndAssignColumns(sortedItems);
        }

        var dailyItems = new Dictionary<DateTime, List<ScheduleItem>>();
        foreach (var item in ScheduleItems)
        {
            var start = item.StartTime.Date;
            var end = item.EndTime.Date;
            for (var d = start; d <= end; d = d.AddDays(1))
            {
                if (!dailyItems.TryGetValue(d, out var list))
                {
                    list = [];
                    dailyItems[d] = list;
                }
                list.Add(item);
            }
        }

        foreach (var key in dailyItems.Keys.ToList())
        {
            dailyItems[key] = [.. dailyItems[key]
                .OrderBy(x => x.IsAllDay ? 0 : 1)
                .ThenBy(x => x.StartTime)];
        }

        DailyScheduleItems = dailyItems;
        StandardItems = newStandardItems;
        AllDayItems = newAllDayItems;
        
        UpdateCalendarCells();
    }

    private void UpdateCalendarCells()
    {
        if (CurrentViewMode != ViewMode.Month) 
            return;

        var cells = new List<CalendarCellViewModel>();
        foreach (var day in VisibleDays)
        {
            DailyScheduleItems.TryGetValue(day.Date, out var items);
            items ??= [];

            bool isCurrentMonth = day.Month == CurrentDate.Month && day.Year == CurrentDate.Year;
            bool isToday = day.Date == DateTime.Today;

            cells.Add(new CalendarCellViewModel(day, isCurrentMonth, isToday, items));
        }
        CalendarCells = cells;
    }
}
