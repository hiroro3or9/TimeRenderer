using System;
using System.Collections.Generic;
using System.Linq;

using TimeRenderer.Helpers;
using TimeRenderer.Models;

namespace TimeRenderer.ViewModels;

/// <summary>ToDo をカレンダーへ置くタイムブロッキング操作。</summary>
public partial class MainViewModel
{
    /// <summary>
    /// ToDo を指定時刻に置いて、作業時間を先に押さえる。
    /// 長さは見積もり時間、無ければ1時間。予定には TodoId が入るので、
    /// この予定から記録を回せば実績が ToDo へ戻ってくる。
    /// </summary>
    /// <param name="todo">置く ToDo</param>
    /// <param name="start">開始時刻</param>
    public void BlockTimeForTodo(TodoItem todo, DateTime start)
    {
        var duration = TodoTimeBlockHelper.GetBlockDuration(todo);

        var item = CreateItemForTodo(todo, start, start.Add(duration));
        ScheduleItems.Add(item);
        RecordAdd(item);

        ShowAutoStartNotice(
            $"「{todo.Title}」を {start:M/d HH:mm} に {TodoTimeBlockHelper.FormatBlockDuration(duration)} で置きました");
    }

    /// <summary>
    /// 記録漏れの帯を ToDo で埋める。
    /// こちらは過去に実際にやっていた時間なので、予定を作ると同時に ToDo へも積算する
    /// （これから押さえる時間ブロックとは扱いが違う）。
    /// </summary>
    public void FillGapWithTodo(TodoItem todo, DateTime start, DateTime end)
    {
        if (end <= start) return;

        var item = CreateItemForTodo(todo, start, end);
        item.Kind = ScheduleItemKind.Recorded;
        item.Content = $"記録時間: {(int)(end - start).TotalHours}:{(end - start).Minutes:D2}";

        var beforeTicks = todo.RecordedTicks;

        _isUpdatingTodo = true;
        try
        {
            ScheduleItems.Add(item);
            TodoTimeBlockHelper.AccumulateRecordedTime(todo, [(start, end)]);
        }
        finally
        {
            _isUpdatingTodo = false;
        }

        // 予定の追加と ToDo への積算で1回の操作なので、まとめて1件の履歴にする
        var edits = new List<IUndoableEdit> { new AddItemEdit(item) };
        if (todo.RecordedTicks != beforeTicks)
        {
            edits.Add(new ChangeTodoRecordedTimeEdit(
                todo, beforeTicks, todo.RecordedTicks, "実績の追加"));
        }
        PushEdits(edits, $"「{todo.Title}」で記録漏れを埋める");

        OnTodoChanged();
    }

    /// <summary>
    /// 記録漏れの帯を埋める操作の入口。
    /// 未完了の ToDo から選ばせ、選ばれたらその帯の時間幅で実績を作る。
    /// </summary>
    public void FillGapFromTodoPicker(UnrecordedGap gap)
    {
        var candidates = Todos.Where(t => !t.IsCompleted).ToList();
        if (candidates.Count == 0)
        {
            _dialogService.ShowMessage("未完了の ToDo がありません。", "ToDo から埋める");
            return;
        }

        var message = $"{gap.StartTime:M/d (ddd) HH:mm} 〜 {gap.EndTime:HH:mm} の記録として残す ToDo を選んでください。";
        var todo = _dialogService.ShowTodoPickerDialog(message, candidates);
        if (todo == null) return;

        FillGapWithTodo(todo, gap.StartTime, gap.EndTime);
    }

    /// <summary>ToDo のタイトル・色・紐づけを引き継いだ予定アイテムを作る</summary>
    private ScheduleItem CreateItemForTodo(TodoItem todo, DateTime start, DateTime end) =>
        TodoTimeBlockHelper.CreateScheduleItem(
            todo,
            start,
            end,
            ResolveCategory(todo.CategoryId, todo.ColorCode)?.Id,
            DefaultProjectCode?.Id);
}
