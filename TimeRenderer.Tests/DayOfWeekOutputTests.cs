using System;
using System.Collections.Generic;

using TimeRenderer.Helpers;
using TimeRenderer.Models;

using TUnit.Assertions;
using TUnit.Assertions.Extensions;
using TUnit.Core;

namespace TimeRenderer.Tests;

/// <summary>
/// 曜日表記と月曜始まりの並び順を使う公開出力の特性テスト。
/// 共通化の前後で、保存済みの曜日順に依存せず表示が変わらないことを固定する。
/// </summary>
public class DayOfWeekOutputTests
{
    private static readonly List<DayOfWeek> UnorderedWeekdays =
    [
        DayOfWeek.Friday,
        DayOfWeek.Monday,
        DayOfWeek.Wednesday,
    ];

    [Test]
    [Arguments(DayOfWeek.Sunday, "日")]
    [Arguments(DayOfWeek.Monday, "月")]
    [Arguments(DayOfWeek.Tuesday, "火")]
    [Arguments(DayOfWeek.Wednesday, "水")]
    [Arguments(DayOfWeek.Thursday, "木")]
    [Arguments(DayOfWeek.Friday, "金")]
    [Arguments(DayOfWeek.Saturday, "土")]
    public async Task 共通定義は曜日と一文字和名を相互変換する(DayOfWeek day, string expectedName)
    {
        var parsed = DayOfWeekHelper.TryParseShortJapaneseName(expectedName, out var parsedDay);

        await Assert.That(DayOfWeekHelper.GetShortJapaneseName(day)).IsEqualTo(expectedName);
        await Assert.That(parsed).IsTrue();
        await Assert.That(parsedDay).IsEqualTo(day);
    }

    [Test]
    public async Task 共通の曜日順は月曜日から日曜日までになる()
    {
        DayOfWeek[] expected =
        [
            DayOfWeek.Monday,
            DayOfWeek.Tuesday,
            DayOfWeek.Wednesday,
            DayOfWeek.Thursday,
            DayOfWeek.Friday,
            DayOfWeek.Saturday,
            DayOfWeek.Sunday,
        ];

        await Assert.That(DayOfWeekHelper.WeekOrder.Count).IsEqualTo(expected.Length);
        for (var i = 0; i < expected.Length; i++)
        {
            await Assert.That(DayOfWeekHelper.WeekOrder[i]).IsEqualTo(expected[i]);
        }
    }

    [Test]
    public async Task 定期予定の曜日は月曜始まりで和名表示する()
    {
        var routine = new RoutineScheduleItem
        {
            Recurrence = RecurrenceType.Weekly,
            Interval = 1,
            DaysOfWeek = [.. UnorderedWeekdays],
        };

        await Assert.That(routine.RecurrenceDisplay).IsEqualTo("毎週 月・水・金");
    }

    [Test]
    public async Task ToDoの繰り返し曜日は月曜始まりで和名表示する()
    {
        var todo = new TodoItem
        {
            Recurrence = TodoRecurrenceUnit.Week,
            RecurrenceInterval = 1,
            RecurrenceDaysOfWeek = [.. UnorderedWeekdays],
        };

        await Assert.That(todo.RecurrenceDisplay).IsEqualTo("毎週 月・水・金");
    }

    [Test]
    public async Task 七曜日すべてを選んだ表示は毎日になる()
    {
        List<DayOfWeek> allDays =
        [
            DayOfWeek.Sunday,
            DayOfWeek.Saturday,
            DayOfWeek.Friday,
            DayOfWeek.Thursday,
            DayOfWeek.Wednesday,
            DayOfWeek.Tuesday,
            DayOfWeek.Monday,
        ];

        var routine = new RoutineScheduleItem
        {
            Recurrence = RecurrenceType.Weekly,
            DaysOfWeek = [.. allDays],
        };
        var todo = new TodoItem
        {
            Recurrence = TodoRecurrenceUnit.Week,
            RecurrenceDaysOfWeek = [.. allDays],
        };

        await Assert.That(routine.RecurrenceDisplay).IsEqualTo("毎日");
        await Assert.That(todo.RecurrenceDisplay).IsEqualTo("毎週 毎日");
    }

    [Test]
    public async Task クイック追加の期限説明には対象日の和名を表示する()
    {
        var now = new DateTime(2026, 8, 27, 10, 30, 0); // 木曜日

        var result = TodoQuickParser.Parse("@明日 X", null, now);

        await Assert.That(result.Summary[0].EndsWith("(金)", StringComparison.Ordinal)).IsTrue();
    }
}
