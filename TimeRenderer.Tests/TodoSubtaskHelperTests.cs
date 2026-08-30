using System.Collections.Generic;
using System.Collections.ObjectModel;

using TimeRenderer.Helpers;
using TimeRenderer.Models;

using TUnit.Assertions;
using TUnit.Assertions.Extensions;
using TUnit.Core;

namespace TimeRenderer.Tests;

/// <summary>サブタスクの入力保護、進捗、完了判定、Undo/Redoを固定する。</summary>
public class TodoSubtaskHelperTests
{
    [Test]
    public async Task 空白だけのタイトルは追加せず有効なタイトルは前後空白を除く()
    {
        var parent = new TodoItem { Title = "親" };

        var ignored = TodoSubtaskHelper.Add(parent, "   ");
        var added = TodoSubtaskHelper.Add(parent, "  設計を確認  ");

        await Assert.That(ignored).IsNull();
        await Assert.That(added).IsNotNull();
        await Assert.That(added!.Title).IsEqualTo("設計を確認");
        await Assert.That(parent.SubtaskTotalCount).IsEqualTo(1);
        await Assert.That(parent.SubtaskProgressText).IsEqualTo("0/1");
    }

    [Test]
    public async Task 最後の未完了サブタスクを完了すると親の全完了条件が成立する()
    {
        var done = new TodoSubtask { Title = "済", IsCompleted = true };
        var remaining = new TodoSubtask { Title = "残" };
        var parent = new TodoItem { Subtasks = [done, remaining] };

        TodoSubtaskHelper.ToggleCompleted(parent, remaining);

        await Assert.That(parent.SubtaskDoneCount).IsEqualTo(2);
        await Assert.That(parent.AreAllSubtasksDone).IsTrue();
        await Assert.That(parent.SubtaskProgressPercent).IsEqualTo(100);
    }

    [Test]
    public async Task 最後のサブタスクを削除して空になった親は全完了扱いにしない()
    {
        var subtask = new TodoSubtask { Title = "唯一", IsCompleted = true };
        var parent = new TodoItem { Subtasks = [subtask] };

        var removed = TodoSubtaskHelper.Remove(parent, subtask);

        await Assert.That(removed).IsTrue();
        await Assert.That(parent.HasSubtasks).IsFalse();
        await Assert.That(parent.AreAllSubtasksDone).IsFalse();
        await Assert.That(parent.SubtaskProgressPercent).IsEqualTo(0);
    }

    [Test]
    public async Task サブタスク変更のスナップショットはUndoとRedoで同じ親へ戻せる()
    {
        var parent = new TodoItem
        {
            Title = "親",
            Subtasks = [new TodoSubtask { Title = "既存" }],
        };
        var before = TodoSnapshot.Capture(parent);
        TodoSubtaskHelper.Add(parent, "追加");
        var after = TodoSnapshot.Capture(parent);
        var edit = new ModifyTodoEdit(parent, before, after, "サブタスクの追加");
        var context = new UndoContext(
            new ObservableCollection<ScheduleItem>(),
            new ObservableCollection<TodoItem>([parent]));

        edit.Undo(context);
        var undone = TodoSnapshot.Capture(parent);
        edit.Redo(context);
        var redone = TodoSnapshot.Capture(parent);

        await Assert.That(before.IsSameAs(undone)).IsTrue();
        await Assert.That(after.IsSameAs(redone)).IsTrue();
        await Assert.That(parent.Subtasks.Count).IsEqualTo(2);
    }

    [Test]
    public async Task 開閉操作は保存対象のサブタスク内容を変えない()
    {
        var parent = new TodoItem { Subtasks = [new TodoSubtask { Title = "手順" }] };
        var before = TodoSnapshot.Capture(parent);

        TodoSubtaskHelper.ToggleExpanded(parent);

        await Assert.That(parent.IsExpanded).IsTrue();
        await Assert.That(before.IsSameAs(TodoSnapshot.Capture(parent))).IsTrue();
    }
}
