using System;
using System.Collections.Generic;

using TimeRenderer.Helpers;
using TimeRenderer.Models;

using TUnit.Assertions;
using TUnit.Assertions.Extensions;
using TUnit.Core;

namespace TimeRenderer.Tests;

/// <summary>日・週表示で重なる予定を列へ割り当てる規則を固定する。</summary>
public class ScheduleLayoutHelperTests
{
    private static readonly DateTime Day = new(2026, 8, 30);

    [Test]
    public async Task 同時に重なる三件を別々の列へ割り当てる()
    {
        var segments = new List<ScheduleSegment>
        {
            Segment(9, 12),
            Segment(10, 13),
            Segment(11, 14),
        };

        ScheduleLayoutHelper.CalculateClustersAndAssignColumns(segments);

        await Assert.That(segments[0].ColumnIndex).IsEqualTo(0);
        await Assert.That(segments[1].ColumnIndex).IsEqualTo(1);
        await Assert.That(segments[2].ColumnIndex).IsEqualTo(2);
        await Assert.That(segments[0].MaxColumnIndex).IsEqualTo(2);
        await Assert.That(segments[2].MaxColumnIndex).IsEqualTo(2);
    }

    [Test]
    public async Task 終了時刻と次の開始時刻が同じなら列を再利用する()
    {
        var segments = new List<ScheduleSegment>
        {
            Segment(9, 10),
            Segment(10, 11),
        };

        ScheduleLayoutHelper.CalculateClustersAndAssignColumns(segments);

        await Assert.That(segments[0].ColumnIndex).IsEqualTo(0);
        await Assert.That(segments[1].ColumnIndex).IsEqualTo(0);
        await Assert.That(segments[0].MaxColumnIndex).IsEqualTo(0);
        await Assert.That(segments[1].MaxColumnIndex).IsEqualTo(0);
    }

    [Test]
    public async Task 長い予定でつながるクラスタ内でも空いた列を再利用する()
    {
        var segments = new List<ScheduleSegment>
        {
            Segment(9, 12),
            Segment(10, 11),
            Segment(11, 13),
        };

        ScheduleLayoutHelper.CalculateClustersAndAssignColumns(segments);

        await Assert.That(segments[0].ColumnIndex).IsEqualTo(0);
        await Assert.That(segments[1].ColumnIndex).IsEqualTo(1);
        await Assert.That(segments[2].ColumnIndex).IsEqualTo(1);
        await Assert.That(segments[2].MaxColumnIndex).IsEqualTo(1);
    }

    [Test]
    public async Task 離れたクラスタごとに最大列数を計算し直す()
    {
        var segments = new List<ScheduleSegment>
        {
            Segment(9, 11),
            Segment(10, 12),
            Segment(14, 15),
        };

        ScheduleLayoutHelper.CalculateClustersAndAssignColumns(segments);

        await Assert.That(segments[0].MaxColumnIndex).IsEqualTo(1);
        await Assert.That(segments[1].MaxColumnIndex).IsEqualTo(1);
        await Assert.That(segments[2].ColumnIndex).IsEqualTo(0);
        await Assert.That(segments[2].MaxColumnIndex).IsEqualTo(0);
    }

    private static ScheduleSegment Segment(int startHour, int endHour)
    {
        var item = new ScheduleItem
        {
            Title = $"{startHour}-{endHour}",
            StartTime = Day.AddHours(startHour),
            EndTime = Day.AddHours(endHour),
        };
        return new ScheduleSegment(item, item.StartTime, item.EndTime);
    }
}
