using System;
using System.Globalization;

namespace TimeRenderer.Helpers;

/// <summary>朝のToDoまとめ通知の日次判定、本文、保存用日付形式。</summary>
public static class TodoDigestHelper
{
    private const string DateFormat = "yyyy-MM-dd";

    public static bool ShouldRun(
        bool isEnabled,
        DateTime lastRunDate,
        DateTime now,
        int digestHour) =>
        isEnabled &&
        lastRunDate.Date != now.Date &&
        now.Hour >= digestHour;

    public static string? BuildNotice(int dueToday, int overdue)
    {
        if (dueToday <= 0 && overdue <= 0) return null;
        if (dueToday <= 0) return $"期限を過ぎた ToDo が {overdue} 件 あります。";
        if (overdue <= 0) return $"今日が期限の ToDo が {dueToday} 件 あります。";
        return $"今日が期限の ToDo が {dueToday} 件、期限を過ぎた ToDo が {overdue} 件 あります。";
    }

    public static string? FormatLastRunDate(DateTime value) =>
        value == default
            ? null
            : value.ToString(DateFormat, CultureInfo.InvariantCulture);

    public static DateTime ParseLastRunDate(string? value) =>
        DateTime.TryParseExact(
            value,
            DateFormat,
            CultureInfo.InvariantCulture,
            DateTimeStyles.None,
            out var parsed)
            ? parsed
            : default;
}
