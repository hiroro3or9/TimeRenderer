using System;
using System.Collections.Generic;

namespace TimeRenderer.Helpers;

/// <summary>
/// ドラッグ中の時刻を、近くの「区切り」へ吸い寄せる計算。
///
/// 刻み幅（<c>SnapMinutes</c>）の格子だけに丸めると、隣の予定との間に
/// 数分の隙間や重なりが残る。作業記録では前後がぴったり続いていること自体に
/// 意味があるため、格子とは別に「隣接する端」への吸着を用意する。
///
/// ビューの座標系を持ち込まない純粋な計算にしてある（許容範囲は呼び出し側が
/// ピクセルから時間へ換算して渡す）。
/// </summary>
public static class MagnetSnapHelper
{
    /// <summary>吸着の結果。<see cref="Offset"/> を足すと吸着先に一致する</summary>
    /// <param name="Offset">元の時刻から吸着先までのずれ</param>
    /// <param name="GuideTime">吸着先の時刻（ガイド線の描画位置）</param>
    public sealed record Result(TimeSpan Offset, DateTime GuideTime);

    /// <summary>
    /// 1つの端（開始または終了）を、許容範囲内で最も近い吸着先へ寄せる。
    /// 範囲内に候補が無ければ null。
    /// </summary>
    public static Result? SnapEdge(DateTime edge, IReadOnlyList<DateTime> targets, TimeSpan tolerance)
    {
        Result? best = null;

        foreach (var target in targets)
        {
            var offset = target - edge;
            var distance = offset.Duration();
            if (distance > tolerance) continue;
            if (best != null && distance >= best.Offset.Duration()) continue;

            best = new Result(offset, target);
        }

        return best;
    }

    /// <summary>
    /// 開始と終了の両方を候補にして、ずらす量が最も小さい吸着を返す（移動ドラッグ用）。
    ///
    /// 移動では長さを変えないため、返す <see cref="Result.Offset"/> は両端へ同じだけ足す。
    /// 「終わりを前の予定の終わりに合わせたい」場合と
    /// 「始まりを前の予定の終わりに合わせたい」場合の両方を1回の判定で扱う。
    /// </summary>
    public static Result? SnapRange(
        DateTime start, DateTime end, IReadOnlyList<DateTime> targets, TimeSpan tolerance)
    {
        var byStart = SnapEdge(start, targets, tolerance);
        var byEnd = SnapEdge(end, targets, tolerance);

        if (byStart == null) return byEnd;
        if (byEnd == null) return byStart;

        return byStart.Offset.Duration() <= byEnd.Offset.Duration() ? byStart : byEnd;
    }
}
