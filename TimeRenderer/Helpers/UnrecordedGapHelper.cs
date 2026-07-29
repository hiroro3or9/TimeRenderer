using System;
using System.Collections.Generic;
using System.Linq;

using TimeRenderer.Models;

namespace TimeRenderer.Helpers;

/// <summary>
/// 勤務時間から作業記録を引いて「記録が無い区間」を求める。
///
/// ViewModel から切り離した純粋な計算にしてある。
/// 境界（勤務の途中で終わる記録、重なり合う記録、日またぎ）で間違えやすい部分なので、
/// 入力と出力だけで確かめられる形にしておきたい。
/// </summary>
public static class UnrecordedGapHelper
{
    /// <summary>
    /// [workStart, workEnd) から covered の区間を取り除き、残った区間のうち
    /// minDuration 以上のものを返す。covered は順不同・重なりありでよい。
    /// </summary>
    public static List<UnrecordedGap> Detect(
        DateTime workStart,
        DateTime workEnd,
        IEnumerable<(DateTime Start, DateTime End)> covered,
        TimeSpan minDuration)
    {
        var gaps = new List<UnrecordedGap>();
        if (workEnd <= workStart) return gaps;

        // 勤務時間の外へはみ出す部分は切り詰める（前日から続く記録などがあるため）
        var ranges = covered
            .Select(c => (
                Start: c.Start < workStart ? workStart : c.Start,
                End: c.End > workEnd ? workEnd : c.End))
            .Where(c => c.End > c.Start)
            .OrderBy(c => c.Start)
            .ToList();

        // 開始順に走査し、「まだ埋まっていない位置」を cursor で追う。
        // 重なり・入れ子は cursor を進めるだけで自然に吸収される
        var cursor = workStart;
        foreach (var (start, end) in ranges)
        {
            if (start > cursor) AddIfLongEnough(gaps, cursor, start, minDuration);
            if (end > cursor) cursor = end;
            if (cursor >= workEnd) return gaps;
        }

        AddIfLongEnough(gaps, cursor, workEnd, minDuration);
        return gaps;
    }

    private static void AddIfLongEnough(
        List<UnrecordedGap> gaps, DateTime start, DateTime end, TimeSpan minDuration)
    {
        if (end - start >= minDuration) gaps.Add(new UnrecordedGap(start, end));
    }

    /// <summary>
    /// 日をまたぐ区間を日単位に切り分ける（深夜勤務の対応）。
    /// 日/週ビューは1日1列で描くため、またいだままでは翌日側に何も出ない。
    /// </summary>
    public static IEnumerable<UnrecordedGap> SplitByDay(UnrecordedGap gap)
    {
        if (gap.EndTime <= gap.StartTime) yield break;

        var cursor = gap.StartTime;
        while (cursor < gap.EndTime)
        {
            var dayEnd = cursor.Date.AddDays(1);
            var end = gap.EndTime < dayEnd ? gap.EndTime : dayEnd;
            yield return new UnrecordedGap(cursor, end);
            cursor = end;
        }
    }
}
