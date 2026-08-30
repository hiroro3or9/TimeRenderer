using System;

using TimeRenderer.Models;

namespace TimeRenderer.Helpers;

/// <summary>ToDo通知の時刻判定。画面や通知音に依存しない規則をまとめる。</summary>
public static class TodoReminderHelper
{
    public static TodoReminderState Classify(
        TodoItem todo,
        DateTime now,
        TimeSpan reminderGrace,
        TimeSpan missedWindow)
    {
        ArgumentNullException.ThrowIfNull(todo);

        if (todo.IsCompleted || todo.RemindAt is not { } remindAt || now < remindAt)
        {
            return TodoReminderState.NotDue;
        }

        var delay = now - remindAt;
        if (delay <= reminderGrace) return TodoReminderState.Due;
        if (delay <= missedWindow) return TodoReminderState.Missed;
        return TodoReminderState.Expired;
    }

    /// <summary>同じ時刻の通知を一度だけ扱うためのセッション内キー。</summary>
    public static string BuildKey(TodoItem todo)
    {
        ArgumentNullException.ThrowIfNull(todo);
        return $"{todo.Id}|{todo.RemindAt:O}";
    }

    /// <summary>見逃した通知を1本へ集約した本文。対象が無ければnull。</summary>
    public static string? BuildMissedNotice(
        System.Collections.Generic.IReadOnlyList<TodoItem> todos)
    {
        ArgumentNullException.ThrowIfNull(todos);
        if (todos.Count == 0) return null;

        var head = todos[0].Title;
        return todos.Count == 1
            ? $"通知時刻を過ぎた ToDo があります：{head}"
            : $"通知時刻を過ぎた ToDo が {todos.Count} 件あります（{head} ほか）";
    }
}

public enum TodoReminderState
{
    NotDue,
    Due,
    Missed,
    Expired,
}
