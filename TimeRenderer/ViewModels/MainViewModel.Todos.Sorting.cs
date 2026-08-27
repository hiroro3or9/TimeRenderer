using System;
using System.Collections.Generic;
using System.Linq;

using TimeRenderer.Helpers;
using TimeRenderer.Models;

namespace TimeRenderer.ViewModels;

/// <summary>ToDo一覧の表示順、手動並べ替え、日・週ビュー用チップ。</summary>
public partial class MainViewModel
{
    /// <summary>ToDo が増減・変化したときの共通処理（表示の作り直しと保存）</summary>
    private void OnTodoChanged()
    {
        RebuildVisibleTodos();
        NotifyTodoCountsChanged();
        InvalidateEstimateStats(); // 完了・記録時間が動くと見積もりの傾向も変わる
        RecalculateLayout();       // 終日行のチップも作り直す
        ScheduleTodoSave();
    }

    /// <summary>
    /// 表示用の一覧を作り直す。
    /// 完了済みは常に末尾へ送り、その中では選択中の並び順に従う。
    /// </summary>
    private void RebuildVisibleTodos()
    {
        IEnumerable<TodoItem> source = ShowCompletedTodos ? Todos : Todos.Where(t => !t.IsCompleted);
        VisibleTodos = TodoOrderHelper.Order(source, CurrentTodoSortMode);
    }

    /// <summary>
    /// 手動モードでなければ切り替える。
    /// 切り替えた瞬間に並びが崩れないよう、今見えている順をそのまま初期値にする。
    /// </summary>
    private void EnsureManualSort()
    {
        if (CurrentTodoSortMode == TodoSortMode.Manual) return;

        ApplyManualOrder(VisibleTodos);
        CurrentTodoSortMode = TodoSortMode.Manual;
    }

    /// <summary>
    /// 並び順を SortOrder へ書き戻す。
    /// 一覧に出ていない ToDo（完了済み・絞り込みで隠れているもの）は、
    /// 番号がぶつからないよう後ろへ続けて振る。
    /// </summary>
    private void ApplyManualOrder(IReadOnlyList<TodoItem> ordered)
    {
        // 1件ごとの再構築・保存を避けるため、書き戻しの間は通知の処理を止める
        _isUpdatingTodo = true;
        try
        {
            TodoOrderHelper.ApplyManualOrder(Todos, ordered);
        }
        finally
        {
            _isUpdatingTodo = false;
        }
    }

    /// <summary>
    /// 手動並べ替え：moved を target の位置へ差し込む（ドラッグ＆ドロップ用）。
    /// 並べ替えは手動モードでしか意味がないため、必要なら自動で切り替える。
    /// </summary>
    public void MoveTodoTo(TodoItem moved, TodoItem target)
    {
        if (ReferenceEquals(moved, target)) return;

        // 手動モードへの切り替えで並び順が動くため、その前に控える
        var before = CaptureTodoOrder();
        EnsureManualSort();

        var list = VisibleTodos.ToList();
        var from = list.IndexOf(moved);
        var to = list.IndexOf(target);
        if (from < 0 || to < 0) return;

        list.RemoveAt(from);
        list.Insert(to, moved);

        ApplyManualOrder(list);
        RecordTodoReorder(before);
        OnTodoChanged();
    }

    /// <summary>手動並べ替え：1つ上／下へ移動する（Ctrl+↑↓ とコンテキストメニュー用）</summary>
    private void MoveTodoBy(TodoItem? todo, int delta)
    {
        if (todo == null) return;

        var before = CaptureTodoOrder();
        EnsureManualSort();

        var list = VisibleTodos.ToList();
        var from = list.IndexOf(todo);
        var to = from + delta;
        if (from < 0 || to < 0 || to >= list.Count) return;

        list.RemoveAt(from);
        list.Insert(to, todo);

        ApplyManualOrder(list);
        RecordTodoReorder(before);
        OnTodoChanged();

        SelectedTodo = todo; // 移動しても選択が外れないようにする
    }

    /// <summary>
    /// 終日行に並べる ToDo チップを作り直し、必要な段数を返す。
    /// 期限付きの未完了 ToDo だけを対象にし、終日イベントの段の続きに積む。
    /// </summary>
    /// <param name="rangeStart">表示範囲の開始日</param>
    /// <param name="rangeEnd">表示範囲の終了日（この日は含まない）</param>
    /// <param name="allDayRowCounts">日付ごとの終日イベントの段数</param>
    /// <returns>チップまで含めた必要段数（チップが無ければ 0）</returns>
    private int RebuildTodoChips(
        DateTime rangeStart, DateTime rangeEnd, Dictionary<DateTime, int> allDayRowCounts)
    {
        // 終日行を描くのは日/週ビューだけ。他のモードでは作っても誰も見ない
        if (!IsDayOrWeekMode || Todos.Count == 0)
        {
            if (TodoChips.Count > 0) TodoChips = [];
            return 0;
        }

        var targets = Todos
            .Where(t => !t.IsCompleted && t.DueDate is { } due && due >= rangeStart && due < rangeEnd)
            .Where(IsTodoVisible) // 色フィルタはビュー上の表示にだけ効かせる（パネルは全件のまま）
            .GroupBy(t => t.DueDate!.Value.Date);

        var chips = new List<TodoChip>();
        int maxRows = 0;

        foreach (var group in targets)
        {
            var baseRow = allDayRowCounts.TryGetValue(group.Key, out var count) ? count : 0;

            var index = 0;
            foreach (var todo in group.OrderByDescending(t => t.Priority).ThenBy(t => t.CreatedAt))
            {
                chips.Add(new TodoChip(todo, group.Key, baseRow + index));
                index++;
            }

            if (baseRow + index > maxRows) maxRows = baseRow + index;
        }

        TodoChips = chips;
        return maxRows;
    }
}
