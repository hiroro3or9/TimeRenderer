using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;

using TimeRenderer.Models;

namespace TimeRenderer.ViewModels;

/// <summary>
/// AppSettings と MainViewModel の対応関係。
/// 各設定を1つの binding として定義し、保存と復元が同じ一覧を通るようにする。
/// </summary>
public partial class MainViewModel
{
    private interface IAppSettingsBinding
    {
        string PropertyName { get; }
        void Capture(MainViewModel viewModel, AppSettings settings);
        void Apply(MainViewModel viewModel, AppSettings settings);
    }

    private sealed class AppSettingsBinding<T>(
        string propertyName,
        Func<MainViewModel, T> capture,
        Action<AppSettings, T> write,
        Func<AppSettings, T> read,
        Action<MainViewModel, T> apply) : IAppSettingsBinding
    {
        public string PropertyName { get; } = propertyName;

        public void Capture(MainViewModel viewModel, AppSettings settings)
            => write(settings, capture(viewModel));

        public void Apply(MainViewModel viewModel, AppSettings settings)
            => apply(viewModel, read(settings));
    }

    private static IAppSettingsBinding Bind<T>(
        string propertyName,
        Func<MainViewModel, T> capture,
        Action<AppSettings, T> write,
        Func<AppSettings, T> read,
        Action<MainViewModel, T> apply)
        => new AppSettingsBinding<T>(propertyName, capture, write, read, apply);

    private static readonly IReadOnlyList<IAppSettingsBinding> AppSettingsBindings =
    [
        Bind(nameof(AppSettings.IsSettingsPanelVisible),
            vm => vm.IsSettingsPanelVisible,
            (settings, value) => settings.IsSettingsPanelVisible = value,
            settings => settings.IsSettingsPanelVisible,
            (vm, value) => vm.ApplySetting(ref vm._isSettingsPanelVisible, value,
                nameof(IsSettingsPanelVisible))),

        Bind(nameof(AppSettings.IsManagementPanelVisible),
            vm => vm.IsManagementPanelVisible,
            (settings, value) => settings.IsManagementPanelVisible = value,
            settings => settings.IsManagementPanelVisible,
            (vm, value) => vm.ApplySetting(ref vm._isManagementPanelVisible, value,
                nameof(IsManagementPanelVisible))),

        Bind(nameof(AppSettings.IsTodoPanelVisible),
            vm => vm.IsTodoPanelVisible,
            (settings, value) => settings.IsTodoPanelVisible = value,
            settings => settings.IsTodoPanelVisible,
            (vm, value) => vm.ApplySetting(ref vm._isTodoPanelVisible, value,
                nameof(IsTodoPanelVisible))),

        Bind(nameof(AppSettings.ShowCompletedTodos),
            vm => vm.ShowCompletedTodos,
            (settings, value) => settings.ShowCompletedTodos = value,
            settings => settings.ShowCompletedTodos,
            (vm, value) => vm.ApplySetting(ref vm._showCompletedTodos, value,
                nameof(ShowCompletedTodos))),

        Bind(nameof(AppSettings.TodoSortMode),
            vm => (int)vm.CurrentTodoSortMode,
            (settings, value) => settings.TodoSortMode = value,
            settings => settings.TodoSortMode,
            (vm, value) => vm.ApplySetting(ref vm._todoSortMode, (TodoSortMode)value,
                nameof(CurrentTodoSortMode), nameof(SelectedTodoSortOption))),

        Bind(nameof(AppSettings.IsTodoDigestEnabled),
            vm => vm.IsTodoDigestEnabled,
            (settings, value) => settings.IsTodoDigestEnabled = value,
            settings => settings.IsTodoDigestEnabled,
            (vm, value) => vm.ApplySetting(ref vm._isTodoDigestEnabled, value,
                nameof(IsTodoDigestEnabled))),

        Bind(nameof(AppSettings.TodoDigestHour),
            vm => vm.TodoDigestHour,
            (settings, value) => settings.TodoDigestHour = value,
            settings => settings.TodoDigestHour,
            (vm, value) => vm.ApplySetting(ref vm._todoDigestHour, value,
                nameof(TodoDigestHour))),

        Bind(nameof(AppSettings.LastTodoDigestDate),
            vm => vm.FormatTodoDigestDate(),
            (settings, value) => settings.LastTodoDigestDate = value,
            settings => settings.LastTodoDigestDate,
            (vm, value) => vm.ParseTodoDigestDate(value)),

        Bind(nameof(AppSettings.TodoArchiveRetentionDays),
            vm => vm.TodoArchiveRetentionDays,
            (settings, value) => settings.TodoArchiveRetentionDays = value,
            settings => settings.TodoArchiveRetentionDays,
            (vm, value) => vm.ApplySetting(ref vm._todoArchiveRetentionDays, value,
                nameof(TodoArchiveRetentionDays))),

        Bind(nameof(AppSettings.IsTodoQuickSyntaxEnabled),
            vm => vm.IsTodoQuickSyntaxEnabled,
            (settings, value) => settings.IsTodoQuickSyntaxEnabled = value,
            settings => settings.IsTodoQuickSyntaxEnabled,
            (vm, value) => vm.ApplySetting(ref vm._isTodoQuickSyntaxEnabled, value,
                nameof(IsTodoQuickSyntaxEnabled))),

        Bind(nameof(AppSettings.TodoDefaultRemindHour),
            vm => vm.TodoDefaultRemindHour,
            (settings, value) => settings.TodoDefaultRemindHour = value,
            settings => settings.TodoDefaultRemindHour,
            (vm, value) => vm.ApplySetting(ref vm._todoDefaultRemindHour, value,
                nameof(TodoDefaultRemindHour))),

        Bind(nameof(AppSettings.IsTodoReminderSoundEnabled),
            vm => vm.IsTodoReminderSoundEnabled,
            (settings, value) => settings.IsTodoReminderSoundEnabled = value,
            settings => settings.IsTodoReminderSoundEnabled,
            (vm, value) => vm.ApplySetting(ref vm._isTodoReminderSoundEnabled, value,
                nameof(IsTodoReminderSoundEnabled))),

        Bind(nameof(AppSettings.TodoSnoozeMinutes),
            vm => vm.TodoSnoozeMinutes,
            (settings, value) => settings.TodoSnoozeMinutes = value,
            settings => settings.TodoSnoozeMinutes,
            (vm, value) => vm.ApplySetting(ref vm._todoSnoozeMinutes, value,
                nameof(TodoSnoozeMinutes), nameof(TodoSnoozeLabel))),

        Bind(nameof(AppSettings.ViewMode),
            vm => (int)vm.CurrentViewMode,
            (settings, value) => settings.ViewMode = value,
            settings => settings.ViewMode,
            (vm, value) =>
            {
                vm.ApplySetting(ref vm._currentViewMode, (ViewMode)value, nameof(CurrentViewMode));
                vm.NotifyViewModeDependents();
            }),

        Bind(nameof(AppSettings.DisplayStartHour),
            vm => vm.DisplayStartHour,
            (settings, value) => settings.DisplayStartHour = value,
            settings => settings.DisplayStartHour,
            (vm, value) => vm.ApplySetting(ref vm._displayStartHour, value,
                nameof(DisplayStartHour))),

        Bind(nameof(AppSettings.DisplayEndHour),
            vm => vm.DisplayEndHour,
            (settings, value) => settings.DisplayEndHour = value,
            settings => settings.DisplayEndHour,
            (vm, value) => vm.ApplySetting(ref vm._displayEndHour, value,
                nameof(DisplayEndHour))),

        Bind(nameof(AppSettings.IsDarkMode),
            vm => vm.IsDarkMode,
            (settings, value) => settings.IsDarkMode = value,
            settings => settings.IsDarkMode,
            (vm, value) =>
            {
                vm.ApplySetting(ref vm._isDarkMode, value, nameof(IsDarkMode));
                App.ApplyTheme(value);
            }),

        Bind(nameof(AppSettings.TimelinePixelsPerDay),
            vm => vm.TimelinePixelsPerDay,
            (settings, value) => settings.TimelinePixelsPerDay = value,
            settings => settings.TimelinePixelsPerDay,
            (vm, value) => vm.ApplySetting(ref vm._timelinePixelsPerDay, value,
                nameof(TimelinePixelsPerDay), nameof(TimelineZoomText))),

        Bind(nameof(AppSettings.TimelineGroupMode),
            vm => (int)vm.CurrentTimelineGroupMode,
            (settings, value) => settings.TimelineGroupMode = value,
            settings => settings.TimelineGroupMode,
            (vm, value) => vm.ApplySetting(ref vm._timelineGroupMode, (TimelineGroupMode)value,
                nameof(CurrentTimelineGroupMode), nameof(SelectedTimelineGroupModeOption),
                nameof(IsTimelineCategoryMode), nameof(TimelineLabelColumnWidth))),

        Bind(nameof(AppSettings.TimelineSprintCount),
            vm => vm.TimelineSprintCount,
            (settings, value) => settings.TimelineSprintCount = value,
            settings => settings.TimelineSprintCount,
            (vm, value) => vm.ApplySetting(ref vm._timelineSprintCount, value,
                nameof(TimelineSprintCount), nameof(SelectedTimelineSpanOption))),

        Bind(nameof(AppSettings.IsAwayDetectionEnabled),
            vm => vm.IsAwayDetectionEnabled,
            (settings, value) => settings.IsAwayDetectionEnabled = value,
            settings => settings.IsAwayDetectionEnabled,
            (vm, value) => vm.ApplySetting(ref vm._isAwayDetectionEnabled, value,
                nameof(IsAwayDetectionEnabled))),

        Bind(nameof(AppSettings.AwayThresholdMinutes),
            vm => vm.AwayThresholdMinutes,
            (settings, value) => settings.AwayThresholdMinutes = value,
            settings => settings.AwayThresholdMinutes,
            (vm, value) => vm.ApplySetting(ref vm._awayThresholdMinutes, value,
                nameof(AwayThresholdMinutes))),

        Bind(nameof(AppSettings.AwayHandlingMode),
            vm => (int)vm.CurrentAwayHandlingMode,
            (settings, value) => settings.AwayHandlingMode = value,
            settings => settings.AwayHandlingMode,
            (vm, value) => vm.ApplySetting(ref vm._awayHandlingMode, (AwayHandlingMode)value,
                nameof(CurrentAwayHandlingMode), nameof(SelectedAwayHandlingOption))),

        Bind(nameof(AppSettings.IsWorkEndDetectionEnabled),
            vm => vm.IsWorkEndDetectionEnabled,
            (settings, value) => settings.IsWorkEndDetectionEnabled = value,
            settings => settings.IsWorkEndDetectionEnabled,
            (vm, value) => vm.ApplySetting(ref vm._isWorkEndDetectionEnabled, value,
                nameof(IsWorkEndDetectionEnabled))),

        Bind(nameof(AppSettings.WorkEndThresholdMinutes),
            vm => vm.WorkEndThresholdMinutes,
            (settings, value) => settings.WorkEndThresholdMinutes = value,
            settings => settings.WorkEndThresholdMinutes,
            (vm, value) => vm.ApplySetting(ref vm._workEndThresholdMinutes, value,
                nameof(WorkEndThresholdMinutes))),

        Bind(nameof(AppSettings.WorkEndEarliestHour),
            vm => vm.WorkEndEarliestHour,
            (settings, value) => settings.WorkEndEarliestHour = value,
            settings => settings.WorkEndEarliestHour,
            (vm, value) => vm.ApplySetting(ref vm._workEndEarliestHour, value,
                nameof(WorkEndEarliestHour), nameof(SelectedWorkEndEarliestOption))),

        Bind(nameof(AppSettings.IsWorkEndReviewEnabled),
            vm => vm.IsWorkEndReviewEnabled,
            (settings, value) => settings.IsWorkEndReviewEnabled = value,
            settings => settings.IsWorkEndReviewEnabled,
            (vm, value) => vm.ApplySetting(ref vm._isWorkEndReviewEnabled, value,
                nameof(IsWorkEndReviewEnabled))),

        Bind(nameof(AppSettings.IsAppUsageTrackingEnabled),
            vm => vm.IsAppUsageTrackingEnabled,
            (settings, value) => settings.IsAppUsageTrackingEnabled = value,
            settings => settings.IsAppUsageTrackingEnabled,
            (vm, value) => vm.ApplySetting(ref vm._isAppUsageTrackingEnabled, value,
                nameof(IsAppUsageTrackingEnabled))),

        Bind(nameof(AppSettings.IsMiniRecordingBarEnabled),
            vm => vm.IsMiniRecordingBarEnabled,
            (settings, value) => settings.IsMiniRecordingBarEnabled = value,
            settings => settings.IsMiniRecordingBarEnabled,
            (vm, value) => vm.ApplySetting(ref vm._isMiniRecordingBarEnabled, value,
                nameof(IsMiniRecordingBarEnabled))),

        Bind(nameof(AppSettings.MiniRecordingBarLeft),
            vm => vm.MiniRecordingBarLeft,
            (settings, value) => settings.MiniRecordingBarLeft = value,
            settings => settings.MiniRecordingBarLeft,
            (vm, value) => vm.ApplySetting(ref vm._miniRecordingBarLeft, value,
                nameof(MiniRecordingBarLeft))),

        Bind(nameof(AppSettings.MiniRecordingBarTop),
            vm => vm.MiniRecordingBarTop,
            (settings, value) => settings.MiniRecordingBarTop = value,
            settings => settings.MiniRecordingBarTop,
            (vm, value) => vm.ApplySetting(ref vm._miniRecordingBarTop, value,
                nameof(MiniRecordingBarTop))),

        Bind(nameof(AppSettings.IsGitCommitLookupEnabled),
            vm => vm.IsGitCommitLookupEnabled,
            (settings, value) => settings.IsGitCommitLookupEnabled = value,
            settings => settings.IsGitCommitLookupEnabled,
            (vm, value) => vm.ApplySetting(ref vm._isGitCommitLookupEnabled, value,
                nameof(IsGitCommitLookupEnabled))),

        Bind(nameof(AppSettings.GitRepositories),
            vm => [.. vm.GitRepositories],
            (settings, value) => settings.GitRepositories = value,
            settings => settings.GitRepositories,
            (vm, value) => vm.LoadGitRepositories(value)),

        Bind(nameof(AppSettings.SnapMinutes),
            vm => vm.SnapMinutes,
            (settings, value) => settings.SnapMinutes = value,
            settings => settings.SnapMinutes,
            (vm, value) => vm.ApplySetting(ref vm._snapMinutes, value,
                nameof(SnapMinutes))),

        Bind(nameof(AppSettings.IsMagnetSnapEnabled),
            vm => vm.IsMagnetSnapEnabled,
            (settings, value) => settings.IsMagnetSnapEnabled = value,
            settings => settings.IsMagnetSnapEnabled,
            (vm, value) => vm.ApplySetting(ref vm._isMagnetSnapEnabled, value,
                nameof(IsMagnetSnapEnabled))),

        Bind(nameof(AppSettings.ManualSprints),
            vm => [.. vm.ManualSprints],
            (settings, value) => settings.ManualSprints = value,
            settings => settings.ManualSprints,
            (vm, value) => vm._manualSprints = value),

        Bind(nameof(AppSettings.Categories),
            vm => [.. vm.Categories],
            (settings, value) => settings.Categories = value,
            settings => settings.Categories,
            (vm, value) => vm.LoadCategories(value)),

        Bind(nameof(AppSettings.RecordingCategoryId),
            vm => vm._recordingCategoryDefaultId,
            (settings, value) => settings.RecordingCategoryId = value,
            settings => settings.RecordingCategoryId,
            (vm, value) => vm.LoadRecordingCategoryId(value)),

        Bind(nameof(AppSettings.ProjectCodes),
            vm => [.. vm.ProjectCodes],
            (settings, value) => settings.ProjectCodes = value,
            settings => settings.ProjectCodes,
            (vm, value) => vm.LoadProjectCodes(value)),

        Bind(nameof(AppSettings.DefaultProjectCodeId),
            vm => vm._defaultProjectCodeId,
            (settings, value) => settings.DefaultProjectCodeId = value,
            settings => settings.DefaultProjectCodeId,
            (vm, value) => vm.LoadDefaultProjectCodeId(value)),

        Bind(nameof(AppSettings.IsUnrecordedTimeProjectAggregationEnabled),
            vm => vm.IsUnrecordedTimeProjectAggregationEnabled,
            (settings, value) => settings.IsUnrecordedTimeProjectAggregationEnabled = value,
            settings => settings.IsUnrecordedTimeProjectAggregationEnabled,
            (vm, value) => vm._isUnrecordedTimeProjectAggregationEnabled = value),

        Bind(nameof(AppSettings.UnrecordedTimeProjectCodeId),
            vm => vm._unrecordedTimeProjectCodeId,
            (settings, value) => settings.UnrecordedTimeProjectCodeId = value,
            settings => settings.UnrecordedTimeProjectCodeId,
            (vm, value) => vm._unrecordedTimeProjectCodeId = value),

        Bind(nameof(AppSettings.PinnedTitles),
            vm => [.. vm.PinnedTitles.Select(title => title.Text)],
            (settings, value) => settings.PinnedTitles = value,
            settings => settings.PinnedTitles,
            (vm, value) => vm.LoadPinnedTitles(value)),

        Bind(nameof(AppSettings.RoutineSchedules),
            vm => [..vm.Routines],
            (settings, value) => settings.RoutineSchedules = value,
            settings => settings.RoutineSchedules,
            (vm, value) =>
            {
                vm._routines = value;
                vm.OnPropertyChanged(nameof(Routines));
            }),

        Bind(nameof(AppSettings.EnabledDaysOfWeek),
            vm => [..vm.EnabledDaysOfWeek],
            (settings, value) => settings.EnabledDaysOfWeek = value,
            settings => settings.EnabledDaysOfWeek,
            (vm, value) => vm._enabledDaysOfWeek = value),
    ];

    private static readonly bool AppSettingsMappingsAreComplete = ValidateAppSettingsBindings();

    internal static IReadOnlyList<string> MappedAppSettingsPropertyNames =>
        [.. AppSettingsBindings.Select(binding => binding.PropertyName)];

    private static bool ValidateAppSettingsBindings()
    {
        var mappedNames = AppSettingsBindings.Select(binding => binding.PropertyName).ToList();
        var duplicateNames = mappedNames
            .GroupBy(name => name, StringComparer.Ordinal)
            .Where(group => group.Count() > 1)
            .Select(group => group.Key)
            .ToList();
        var actualNames = typeof(AppSettings)
            .GetProperties(BindingFlags.Instance | BindingFlags.Public)
            .Select(property => property.Name)
            .ToHashSet(StringComparer.Ordinal);
        var missingNames = actualNames.Except(mappedNames, StringComparer.Ordinal).ToList();
        var unknownNames = mappedNames.Except(actualNames, StringComparer.Ordinal).ToList();

        if (duplicateNames.Count == 0 && missingNames.Count == 0 && unknownNames.Count == 0)
            return true;

        throw new InvalidOperationException(
            $"AppSettings mapping is incomplete. " +
            $"Missing=[{string.Join(", ", missingNames)}], " +
            $"Duplicate=[{string.Join(", ", duplicateNames)}], " +
            $"Unknown=[{string.Join(", ", unknownNames)}]");
    }

    private static void CaptureAppSettings(MainViewModel viewModel, AppSettings settings)
    {
        _ = AppSettingsMappingsAreComplete;
        foreach (var binding in AppSettingsBindings) binding.Capture(viewModel, settings);
    }

    private static void ApplyAppSettings(MainViewModel viewModel, AppSettings settings)
    {
        _ = AppSettingsMappingsAreComplete;
        foreach (var binding in AppSettingsBindings) binding.Apply(viewModel, settings);
    }

    private void ApplySetting<T>(ref T field, T value, params string[] propertyNames)
    {
        field = value;
        foreach (var propertyName in propertyNames) OnPropertyChanged(propertyName);
    }

    private void FinalizeAppSettingsApplication()
    {
        // 複数設定に依存する副作用は、全 binding の適用後に1回だけ実行する。
        ApplyDefaultRemindHour();
        OnPropertyChanged(nameof(ScheduleGridHeight));
        InitializeTimeLabels();
        ApplyAwaySettings();

        LoadUnrecordedTimeProjectAggregation(
            _isUnrecordedTimeProjectAggregationEnabled,
            _unrecordedTimeProjectCodeId);
        RefreshSprintProjectCodeLabels(_manualSprints);
        OnPropertyChanged(nameof(ManualSprints));

        NotifyShowDaysProperties();
        OnPropertyChanged(nameof(EnabledDaysCount));
        OnPropertyChanged(nameof(EnabledDayHeaders));
        UpdateVisibleDays();

        // 起動時の旧データ移行は、関連する設定・マスターをすべて読み込んだ後に行う。
        MigrateRoutineStartDates();
        MigrateGeneratedRoutineItems();
        EnsureRoutineOccurrences(CurrentDate);
    }
}
