using System;
using System.Collections.Generic;
using System.Linq;

using TimeRenderer.Models;
using TimeRenderer.ViewModels;

namespace TimeRenderer.Helpers;

/// <summary>ToDo一覧の表示順と手動順の採番規則。</summary>
public static class TodoOrderHelper
{
    /// <summary>
    /// 完了済みを末尾へ送り、「今日やる」を先頭へ寄せたうえで選択中の規則を適用する。
    /// </summary>
    public static IReadOnlyList<TodoItem> Order(IEnumerable<TodoItem> source, TodoSortMode sortMode)
    {
        ArgumentNullException.ThrowIfNull(source);

        var ordered = sortMode switch
        {
            TodoSortMode.DueDate => source
                .OrderBy(todo => todo.IsCompleted)
                .ThenByDescending(todo => todo.IsPlannedToday)
                .ThenBy(todo => todo.DueDate.HasValue ? 0 : 1)
                .ThenBy(todo => todo.DueDate ?? DateTime.MaxValue)
                .ThenByDescending(todo => todo.Priority)
                .ThenBy(todo => todo.CreatedAt),

            TodoSortMode.Priority => source
                .OrderBy(todo => todo.IsCompleted)
                .ThenByDescending(todo => todo.IsPlannedToday)
                .ThenByDescending(todo => todo.Priority)
                .ThenBy(todo => todo.DueDate ?? DateTime.MaxValue)
                .ThenBy(todo => todo.CreatedAt),

            TodoSortMode.Manual => source
                .OrderBy(todo => todo.IsCompleted)
                .ThenByDescending(todo => todo.IsPlannedToday)
                .ThenBy(todo => todo.SortOrder)
                .ThenBy(todo => todo.CreatedAt),

            _ => source
                .OrderBy(todo => todo.IsCompleted)
                .ThenByDescending(todo => todo.IsPlannedToday)
                .ThenBy(todo => todo.CreatedAt),
        };

        return [.. ordered];
    }

    /// <summary>
    /// 表示中の順を先に採番し、一覧に含まれないToDoを元コレクション順で後ろへ続ける。
    /// </summary>
    public static void ApplyManualOrder(
        IEnumerable<TodoItem> allTodos,
        IReadOnlyList<TodoItem> visibleOrder)
    {
        ArgumentNullException.ThrowIfNull(allTodos);
        ArgumentNullException.ThrowIfNull(visibleOrder);

        var placed = new HashSet<TodoItem>(visibleOrder);
        var index = 0;
        foreach (var todo in visibleOrder) todo.SortOrder = index++;
        foreach (var todo in allTodos)
        {
            if (!placed.Contains(todo)) todo.SortOrder = index++;
        }
    }
}
