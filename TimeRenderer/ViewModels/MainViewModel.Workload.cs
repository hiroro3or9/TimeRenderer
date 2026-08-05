using System;
using System.Collections.Generic;
using System.Linq;

using TimeRenderer.Models;

namespace TimeRenderer.ViewModels;

/// <summary>
/// 「今日やる」の積みすぎ検知。
///
/// これまで、決めた量が多すぎたことに気づけるのは<b>退勤時のふりかえり</b>だった。
/// 終わってから「片付かなかった4件を明日へ送りますか」と聞かれる形で、
/// そのときにはもう積み直しようがない。
///
/// 見積もりの傾向（<see cref="TodoEstimateStats"/>）は既に持っているので、
/// それを today の残り勤務時間と突き合わせて、朝のうちに出す。
///
/// 方針:
/// - <b>止めない・確認も出さない</b>。ToDo パネルに1行出すだけにする。
///   毎朝ダイアログが出るようになれば、中身を読まずに閉じるようになる
/// - 見積もりが無い ToDo は数に入れない。0分として足すと「まだ余裕がある」と嘘をつく
/// - いつもの退勤時刻は<b>自分で押した退勤</b>の中央値から求める。
///   自動で締めた退勤は推測値なので、それを基準にすると推測が推測を生む
/// </summary>
public partial class MainViewModel
{
    /// <summary>いつもの退勤時刻を出すのに最低限必要な勤務記録の数</summary>
    private const int MinWorkDaySamplesForTypicalEnd = 5;

    /// <summary>この割合を超えたら「積みすぎ」として強調する（見込み ÷ 残り時間）</summary>
    private const double OvercommitThreshold = 1.0;

    /// <summary>
    /// ToDo パネルに出す1行。
    /// </summary>
    /// <param name="Text">本文（例: "見込み 6:30 ・ 退勤まで 3:20"）</param>
    /// <param name="IsOver">残り時間を超えているか（強調するかの判定）</param>
    /// <param name="ToolTipText">内訳の説明</param>
    public sealed record WorkloadNotice(string Text, bool IsOver, string ToolTipText);

    private WorkloadNotice? _todayWorkload;
    /// <summary>今日の見込みと残り時間（出せる材料が無ければ null）</summary>
    public WorkloadNotice? TodayWorkload
    {
        get => _todayWorkload;
        private set
        {
            if (SetProperty(ref _todayWorkload, value)) OnPropertyChanged(nameof(HasTodayWorkload));
        }
    }

    public bool HasTodayWorkload => TodayWorkload != null;

    /// <summary>
    /// 今日やるの見込み時間と、いつもの退勤時刻までの残りを突き合わせる。
    ///
    /// 出さない条件がいくつもあるのは、材料が薄いまま数字を出すと外れ続けて
    /// 読まれなくなるため。無言でいるほうが害が少ない。
    /// </summary>
    private void RebuildTodayWorkload()
    {
        var now = DateTime.Now;

        // 勤務中でなければ「退勤まで」の基準が無い
        if (_activeWorkLog == null)
        {
            TodayWorkload = null;
            return;
        }

        var planned = Todos.Where(t => t.IsPlannedToday).ToList();
        if (planned.Count == 0)
        {
            TodayWorkload = null;
            return;
        }

        var (need, counted) = EstimateRemainingWork(planned);
        if (counted == 0)
        {
            TodayWorkload = null;
            return;
        }

        var available = GetRemainingWorkTime(now);
        if (available == null)
        {
            TodayWorkload = null;
            return;
        }

        var isOver = available.Value <= TimeSpan.Zero
            || need.TotalMinutes > available.Value.TotalMinutes * OvercommitThreshold;

        var skipped = planned.Count - counted;
        var tooltip =
            $"今日やる {planned.Count} 件のうち、見積もりのある {counted} 件の残りを合計し、" +
            "これまでの実績（見積もりに対して実際どれだけかかったか）で補正しています。" +
            (skipped > 0 ? $"\n見積もりの無い {skipped} 件は含めていません。" : string.Empty) +
            "\n退勤時刻は、自分で押した退勤の中央値から見ています。";

        TodayWorkload = new WorkloadNotice(
            $"見込み {Format(need)} ・ 退勤まで {Format(available.Value)}",
            isOver,
            tooltip);
    }

    /// <summary>
    /// 見積もりのある ToDo について、残り時間を実績の傾向で補正して合計する。
    /// 既に記録した分は差し引く（半分終わっているものを丸ごと数えない）。
    /// </summary>
    private (TimeSpan Need, int Counted) EstimateRemainingWork(IReadOnlyList<TodoItem> planned)
    {
        double minutes = 0;
        int counted = 0;

        foreach (var todo in planned)
        {
            if (!todo.HasEstimate) continue;

            counted++;

            var remaining = todo.EstimatedDuration - todo.RecordedDuration;
            if (remaining <= TimeSpan.Zero) continue;

            // 傾向が取れていなければ等倍（見積もりをそのまま信じる）
            var ratio = EstimateStats.For(todo.CategoryId)?.Ratio ?? 1.0;
            minutes += remaining.TotalMinutes * ratio;
        }

        return (TimeSpan.FromMinutes(minutes), counted);
    }

    /// <summary>
    /// いつもの退勤時刻まで、あとどれだけ働ける見込みか。
    /// 勤務中でない、または標本が足りない場合は null。
    /// </summary>
    private TimeSpan? GetRemainingWorkTime(DateTime now)
    {
        if (_activeWorkLog == null) return null;

        var typical = GetTypicalWorkEndOffset();
        if (typical == null) return null;

        var target = _activeWorkLog.StartTime.Date + typical.Value;
        return target > now ? target - now : TimeSpan.Zero;
    }

    /// <summary>
    /// いつもの退勤時刻を「勤務日の0時からの経過時間」で返す。
    ///
    /// 時刻（TimeOfDay）ではなく経過時間で持つのは、深夜勤務のため。
    /// 2時に退勤した日を 02:00 として混ぜると中央値が大きく前へ引っ張られるので、
    /// 26:00 として扱う。
    /// </summary>
    private TimeSpan? GetTypicalWorkEndOffset()
    {
        var offsets = _workDayLogs
            .Where(l => l.EndTime is { } end
                        && end > l.StartTime
                        && l.EndSource == WorkEndSource.Manual) // 自動で締めた分は推測値なので使わない
            .Select(l => l.EndTime!.Value - l.StartTime.Date)
            .Where(o => o > TimeSpan.Zero && o < TimeSpan.FromHours(30))
            .OrderBy(o => o)
            .ToList();

        if (offsets.Count < MinWorkDaySamplesForTypicalEnd) return null;

        int mid = offsets.Count / 2;
        return offsets.Count % 2 == 1
            ? offsets[mid]
            : TimeSpan.FromTicks((offsets[mid - 1].Ticks + offsets[mid].Ticks) / 2);
    }

    private static string Format(TimeSpan span)
    {
        if (span <= TimeSpan.Zero) return "0:00";
        return $"{(int)span.TotalHours}:{span.Minutes:D2}";
    }
}
