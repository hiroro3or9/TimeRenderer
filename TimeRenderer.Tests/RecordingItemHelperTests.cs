using System;

using TimeRenderer.Helpers;
using TimeRenderer.Models;

using TUnit.Assertions;
using TUnit.Assertions.Extensions;
using TUnit.Core;

namespace TimeRenderer.Tests;

/// <summary>記録停止時に実績アイテムへ書き込む項目を固定する。</summary>
public class RecordingItemHelperTests
{
    private static readonly DateTime Start = new(2026, 8, 30, 9, 0, 0);

    [Test]
    public async Task 元予定を実績化しても既存のメモを残す()
    {
        var item = new ScheduleItem
        {
            Kind = ScheduleItemKind.Planned,
            Title = "予定",
            Content = "確認事項あり",
        };

        RecordingItemHelper.ApplyRecordedSegment(
            item, "実績", Start, Start.AddMinutes(45));

        await Assert.That(item.Kind).IsEqualTo(ScheduleItemKind.Recorded);
        await Assert.That(item.Title).IsEqualTo("実績");
        await Assert.That(item.StartTime).IsEqualTo(Start);
        await Assert.That(item.EndTime).IsEqualTo(Start.AddMinutes(45));
        await Assert.That(item.Content).IsEqualTo("確認事項あり");
    }

    [Test]
    public async Task 元予定のメモが空なら記録時間を入れる()
    {
        var item = new ScheduleItem { Content = "  " };

        RecordingItemHelper.ApplyRecordedSegment(
            item, "実績", Start, Start.AddHours(25).AddMinutes(5));

        await Assert.That(item.Content).IsEqualTo("記録時間: 25:05");
    }

    [Test]
    public async Task 分割後の追加実績へすべての紐づけを引き継ぐ()
    {
        var metadata = new RecordingItemMetadata(
            "#FF123456", "category-1", "project-1", "todo-1", "routine-1", "plan-1");

        var item = RecordingItemHelper.CreateRecordedItem(
            "レビュー", Start, Start.AddMinutes(30), metadata);

        await Assert.That(item.Kind).IsEqualTo(ScheduleItemKind.Recorded);
        await Assert.That(item.Content).IsEqualTo("記録時間: 0:30");
        await Assert.That(item.ColorCode).IsEqualTo("#FF123456");
        await Assert.That(item.CategoryId).IsEqualTo("category-1");
        await Assert.That(item.ProjectCodeId).IsEqualTo("project-1");
        await Assert.That(item.TodoId).IsEqualTo("todo-1");
        await Assert.That(item.RoutineId).IsEqualTo("routine-1");
        await Assert.That(item.SourcePlanId).IsEqualTo("plan-1");
    }
}
