using System;
using System.Collections.ObjectModel;

using TimeRenderer.Helpers;
using TimeRenderer.Models;

using TUnit.Assertions;
using TUnit.Assertions.Extensions;
using TUnit.Core;

namespace TimeRenderer.Tests;

/// <summary>ToDoからの予定作成、実績積算、まとめてUndoする契約を固定する。</summary>
public class TodoTimeBlockHelperTests
{
    [Test]
    public async Task 見積もりがあればその長さを使い無ければ一時間にする()
    {
        var estimated = new TodoItem { EstimatedMinutes = 90 };
        var noEstimate = new TodoItem();

        await Assert.That(TodoTimeBlockHelper.GetBlockDuration(estimated)).IsEqualTo(TimeSpan.FromMinutes(90));
        await Assert.That(TodoTimeBlockHelper.GetBlockDuration(noEstimate)).IsEqualTo(TimeSpan.FromHours(1));
    }

    [Test]
    public async Task 予定はToDoの表示情報と紐づけを引き継ぐ()
    {
        var start = new DateTime(2026, 8, 30, 9, 0, 0);
        var todo = new TodoItem
        {
            Id = "todo-1",
            Title = "レビュー",
            ColorCode = "#FF87CEFA",
            CategoryId = "category-1",
        };

        var item = TodoTimeBlockHelper.CreateScheduleItem(
            todo, start, start.AddMinutes(45), "fallback-category", "project-1");

        await Assert.That(item.Kind).IsEqualTo(ScheduleItemKind.Planned);
        await Assert.That(item.Title).IsEqualTo("レビュー");
        await Assert.That(item.StartTime).IsEqualTo(start);
        await Assert.That(item.EndTime).IsEqualTo(start.AddMinutes(45));
        await Assert.That(item.CategoryId).IsEqualTo("category-1");
        await Assert.That(item.ProjectCodeId).IsEqualTo("project-1");
        await Assert.That(item.TodoId).IsEqualTo("todo-1");
    }

    [Test]
    public async Task 旧ToDoのカテゴリIDが無ければ解決済みIDを使う()
    {
        var todo = new TodoItem { CategoryId = null };

        var item = TodoTimeBlockHelper.CreateScheduleItem(
            todo, DateTime.Today, DateTime.Today.AddHours(1), "resolved", null);

        await Assert.That(item.CategoryId).IsEqualTo("resolved");
    }

    [Test]
    public async Task 離席除外後に残った複数区間の合計だけを積算する()
    {
        var todo = new TodoItem();
        var date = new DateTime(2026, 8, 30);

        var changed = TodoTimeBlockHelper.AccumulateRecordedTime(
            todo,
            [
                (date.AddHours(9), date.AddHours(10)),
                (date.AddHours(10.5), date.AddHours(11)),
            ]);

        await Assert.That(changed).IsTrue();
        await Assert.That(todo.RecordedDuration).IsEqualTo(TimeSpan.FromMinutes(90));
    }

    [Test]
    public async Task 空区間と合計が正でない区間は積算しない()
    {
        var todo = new TodoItem();
        var date = new DateTime(2026, 8, 30);

        var emptyChanged = TodoTimeBlockHelper.AccumulateRecordedTime(todo, []);
        var invalidChanged = TodoTimeBlockHelper.AccumulateRecordedTime(
            todo,
            [(date.AddHours(10), date.AddHours(9))]);

        await Assert.That(emptyChanged).IsFalse();
        await Assert.That(invalidChanged).IsFalse();
        await Assert.That(todo.RecordedTicks).IsEqualTo(0);
    }

    [Test]
    public async Task 記録漏れ補完の予定追加とToDo積算は一緒にUndoとRedoできる()
    {
        var start = new DateTime(2026, 8, 30, 9, 0, 0);
        var todo = new TodoItem { Title = "調査" };
        var item = TodoTimeBlockHelper.CreateScheduleItem(todo, start, start.AddHours(1), null, null);
        item.Kind = ScheduleItemKind.Recorded;
        var items = new ObservableCollection<ScheduleItem> { item };
        var todos = new ObservableCollection<TodoItem> { todo };
        var context = new UndoContext(items, todos);
        var beforeTicks = todo.RecordedTicks;
        TodoTimeBlockHelper.AccumulateRecordedTime(todo, [(start, start.AddHours(1))]);
        var afterTicks = todo.RecordedTicks;
        var edits = new IUndoableEdit[]
        {
            new AddItemEdit(item),
            new ChangeTodoRecordedTimeEdit(todo, beforeTicks, afterTicks, "実績の追加"),
        };

        foreach (var edit in edits.Reverse()) edit.Undo(context);
        await Assert.That(items.Count).IsEqualTo(0);
        await Assert.That(todo.RecordedTicks).IsEqualTo(0);

        foreach (var edit in edits) edit.Redo(context);
        await Assert.That(items.Count).IsEqualTo(1);
        await Assert.That(todo.RecordedDuration).IsEqualTo(TimeSpan.FromHours(1));
    }
}
