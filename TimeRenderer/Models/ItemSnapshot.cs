using System;
using System.Windows.Media;
using Brush = System.Windows.Media.Brush;

namespace TimeRenderer.Models;

/// <summary>
/// <see cref="ScheduleItem"/> の編集可能な状態を写し取ったもの。
///
/// 取り消し履歴はアイテムの「参照」と「前後の状態」で表現する。
/// アイテム自体を複製してしまうと、復元後に別インスタンスになり
/// 選択状態や他の履歴エントリとの対応が壊れるため、
/// 復元は必ず元のインスタンスへ書き戻す形にする。
/// </summary>
public sealed class ItemSnapshot
{
    public required string Title { get; init; }
    public required string Content { get; init; }
    public DateTime StartTime { get; init; }
    public DateTime EndTime { get; init; }
    public bool IsAllDay { get; init; }
    public required Brush BackgroundColor { get; init; }
    public string? CategoryId { get; init; }
    public string? RoutineId { get; init; }
    public bool RemindAtStart { get; init; }
    public bool AutoStartRecording { get; init; }
    public bool ForceStartRecording { get; init; }

    public static ItemSnapshot Capture(ScheduleItem item) => new()
    {
        Title = item.Title,
        Content = item.Content,
        StartTime = item.StartTime,
        EndTime = item.EndTime,
        IsAllDay = item.IsAllDay,
        BackgroundColor = item.BackgroundColor,
        CategoryId = item.CategoryId,
        RoutineId = item.RoutineId,
        RemindAtStart = item.RemindAtStart,
        AutoStartRecording = item.AutoStartRecording,
        ForceStartRecording = item.ForceStartRecording
    };

    /// <summary>この状態をアイテムへ書き戻す</summary>
    public void ApplyTo(ScheduleItem item)
    {
        item.Title = Title;
        item.Content = Content;
        item.StartTime = StartTime;
        item.EndTime = EndTime;
        item.IsAllDay = IsAllDay;
        item.BackgroundColor = BackgroundColor;
        item.CategoryId = CategoryId;
        item.RoutineId = RoutineId;
        item.RemindAtStart = RemindAtStart;
        item.AutoStartRecording = AutoStartRecording;
        item.ForceStartRecording = ForceStartRecording;
    }

    /// <summary>2つの状態が同じか（変化のない編集を履歴に積まないための判定）</summary>
    public bool IsSameAs(ItemSnapshot other) =>
        Title == other.Title &&
        Content == other.Content &&
        StartTime == other.StartTime &&
        EndTime == other.EndTime &&
        IsAllDay == other.IsAllDay &&
        Equals(BackgroundColor?.ToString(), other.BackgroundColor?.ToString()) &&
        CategoryId == other.CategoryId &&
        RoutineId == other.RoutineId &&
        RemindAtStart == other.RemindAtStart &&
        AutoStartRecording == other.AutoStartRecording &&
        ForceStartRecording == other.ForceStartRecording;
}
