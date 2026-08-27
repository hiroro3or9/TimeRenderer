using System;
using System.Collections.Generic;

namespace TimeRenderer.Helpers;

/// <summary>曜日の和名と、画面表示で使う月曜始まりの並び順を提供する。</summary>
public static class DayOfWeekHelper
{
    private static readonly string[] ShortJapaneseNames =
        ["日", "月", "火", "水", "木", "金", "土"];

    /// <summary>月曜日から日曜日までの変更不可な並び。</summary>
    public static IReadOnlyList<DayOfWeek> WeekOrder { get; } = Array.AsReadOnly(
    [
        DayOfWeek.Monday,
        DayOfWeek.Tuesday,
        DayOfWeek.Wednesday,
        DayOfWeek.Thursday,
        DayOfWeek.Friday,
        DayOfWeek.Saturday,
        DayOfWeek.Sunday,
    ]);

    /// <summary>曜日の1文字和名（月・火・…）を返す。不正な列挙値なら空文字。</summary>
    public static string GetShortJapaneseName(DayOfWeek day) =>
        (uint)day < ShortJapaneseNames.Length ? ShortJapaneseNames[(int)day] : string.Empty;

    /// <summary>曜日の1文字和名を <see cref="DayOfWeek"/> へ変換する。</summary>
    public static bool TryParseShortJapaneseName(string? name, out DayOfWeek day)
    {
        var index = Array.IndexOf(ShortJapaneseNames, name);
        day = index >= 0 ? (DayOfWeek)index : default;
        return index >= 0;
    }
}
