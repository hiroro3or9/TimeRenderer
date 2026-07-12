using System;
using Brush = System.Windows.Media.Brush;

namespace TimeRenderer.Models;

/// <summary>
/// 週/日ビュー描画用の「1日分の区間」を表すクラス。
/// 日付をまたぐ ScheduleItem は日単位の複数セグメントに分割して描画される。
/// レイアウト再計算のたびに再生成されるスナップショットのため変更通知は持たない。
/// </summary>
public class ScheduleSegment(ScheduleItem item, DateTime startTime, DateTime endTime)
{
    /// <summary>元のスケジュールアイテム（編集・削除の対象）</summary>
    public ScheduleItem Item { get; } = item;

    /// <summary>このセグメントの開始時刻（その日の範囲内）</summary>
    public DateTime StartTime { get; } = startTime;

    /// <summary>このセグメントの終了時刻（その日の範囲内）</summary>
    public DateTime EndTime { get; } = endTime;

    public string Title => Item.Title;
    public string Content => Item.Content;
    public Brush BackgroundColor => Item.BackgroundColor;
    public bool IsAllDay => Item.IsAllDay;

    /// <summary>セグメント自体の長さ（時間）</summary>
    public double DurationHours => (EndTime - StartTime).TotalHours;

    /// <summary>重なりレイアウト用の列インデックス</summary>
    public int ColumnIndex { get; set; }

    /// <summary>クラスタ内の最大列インデックス</summary>
    public int MaxColumnIndex { get; set; }
}
