using System;
using System.Collections.Generic;
using System.Linq;

using TimeRenderer.Models;

namespace TimeRenderer.Helpers;

/// <summary>
/// タイムラインの行（レーン）割り当て。
///
/// 従来は1アイテム＝1行だったため、縦に極端に間延びし、各行の横方向はほぼ空白だった。
/// ここでは「時間的に重ならないアイテムを同じ行にまとめる」貪欲法で行数を圧縮する。
/// アルゴリズムは <see cref="ScheduleLayoutHelper"/> の列割り当てと同じで、軸が縦横で入れ替わっただけ。
/// </summary>
public static class TimelineLaneHelper
{
    /// <summary>バー同士の最小の間隔（ピクセル）</summary>
    private const double GapPixels = 6.0;

    /// <summary>
    /// 隣接判定に含めるラベル幅の上限（ピクセル）。
    /// 長いタイトルのせいでレーンが無駄に増えるのを防ぐ。
    /// </summary>
    private const double MaxLabelReservation = 120.0;

    /// <summary>
    /// アイテム列にレーン番号を割り当て、使用したレーン数を返す。
    /// items は開始時刻の昇順であること。
    /// </summary>
    /// <param name="lanes">アイテム→レーン番号の割り当て先</param>
    public static int AssignLanes(
        IReadOnlyList<ScheduleItem> items,
        TimelineScale scale,
        Dictionary<ScheduleItem, int> lanes)
    {
        if (items.Count == 0) return 0;

        // laneFreeFrom[i] = レーン i が次に空く時刻
        var laneFreeFrom = new List<DateTime>();

        foreach (var item in items)
        {
            var start = item.StartTime;
            var occupiedUntil = GetOccupiedUntil(item, scale);

            int lane = laneFreeFrom.FindIndex(freeFrom => freeFrom <= start);
            if (lane < 0)
            {
                lane = laneFreeFrom.Count;
                laneFreeFrom.Add(occupiedUntil);
            }
            else
            {
                // items は開始時刻順なので occupiedUntil は必ず現在の空き時刻より後になる
                laneFreeFrom[lane] = occupiedUntil;
            }

            lanes[item] = lane;
        }

        return laneFreeFrom.Count;
    }

    /// <summary>
    /// アイテムが実際に画面を占有する終端時刻。
    ///
    /// 実際の終了時刻ではなく「描画幅・ヒット判定幅・（バー外に出る）ラベル幅」を
    /// 時間に換算した終端を使う。こうすると、ズームアウトして極端に細くなったバーでも
    /// 隣のバーやラベルと重ならない。
    /// </summary>
    private static DateTime GetOccupiedUntil(ScheduleItem item, TimelineScale scale)
    {
        var duration = item.EndTime - item.StartTime;
        if (duration < TimeSpan.Zero) duration = TimeSpan.Zero;

        double actualWidth = scale.ToWidth(duration);
        double occupied = Math.Max(actualWidth, ViewModels.TimelineBar.MinHitWidth);

        // ラベルがバー内に収まらないぶんは右にはみ出すので、その幅も占有として数える
        double labelWidth = ViewModels.TimelineBar.EstimateLabelWidth(item.Title);
        double drawWidth = Math.Max(actualWidth, ViewModels.TimelineBar.MinDrawWidth);
        if (labelWidth > drawWidth)
        {
            occupied = Math.Max(occupied, drawWidth + Math.Min(labelWidth, MaxLabelReservation) + 4);
        }

        return item.StartTime + scale.PixelsToDuration(occupied + GapPixels);
    }

    /// <summary>
    /// カテゴリ別レーン割り当て。カテゴリごとに独立してレーンを詰め、
    /// 群の先頭レーン番号（オフセット）を返す。
    /// </summary>
    /// <returns>(カテゴリ名, 所属アイテム, 先頭レーン, レーン数) の並び</returns>
    public static List<(string Name, List<ScheduleItem> Items, int LaneOffset, int LaneCount)> AssignLanesByGroup(
        IReadOnlyList<ScheduleItem> items,
        Func<ScheduleItem, string> groupSelector,
        TimelineScale scale,
        Dictionary<ScheduleItem, int> lanes)
    {
        var result = new List<(string, List<ScheduleItem>, int, int)>();
        int offset = 0;

        var groups = items
            .GroupBy(groupSelector)
            .OrderBy(g => g.Key, StringComparer.CurrentCulture);

        foreach (var group in groups)
        {
            var groupItems = group.OrderBy(x => x.StartTime).ToList();
            var localLanes = new Dictionary<ScheduleItem, int>();
            int count = AssignLanes(groupItems, scale, localLanes);

            foreach (var kv in localLanes)
            {
                lanes[kv.Key] = offset + kv.Value;
            }

            result.Add((group.Key, groupItems, offset, count));
            offset += count;
        }

        return result;
    }
}
