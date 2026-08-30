using System;
using System.Collections.Generic;

using TimeRenderer.Models;
using TimeRenderer.Services;
using TimeRenderer.ViewModels;

namespace TimeRenderer.Tests;

/// <summary>画面を開かずに MainViewModel の操作経路を通すためのダイアログ代替。</summary>
internal sealed class TestDialogService : IDialogService
{
    public bool ConfirmationResult { get; set; } = true;
    public bool AwayReviewResult { get; set; }
    public List<(string Message, string Title)> Messages { get; } = [];

    public ScheduleItem? ShowScheduleEditDialog(
        ScheduleItem? initialItem = null,
        IReadOnlyList<CategoryInfo>? categories = null,
        IReadOnlyList<string>? titleSuggestions = null,
        IReadOnlyList<ProjectCodeInfo>? projectCodes = null,
        ProjectCodeInfo? defaultProjectCode = null) => null;

    public RoutineScheduleItem? ShowRoutineEditDialog(
        RoutineScheduleItem? initialRoutine = null,
        IReadOnlyList<CategoryInfo>? categories = null,
        IReadOnlyList<string>? titleSuggestions = null,
        IReadOnlyList<ProjectCodeInfo>? projectCodes = null,
        ProjectCodeInfo? defaultProjectCode = null) => null;

    public TodoItem? ShowTodoEditDialog(
        TodoItem? initialTodo = null,
        IReadOnlyList<CategoryInfo>? categories = null,
        IReadOnlyList<string>? titleSuggestions = null,
        TodoEstimateStats? estimateStats = null) => null;

    public TodoItem? ShowTodoPickerDialog(
        string message,
        IReadOnlyList<TodoItem> todos) => null;

    public (string Title, TimerOption SelectedOption, string? ProjectCodeId)? ShowRecordingStartDialog(
        string defaultTitle,
        List<TimerOption> timerOptions,
        TimerOption defaultOption,
        IReadOnlyList<string>? titleSuggestions = null,
        IReadOnlyList<ProjectCodeInfo>? projectCodes = null,
        ProjectCodeInfo? defaultProjectCode = null) => null;

    public bool ShowConfirmationDialog(string message, string title) => ConfirmationResult;

    public RoutineScope? ShowRoutineScopeDialog(string message, string title) => null;

    public void ShowMessage(string message, string title) => Messages.Add((message, title));

    public WorkDayEditResult? ShowWorkDayEditDialog(
        DateTime date,
        DateTime? start,
        DateTime? end,
        bool canDelete,
        string note) => null;

    public WorkEndReviewResult ShowWorkEndReviewDialog(
        DateTime date,
        DateTime start,
        DateTime end,
        TimeSpan recorded,
        int completedCount,
        IReadOnlyList<WorkEndCarryOver> candidates,
        IReadOnlyList<GitCommit> commits,
        string initialNote) => new([], initialNote);

    public bool ShowAwayReviewDialog(
        string recordTitle,
        DateTime recordStart,
        DateTime recordEnd,
        IReadOnlyList<AwayPeriod> awayPeriods) => AwayReviewResult;

    public void ShowAppUsageDialog(
        string itemTitle,
        DateTime rangeStart,
        DateTime rangeEnd,
        IReadOnlyList<AppUsageStat> stats)
    {
    }

    public GapFillResult? ShowGapFillDialog(
        DateTime start,
        DateTime end,
        GapFillSuggestion suggestion,
        IReadOnlyList<CategoryInfo> categories,
        IReadOnlyList<ProjectCodeInfo> projectCodes) => null;

    public string? ShowFolderPicker(string description) => null;
}

/// <summary>テストから明示的に進められるローカル時刻。</summary>
internal sealed class ManualTimeProvider(DateTime initialNow) : TimeProvider
{
    private DateTimeOffset _utcNow = new(
        DateTime.SpecifyKind(initialNow, DateTimeKind.Utc));

    public override DateTimeOffset GetUtcNow() => _utcNow;

    public override TimeZoneInfo LocalTimeZone => TimeZoneInfo.Utc;

    public void Advance(TimeSpan amount) => _utcNow = _utcNow.Add(amount);
}
