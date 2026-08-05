using System;

namespace TimeRenderer.Models;

/// <summary>
/// <see cref="TodoItem"/> の編集可能な状態を写し取ったもの。
///
/// <see cref="ItemSnapshot"/> と同じ考え方で、取り消し履歴は
/// 「対象の参照」と「前後の状態」で表現する。復元は必ず元のインスタンスへ書き戻す
/// （複製で置き換えると、選択状態や他の履歴エントリが指す参照が食い違う）。
///
/// 記録した累計時間（RecordedTicks）は含めない。
/// これは記録の停止に伴って積まれる実績であり、その記録自体が別途取り消せるため、
/// ここでも巻き戻すと二重に戻ってしまう。
/// </summary>
public sealed class TodoSnapshot
{
    public required string Title { get; init; }
    public required string Content { get; init; }
    public DateTime? DueDate { get; init; }
    public DateTime? RemindAt { get; init; }
    public int? RemindOffsetDays { get; init; }
    public DateTime? PlannedOn { get; init; }
    public TodoPriority Priority { get; init; }
    public bool IsCompleted { get; init; }
    public DateTime? CompletedAt { get; init; }
    public string? CategoryId { get; init; }
    public required string ColorCode { get; init; }
    public int EstimatedMinutes { get; init; }
    public TodoRecurrenceUnit Recurrence { get; init; }
    public int RecurrenceInterval { get; init; }
    public required IReadOnlyList<DayOfWeek> RecurrenceDaysOfWeek { get; init; }
    public bool RecurrenceFromCompletion { get; init; }

    /// <summary>
    /// サブタスクは複製で持つ。参照のまま持つと、中身を書き換えたときに
    /// 「変更前の状態」まで一緒に変わってしまい、取り消しで戻せなくなる。
    /// </summary>
    public required IReadOnlyList<TodoSubtask> Subtasks { get; init; }

    public static TodoSnapshot Capture(TodoItem todo) => new()
    {
        Title = todo.Title,
        Content = todo.Content,
        DueDate = todo.DueDate,
        RemindAt = todo.RemindAt,
        RemindOffsetDays = todo.RemindOffsetDays,
        PlannedOn = todo.PlannedOn,
        Priority = todo.Priority,
        IsCompleted = todo.IsCompleted,
        CompletedAt = todo.CompletedAt,
        CategoryId = todo.CategoryId,
        ColorCode = todo.ColorCode,
        EstimatedMinutes = todo.EstimatedMinutes,
        Recurrence = todo.Recurrence,
        RecurrenceInterval = todo.RecurrenceInterval,
        RecurrenceDaysOfWeek = [.. todo.RecurrenceDaysOfWeek],
        RecurrenceFromCompletion = todo.RecurrenceFromCompletion,
        Subtasks = [.. todo.Subtasks.Select(s => s.Clone())],
    };

    /// <summary>この状態を ToDo へ書き戻す</summary>
    public void ApplyTo(TodoItem todo)
    {
        todo.Title = Title;
        todo.Content = Content;
        // 相対指定（RemindOffsetDays）は期限に連動して通知日時を書き換えるため、
        // 先に解除してから 期限 → 通知日時 → 相対指定 の順で戻す。
        // 古い相対指定が残ったまま期限を入れると、戻したい通知日時が計算し直されてしまう
        todo.RemindOffsetDays = null;
        todo.DueDate = DueDate;
        todo.RemindAt = RemindAt;
        todo.RemindOffsetDays = RemindOffsetDays;
        todo.PlannedOn = PlannedOn;
        todo.Priority = Priority;
        todo.IsCompleted = IsCompleted;
        // IsCompleted の設定で「今」が入るため、完了日時はその後に書き戻す
        todo.CompletedAt = CompletedAt;
        todo.CategoryId = CategoryId;
        todo.ColorCode = ColorCode;
        todo.EstimatedMinutes = EstimatedMinutes;
        todo.Recurrence = Recurrence;
        todo.RecurrenceInterval = RecurrenceInterval;
        todo.RecurrenceDaysOfWeek = [.. RecurrenceDaysOfWeek];
        todo.RecurrenceFromCompletion = RecurrenceFromCompletion;
        todo.Subtasks = [.. Subtasks.Select(s => s.Clone())];
    }

    /// <summary>2つの状態が同じか（変化のない編集を履歴に積まないための判定）</summary>
    public bool IsSameAs(TodoSnapshot other) =>
        Title == other.Title &&
        Content == other.Content &&
        DueDate == other.DueDate &&
        RemindAt == other.RemindAt &&
        RemindOffsetDays == other.RemindOffsetDays &&
        PlannedOn == other.PlannedOn &&
        Priority == other.Priority &&
        IsCompleted == other.IsCompleted &&
        CategoryId == other.CategoryId &&
        ColorCode == other.ColorCode &&
        EstimatedMinutes == other.EstimatedMinutes &&
        Recurrence == other.Recurrence &&
        RecurrenceInterval == other.RecurrenceInterval &&
        RecurrenceDaysOfWeek.SequenceEqual(other.RecurrenceDaysOfWeek) &&
        RecurrenceFromCompletion == other.RecurrenceFromCompletion &&
        Subtasks.Count == other.Subtasks.Count &&
        Subtasks.Zip(other.Subtasks).All(pair => pair.First.IsSameAs(pair.Second));
}
