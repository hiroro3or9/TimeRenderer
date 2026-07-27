using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json.Serialization;
using System.Windows.Media;
using Brushes = System.Windows.Media.Brushes;

namespace TimeRenderer.Models;

/// <summary>
/// 定期予定の繰り返し方。
/// 数値で保存されるため、既存の値の割り当ては変更しないこと
/// （未設定の旧データは 0 = Weekly として読み込まれる）。
/// </summary>
public enum RecurrenceType
{
    /// <summary>曜日指定。Interval 週ごとに、DaysOfWeek の曜日で繰り返す</summary>
    Weekly = 0,

    /// <summary>日付指定。Interval ヶ月ごとに、DayOfMonth 日で繰り返す</summary>
    MonthlyByDate = 1,
}

/// <summary>
/// 定期予定（ルーティン）のテンプレート。
/// 指定した繰り返し（曜日／日付）と時刻に基づき、スケジュールへ予定アイテムを自動生成する。
/// 「記録開始を忘れる」対策：毎週・隔週・毎月同じ時間にある会議などを登録しておくと、
/// 予定アイテムが自動で並び、開始時刻にはリマインダー通知（または自動記録開始）が行われる。
/// </summary>
public class RoutineScheduleItem
{
    /// <summary>識別子。生成された ScheduleItem.RoutineId との紐付けに使用する</summary>
    public string Id { get; set; } = Guid.NewGuid().ToString("N");

    /// <summary>予定タイトル（生成されるアイテムのタイトルにそのまま使われる）</summary>
    public string Title { get; set; } = string.Empty;

    /// <summary>繰り返し方（既定は毎週）。旧データはこの値を持たないため Weekly として読み込まれる</summary>
    public RecurrenceType Recurrence { get; set; } = RecurrenceType.Weekly;

    private int _interval = 1;

    /// <summary>
    /// 繰り返しの間隔。Weekly なら週数、MonthlyByDate なら月数（1 なら毎週／毎月、2 なら隔週／隔月）。
    /// 起点は StartDate。旧データは 0 で読み込まれるため、1 未満は 1 として扱う。
    /// </summary>
    public int Interval
    {
        get => _interval < 1 ? 1 : _interval;
        set => _interval = value;
    }

    /// <summary>繰り返す曜日（Recurrence が Weekly のときのみ使用）</summary>
    public List<DayOfWeek> DaysOfWeek { get; set; } = [];

    private int _dayOfMonth = 1;

    /// <summary>
    /// 繰り返す日（Recurrence が MonthlyByDate のときのみ使用）。
    /// 指定日が存在しない月（2月の31日など）ではその月の末日に丸める。
    /// 31 を指定すると常に末日になる。旧データ対策として範囲外は 1〜31 に収める。
    /// </summary>
    public int DayOfMonth
    {
        get => Math.Clamp(_dayOfMonth, 1, 31);
        set => _dayOfMonth = value;
    }

    /// <summary>
    /// 有効開始日（日付部分のみ）。この日より前には予定を生成しない。
    /// 新規作成時は作成日が入る。既定値（DateTime.MinValue）は開始日未設定の旧データを表し、
    /// 起動時の移行処理で当日に設定される。
    /// </summary>
    public DateTime StartDate { get; set; }

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

    /// <summary>
    /// 予定を生成しない日付（日付部分のみ有効）。
    /// 「この日だけ削除」した日と、「この日だけ編集」で実体化した日がここに入る。
    /// 実体化した日は、除外により仮想アイテムと実体アイテムの二重表示を防ぐ。
    /// </summary>
    public List<DateTime> ExcludedDates { get; set; } = [];

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

    /// <summary>
    /// 繰り返しの設定が予定を生成できる状態か。
    /// 曜日が1つも選ばれていない Weekly や、終了時刻が開始時刻以前のものは生成対象外。
    /// </summary>
    [JsonIgnore]
    public bool IsValidRecurrence =>
        EndTime > StartTime &&
        (Recurrence != RecurrenceType.Weekly || DaysOfWeek.Count > 0);

    /// <summary>
    /// 指定日に予定が発生するかを判定する。開始日より前は常に false。
    /// </summary>
    public bool OccursOn(DateTime date)
    {
        var day = date.Date;
        if (day < StartDate.Date) return false;

        return Recurrence switch
        {
            RecurrenceType.MonthlyByDate => OccursOnMonthly(day),
            _ => OccursOnWeekly(day),
        };
    }

    private bool OccursOnWeekly(DateTime day)
    {
        if (!DaysOfWeek.Contains(day.DayOfWeek)) return false;
        if (Interval == 1) return true;

        // 開始日を含む週を 0 週目として数える（週の区切りは月曜始まり）
        var weeks = (StartOfWeek(day) - StartOfWeek(StartDate.Date)).Days / 7;
        return weeks % Interval == 0;
    }

    private bool OccursOnMonthly(DateTime day)
    {
        if (day.Day != ResolveDayOfMonth(day.Year, day.Month)) return false;
        if (Interval == 1) return true;

        var months = ((day.Year - StartDate.Year) * 12) + day.Month - StartDate.Month;
        return months % Interval == 0;
    }

    /// <summary>指定月における実際の発生日。日数が足りない月は末日に丸める</summary>
    private int ResolveDayOfMonth(int year, int month) =>
        Math.Min(DayOfMonth, DateTime.DaysInMonth(year, month));

    /// <summary>週の始まり（月曜日）を返す</summary>
    private static DateTime StartOfWeek(DateTime date) =>
        date.Date.AddDays(-(((int)date.DayOfWeek + 6) % 7));

    /// <summary>
    /// 一覧表示用：繰り返しの説明
    /// （例: "毎週 月・水・金" / "隔週 月" / "毎日" / "毎月15日" / "3ヶ月ごと 末日"）
    /// </summary>
    [JsonIgnore]
    public string RecurrenceDisplay =>
        Recurrence == RecurrenceType.MonthlyByDate ? MonthlyDisplay : WeeklyDisplay;

    private string WeeklyDisplay
    {
        get
        {
            if (DaysOfWeek.Count == 0) return "（曜日未設定）";

            var days = DaysOfWeek.Count == 7
                ? "毎日"
                : string.Join("・", WeekOrder.Where(DaysOfWeek.Contains).Select(d => DayNames[d]));

            return Interval switch
            {
                1 => DaysOfWeek.Count == 7 ? days : $"毎週 {days}",
                2 => $"隔週 {days}",
                _ => $"{Interval}週ごと {days}",
            };
        }
    }

    private string MonthlyDisplay
    {
        get
        {
            var day = DayOfMonth == 31 ? "末日" : $"{DayOfMonth}日";
            return Interval == 1 ? $"毎月{day}" : $"{Interval}ヶ月ごと {day}";
        }
    }

    /// <summary>一覧表示用：時刻範囲（例: "10:00-11:00"）</summary>
    [JsonIgnore]
    public string TimeRangeDisplay => $"{FormatTime(StartTime)}-{FormatTime(EndTime)}";

    /// <summary>一覧表示用：開始日（例: "2026/07/25〜"）</summary>
    [JsonIgnore]
    public string StartDateDisplay =>
        StartDate == default ? string.Empty : $"{StartDate:yyyy/MM/dd}〜";

    private static string FormatTime(TimeSpan t) => $"{(int)t.TotalHours:D2}:{t.Minutes:D2}";
}
