using System;

using TimeRenderer.Models;

namespace TimeRenderer.Helpers;

/// <summary>記録区間を実績アイテムへ変換する、画面状態に依存しない規則。</summary>
public static class RecordingItemHelper
{
    public static void ApplyRecordedSegment(
        ScheduleItem item,
        string title,
        DateTime start,
        DateTime end)
    {
        ArgumentNullException.ThrowIfNull(item);

        item.Kind = ScheduleItemKind.Recorded;
        item.Title = title;
        item.StartTime = start;
        item.EndTime = end;

        // 予定に書かれていたメモは残し、空の場合だけ記録時間を補う。
        if (string.IsNullOrWhiteSpace(item.Content))
        {
            item.Content = FormatDuration(end - start);
        }
    }

    public static ScheduleItem CreateRecordedItem(
        string title,
        DateTime start,
        DateTime end,
        RecordingItemMetadata metadata)
    {
        ArgumentNullException.ThrowIfNull(metadata);

        return new ScheduleItem
        {
            Kind = ScheduleItemKind.Recorded,
            Title = title,
            Content = FormatDuration(end - start),
            StartTime = start,
            EndTime = end,
            ColorCode = metadata.ColorCode,
            CategoryId = metadata.CategoryId,
            ProjectCodeId = metadata.ProjectCodeId,
            TodoId = metadata.TodoId,
            RoutineId = metadata.RoutineId,
            SourcePlanId = metadata.SourcePlanId,
            ColumnIndex = 0,
        };
    }

    /// <summary>TimeSpanのhhは24時間で桁落ちするため、総時間数で表記する。</summary>
    public static string FormatDuration(TimeSpan duration) =>
        $"記録時間: {(int)duration.TotalHours}:{duration.Minutes:D2}";
}

public sealed record RecordingItemMetadata(
    string ColorCode,
    string? CategoryId,
    string? ProjectCodeId,
    string? TodoId,
    string? RoutineId,
    string? SourcePlanId);
