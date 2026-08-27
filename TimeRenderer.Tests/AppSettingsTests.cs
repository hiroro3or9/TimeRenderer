using System;
using System.Text.Json;

using TimeRenderer.Helpers;
using TimeRenderer.Models;
using TimeRenderer.Services;
using TimeRenderer.ViewModels;

using TUnit.Assertions;
using TUnit.Assertions.Extensions;
using TUnit.Core;

namespace TimeRenderer.Tests;

/// <summary>
/// 設定の既定値、旧形式との互換性、不正値の補正、JSON 保存往復を固定する。
/// 実ファイルを使わず、設定契約だけを検証する。
/// </summary>
public class AppSettingsTests
{
    [Test]
    public async Task 空の旧形式JSONは現在の既定値で補完される()
    {
        var settings = JsonSerializer.Deserialize<AppSettings>("{}")!;

        await Assert.That(settings.ViewMode).IsEqualTo((int)ViewMode.Today);
        await Assert.That(settings.TodoDigestHour).IsEqualTo(9);
        await Assert.That(settings.TodoArchiveRetentionDays).IsEqualTo(90);
        await Assert.That(settings.DisplayStartHour).IsEqualTo(0);
        await Assert.That(settings.DisplayEndHour).IsEqualTo(24);
        await Assert.That(settings.EnabledDaysOfWeek.Count).IsEqualTo(7);
    }

    [Test]
    public async Task 代表的な設定値はJSON保存往復で維持される()
    {
        var original = new AppSettings
        {
            IsTodoPanelVisible = true,
            TodoSortMode = (int)TodoSortMode.Manual,
            TodoDigestHour = 18,
            LastTodoDigestDate = "2026-08-27",
            TodoSnoozeMinutes = 60,
            ViewMode = (int)ViewMode.SprintTimeline,
            DisplayStartHour = 7,
            DisplayEndHour = 21,
            TimelinePixelsPerDay = 240,
            TimelineGroupMode = (int)TimelineGroupMode.Category,
            TimelineSprintCount = 9,
            AwayHandlingMode = (int)AwayHandlingMode.AlwaysExclude,
            SnapMinutes = 30,
            RecordingCategoryId = "category-1",
            DefaultProjectCodeId = "project-1",
            PinnedTitles = ["レビュー", "実装"],
            EnabledDaysOfWeek = [DayOfWeek.Monday, DayOfWeek.Wednesday, DayOfWeek.Friday],
        };

        var json = JsonSerializer.Serialize(original);
        var restored = JsonSerializer.Deserialize<AppSettings>(json)!;

        await Assert.That(restored.IsTodoPanelVisible).IsTrue();
        await Assert.That(restored.TodoSortMode).IsEqualTo((int)TodoSortMode.Manual);
        await Assert.That(restored.LastTodoDigestDate).IsEqualTo("2026-08-27");
        await Assert.That(restored.TimelinePixelsPerDay).IsEqualTo(240);
        await Assert.That(restored.RecordingCategoryId).IsEqualTo("category-1");
        await Assert.That(restored.PinnedTitles).IsEquivalentTo(["レビュー", "実装"]);
        await Assert.That(restored.EnabledDaysOfWeek)
            .IsEquivalentTo([DayOfWeek.Monday, DayOfWeek.Wednesday, DayOfWeek.Friday]);
    }

    [Test]
    public async Task 不正な設定値は安全な範囲へ補正される()
    {
        var settings = new AppSettings
        {
            TodoSortMode = int.MaxValue,
            TodoDigestHour = 99,
            TodoArchiveRetentionDays = 0,
            TodoDefaultRemindHour = -1,
            TodoSnoozeMinutes = 11,
            ViewMode = int.MaxValue,
            DisplayStartHour = 99,
            DisplayEndHour = -1,
            TimelinePixelsPerDay = double.NaN,
            TimelineGroupMode = int.MaxValue,
            TimelineSprintCount = 0,
            AwayThresholdMinutes = 0,
            AwayHandlingMode = int.MaxValue,
            WorkEndThresholdMinutes = 0,
            WorkEndEarliestHour = 99,
            SnapMinutes = 0,
            GitRepositories = null!,
            ManualSprints = null!,
            Categories = null!,
            ProjectCodes = null!,
            RoutineSchedules = null!,
            EnabledDaysOfWeek = null!,
        };

        var normalized = AppSettingsNormalizer.Normalize(settings);

        await Assert.That(normalized).IsSameReferenceAs(settings);
        await Assert.That(settings.TodoSortMode).IsEqualTo((int)TodoSortMode.DueDate);
        await Assert.That(settings.TodoDigestHour).IsEqualTo(23);
        await Assert.That(settings.TodoArchiveRetentionDays).IsEqualTo(90);
        await Assert.That(settings.TodoDefaultRemindHour).IsEqualTo(0);
        await Assert.That(settings.TodoSnoozeMinutes).IsEqualTo(10);
        await Assert.That(settings.ViewMode).IsEqualTo((int)ViewMode.Today);
        await Assert.That(settings.DisplayStartHour).IsEqualTo(23);
        await Assert.That(settings.DisplayEndHour).IsEqualTo(24);
        await Assert.That(settings.TimelinePixelsPerDay).IsEqualTo(TimelineScale.DefaultPixelsPerDay);
        await Assert.That(settings.TimelineGroupMode).IsEqualTo((int)TimelineGroupMode.Packed);
        await Assert.That(settings.TimelineSprintCount).IsEqualTo(5);
        await Assert.That(settings.AwayThresholdMinutes).IsEqualTo(10);
        await Assert.That(settings.AwayHandlingMode).IsEqualTo((int)AwayHandlingMode.Ask);
        await Assert.That(settings.WorkEndThresholdMinutes).IsEqualTo(30);
        await Assert.That(settings.WorkEndEarliestHour).IsEqualTo(23);
        await Assert.That(settings.SnapMinutes).IsEqualTo(15);
        await Assert.That(settings.GitRepositories).IsEmpty();
        await Assert.That(settings.ManualSprints).IsEmpty();
        await Assert.That(settings.Categories).IsEmpty();
        await Assert.That(settings.ProjectCodes).IsEmpty();
        await Assert.That(settings.RoutineSchedules).IsEmpty();
        await Assert.That(settings.EnabledDaysOfWeek).IsEquivalentTo(DayOfWeekHelper.WeekOrder);
    }

    [Test]
    public async Task 同じ位置のパネルが複数指定されたら優先順位に従って一つだけ残す()
    {
        var settings = new AppSettings
        {
            IsSettingsPanelVisible = true,
            IsManagementPanelVisible = true,
            IsTodoPanelVisible = true,
        };

        AppSettingsNormalizer.Normalize(settings);

        await Assert.That(settings.IsSettingsPanelVisible).IsTrue();
        await Assert.That(settings.IsManagementPanelVisible).IsFalse();
        await Assert.That(settings.IsTodoPanelVisible).IsFalse();
    }

    [Test]
    public async Task 有効な境界値と選択値は補正せず維持する()
    {
        var settings = new AppSettings
        {
            TodoSortMode = (int)TodoSortMode.Priority,
            TodoDigestHour = 0,
            TodoArchiveRetentionDays = 3650,
            TodoDefaultRemindHour = 23,
            TodoSnoozeMinutes = 120,
            ViewMode = (int)ViewMode.Stats,
            DisplayStartHour = 22,
            DisplayEndHour = 23,
            TimelinePixelsPerDay = TimelineScale.MinPixelsPerDay,
            TimelineGroupMode = (int)TimelineGroupMode.Flat,
            TimelineSprintCount = 25,
            AwayThresholdMinutes = 240,
            AwayHandlingMode = (int)AwayHandlingMode.AlwaysKeep,
            WorkEndThresholdMinutes = 480,
            WorkEndEarliestHour = 0,
            SnapMinutes = 60,
        };

        AppSettingsNormalizer.Normalize(settings);

        await Assert.That(settings.TodoSortMode).IsEqualTo((int)TodoSortMode.Priority);
        await Assert.That(settings.TodoSnoozeMinutes).IsEqualTo(120);
        await Assert.That(settings.ViewMode).IsEqualTo((int)ViewMode.Stats);
        await Assert.That(settings.DisplayStartHour).IsEqualTo(22);
        await Assert.That(settings.DisplayEndHour).IsEqualTo(23);
        await Assert.That(settings.TimelineGroupMode).IsEqualTo((int)TimelineGroupMode.Flat);
        await Assert.That(settings.AwayHandlingMode).IsEqualTo((int)AwayHandlingMode.AlwaysKeep);
        await Assert.That(settings.SnapMinutes).IsEqualTo(60);
    }
}
