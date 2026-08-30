using System;
using System.Collections.Generic;
using System.Linq;

using TimeRenderer.Models;

namespace TimeRenderer.Helpers;

/// <summary>完了済みToDoを現役一覧からアーカイブへ移す日付規則。</summary>
public static class TodoArchiveHelper
{
    public static IReadOnlyList<TodoItem> GetArchiveTargets(
        IEnumerable<TodoItem> todos,
        DateTime today,
        int retentionDays)
    {
        ArgumentNullException.ThrowIfNull(todos);

        var cutoff = today.Date.AddDays(-retentionDays);
        return
        [
            .. todos.Where(todo =>
                todo.IsCompleted &&
                todo.CompletedAt is { } completedAt &&
                completedAt.Date < cutoff),
        ];
    }
}
