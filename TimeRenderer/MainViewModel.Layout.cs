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
            if (_displayStartHour != clamped)
            {
                _displayStartHour = clamped;
                OnPropertyChanged();
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
            if (_displayEndHour != clamped)
            {
                _displayEndHour = clamped;
                OnPropertyChanged();
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
        set
        {
            if (_allDayPanelHeight != value)
            {
                _allDayPanelHeight = value;
                OnPropertyChanged();
            }
        }
    }

    private void InitializeTimeLabels()
    {
        TimeLabels.Clear();
        for (int i = _displayStartHour; i <= _displayEndHour; i++)
        {
            TimeLabels.Add($"{i}:00");
        }
    }

    private void UpdateVisibleDays()
    {
        VisibleDays.Clear();
        if (CurrentViewMode == ViewMode.Day)
        {
            VisibleDays.Add(CurrentDate);
        }
        else
        {
            var start = CurrentWeekStart;
            for (int i = 0; i < 7; i++)
            {
                VisibleDays.Add(start.AddDays(i));
            }
        }
    }

    private void RecalculateLayout()
    {
        StandardItems.Clear();
        AllDayItems.Clear();

        foreach (var item in ScheduleItems)
        {
            if (item.IsAllDay)
            {
                AllDayItems.Add(item);
            }
            else
            {
                StandardItems.Add(item);
            }
        }

        var allDayGrouped = AllDayItems.GroupBy(x => x.StartTime.Date);
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

        var grouped = StandardItems.GroupBy(x => x.StartTime.Date);
        foreach (var group in grouped)
        {
            var sortedItems = group.OrderBy(x => x.StartTime).ThenByDescending(x => x.EndTime).ToList();
            Helpers.ScheduleLayoutHelper.CalculateClustersAndAssignColumns(sortedItems);
        }
    }
}
