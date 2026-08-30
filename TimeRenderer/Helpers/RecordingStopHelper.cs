using System;
using System.Collections.Generic;
using System.Linq;

using TimeRenderer.Models;

namespace TimeRenderer.Helpers;

/// <summary>記録停止時の離席区間整理と、保存すべき実作業区間の算出。</summary>
public static class RecordingStopHelper
{
    /// <summary>記録範囲と重なる離席だけを切り詰め、開始時刻順に返す。</summary>
    public static List<AwayPeriod> ClipAndSortAwayPeriods(
        DateTime start,
        DateTime end,
        IEnumerable<AwayPeriod> awayPeriods)
    {
        ArgumentNullException.ThrowIfNull(awayPeriods);
        if (end <= start) return [];

        return
        [
            .. awayPeriods
                .Select(period => period.ClipTo(start, end))
                .Where(period => period != null)
                .Select(period => period!)
                .OrderBy(period => period.Start),
        ];
    }

    /// <summary>
    /// 記録停止時に保存する区間を返す。離席を残す場合は全区間を1件、
    /// 除外する場合は重複・隣接する離席をまとめて差し引く。
    /// </summary>
    public static List<(DateTime Start, DateTime End)> BuildSegments(
        DateTime start,
        DateTime end,
        IEnumerable<AwayPeriod> awayPeriods,
        bool excludeAway)
    {
        ArgumentNullException.ThrowIfNull(awayPeriods);
        if (end <= start) return [];
        if (!excludeAway) return [(start, end)];

        var merged = MergeAwayPeriods(start, end, awayPeriods);
        var result = new List<(DateTime Start, DateTime End)>();
        var cursor = start;

        foreach (var (awayStart, awayEnd) in merged)
        {
            if (awayStart > cursor) result.Add((cursor, awayStart));
            if (awayEnd > cursor) cursor = awayEnd;
        }

        if (cursor < end) result.Add((cursor, end));
        return result;
    }

    /// <summary>重複する離席を二重計上せず、記録から除かれる合計時間を返す。</summary>
    public static TimeSpan GetExcludedDuration(
        DateTime start,
        DateTime end,
        IEnumerable<AwayPeriod> awayPeriods)
    {
        ArgumentNullException.ThrowIfNull(awayPeriods);
        if (end <= start) return TimeSpan.Zero;

        var workTicks = BuildSegments(start, end, awayPeriods, excludeAway: true)
            .Sum(segment => (segment.End - segment.Start).Ticks);
        return TimeSpan.FromTicks((end - start).Ticks - workTicks);
    }

    public static string BuildAutoExcludeNotice(
        DateTime start,
        DateTime end,
        IReadOnlyCollection<AwayPeriod> awayPeriods)
    {
        ArgumentNullException.ThrowIfNull(awayPeriods);

        var duration = GetExcludedDuration(start, end, awayPeriods);
        var durationText = duration.TotalHours >= 1
            ? $"{(int)duration.TotalHours}時間{duration.Minutes}分"
            : $"{(int)duration.TotalMinutes}分";

        return $"離席 {awayPeriods.Count} 件・合計 {durationText} を記録から除きました（Ctrl+Z でこの記録ごと取り消せます）";
    }

    private static List<(DateTime Start, DateTime End)> MergeAwayPeriods(
        DateTime start,
        DateTime end,
        IEnumerable<AwayPeriod> awayPeriods)
    {
        var merged = new List<(DateTime Start, DateTime End)>();

        foreach (var period in ClipAndSortAwayPeriods(start, end, awayPeriods))
        {
            if (merged.Count > 0 && period.Start <= merged[^1].End)
            {
                if (period.End > merged[^1].End)
                {
                    merged[^1] = (merged[^1].Start, period.End);
                }
            }
            else
            {
                merged.Add((period.Start, period.End));
            }
        }

        return merged;
    }
}
