using System;
using System.Collections.Generic;
using System.Linq;

using TimeRenderer.Models;

namespace TimeRenderer.ViewModels;

/// <summary>今日の判断に必要な予定・ToDo・実績をまとめるホーム画面。</summary>
public partial class MainViewModel
{
    public sealed record TodayPlanEntry(
        ScheduleItem Item,
        string TimeText,
        string DurationText,
        string CategoryName,
        bool IsNext,
        bool IsPast);

    private IReadOnlyList<TodayPlanEntry> _todayPlanItems = [];
    public IReadOnlyList<TodayPlanEntry> TodayPlanItems
    {
        get => _todayPlanItems;
        private set
        {
            if (SetProperty(ref _todayPlanItems, value))
            {
                OnPropertyChanged(nameof(HasTodayPlans));
            }
        }
    }

    public bool HasTodayPlans => TodayPlanItems.Count > 0;

    private IReadOnlyList<TodoItem> _todayTodoItems = [];
    public IReadOnlyList<TodoItem> TodayTodoItems
    {
        get => _todayTodoItems;
        private set
        {
            if (SetProperty(ref _todayTodoItems, value))
            {
                OnPropertyChanged(nameof(HasTodayTodos));
            }
        }
    }

    public bool HasTodayTodos => TodayTodoItems.Count > 0;

    private TodayPlanEntry? _todayNextPlan;
    public TodayPlanEntry? TodayNextPlan
    {
        get => _todayNextPlan;
        private set
        {
            if (SetProperty(ref _todayNextPlan, value))
            {
                OnPropertyChanged(nameof(HasTodayNextPlan));
            }
        }
    }

    public bool HasTodayNextPlan => TodayNextPlan != null;

    private string _todayRecordedSummaryText = "0:00";
    public string TodayRecordedSummaryText
    {
        get => _todayRecordedSummaryText;
        private set => SetProperty(ref _todayRecordedSummaryText, value);
    }

    private string _todayPlannedSummaryText = "0:00";
    public string TodayPlannedSummaryText
    {
        get => _todayPlannedSummaryText;
        private set => SetProperty(ref _todayPlannedSummaryText, value);
    }

    private string _todayGapSummaryText = "なし";
    public string TodayGapSummaryText
    {
        get => _todayGapSummaryText;
        private set => SetProperty(ref _todayGapSummaryText, value);
    }

    private string _todayWorkloadSummaryText = "見積もり未設定";
    public string TodayWorkloadSummaryText
    {
        get => _todayWorkloadSummaryText;
        private set => SetProperty(ref _todayWorkloadSummaryText, value);
    }

    private bool _isTodayWorkloadOver;
    public bool IsTodayWorkloadOver
    {
        get => _isTodayWorkloadOver;
        private set => SetProperty(ref _isTodayWorkloadOver, value);
    }

    /// <summary>現在のデータから今日ホーム用の小さな読み取りモデルを作り直す。</summary>
    private void RebuildTodayOverview()
    {
        if (ScheduleItems == null) return;

        var now = DateTime.Now;
        var dayStart = now.Date;
        var dayEnd = dayStart.AddDays(1);

        var plans = ScheduleItems
            .Where(i => i.IsPlanned && i.StartTime < dayEnd && i.EndTime > dayStart)
            .OrderBy(i => i.StartTime)
            .ThenBy(i => i.Title)
            .ToList();

        var next = plans.FirstOrDefault(i => i.EndTime > now);
        TodayPlanItems = [.. plans.Select(item => new TodayPlanEntry(
            item,
            item.IsAllDay ? "終日" : $"{item.StartTime:HH:mm}–{item.EndTime:HH:mm}",
            FormatTodayDuration(ClipDuration(item, dayStart, dayEnd)),
            ResolveCategory(item)?.Name ?? "未分類",
            ReferenceEquals(item, next),
            item.EndTime <= now))];
        TodayNextPlan = TodayPlanItems.FirstOrDefault(p => p.IsNext);

        TodayTodoItems = [.. Todos
            .Where(t => !t.IsCompleted && t.IsPlannedToday)
            .OrderByDescending(t => t.Priority)
            .ThenBy(t => t.DueDate ?? DateTime.MaxValue)
            .ThenBy(t => t.SortOrder)];

        var recorded = ScheduleItems
            .Where(i => i.IsRecorded && !i.IsAllDay && i.StartTime < dayEnd && i.EndTime > dayStart)
            .Sum(i => ClipDuration(i, dayStart, dayEnd).Ticks);
        var planned = plans
            .Where(i => !i.IsAllDay)
            .Sum(i => ClipDuration(i, dayStart, dayEnd).Ticks);

        var todayGaps = UnrecordedGaps
            .Where(g => g.StartTime < dayEnd && g.EndTime > dayStart)
            .ToList();
        var gapTicks = todayGaps.Sum(g =>
            (Min(g.EndTime, dayEnd) - Max(g.StartTime, dayStart)).Ticks);

        TodayRecordedSummaryText = FormatTodayDuration(TimeSpan.FromTicks(recorded));
        TodayPlannedSummaryText = FormatTodayDuration(TimeSpan.FromTicks(planned));
        TodayGapSummaryText = todayGaps.Count == 0
            ? "なし"
            : $"{FormatTodayDuration(TimeSpan.FromTicks(gapTicks))}・{todayGaps.Count}件";
        TodayWorkloadSummaryText = TodayWorkload?.Text ?? "見積もり未設定";
        IsTodayWorkloadOver = TodayWorkload?.IsOver == true;
    }

    private static TimeSpan ClipDuration(ScheduleItem item, DateTime start, DateTime end)
    {
        var clippedStart = Max(item.StartTime, start);
        var clippedEnd = Min(item.EndTime, end);
        return clippedEnd > clippedStart ? clippedEnd - clippedStart : TimeSpan.Zero;
    }

    private static DateTime Max(DateTime left, DateTime right) => left > right ? left : right;
    private static DateTime Min(DateTime left, DateTime right) => left < right ? left : right;

    private static string FormatTodayDuration(TimeSpan duration) =>
        $"{(int)duration.TotalHours}:{duration.Minutes:D2}";
}
