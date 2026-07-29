using System;

namespace TimeRenderer.Models;

/// <summary>
/// 勤務時間内で、作業の記録が1件も無い区間（記録漏れの候補）。
///
/// 保存はしない。表示のたびに勤務記録と作業記録から算出し直す。
/// 状態として持つと、アイテムを編集したときに実態とズレて信用できなくなる。
///
/// プロパティ名は <see cref="ScheduleSegment"/> に揃えてある。
/// 日/週ビューの位置・高さの計算（TimeToPositionConverter / DurationToHeightConverter /
/// DateToPagePositionConverter）をそのまま共用するため。
/// </summary>
public sealed record UnrecordedGap(DateTime StartTime, DateTime EndTime)
{
    public TimeSpan Duration => EndTime > StartTime ? EndTime - StartTime : TimeSpan.Zero;

    /// <summary>描画用の長さ（時間）</summary>
    public double DurationHours => Duration.TotalHours;

    /// <summary>重なりレイアウトは行わないが、位置計算のコンバーターが要求するため常に 0</summary>
    public int ColumnIndex => 0;

    public int MaxColumnIndex => 0;

    /// <summary>終日ではない（位置計算のコンバーターへ渡す）</summary>
    public bool IsAllDay => false;

    public string RangeText => $"{StartTime:H:mm}-{EndTime:H:mm}";

    public string DurationText => WorkDayLog.FormatDuration(Duration);

    /// <summary>帯の中に出す文言（例: "未記録 45分"）</summary>
    public string Label => $"未記録 {DurationText}";

    public string ToolTipText => $"{RangeText}（{DurationText}）\nこの時間の記録がありません";
}
