using System;

using TimeRenderer.Models;

namespace TimeRenderer.Helpers;

/// <summary>ToDoのサブタスクに対する、画面やUndo履歴に依存しない状態変更。</summary>
public static class TodoSubtaskHelper
{
    public static void ToggleExpanded(TodoItem todo)
    {
        ArgumentNullException.ThrowIfNull(todo);
        todo.IsExpanded = !todo.IsExpanded;
    }

    public static TodoSubtask? Add(TodoItem parent, string? title)
    {
        ArgumentNullException.ThrowIfNull(parent);

        var normalizedTitle = title?.Trim() ?? string.Empty;
        if (normalizedTitle.Length == 0) return null;

        var subtask = new TodoSubtask { Title = normalizedTitle };
        parent.Subtasks.Add(subtask);
        parent.NotifySubtasksChanged();
        return subtask;
    }

    public static bool Remove(TodoItem parent, TodoSubtask subtask)
    {
        ArgumentNullException.ThrowIfNull(parent);
        ArgumentNullException.ThrowIfNull(subtask);

        var removed = parent.Subtasks.Remove(subtask);
        if (removed) parent.NotifySubtasksChanged();
        return removed;
    }

    public static void ToggleCompleted(TodoItem parent, TodoSubtask subtask)
    {
        ArgumentNullException.ThrowIfNull(parent);
        ArgumentNullException.ThrowIfNull(subtask);

        subtask.IsCompleted = !subtask.IsCompleted;
        parent.NotifySubtasksChanged();
    }
}
