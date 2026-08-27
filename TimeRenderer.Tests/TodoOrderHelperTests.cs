using System;
using System.Collections.Generic;

using TimeRenderer.Helpers;
using TimeRenderer.Models;
using TimeRenderer.ViewModels;

using TUnit.Assertions;
using TUnit.Assertions.Extensions;
using TUnit.Core;

namespace TimeRenderer.Tests;

/// <summary>ToDoの期限・優先度・追加・手動順と、共通の先頭／末尾規則を固定する。</summary>
public class TodoOrderHelperTests
{
    private static TodoItem Todo(
        string title,
        DateTime createdAt,
        DateTime? dueDate = null,
        TodoPriority priority = TodoPriority.Normal,
        int sortOrder = 0,
        bool completed = false,
        bool plannedToday = false) =>
        new()
        {
            Title = title,
            CreatedAt = createdAt,
            DueDate = dueDate,
            Priority = priority,
            SortOrder = sortOrder,
            IsCompleted = completed,
            PlannedOn = plannedToday ? DateTime.Today : null,
        };

    [Test]
    public async Task 期限順は今日やるを先頭へ期限なしと完了済みを後ろへ送る()
    {
        var baseDate = new DateTime(2026, 8, 1);
        var plannedToday = Todo("今日やる", baseDate.AddHours(4), baseDate.AddDays(10), plannedToday: true);
        var urgent = Todo("期限早", baseDate.AddHours(3), baseDate.AddDays(1), TodoPriority.Low);
        var laterHigh = Todo("期限遅・高", baseDate.AddHours(2), baseDate.AddDays(2), TodoPriority.High);
        var noDue = Todo("期限なし", baseDate.AddHours(1), priority: TodoPriority.High);
        var completed = Todo("完了", baseDate, baseDate, TodoPriority.High, completed: true);

        var result = TodoOrderHelper.Order(
            [completed, noDue, laterHigh, urgent, plannedToday],
            TodoSortMode.DueDate);

        await AssertTitles(result, "今日やる", "期限早", "期限遅・高", "期限なし", "完了");
    }

    [Test]
    public async Task 優先度順は同じ優先度なら期限と追加日時で決める()
    {
        var created = new DateTime(2026, 8, 1);
        var low = Todo("低", created, created, TodoPriority.Low);
        var highLater = Todo("高・期限遅", created, created.AddDays(2), TodoPriority.High);
        var highSoonNew = Todo("高・期限早・新", created.AddHours(1), created.AddDays(1), TodoPriority.High);
        var highSoonOld = Todo("高・期限早・旧", created, created.AddDays(1), TodoPriority.High);

        var result = TodoOrderHelper.Order(
            [low, highLater, highSoonNew, highSoonOld],
            TodoSortMode.Priority);

        await AssertTitles(result, "高・期限早・旧", "高・期限早・新", "高・期限遅", "低");
    }

    [Test]
    public async Task 追加順と手動順でも今日やると完了済みの優先規則を保つ()
    {
        var created = new DateTime(2026, 8, 1);
        var old = Todo("旧", created, sortOrder: 2);
        var recent = Todo("新", created.AddHours(1), sortOrder: 0);
        var plannedToday = Todo("今日やる", created.AddHours(2), sortOrder: 3, plannedToday: true);
        var completed = Todo("完了", created.AddHours(-1), sortOrder: -1, completed: true);
        TodoItem[] source = [completed, plannedToday, recent, old];

        var createdOrder = TodoOrderHelper.Order(source, TodoSortMode.Created);
        var manualOrder = TodoOrderHelper.Order(source, TodoSortMode.Manual);

        await AssertTitles(createdOrder, "今日やる", "旧", "新", "完了");
        await AssertTitles(manualOrder, "今日やる", "新", "旧", "完了");
    }

    [Test]
    public async Task 手動採番は非表示のToDoを表示中の項目より後ろへ続ける()
    {
        var created = new DateTime(2026, 8, 1);
        var first = Todo("先頭", created);
        var second = Todo("次", created);
        var hiddenA = Todo("非表示A", created);
        var hiddenB = Todo("非表示B", created);
        TodoItem[] all = [hiddenA, first, hiddenB, second];

        TodoOrderHelper.ApplyManualOrder(all, [second, first]);

        await Assert.That(second.SortOrder).IsEqualTo(0);
        await Assert.That(first.SortOrder).IsEqualTo(1);
        await Assert.That(hiddenA.SortOrder).IsEqualTo(2);
        await Assert.That(hiddenB.SortOrder).IsEqualTo(3);
    }

    private static async Task AssertTitles(IReadOnlyList<TodoItem> actual, params string[] expected)
    {
        await Assert.That(actual.Count).IsEqualTo(expected.Length);
        for (var index = 0; index < expected.Length; index++)
        {
            await Assert.That(actual[index].Title).IsEqualTo(expected[index]);
        }
    }
}
