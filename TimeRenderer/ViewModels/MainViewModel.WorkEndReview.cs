using System;
using System.Collections.Generic;
using System.Linq;

using TimeRenderer.Helpers;
using TimeRenderer.Models;

namespace TimeRenderer.ViewModels;

/// <summary>
/// 退勤時のふりかえり。
///
/// 「今日やる」（<see cref="TodoItem.PlannedOn"/>）は日付で持っているため、
/// 日をまたげば自動的に外れる。狙いどおりの挙動だが、そのままだと
/// <b>手を付けなかったものが黙って消える</b>。決めたのにやらなかった、という事実が残らない。
///
/// 退勤は1日の中で唯一「終わり」がはっきりしている操作なので、そこで一度だけ
/// 今日の実績を出し、残ったものを明日へ送るかどうかを聞く。
/// 送るのは <see cref="TodoItem.PlannedOn"/> だけで、期限には触らない
/// （期限は約束、今日やるは計画で、動かしてよい方が違う）。
///
/// ふりかえりの一言（<see cref="WorkDayLog.Note"/>）もここで書く。
/// 常設の入力欄を置いても書くきっかけが無く、書いたものを読み返す導線も生まれなかった。
/// 1日の区切りに一度だけ聞き、統計から読み返せる形にしている。
/// </summary>
public partial class MainViewModel
{
    private bool _isWorkEndReviewEnabled = true;
    /// <summary>退勤したときに、今日のふりかえりと繰り越しの確認を出すか</summary>
    public bool IsWorkEndReviewEnabled
    {
        get => _isWorkEndReviewEnabled;
        set
        {
            if (SetProperty(ref _isWorkEndReviewEnabled, value)) SaveSettings();
        }
    }

    /// <summary>ふりかえりを表示中か（多重表示の抑止）</summary>
    private bool _isShowingWorkEndReview;

    /// <summary>
    /// 退勤の直後に呼ぶ。出すものが無ければ何もしない。
    ///
    /// 自動で締めた退勤（日付またぎの後始末）では呼ばない。
    /// 本人が画面を見ていないときに出しても、明日へ送る判断はできない。
    /// </summary>
    private void ShowWorkEndReview(WorkDayLog log)
    {
        if (!IsWorkEndReviewEnabled) return;
        if (_isShowingWorkEndReview) return;
        if (log.EndTime is not { } end) return;

        var date = log.StartTime.Date;
        var recorded = SumRecordedOn(date);
        var completed = Todos.Count(t => t.IsCompleted && t.CompletedAt?.Date == date);
        var candidates = BuildCarryOverCandidates(date);

        // 実績も残りものも無い日は、ただ手を止めるだけになるので出さない
        if (candidates.Count == 0 && completed == 0 && recorded == TimeSpan.Zero) return;

        _isShowingWorkEndReview = true;
        try
        {
            var result = _dialogService.ShowWorkEndReviewDialog(
                date, log.StartTime, end, recorded, completed, candidates, log.Note);

            SetWorkDayNote(log, result.Note);

            if (result.CarriedOver is { Count: > 0 }) CarryOverTodos(result.CarriedOver);
        }
        finally
        {
            _isShowingWorkEndReview = false;
        }
    }

    /// <summary>
    /// 勤務記録にふりかえりの一言を書き込む。
    /// 変わっていなければ何もしない（退勤のたびに同じ内容で保存し直さない）。
    /// </summary>
    private void SetWorkDayNote(WorkDayLog log, string? note)
    {
        var value = (note ?? string.Empty).Trim();
        if (log.Note == value) return;

        log.Note = value;
        SaveWorkDays();
        RebuildWorkDayMarkers(); // マーカーのツールチップに一言を出しているため
        NotifyWorkDayNotesChanged();
    }

    /// <summary>その日に記録した時間の合計（終日アイテムは対象外）</summary>
    private TimeSpan SumRecordedOn(DateTime date)
    {
        var ticks = ScheduleItems
            .Where(i => i.IsRecorded && !i.IsAllDay && i.StartTime.Date == date.Date && i.EndTime > i.StartTime)
            .Sum(i => (i.EndTime - i.StartTime).Ticks);

        return TimeSpan.FromTicks(ticks);
    }

    /// <summary>
    /// 明日へ送る候補を集める。
    ///
    /// 対象は「今日やると決めたもの」と「期限が今日までのもの」。
    /// 期限が先のものまで並べると、毎日ほぼ全件が出てきて意味を失う。
    /// </summary>
    private List<WorkEndCarryOver> BuildCarryOverCandidates(DateTime date)
    {
        var isToday = date.Date == DateTime.Today;

        // 今日ぶんの記録を ToDo ごとに合計する。
        // 「決めたのに一度も触らなかった」のか「着手はしたが終わらなかった」のかで、
        // 明日へ送るかどうかの判断が変わる
        var recordedByTodo = ScheduleItems
            .Where(i => i.IsRecorded && !i.IsAllDay && i.StartTime.Date == date.Date && !string.IsNullOrEmpty(i.TodoId))
            .GroupBy(i => i.TodoId!)
            .ToDictionary(g => g.Key, g => TimeSpan.FromTicks(g.Sum(i => (i.EndTime - i.StartTime).Ticks)));

        var candidates = new List<WorkEndCarryOver>();

        foreach (var todo in Todos)
        {
            if (todo.IsCompleted) continue;

            var planned = todo.PlannedOn?.Date == date.Date;
            var due = todo.DueDate?.Date;
            var overdueDays = due.HasValue ? (date.Date - due.Value).Days : 0;

            if (!planned && (due == null || overdueDays < 0)) continue;

            var reason = planned ? "今日やる"
                : overdueDays > 0 ? $"期限 {overdueDays}日超過"
                : "期限が今日";

            // 過去の日を締めている場合に「今日」と書くと嘘になるので、日付で言い方を変える
            var detail = recordedByTodo.TryGetValue(todo.Id, out var span) && span > TimeSpan.Zero
                ? $"{(isToday ? "今日" : date.ToString("M/d"))} {(int)span.TotalHours}:{span.Minutes:D2} 記録"
                : "手つかず";

            candidates.Add(new WorkEndCarryOver
            {
                Todo = todo,
                ReasonText = reason,
                DetailText = detail,
                // 既に明日以降の予定になっているものは、送り直す必要が無いので最初から外しておく
                IsSelected = todo.PlannedOn is null || todo.PlannedOn.Value.Date <= date.Date,
            });
        }

        // 今日やると決めたもの → 超過が大きいもの → 期限が今日、の順に並べる
        return
        [
            .. candidates
                .OrderByDescending(c => c.Todo.PlannedOn?.Date == date.Date)
                .ThenByDescending(c => c.Todo.DueDate.HasValue ? (date.Date - c.Todo.DueDate.Value.Date).Days : int.MinValue)
                .ThenByDescending(c => c.Todo.Priority)
                .ThenBy(c => c.Todo.CreatedAt)
        ];
    }

    /// <summary>
    /// 選ばれた ToDo を翌日へ送る。
    /// 1回の操作なので、取り消し履歴にはまとめて1件として積む。
    /// </summary>
    private void CarryOverTodos(IReadOnlyList<TodoItem> todos)
    {
        var target = DateTime.Today.AddDays(1);
        var edits = new List<IUndoableEdit>();

        _isUpdatingTodo = true;
        try
        {
            foreach (var todo in todos)
            {
                if (!Todos.Contains(todo)) continue;
                if (todo.PlannedOn?.Date == target) continue;

                var before = TodoSnapshot.Capture(todo);
                todo.PlannedOn = target;
                var after = TodoSnapshot.Capture(todo);

                if (!before.IsSameAs(after))
                {
                    edits.Add(new ModifyTodoEdit(todo, before, after, "明日へ繰り越し"));
                }
            }
        }
        finally
        {
            _isUpdatingTodo = false;
        }

        if (edits.Count == 0) return;

        PushEdits(edits, $"ToDo {edits.Count} 件を明日へ繰り越し");
        OnTodoChanged();

        ShowAutoStartNotice($"{edits.Count} 件の ToDo を明日やることにしました");
    }
}
