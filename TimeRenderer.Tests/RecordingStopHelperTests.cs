using System;

using TimeRenderer.Helpers;
using TimeRenderer.Models;

using TUnit.Assertions;
using TUnit.Assertions.Extensions;
using TUnit.Core;

namespace TimeRenderer.Tests;

/// <summary>記録停止時の離席除外と時間境界を固定する。</summary>
public class RecordingStopHelperTests
{
    private static readonly DateTime Start = new(2026, 8, 30, 9, 0, 0);
    private static readonly DateTime End = Start.AddHours(3);

    [Test]
    public async Task 離席を残す場合は記録全体を一つの区間にする()
    {
        var away = new AwayPeriod(Start.AddMinutes(30), Start.AddMinutes(60), AwayReason.Idle);

        var segments = RecordingStopHelper.BuildSegments(Start, End, [away], excludeAway: false);

        await Assert.That(segments.Count).IsEqualTo(1);
        await Assert.That(segments[0].Start).IsEqualTo(Start);
        await Assert.That(segments[0].End).IsEqualTo(End);
    }

    [Test]
    public async Task 範囲外を切り詰め重複と隣接をまとめて離席を除く()
    {
        var periods = new[]
        {
            new AwayPeriod(Start.AddMinutes(-30), Start.AddMinutes(30), AwayReason.Idle),
            new AwayPeriod(Start.AddMinutes(20), Start.AddMinutes(60), AwayReason.Locked),
            new AwayPeriod(Start.AddMinutes(60), Start.AddMinutes(90), AwayReason.Sleep),
            new AwayPeriod(End.AddMinutes(1), End.AddMinutes(10), AwayReason.Idle),
        };

        var segments = RecordingStopHelper.BuildSegments(Start, End, periods, excludeAway: true);

        await Assert.That(segments.Count).IsEqualTo(1);
        await Assert.That(segments[0].Start).IsEqualTo(Start.AddMinutes(90));
        await Assert.That(segments[0].End).IsEqualTo(End);
    }

    [Test]
    public async Task 記録全体が離席なら保存区間を返さない()
    {
        var away = new AwayPeriod(Start.AddHours(-1), End.AddHours(1), AwayReason.Sleep);

        var segments = RecordingStopHelper.BuildSegments(Start, End, [away], excludeAway: true);

        await Assert.That(segments).IsEmpty();
    }

    [Test]
    public async Task 終了が開始以前なら区間を返さない()
    {
        var segments = RecordingStopHelper.BuildSegments(Start, Start, [], excludeAway: false);

        await Assert.That(segments).IsEmpty();
    }

    [Test]
    public async Task 重複した離席時間を通知の合計へ二重計上しない()
    {
        var periods = new[]
        {
            new AwayPeriod(Start, Start.AddHours(1), AwayReason.Idle),
            new AwayPeriod(Start.AddMinutes(30), Start.AddMinutes(90), AwayReason.Locked),
        };

        var duration = RecordingStopHelper.GetExcludedDuration(Start, End, periods);
        var notice = RecordingStopHelper.BuildAutoExcludeNotice(Start, End, periods);

        await Assert.That(duration).IsEqualTo(TimeSpan.FromMinutes(90));
        await Assert.That(notice).Contains("離席 2 件・合計 1時間30分");
    }
}
