using System;
using System.Collections.Generic;
using System.Linq;

using TimeRenderer.Helpers;
using TimeRenderer.Models;

using TUnit.Assertions;
using TUnit.Assertions.Extensions;
using TUnit.Core;

namespace TimeRenderer.Tests;

/// <summary>定期予定の発生日と仮想アイテム生成の境界を固定する。</summary>
public class RoutineOccurrencePlannerTests
{
    private static readonly DateTime Around = new(2026, 8, 30);

    [Test]
    public async Task 開始日から先60日までの日次予定を生成する()
    {
        var routine = DailyRoutine("daily", Around);

        var items = RoutineOccurrencePlanner.BuildVirtualItems(
            [routine], [], [], Around);

        await Assert.That(items.Count).IsEqualTo(61);
        await Assert.That(items[0].StartTime).IsEqualTo(Around.AddHours(9));
        await Assert.That(items[^1].StartTime).IsEqualTo(Around.AddDays(60).AddHours(9));
        await Assert.That(items.All(i => i.IsVirtual)).IsTrue();
        await Assert.That(items.All(i => i.IsPlanned)).IsTrue();
    }

    [Test]
    public async Task 除外日と生成済み日を重複生成しない()
    {
        var routine = DailyRoutine("daily", Around);
        routine.ExcludedDates = [Around.AddDays(1).AddHours(12)];
        var existing = new ScheduleItem
        {
            Kind = ScheduleItemKind.Planned,
            StartTime = Around.AddDays(2).AddHours(10),
            EndTime = Around.AddDays(2).AddHours(11),
            RoutineId = routine.Id,
        };

        var items = RoutineOccurrencePlanner.BuildVirtualItems(
            [routine], [existing], [], Around);

        await Assert.That(items.Any(i => i.StartTime.Date == Around.AddDays(1))).IsFalse();
        await Assert.That(items.Any(i => i.StartTime.Date == Around.AddDays(2))).IsFalse();
        await Assert.That(items.Any(i => i.StartTime.Date == Around)).IsTrue();
    }

    [Test]
    public async Task 無効または時刻範囲が壊れた定期予定は生成しない()
    {
        var disabled = DailyRoutine("disabled", Around);
        disabled.IsEnabled = false;
        var invalid = DailyRoutine("invalid", Around);
        invalid.EndTime = invalid.StartTime;

        var items = RoutineOccurrencePlanner.BuildVirtualItems(
            [disabled, invalid], [], [], Around);

        await Assert.That(items).IsEmpty();
    }

    [Test]
    public async Task コードを指定しない定期予定は未設定のまま生成する()
    {
        var routine = DailyRoutine("unassigned", Around);
        routine.ProjectCodeId = null;

        var item = RoutineOccurrencePlanner.BuildVirtualItems(
            [routine], [], [], Around)[0];

        await Assert.That(item.ProjectCodeId).IsNull();
    }

    [Test]
    public async Task カテゴリ色と定期予定固有のプロジェクトを優先する()
    {
        var routine = DailyRoutine("mapped", Around);
        routine.CategoryId = "category-1";
        routine.ColorCode = "#111111";
        routine.ProjectCodeId = "routine-project";
        var categories = new List<CategoryInfo>
        {
            new() { Id = "category-1", ColorCode = "#abcdef" },
        };

        var item = RoutineOccurrencePlanner.BuildVirtualItems(
            [routine], [], categories, Around)[0];

        await Assert.That(item.ColorCode).IsEqualTo("#FFABCDEF");
        await Assert.That(item.CategoryId).IsEqualTo("category-1");
        await Assert.That(item.ProjectCodeId).IsEqualTo("routine-project");
        await Assert.That(item.Id).IsEqualTo("routine:mapped:20260830");
    }

    [Test]
    public async Task 月末指定は日数が足りない月の末日に発生する()
    {
        var routine = new RoutineScheduleItem
        {
            Id = "month-end",
            Title = "月末",
            Recurrence = RecurrenceType.MonthlyByDate,
            DayOfMonth = 31,
            StartDate = new DateTime(2026, 1, 31),
            StartTime = TimeSpan.FromHours(9),
            EndTime = TimeSpan.FromHours(10),
        };

        await Assert.That(routine.OccursOn(new DateTime(2026, 2, 28))).IsTrue();
        await Assert.That(routine.OccursOn(new DateTime(2026, 2, 27))).IsFalse();
        await Assert.That(routine.OccursOn(new DateTime(2026, 4, 30))).IsTrue();
    }

    [Test]
    public async Task 最終曜日指定は月の最後の対象曜日だけに発生する()
    {
        var routine = new RoutineScheduleItem
        {
            Id = "last-monday",
            Title = "月末週",
            Recurrence = RecurrenceType.MonthlyByWeekday,
            DaysOfWeek = [DayOfWeek.Monday],
            WeeksOfMonth = [RoutineScheduleItem.LastWeekOfMonth],
            StartDate = new DateTime(2026, 8, 1),
            StartTime = TimeSpan.FromHours(9),
            EndTime = TimeSpan.FromHours(10),
        };

        await Assert.That(routine.OccursOn(new DateTime(2026, 8, 24))).IsFalse();
        await Assert.That(routine.OccursOn(new DateTime(2026, 8, 31))).IsTrue();
    }

    [Test]
    public async Task 休み周期は開始後の発生回数で判定する()
    {
        var routine = new RoutineScheduleItem
        {
            Id = "skip",
            Title = "一回おき",
            Recurrence = RecurrenceType.Weekly,
            DaysOfWeek = [DayOfWeek.Monday],
            StartDate = new DateTime(2026, 8, 24),
            StartTime = TimeSpan.FromHours(9),
            EndTime = TimeSpan.FromHours(10),
            SkipEvery = 2,
            SkipIndex = 2,
        };

        await Assert.That(routine.OccursOn(new DateTime(2026, 8, 24))).IsTrue();
        await Assert.That(routine.OccursOn(new DateTime(2026, 8, 31))).IsFalse();
        await Assert.That(routine.OccursOn(new DateTime(2026, 9, 7))).IsTrue();
    }

    private static RoutineScheduleItem DailyRoutine(string id, DateTime startDate) => new()
    {
        Id = id,
        Title = "日次",
        Recurrence = RecurrenceType.Weekly,
        DaysOfWeek =
        [
            DayOfWeek.Monday,
            DayOfWeek.Tuesday,
            DayOfWeek.Wednesday,
            DayOfWeek.Thursday,
            DayOfWeek.Friday,
            DayOfWeek.Saturday,
            DayOfWeek.Sunday,
        ],
        StartDate = startDate,
        StartTime = TimeSpan.FromHours(9),
        EndTime = TimeSpan.FromHours(10),
    };
}
