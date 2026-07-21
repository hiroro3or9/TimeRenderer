using System;
using System.Text.Json.Serialization;

namespace TimeRenderer.Models;

/// <summary>勤務終了が入った経緯</summary>
public enum WorkEndSource
{
    /// <summary>自分で退勤を登録した</summary>
    Manual,
    /// <summary>離席・スリープの検知をきっかけに確定した</summary>
    AwayDetected,
    /// <summary>翌日以降に持ち越されていたため、最終操作時刻で自動的に締めた</summary>
    AutoClosed
}

/// <summary>
/// 1日分の勤務記録（出勤・退勤）。
///
/// 作業内容の記録（<see cref="ScheduleItem"/>）とは別の軸で、
/// 「その日いつ働き始めて、いつ終えたか」だけを持つ。
/// 予定バーと混ざらないよう、日/週ビューでは横ラインのマーカーとして描く。
/// </summary>
public sealed class WorkDayLog
{
    /// <summary>勤務日（日付のみ）。出勤した時刻の日付を採用する</summary>
    public DateTime Date { get; set; }

    /// <summary>出勤時刻</summary>
    public DateTime StartTime { get; set; }

    /// <summary>退勤時刻。未退勤の場合は null</summary>
    public DateTime? EndTime { get; set; }

    /// <summary>退勤がどう入ったか（自動で入ったものはマーカーに印を付ける）</summary>
    public WorkEndSource EndSource { get; set; } = WorkEndSource.Manual;

    [JsonIgnore]
    public bool IsFinished => EndTime.HasValue;

    /// <summary>勤務時間（未退勤の場合は現在時刻まで）</summary>
    [JsonIgnore]
    public TimeSpan Duration
    {
        get
        {
            var end = EndTime ?? DateTime.Now;
            return end > StartTime ? end - StartTime : TimeSpan.Zero;
        }
    }

    [JsonIgnore]
    public string DurationText => FormatDuration(Duration);

    public static string FormatDuration(TimeSpan duration)
        => duration.TotalHours >= 1
            ? $"{(int)duration.TotalHours}時間{duration.Minutes}分"
            : $"{(int)duration.TotalMinutes}分";
}
