using System;
using System.Collections.Generic;

using TimeRenderer.Helpers;
using TimeRenderer.Models;

using TUnit.Assertions;
using TUnit.Assertions.Extensions;
using TUnit.Core;

namespace TimeRenderer.Tests;

/// <summary>タイムラインで予定をレーンへ詰める規則を固定する。</summary>
public class TimelineLaneHelperTests
{
    private static readonly DateTime Day = new(2026, 8, 30);
    private static readonly TimelineScale Scale = new(Day, Day.AddDays(1), 120);

    [Test]
    public async Task 時間が重なる予定は別レーンへ割り当てる()
    {
        var first = Item("A", 9, 11);
        var second = Item("B", 10, 12);
        var lanes = new Dictionary<ScheduleItem, int>();

        var count = TimelineLaneHelper.AssignLanes([first, second], Scale, lanes);

        await Assert.That(count).IsEqualTo(2);
        await Assert.That(lanes[first]).IsEqualTo(0);
        await Assert.That(lanes[second]).IsEqualTo(1);
    }

    [Test]
    public async Task 十分に離れた予定は同じレーンを再利用する()
    {
        var first = Item(string.Empty, 9, 10);
        var second = Item(string.Empty, 14, 15);
        var lanes = new Dictionary<ScheduleItem, int>();

        var count = TimelineLaneHelper.AssignLanes([first, second], Scale, lanes);

        await Assert.That(count).IsEqualTo(1);
        await Assert.That(lanes[first]).IsEqualTo(0);
        await Assert.That(lanes[second]).IsEqualTo(0);
    }

    [Test]
    public async Task 短い予定は実時間が重ならなくても最小ヒット幅を確保する()
    {
        var first = Item(string.Empty, 9, 9, 1);
        var second = Item(string.Empty, 10, 10, 1);
        var lanes = new Dictionary<ScheduleItem, int>();

        var count = TimelineLaneHelper.AssignLanes([first, second], Scale, lanes);

        await Assert.That(count).IsEqualTo(2);
        await Assert.That(lanes[first]).IsEqualTo(0);
        await Assert.That(lanes[second]).IsEqualTo(1);
    }

    [Test]
    public async Task グループごとに独立して詰めレーン番号へオフセットを足す()
    {
        var b = Item("B1", 9, 10, group: "B");
        var a1 = Item("A1", 9, 11, group: "A");
        var a2 = Item("A2", 10, 12, group: "A");
        var lanes = new Dictionary<ScheduleItem, int>();

        var groups = TimelineLaneHelper.AssignLanesByGroup(
            [b, a1, a2], item => item.CategoryId!, Scale, lanes);

        await Assert.That(groups.Count).IsEqualTo(2);
        await Assert.That(groups[0].Name).IsEqualTo("A");
        await Assert.That(groups[0].LaneOffset).IsEqualTo(0);
        await Assert.That(groups[0].LaneCount).IsEqualTo(2);
        await Assert.That(groups[1].Name).IsEqualTo("B");
        await Assert.That(groups[1].LaneOffset).IsEqualTo(2);
        await Assert.That(groups[1].LaneCount).IsEqualTo(1);
        await Assert.That(lanes[b]).IsEqualTo(2);
    }

    [Test]
    public async Task 空の一覧はレーンを使わない()
    {
        var lanes = new Dictionary<ScheduleItem, int>();

        var count = TimelineLaneHelper.AssignLanes([], Scale, lanes);

        await Assert.That(count).IsEqualTo(0);
        await Assert.That(lanes).IsEmpty();
    }

    private static ScheduleItem Item(
        string title,
        int startHour,
        int endHour,
        int endMinute = 0,
        string? group = null) => new()
    {
        Title = title,
        StartTime = Day.AddHours(startHour),
        EndTime = Day.AddHours(endHour).AddMinutes(endMinute),
        CategoryId = group,
    };
}
