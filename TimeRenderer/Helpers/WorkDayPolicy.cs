using System;
using System.Collections.Generic;
using System.Linq;

using TimeRenderer.Models;

namespace TimeRenderer.Helpers;

/// <summary>
/// 勤務日の自動締めと離席確認に使う、画面状態に依存しない判断規則。
/// </summary>
public static class WorkDayPolicy
{
    /// <summary>未退勤の勤務日が現在日より前なら自動締めの対象にする。</summary>
    public static bool ShouldAutoClose(WorkDayLog? log, DateTime now, bool isRecording) =>
        !isRecording && log != null && log.StartTime.Date < now.Date;

    /// <summary>
    /// 出勤後に開始し、その勤務日にかかる実績のうち、最後の終了時刻を返す。
    /// 日付をまたぐ実績は翌日の終了時刻まで勤務していたものとして扱う。
    /// 対象が無ければ勤務時間を水増ししないよう出勤時刻を返す。
    /// </summary>
    public static DateTime FindLastActivityEnd(
        WorkDayLog log,
        IEnumerable<ScheduleItem> items)
    {
        ArgumentNullException.ThrowIfNull(log);
        ArgumentNullException.ThrowIfNull(items);

        var dayEnd = log.StartTime.Date.AddDays(1);
        return items
            .Where(i => i.IsRecorded &&
                        !i.IsAllDay &&
                        i.StartTime < dayEnd &&
                        i.EndTime > log.StartTime &&
                        i.EndTime > i.StartTime)
            .Select(i => i.EndTime)
            .DefaultIfEmpty(log.StartTime)
            .Max();
    }

    /// <summary>退勤時刻が出勤時刻より前へ戻らないよう下限を適用する。</summary>
    public static DateTime ClampEnd(WorkDayLog log, DateTime requestedEnd)
    {
        ArgumentNullException.ThrowIfNull(log);
        return requestedEnd < log.StartTime ? log.StartTime : requestedEnd;
    }

    /// <summary>
    /// 離席を退勤候補として確認するかを判定する。
    /// 日付をまたぐ離席は、開始時刻の下限より早くても候補にする。
    /// </summary>
    public static bool ShouldPromptForAwayEnd(
        WorkDayLog log,
        AwayPeriod period,
        int thresholdMinutes,
        int earliestHour)
    {
        ArgumentNullException.ThrowIfNull(log);
        ArgumentNullException.ThrowIfNull(period);

        if (period.Start <= log.StartTime) return false;
        if (period.Duration < TimeSpan.FromMinutes(thresholdMinutes)) return false;

        return earliestHour <= 0 ||
               period.Start.Hour >= earliestHour ||
               period.End.Date != period.Start.Date;
    }
}
