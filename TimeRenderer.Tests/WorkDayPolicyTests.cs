using System;

using TimeRenderer.Helpers;
using TimeRenderer.Models;

using TUnit.Assertions;
using TUnit.Assertions.Extensions;
using TUnit.Core;

namespace TimeRenderer.Tests;

/// <summary>勤務日の自動締めと離席確認の日付・時刻境界を固定する。</summary>
public class WorkDayPolicyTests
{
    private static readonly DateTime Start = new(2026, 8, 30, 9, 0, 0);

    [Test]
    public async Task 前日以前の勤務だけを自動締め対象にする()
    {
        var log = new WorkDayLog { Date = Start.Date, StartTime = Start };

        await Assert.That(WorkDayPolicy.ShouldAutoClose(log, Start.AddHours(8), isRecording: false)).IsFalse();
        await Assert.That(WorkDayPolicy.ShouldAutoClose(log, Start.Date.AddDays(1), isRecording: false)).IsTrue();
        await Assert.That(WorkDayPolicy.ShouldAutoClose(null, Start.Date.AddDays(1), isRecording: false)).IsFalse();
    }

    [Test]
    public async Task 日をまたいでも記録中は勤務を自動締めしない()
    {
        var log = new WorkDayLog { Date = Start.Date, StartTime = Start };

        var shouldClose = WorkDayPolicy.ShouldAutoClose(
            log, Start.Date.AddDays(1).AddMinutes(1), isRecording: true);

        await Assert.That(shouldClose).IsFalse();
    }

    [Test]
    public async Task 最後の活動は当日の実績だけから選ぶ()
    {
        var log = new WorkDayLog { Date = Start.Date, StartTime = Start };
        var valid = Recorded(Start.AddHours(1), Start.AddHours(2));
        var later = Recorded(Start.AddHours(3), Start.AddHours(4));
        var planned = Recorded(Start.AddHours(5), Start.AddHours(6));
        planned.Kind = ScheduleItemKind.Planned;
        var allDay = Recorded(Start.AddHours(6), Start.AddHours(7));
        allDay.IsAllDay = true;
        var nextDay = Recorded(Start.Date.AddDays(1), Start.Date.AddDays(1).AddHours(1));

        var end = WorkDayPolicy.FindLastActivityEnd(
            log, [nextDay, planned, valid, allDay, later]);

        await Assert.That(end).IsEqualTo(Start.AddHours(4));
    }

    [Test]
    public async Task 実績が無ければ出勤時刻で締める()
    {
        var log = new WorkDayLog { Date = Start.Date, StartTime = Start };

        var end = WorkDayPolicy.FindLastActivityEnd(log, []);

        await Assert.That(end).IsEqualTo(Start);
    }

    [Test]
    public async Task 勤務日に始まった日またぎ実績は翌日の終了時刻を採用する()
    {
        var log = new WorkDayLog { Date = Start.Date, StartTime = Start };
        var overnight = Recorded(
            Start.Date.AddHours(23),
            Start.Date.AddDays(1).AddMinutes(30));

        var end = WorkDayPolicy.FindLastActivityEnd(log, [overnight]);

        await Assert.That(end).IsEqualTo(Start.Date.AddDays(1).AddMinutes(30));
    }

    [Test]
    public async Task 退勤時刻は出勤時刻より前へ戻さない()
    {
        var log = new WorkDayLog { Date = Start.Date, StartTime = Start };

        await Assert.That(WorkDayPolicy.ClampEnd(log, Start.AddMinutes(-30))).IsEqualTo(Start);
        await Assert.That(WorkDayPolicy.ClampEnd(log, Start.AddHours(1))).IsEqualTo(Start.AddHours(1));
    }

    [Test]
    public async Task 離席時間が閾値ちょうどなら退勤候補にする()
    {
        var log = new WorkDayLog { Date = Start.Date, StartTime = Start };
        var period = new AwayPeriod(
            Start.AddHours(9), Start.AddHours(9).AddMinutes(30), AwayReason.Idle);

        var result = WorkDayPolicy.ShouldPromptForAwayEnd(log, period, 30, 17);

        await Assert.That(result).IsTrue();
    }

    [Test]
    public async Task 出勤以前または閾値未満の離席は退勤候補にしない()
    {
        var log = new WorkDayLog { Date = Start.Date, StartTime = Start };
        var beforeClockIn = new AwayPeriod(Start, Start.AddHours(1), AwayReason.Sleep);
        var tooShort = new AwayPeriod(Start.AddHours(9), Start.AddHours(9).AddMinutes(29), AwayReason.Idle);

        await Assert.That(WorkDayPolicy.ShouldPromptForAwayEnd(log, beforeClockIn, 30, 0)).IsFalse();
        await Assert.That(WorkDayPolicy.ShouldPromptForAwayEnd(log, tooShort, 30, 0)).IsFalse();
    }

    [Test]
    public async Task 設定時刻より早い同日離席は除外し日またぎなら候補にする()
    {
        var log = new WorkDayLog { Date = Start.Date, StartTime = Start };
        var daytime = new AwayPeriod(
            Start.AddHours(6), Start.AddHours(7), AwayReason.Idle); // 15:00-16:00
        var overnight = new AwayPeriod(
            Start.AddHours(6), Start.Date.AddDays(1).AddHours(8), AwayReason.Sleep);

        await Assert.That(WorkDayPolicy.ShouldPromptForAwayEnd(log, daytime, 30, 17)).IsFalse();
        await Assert.That(WorkDayPolicy.ShouldPromptForAwayEnd(log, overnight, 30, 17)).IsTrue();
    }

    private static ScheduleItem Recorded(DateTime start, DateTime end) => new()
    {
        Kind = ScheduleItemKind.Recorded,
        Title = "実績",
        StartTime = start,
        EndTime = end,
    };
}
