using System;
using System.Collections.Generic;

using TimeRenderer.Helpers;
using TimeRenderer.Models;

namespace TimeRenderer.ViewModels;

/// <summary>
/// 記録漏れ（未記録の帯）の検出。
///
/// 離席検知が「記録しすぎ」を防ぐ仕組みなのに対し、こちらは逆に
/// 「記録が無い時間」を見えるようにする。日ビューの空白が
/// 「何もしていなかった」のか「付け忘れた」のかを、その場で区別できるようにするのが狙い。
///
/// 方針:
/// - 対象は<b>勤務記録がある日の勤務時間内だけ</b>。出勤していない日は一切指摘しない
/// - 保存しない。表示のたびに算出する（状態で持つと編集のたびにズレる）
/// - 短い空白は無視する。トイレや小休止まで指摘すると、帯そのものが背景になってしまう
/// </summary>
public partial class MainViewModel
{
    /// <summary>この長さ未満の空白は記録漏れとみなさない（設定化は後続の作業）</summary>
    private static readonly TimeSpan UnrecordedGapMinDuration = TimeSpan.FromMinutes(15);

    /// <summary>当日ぶんを伸ばすための再計算の間隔（時間の経過だけで変わる部分）</summary>
    private static readonly TimeSpan UnrecordedGapRefreshInterval = TimeSpan.FromMinutes(5);

    private DateTime _lastUnrecordedGapRefresh = DateTime.MinValue;

    private IReadOnlyList<UnrecordedGap> _unrecordedGaps = [];

    /// <summary>日/週ビューに描く未記録の帯（日単位に分割済み）</summary>
    public IReadOnlyList<UnrecordedGap> UnrecordedGaps
    {
        get => _unrecordedGaps;
        private set => SetProperty(ref _unrecordedGaps, value);
    }

    /// <summary>
    /// 表示範囲に重なる勤務記録それぞれについて、記録で埋まっていない区間を求める。
    /// レイアウトの再計算（<c>RecalculateLayoutCore</c>）と勤務記録の変更から呼ばれる。
    /// </summary>
    private void RebuildUnrecordedGaps()
    {
        _lastUnrecordedGapRefresh = DateTime.Now;

        // 帯を描くのは日/週ビューだけ。月・スプリント・タイムラインでは情報密度が保たない
        if (!IsDayOrWeekMode || _workDayLogs.Count == 0)
        {
            if (UnrecordedGaps.Count > 0) UnrecordedGaps = [];
            return;
        }

        var range = GetLayoutRange();
        if (range is null)
        {
            if (UnrecordedGaps.Count > 0) UnrecordedGaps = [];
            return;
        }

        var (rangeStart, rangeEnd) = range.Value;
        var now = DateTime.Now;
        var gaps = new List<UnrecordedGap>();

        foreach (var log in _workDayLogs)
        {
            // 未退勤の勤務は「今まで」で切る。これからの時間を記録漏れにはしない
            var workStart = log.StartTime;
            var workEnd = log.EndTime ?? now;
            if (workEnd > now) workEnd = now;

            // 表示範囲でクリップする（範囲外の日ぶんの帯は作らない）
            if (workStart < rangeStart) workStart = rangeStart;
            if (workEnd > rangeEnd) workEnd = rangeEnd;
            if (workEnd <= workStart) continue;

            foreach (var gap in UnrecordedGapHelper.Detect(
                         workStart, workEnd, CollectCoveredRanges(workStart, workEnd), UnrecordedGapMinDuration))
            {
                gaps.AddRange(UnrecordedGapHelper.SplitByDay(gap));
            }
        }

        UnrecordedGaps = gaps;
    }

    /// <summary>
    /// 指定範囲を「記録済み」として覆う区間を集める。
    ///
    /// 仮想アイテム（まだ消化していない定期予定）は覆いに含めない。
    /// 予定であって実績ではないうえ、「予定はあるのに記録が無い」こそ拾いたい対象のため。
    /// 色フィルタで隠れているアイテムは含める（隠れていても記録は記録）。
    /// </summary>
    private List<(DateTime Start, DateTime End)> CollectCoveredRanges(DateTime rangeStart, DateTime rangeEnd)
    {
        var covered = new List<(DateTime Start, DateTime End)>();

        foreach (var item in ScheduleItems)
        {
            if (item.IsAllDay || item.IsVirtual) continue;
            if (item.EndTime <= rangeStart || item.StartTime >= rangeEnd) continue;
            covered.Add((item.StartTime, item.EndTime));
        }

        // 記録中の分はまだアイテムになっていないため、進行中の区間として足す
        if (IsRecording && RecordingStartTime.HasValue)
        {
            var now = DateTime.Now;
            if (RecordingStartTime.Value < rangeEnd && now > rangeStart)
            {
                covered.Add((RecordingStartTime.Value, now));
            }
        }

        return covered;
    }

    /// <summary>
    /// 時計から定期的に呼ぶ。当日の未記録は時間の経過だけで伸びるが、
    /// 毎tick再計算する必要は無いので間隔を空ける。
    /// </summary>
    private void UpdateUnrecordedGapTick(DateTime now)
    {
        if (!IsDayOrWeekMode) return;
        if (now - _lastUnrecordedGapRefresh < UnrecordedGapRefreshInterval) return;

        RebuildUnrecordedGaps();
    }
}
