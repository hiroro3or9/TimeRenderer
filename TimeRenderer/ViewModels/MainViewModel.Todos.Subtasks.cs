using System;
using System.Collections.Generic;

using TimeRenderer.Helpers;
using TimeRenderer.Models;

namespace TimeRenderer.ViewModels;

/// <summary>ToDoのサブタスク操作と、親単位のUndo履歴。</summary>
public partial class MainViewModel
{
    /// <summary>サブタスクの一覧を開閉する。</summary>
    public static void ToggleTodoExpanded(TodoItem todo) => TodoSubtaskHelper.ToggleExpanded(todo);

    /// <summary>その場の入力欄からサブタスクを1件足す。</summary>
    public void AddSubtask(TodoItem parent)
    {
        var title = parent.NewSubtaskTitle.Trim();
        if (title.Length == 0) return;

        ApplyToSubtasks(parent, "サブタスクの追加", () =>
        {
            TodoSubtaskHelper.Add(parent, title);
        });

        parent.NewSubtaskTitle = string.Empty;
        parent.IsExpanded = true; // 続けて足せるように開いたままにする
    }

    /// <summary>サブタスクを削除する。</summary>
    public void RemoveSubtask(TodoItem parent, TodoSubtask subtask)
    {
        ApplyToSubtasks(parent, "サブタスクの削除", () =>
        {
            TodoSubtaskHelper.Remove(parent, subtask);
        });
    }

    /// <summary>
    /// サブタスクの完了を切り替える。
    /// これで全部が済んだら親も完了にする（繰り返しなら次回分の生成まで含めて1件の取り消し単位）。
    /// </summary>
    public void ToggleSubtask(TodoItem parent, TodoSubtask subtask)
    {
        ApplyToSubtasks(parent, "サブタスクの完了", () =>
        {
            TodoSubtaskHelper.ToggleCompleted(parent, subtask);
        });
    }

    /// <summary>
    /// サブタスクへの変更を、親1件の内容変更として履歴へ積む。
    /// 変更の結果すべて済んだ場合は、親の完了と次回分の生成もこの1件に含める。
    /// </summary>
    private void ApplyToSubtasks(TodoItem parent, string label, Action change)
    {
        var before = TodoSnapshot.Capture(parent);
        TodoItem? spawned = null;

        // 途中の状態で一覧の作り直しや保存が走らないよう、まとめて処理する
        _isUpdatingTodo = true;
        try
        {
            change();

            if (!parent.IsCompleted && parent.AreAllSubtasksDone)
            {
                parent.IsCompleted = true;
                spawned = SpawnNextOccurrence(parent);
            }
        }
        finally
        {
            _isUpdatingTodo = false;
        }

        if (parent.IsCompleted) PendingTodoReminders.Remove(parent);

        var edits = new List<IUndoableEdit>();

        var after = TodoSnapshot.Capture(parent);
        if (!before.IsSameAs(after))
        {
            edits.Add(new ModifyTodoEdit(parent, before, after, label));
        }
        if (spawned != null) edits.Add(new AddTodoEdit(spawned));

        PushEdits(edits, $"ToDo「{parent.Title}」の{label}");
        OnTodoChanged();
    }
}
