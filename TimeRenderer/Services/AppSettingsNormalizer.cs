using System;
using System.Collections.Generic;
using System.Linq;

using TimeRenderer.Helpers;
using TimeRenderer.Models;
using TimeRenderer.ViewModels;

namespace TimeRenderer.Services;

/// <summary>
/// 読み込んだ設定値を、画面へ適用できる範囲へ補正する。
/// JSON の読み書きや画面状態には依存しないため、旧形式・不正値の互換性を単体で検証できる。
/// </summary>
public static class AppSettingsNormalizer
{
    private static readonly IReadOnlyList<int> SnoozeOptions =
        Array.AsReadOnly([5, 10, 15, 30, 60, 120]);

    public static IReadOnlyList<int> TodoSnoozeOptions => SnoozeOptions;

    /// <summary>設定を現在の有効範囲へ補正し、同じインスタンスを返す。</summary>
    public static AppSettings Normalize(AppSettings settings)
    {
        ArgumentNullException.ThrowIfNull(settings);

        // 同じ位置に表示するパネルは、設定 > 管理 > ToDo の優先順位で1つだけ開く。
        settings.IsManagementPanelVisible &= !settings.IsSettingsPanelVisible;
        settings.IsTodoPanelVisible &= !settings.IsSettingsPanelVisible
                                       && !settings.IsManagementPanelVisible;

        settings.TodoSortMode = NormalizeEnum(settings.TodoSortMode, TodoSortMode.DueDate);
        settings.TodoDigestHour = Math.Clamp(settings.TodoDigestHour, 0, 23);
        settings.TodoArchiveRetentionDays = Math.Clamp(
            settings.TodoArchiveRetentionDays <= 0 ? 90 : settings.TodoArchiveRetentionDays,
            7,
            3650);
        settings.TodoDefaultRemindHour = Math.Clamp(settings.TodoDefaultRemindHour, 0, 23);
        settings.TodoSnoozeMinutes = SnoozeOptions.Contains(settings.TodoSnoozeMinutes)
            ? settings.TodoSnoozeMinutes
            : 10;

        settings.ViewMode = NormalizeEnum(settings.ViewMode, ViewModels.ViewMode.Today);
        settings.DisplayStartHour = Math.Clamp(settings.DisplayStartHour, 0, 23);
        settings.DisplayEndHour = Math.Clamp(settings.DisplayEndHour, settings.DisplayStartHour + 1, 24);

        settings.TimelinePixelsPerDay = double.IsFinite(settings.TimelinePixelsPerDay)
                                        && settings.TimelinePixelsPerDay > 0
            ? Math.Clamp(
                settings.TimelinePixelsPerDay,
                TimelineScale.MinPixelsPerDay,
                TimelineScale.MaxPixelsPerDay)
            : TimelineScale.DefaultPixelsPerDay;
        settings.TimelineGroupMode = NormalizeEnum(
            settings.TimelineGroupMode,
            ViewModels.TimelineGroupMode.Packed);
        settings.TimelineSprintCount = Math.Clamp(
            settings.TimelineSprintCount <= 0 ? 5 : settings.TimelineSprintCount,
            1,
            25);

        settings.AwayThresholdMinutes = Math.Clamp(
            settings.AwayThresholdMinutes <= 0 ? 10 : settings.AwayThresholdMinutes,
            1,
            240);
        settings.AwayHandlingMode = NormalizeEnum(
            settings.AwayHandlingMode,
            ViewModels.AwayHandlingMode.Ask);
        settings.WorkEndThresholdMinutes = Math.Clamp(
            settings.WorkEndThresholdMinutes <= 0 ? 30 : settings.WorkEndThresholdMinutes,
            5,
            480);
        settings.WorkEndEarliestHour = Math.Clamp(settings.WorkEndEarliestHour, 0, 23);
        settings.SnapMinutes = Math.Clamp(settings.SnapMinutes <= 0 ? 15 : settings.SnapMinutes, 1, 60);

        // JSON に明示的な null が保存されていても、以降の処理では空コレクションとして扱う。
        settings.GitRepositories ??= [];
        settings.ManualSprints ??= [];
        settings.Categories ??= [];
        settings.ProjectCodes ??= [];
        settings.RoutineSchedules ??= [];
        if (settings.EnabledDaysOfWeek is not { Count: > 0 })
        {
            settings.EnabledDaysOfWeek = [.. DayOfWeekHelper.WeekOrder];
        }

        return settings;
    }

    private static int NormalizeEnum<TEnum>(int value, TEnum fallback)
        where TEnum : struct, Enum
        => Enum.IsDefined(typeof(TEnum), value) ? value : Convert.ToInt32(fallback);
}
