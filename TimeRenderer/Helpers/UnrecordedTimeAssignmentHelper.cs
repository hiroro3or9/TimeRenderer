using System;
using System.Collections.Generic;
using System.Linq;

using TimeRenderer.Models;

namespace TimeRenderer.Helpers;

/// <summary>
/// 未記録時間の加算先を期間で切り替える割り当ての並べ替えと参照を行うヘルパークラスです。
///
/// 割り当ては開始日だけを持ち、期間の終わりは次の行の開始日から導出します。
/// 「その日以前で最後の行」を採る規則なので、隙間も重複も起こりません。
/// </summary>
public static class UnrecordedTimeAssignmentHelper
{
    /// <summary>
    /// 割り当てを開始日の昇順に並べ替えます。参照も表示もこの並び順を前提にします。
    /// </summary>
    /// <param name="assignments">並べ替える割り当て</param>
    /// <returns>開始日の昇順に並べた新しいリスト</returns>
    public static List<UnrecordedTimeProjectAssignment> Order(
        IEnumerable<UnrecordedTimeProjectAssignment> assignments) =>
        [.. assignments.OrderBy(a => a.StartDate.Date)];

    /// <summary>
    /// 指定日に適用される割り当てを取得します。
    /// </summary>
    /// <param name="ordered"><see cref="Order"/> で並べ替え済みの割り当て</param>
    /// <param name="date">基準日</param>
    /// <returns>その日以前で最後の割り当て。最初の割り当てより前の日なら null</returns>
    public static UnrecordedTimeProjectAssignment? Resolve(
        IReadOnlyList<UnrecordedTimeProjectAssignment> ordered, DateTime date)
    {
        var target = date.Date;

        for (var i = ordered.Count - 1; i >= 0; i--)
        {
            if (ordered[i].StartDate.Date <= target) return ordered[i];
        }

        return null;
    }

    /// <summary>
    /// 一覧に出す期間の文字列を作ります。終了日は次の行の開始日の前日です。
    /// </summary>
    /// <param name="ordered"><see cref="Order"/> で並べ替え済みの割り当て</param>
    /// <param name="index">対象の行の位置</param>
    /// <returns>「2026/01/05 〜 2026/02/01」のような表示文字列</returns>
    public static string BuildRangeText(
        IReadOnlyList<UnrecordedTimeProjectAssignment> ordered, int index)
    {
        var start = ordered[index].StartDate.Date;
        var startText = start.ToString("yyyy/MM/dd");

        if (index + 1 >= ordered.Count) return $"{startText} 〜 （以降ずっと）";

        var nextStart = ordered[index + 1].StartDate.Date;

        // 同じ日・前の日から始まる行が後ろにあると、この行は一日も受け持たない。
        // 黙って消えると気づけないので、その旨を出す
        return nextStart <= start
            ? $"{startText}（次の行に上書きされます）"
            : $"{startText} 〜 {nextStart.AddDays(-1):yyyy/MM/dd}";
    }
}
