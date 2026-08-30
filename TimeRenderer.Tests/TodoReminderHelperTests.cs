using System;

using TimeRenderer.Helpers;
using TimeRenderer.Models;

using TUnit.Assertions;
using TUnit.Assertions.Extensions;
using TUnit.Core;

namespace TimeRenderer.Tests;

/// <summary>ToDo通知の時刻境界と、再通知キーの契約を固定する。</summary>
public class TodoReminderHelperTests
{
    private static readonly TimeSpan Grace = TimeSpan.FromMinutes(15);
    private static readonly TimeSpan MissedWindow = TimeSpan.FromDays(3);
    private static readonly DateTime Now = new(2026, 8, 30, 12, 0, 0);

    [Test]
    public async Task 未完了で通知時刻から猶予内なら個別通知にする()
    {
        var atTime = new TodoItem { RemindAt = Now };
        var atBoundary = new TodoItem { RemindAt = Now - Grace };

        await Assert.That(Classify(atTime)).IsEqualTo(TodoReminderState.Due);
        await Assert.That(Classify(atBoundary)).IsEqualTo(TodoReminderState.Due);
    }

    [Test]
    public async Task 猶予を超えて見逃し期間内ならまとめ通知にする()
    {
        var justMissed = new TodoItem { RemindAt = Now - Grace - TimeSpan.FromTicks(1) };
        var atBoundary = new TodoItem { RemindAt = Now - MissedWindow };

        await Assert.That(Classify(justMissed)).IsEqualTo(TodoReminderState.Missed);
        await Assert.That(Classify(atBoundary)).IsEqualTo(TodoReminderState.Missed);
    }

    [Test]
    public async Task 見逃し期間より古ければ通知しない()
    {
        var todo = new TodoItem { RemindAt = Now - MissedWindow - TimeSpan.FromTicks(1) };

        await Assert.That(Classify(todo)).IsEqualTo(TodoReminderState.Expired);
    }

    [Test]
    public async Task 完了済みと未設定と未来の通知は対象外にする()
    {
        var completed = new TodoItem { IsCompleted = true, RemindAt = Now };
        var unset = new TodoItem();
        var future = new TodoItem { RemindAt = Now.AddMinutes(1) };

        await Assert.That(Classify(completed)).IsEqualTo(TodoReminderState.NotDue);
        await Assert.That(Classify(unset)).IsEqualTo(TodoReminderState.NotDue);
        await Assert.That(Classify(future)).IsEqualTo(TodoReminderState.NotDue);
    }

    [Test]
    public async Task 通知時刻を変えると同じToDoでもキーが変わる()
    {
        var todo = new TodoItem { Id = "todo-1", RemindAt = Now };
        var first = TodoReminderHelper.BuildKey(todo);

        todo.RemindAt = Now.AddMinutes(10);
        var snoozed = TodoReminderHelper.BuildKey(todo);

        await Assert.That(snoozed).IsNotEqualTo(first);
    }

    [Test]
    public async Task 見逃し通知が一件ならタイトルをそのまま表示する()
    {
        var notice = TodoReminderHelper.BuildMissedNotice(
            [new TodoItem { Title = "レビュー" }]);

        await Assert.That(notice).IsEqualTo("通知時刻を過ぎた ToDo があります：レビュー");
    }

    [Test]
    public async Task 見逃し通知が複数なら件数と先頭タイトルへ集約する()
    {
        var notice = TodoReminderHelper.BuildMissedNotice(
            [
                new TodoItem { Title = "レビュー" },
                new TodoItem { Title = "実装" },
            ]);

        await Assert.That(notice).IsEqualTo(
            "通知時刻を過ぎた ToDo が 2 件あります（レビュー ほか）");
    }

    [Test]
    public async Task 見逃し通知が無ければ本文を作らない()
    {
        var notice = TodoReminderHelper.BuildMissedNotice([]);

        await Assert.That(notice).IsNull();
    }

    private static TodoReminderState Classify(TodoItem todo) =>
        TodoReminderHelper.Classify(todo, Now, Grace, MissedWindow);
}
