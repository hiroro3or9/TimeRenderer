using System;
using System.Collections.Generic;
using System.Linq;

using TimeRenderer.Models;

namespace TimeRenderer.Helpers;

/// <summary>ToDoから予定・実績を作るときの、画面状態に依存しない変換規則。</summary>
public static class TodoTimeBlockHelper
{
    public static readonly TimeSpan DefaultBlockDuration = TimeSpan.FromHours(1);

    public static TimeSpan GetBlockDuration(TodoItem todo)
    {
        ArgumentNullException.ThrowIfNull(todo);
        return todo.HasEstimate ? todo.EstimatedDuration : DefaultBlockDuration;
    }

    public static ScheduleItem CreateScheduleItem(
        TodoItem todo,
        DateTime start,
        DateTime end,
        string? resolvedCategoryId,
        string? defaultProjectCodeId)
    {
        ArgumentNullException.ThrowIfNull(todo);

        return new ScheduleItem
        {
            Kind = ScheduleItemKind.Planned,
            Title = todo.Title,
            StartTime = start,
            EndTime = end,
            ColorCode = todo.ColorCode,
            CategoryId = todo.CategoryId ?? resolvedCategoryId,
            ProjectCodeId = defaultProjectCodeId,
            TodoId = todo.Id,
        };
    }

    /// <summary>有効な実績区間の合計をToDoへ積算する。加算が無ければfalse。</summary>
    public static bool AccumulateRecordedTime(
        TodoItem todo,
        IReadOnlyList<(DateTime Start, DateTime End)> segments)
    {
        ArgumentNullException.ThrowIfNull(todo);
        ArgumentNullException.ThrowIfNull(segments);

        var ticks = segments.Sum(segment => (segment.End - segment.Start).Ticks);
        if (ticks <= 0) return false;

        todo.RecordedTicks += ticks;
        return true;
    }

    public static string FormatBlockDuration(TimeSpan duration) =>
        duration.TotalHours >= 1 ? $"{duration.TotalHours:0.#} 時間" : $"{duration.TotalMinutes:0} 分";
}
