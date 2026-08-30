using System;
using System.Linq;

using TimeRenderer.Helpers;
using TimeRenderer.Models;

using TUnit.Assertions;
using TUnit.Assertions.Extensions;
using TUnit.Core;

namespace TimeRenderer.Tests;

/// <summary>勤務時間から未記録区間を求める境界条件を固定する。</summary>
public class UnrecordedGapHelperTests
{
    private static readonly DateTime WorkStart = new(2026, 8, 30, 9, 0, 0);
    private static readonly DateTime WorkEnd = new(2026, 8, 30, 17, 0, 0);

    [Test]
    public async Task 順不同の重複区間をまとめ勤務時間外を切り詰める()
    {
        var covered = new[]
        {
            (WorkStart.AddHours(7), WorkEnd.AddHours(1)),
            (WorkStart.AddMinutes(30), WorkStart.AddHours(2)),
            (WorkStart.AddHours(-1), WorkStart.AddHours(1)),
            (WorkStart.AddHours(3), WorkStart.AddHours(4)),
        };

        var gaps = UnrecordedGapHelper.Detect(
            WorkStart, WorkEnd, covered, TimeSpan.FromMinutes(1));

        await Assert.That(gaps.Count).IsEqualTo(2);
        await Assert.That(gaps[0]).IsEqualTo(
            new UnrecordedGap(WorkStart.AddHours(2), WorkStart.AddHours(3)));
        await Assert.That(gaps[1]).IsEqualTo(
            new UnrecordedGap(WorkStart.AddHours(4), WorkStart.AddHours(7)));
    }

    [Test]
    public async Task 最小時間と同じ長さの空白は含み一秒短ければ除外する()
    {
        var covered = new[]
        {
            (WorkStart.AddMinutes(30), WorkEnd),
        };

        var included = UnrecordedGapHelper.Detect(
            WorkStart, WorkEnd, covered, TimeSpan.FromMinutes(30));
        var excluded = UnrecordedGapHelper.Detect(
            WorkStart, WorkEnd, covered, TimeSpan.FromMinutes(30).Add(TimeSpan.FromSeconds(1)));

        await Assert.That(included.Count).IsEqualTo(1);
        await Assert.That(included[0].StartTime).IsEqualTo(WorkStart);
        await Assert.That(included[0].EndTime).IsEqualTo(WorkStart.AddMinutes(30));
        await Assert.That(excluded).IsEmpty();
    }

    [Test]
    public async Task 記録が無ければ勤務時間全体を返す()
    {
        var gaps = UnrecordedGapHelper.Detect(
            WorkStart, WorkEnd, [], TimeSpan.Zero);

        await Assert.That(gaps.Count).IsEqualTo(1);
        await Assert.That(gaps[0]).IsEqualTo(new UnrecordedGap(WorkStart, WorkEnd));
    }

    [Test]
    public async Task 勤務終了が開始以前なら返さない()
    {
        var gaps = UnrecordedGapHelper.Detect(
            WorkStart, WorkStart, [], TimeSpan.Zero);

        await Assert.That(gaps).IsEmpty();
    }

    [Test]
    public async Task 日をまたぐ空白を日境界で分割する()
    {
        var gap = new UnrecordedGap(
            new DateTime(2026, 8, 30, 23, 0, 0),
            new DateTime(2026, 9, 1, 1, 0, 0));

        var parts = UnrecordedGapHelper.SplitByDay(gap).ToList();

        await Assert.That(parts.Count).IsEqualTo(3);
        await Assert.That(parts[0].Duration).IsEqualTo(TimeSpan.FromHours(1));
        await Assert.That(parts[1].Duration).IsEqualTo(TimeSpan.FromDays(1));
        await Assert.That(parts[2].Duration).IsEqualTo(TimeSpan.FromHours(1));
        await Assert.That(parts[0].EndTime).IsEqualTo(parts[1].StartTime);
        await Assert.That(parts[1].EndTime).IsEqualTo(parts[2].StartTime);
    }
}
