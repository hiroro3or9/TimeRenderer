using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json.Serialization;
using System.Windows.Media;
using Brushes = System.Windows.Media.Brushes;

namespace TimeRenderer.Models;

/// <summary>
/// 定期予定（ルーティン）のテンプレート。
/// 指定した曜日・時刻に基づき、スケジュールへ予定アイテムを自動生成するために使用する。
/// 「記録開始を忘れる」対策：毎週同じ時間にある会議などを登録しておくと、
/// 予定アイテムが自動で並び、開始時刻にはリマインダー通知（または自動記録開始）が行われる。
/// </summary>
public class RoutineScheduleItem
{
    /// <summary>識別子。生成された ScheduleItem.RoutineId との紐付けに使用する</summary>
    public string Id { get; set; } = Guid.NewGuid().ToString("N");

    /// <summary>予定タイトル（生成されるアイテムのタイトルにそのまま使われる）</summary>
    public string Title { get; set; } = string.Empty;

    /// <summary>繰り返す曜日</summary>
    public List<DayOfWeek> DaysOfWeek { get; set; } = [];

    /// <summary>開始時刻（時刻部分のみ）</summary>
    public TimeSpan StartTime { get; set; }

    /// <summary>終了時刻（時刻部分のみ）</summary>
    public TimeSpan EndTime { get; set; }

    /// <summary>所属カテゴリのID（CategoryInfo.Id）。null の場合は ColorCode を使用する</summary>
    public string? CategoryId { get; set; }

    /// <summary>カテゴリ未設定時のフォールバック色</summary>
    public string ColorCode { get; set; } = Brushes.Lavender.ToString();

    /// <summary>
    /// true の場合、予定時刻になったら確認なしで記録を自動的に開始する。
    /// false の場合は開始時刻にリマインダー通知を表示し、ユーザーの操作を待つ。
    /// </summary>
    public bool IsAutoStart { get; set; }

    /// <summary>
    /// true の場合、自動開始時に既に記録中でも現在の記録を停止・保存して強制的に開始する。
    /// false の場合、記録中はリマインダー通知にフォールバックする。IsAutoStart が true のときのみ有効。
    /// </summary>
    public bool IsForceStart { get; set; }

    /// <summary>無効化すると新規の予定生成・リマインダーを停止する（生成済みの予定アイテムは残る）</summary>
    public bool IsEnabled { get; set; } = true;

    private static readonly DayOfWeek[] WeekOrder =
    [
        DayOfWeek.Monday, DayOfWeek.Tuesday, DayOfWeek.Wednesday, DayOfWeek.Thursday,
        DayOfWeek.Friday, DayOfWeek.Saturday, DayOfWeek.Sunday
    ];

    private static readonly Dictionary<DayOfWeek, string> DayNames = new()
    {
        [DayOfWeek.Monday] = "月",
        [DayOfWeek.Tuesday] = "火",
        [DayOfWeek.Wednesday] = "水",
        [DayOfWeek.Thursday] = "木",
        [DayOfWeek.Friday] = "金",
        [DayOfWeek.Saturday] = "土",
        [DayOfWeek.Sunday] = "日",
    };

    /// <summary>一覧表示用：曜日の和名（例: "月・水・金"、全曜日なら "毎日"）</summary>
    [JsonIgnore]
    public string DaysOfWeekDisplay
    {
        get
        {
            if (DaysOfWeek.Count == 0) return "（曜日未設定）";
            if (DaysOfWeek.Count == 7) return "毎日";
            return string.Join("・", WeekOrder.Where(DaysOfWeek.Contains).Select(d => DayNames[d]));
        }
    }

    /// <summary>一覧表示用：時刻範囲（例: "10:00-11:00"）</summary>
    [JsonIgnore]
    public string TimeRangeDisplay => $"{FormatTime(StartTime)}-{FormatTime(EndTime)}";

    private static string FormatTime(TimeSpan t) => $"{(int)t.TotalHours:D2}:{t.Minutes:D2}";
}
