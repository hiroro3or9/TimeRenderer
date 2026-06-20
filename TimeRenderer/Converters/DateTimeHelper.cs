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
}
