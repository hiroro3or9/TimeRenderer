using System;
using System.Linq;

using TimeRenderer.Models;
using TimeRenderer.Helpers;
using TimeRenderer.Services;

namespace TimeRenderer.ViewModels;

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

    private int _sprintWeekRows = 3;
    /// <summary>スプリントビューのグリッド行数（スプリントの週数に追随）</summary>
    public int SprintWeekRows
    {
        get => _sprintWeekRows;
        private set => SetProperty(ref _sprintWeekRows, value);
    }

    private IReadOnlyList<ScheduleItem> _timelineItems = [];
    /// <summary>スプリントタイムラインの表示範囲内のアイテム（開始時刻順）</summary>
    public IReadOnlyList<ScheduleItem> TimelineItems
    {
        get => _timelineItems;
        private set => SetProperty(ref _timelineItems, value);
    }

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
                var day = start.AddDays(i);
                if (EnabledDaysOfWeek.Contains(day.DayOfWeek))
                {
                    days.Add(day);
                }
            }
        }
        else if (CurrentViewMode == ViewMode.Month)
        {
            // 月初の1日を取得
            var firstDayOfMonth = new DateTime(CurrentDate.Year, CurrentDate.Month, 1);
            // その月を含む週の月曜日を取得（カレンダーの左上）
            var diff = (7 + (firstDayOfMonth.DayOfWeek - DayOfWeek.Monday)) % 7;
            var start = firstDayOfMonth.AddDays(-1 * diff).Date;
            
            // 6週間分ループし、有効な曜日のみを追加
            for (int w = 0; w < 6; w++)
            {
                var weekStart = start.AddDays(w * 7);
                for (int d = 0; d < 7; d++)
                {
                    var day = weekStart.AddDays(d);
                    if (EnabledDaysOfWeek.Contains(day.DayOfWeek))
                    {
                        days.Add(day);
                    }
                }
            }
        }
        else if (CurrentViewMode == ViewMode.Sprint)
        {
            var sprint = Helpers.SprintHelper.GetSprintForDate(ManualSprints, CurrentDate);
            // スプリント開始日の週の月曜日
            var start = Converters.DateTimeHelper.GetStartOfWeek(sprint.StartDate);
            // スプリント終了日の週の日曜日
            var end = Converters.DateTimeHelper.GetStartOfWeek(sprint.EndDate).AddDays(6);

            // グリッド行数をスプリントの実際の週数に合わせる（3週間超の手動スプリント対応）
            SprintWeekRows = Math.Max(1, (int)((end - start).TotalDays + 1) / 7);

            // 週ごとにループして、有効な曜日のみを追加する
            for (var d = start; d <= end; d = d.AddDays(7))
            {
                for (int i = 0; i < 7; i++)
                {
                    var day = d.AddDays(i);
                    if (day > end) break;
                    if (EnabledDaysOfWeek.Contains(day.DayOfWeek))
                    {
                        days.Add(day);
                    }
                }
            }
        }
        else if (CurrentViewMode == ViewMode.SprintTimeline)
        {
            // 基準スプリントを中心とした 5つのスプリントを表示する
            var baseSprint = Helpers.SprintHelper.GetSprintForDate(ManualSprints, CurrentDate);
            var sprints = Helpers.SprintHelper.GetSprintsForRange(ManualSprints, baseSprint.StartDate.AddMonths(-3), baseSprint.EndDate.AddMonths(3));
            
            int baseIdx = sprints.FindIndex(s => s.StartDate.Date == baseSprint.StartDate.Date);
            if (baseIdx < 0) baseIdx = 0;
            
            int startIdx = Math.Max(0, baseIdx - 2);
            int count = Math.Min(sprints.Count - startIdx, 5);
            var displaySprints = sprints.GetRange(startIdx, count);

            TimelineSprints = displaySprints;

            if (displaySprints.Count > 0)
            {
                var start = displaySprints[0].StartDate.Date;
                var end = displaySprints[^1].EndDate.Date;
                for (var d = start; d <= end; d = d.AddDays(1))
                {
                    days.Add(d);
                }
            }
        }
        VisibleDays = days;
        UpdateCalendarCells();
        UpdateTimelineItems();
        UpdateStats();
    }

    /// <summary>
    /// ドラッグ操作中のプレビュー反映。保存はせず、再レイアウトのみ行う
    /// （マウス移動のたびにファイル書き込みが発生するのを防ぐ）。
    /// </summary>
    public bool UpdateItemTimesPreview(ScheduleItem item, DateTime newStart, DateTime newEnd)
    {
        if (item.StartTime == newStart && item.EndTime == newEnd) return false;

        _isBatchUpdatingItem = true;
        try
        {
            item.StartTime = newStart;
            item.EndTime = newEnd;
        }
        finally
        {
            _isBatchUpdatingItem = false;
        }
        RecalculateLayout();
        return true;
    }

    /// <summary>ドラッグ確定時（マウスアップ）にデータを保存する</summary>
    public void CommitItemDrag()
    {
        SaveData();
    }

    private void RecalculateLayout()
    {
        var newAllDayItems = new List<ScheduleItem>();
        var newSegments = new List<ScheduleSegment>();

        // 色フィルタで非表示のカテゴリを除いたアイテムのみを描画対象にする
        var visibleItems = ScheduleItems.Where(x => IsColorVisible(x.ColorCode)).ToList();

        foreach (var item in visibleItems)
        {
            if (item.IsAllDay)
            {
                newAllDayItems.Add(item);
            }
            else
            {
                // 日付をまたぐアイテムは日単位のセグメントに分割する
                // （例: 23:00→翌1:00 は「23:00-24:00」と「0:00-1:00」の2つとして描画）
                var start = item.StartTime;
                var end = item.EndTime;
                if (end <= start)
                {
                    newSegments.Add(new ScheduleSegment(item, start, start));
                    continue;
                }
                for (var d = start.Date; d <= end.Date; d = d.AddDays(1))
                {
                    var segStart = d == start.Date ? start : d;
                    var segEnd = end < d.AddDays(1) ? end : d.AddDays(1);
                    if (segEnd <= segStart) continue; // 終了がちょうど0:00の場合の空セグメントを除外
                    newSegments.Add(new ScheduleSegment(item, segStart, segEnd));
                }
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

        foreach (var group in newSegments.GroupBy(x => x.StartTime.Date))
        {
            var sortedSegments = group.OrderBy(x => x.StartTime).ThenByDescending(x => x.EndTime).ToList();
            Helpers.ScheduleLayoutHelper.CalculateClustersAndAssignColumns(sortedSegments);
        }

        var dailyItems = new Dictionary<DateTime, List<ScheduleItem>>();
        foreach (var item in visibleItems)
        {
            var start = item.StartTime.Date;
            // 終端がちょうど0:00の日は実際にはまたがっていないため含めない
            // （終日イベントは 0:00〜翌0:00 で保存されるため、翌日に重複表示されるのを防ぐ）
            var end = (item.EndTime.TimeOfDay == TimeSpan.Zero && item.EndTime > item.StartTime)
                ? item.EndTime.Date.AddDays(-1)
                : item.EndTime.Date;
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
        StandardItems = newSegments;
        AllDayItems = newAllDayItems;

        UpdateCalendarCells();
        UpdateTimelineItems();
        UpdateStats();
    }

    /// <summary>
    /// タイムラインビュー用に、表示範囲内のアイテムのみを開始時刻順で抽出する。
    /// （全アイテムをそのまま表示すると範囲外の予定が空行として残るため）
    /// </summary>
    private void UpdateTimelineItems()
    {
        if (CurrentViewMode != ViewMode.SprintTimeline || TimelineSprints.Count == 0)
        {
            if (TimelineItems.Count > 0) TimelineItems = [];
            return;
        }

        var rangeStart = TimelineSprints[0].StartDate.Date;
        var rangeEnd = TimelineSprints[^1].EndDate.Date.AddDays(1);

        TimelineItems = [.. ScheduleItems
            .Where(x => IsColorVisible(x.ColorCode))
            .Where(x => x.EndTime >= rangeStart && x.StartTime < rangeEnd)
            .OrderBy(x => x.StartTime)];
    }

    private void UpdateCalendarCells()
    {
        if (CurrentViewMode != ViewMode.Month && CurrentViewMode != ViewMode.Sprint) 
            return;

        var cells = new List<CalendarCellViewModel>();
        var sprint = CurrentViewMode == ViewMode.Sprint ? Helpers.SprintHelper.GetSprintForDate(ManualSprints, CurrentDate) : null;

        foreach (var day in VisibleDays)
        {
            DailyScheduleItems.TryGetValue(day.Date, out var items);
            items ??= [];

            bool isCurrent = false;
            if (CurrentViewMode == ViewMode.Month)
            {
                isCurrent = day.Month == CurrentDate.Month && day.Year == CurrentDate.Year;
            }
            else if (CurrentViewMode == ViewMode.Sprint && sprint != null)
            {
                isCurrent = day.Date >= sprint.StartDate.Date && day.Date <= sprint.EndDate.Date;
            }

            bool isToday = day.Date == DateTime.Today;

            cells.Add(new CalendarCellViewModel(day, isCurrent, isToday, items));
        }
        CalendarCells = cells;
    }
}
