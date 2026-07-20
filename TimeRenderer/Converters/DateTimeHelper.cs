using System;

namespace TimeRenderer.Converters;

internal static class DateTimeHelper
{
    /// <summary>
    /// 指定された日付を含む週の開始日（月曜日）を取得します
    /// </summary>
    public static DateTime GetStartOfWeek(this DateTime date)
    {
        int diff = (7 + (date.DayOfWeek - DayOfWeek.Monday)) % 7;
        return date.AddDays(-1 * diff).Date;
    }

    /// <summary>曜日の1文字表記（月・火・…）。タイムラインの目盛りなど幅の狭い場所で使う</summary>
    public static string GetShortDayOfWeek(DateTime date) => date.DayOfWeek switch
    {
        DayOfWeek.Monday => "月",
        DayOfWeek.Tuesday => "火",
        DayOfWeek.Wednesday => "水",
        DayOfWeek.Thursday => "木",
        DayOfWeek.Friday => "金",
        DayOfWeek.Saturday => "土",
        _ => "日"
    };
}
