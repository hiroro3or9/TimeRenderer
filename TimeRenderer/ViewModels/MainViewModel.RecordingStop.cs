using System;
using System.Collections.Generic;

using TimeRenderer.Helpers;
using TimeRenderer.Models;

namespace TimeRenderer.ViewModels;

/// <summary>記録停止時の離席判断、実績生成、ToDo積算、Undo登録。</summary>
public partial class MainViewModel
{
    /// <summary>
    /// 記録停止時に離席の扱いを決め、実際に保存すべき区間を返す。
    /// </summary>
    private List<(DateTime Start, DateTime End)> ResolveRecordingSegments(
        string title,
        DateTime start,
        DateTime end,
        List<AwayPeriod> periods)
    {
        if (!IsAwayDetectionEnabled || periods.Count == 0)
        {
            return RecordingStopHelper.BuildSegments(
                start, end, periods, excludeAway: false);
        }

        bool excludeAway;
        switch (_awayHandlingMode)
        {
            case AwayHandlingMode.AlwaysExclude:
                excludeAway = true;
                // 黙って記録が変わると戸惑うので、何をしたかは知らせる。
                ShowAutoStartNotice(
                    RecordingStopHelper.BuildAutoExcludeNotice(start, end, periods));
                break;

            case AwayHandlingMode.AlwaysKeep:
                excludeAway = false;
                break;

            default:
                excludeAway = _dialogService.ShowAwayReviewDialog(
                    title, start, end, periods);
                break;
        }

        return RecordingStopHelper.BuildSegments(
            start, end, periods, excludeAway);
    }

    /// <summary>
    /// 記録区間を実績として保存する。元予定の実績化、追加区間、ToDo積算を
    /// 1回の停止操作として同じUndo履歴へ積む。
    /// </summary>
    private void SaveRecordingSegments(
        string title,
        ScheduleItem? source,
        List<(DateTime Start, DateTime End)> segments,
        RecordingItemMetadata metadata,
        TodoItem? todo = null)
    {
        var edits = new List<IUndoableEdit>();
        bool useSource = source != null && source.IsPlanned && ScheduleItems.Contains(source);

        for (int i = 0; i < segments.Count; i++)
        {
            var (start, end) = segments[i];

            if (i == 0 && useSource)
            {
                ConvertSourcePlanToRecordedItem(
                    source!, title, start, end, edits);
                continue;
            }

            var newItem = RecordingItemHelper.CreateRecordedItem(
                title, start, end, metadata);
            ScheduleItems.Add(newItem);
            edits.Add(new AddItemEdit(newItem));
        }

        AddTodoRecordedTimeEdit(todo, segments, edits);
        PushRecordingEdits(title, segments.Count, edits);

        RecalculateLayout();
        SaveData();
    }

    private void ConvertSourcePlanToRecordedItem(
        ScheduleItem source,
        string title,
        DateTime start,
        DateTime end,
        ICollection<IUndoableEdit> edits)
    {
        // 通常は記録開始時に実体化済みだが、停止までに状態が変わった場合にも保証する。
        if (source.IsVirtual) MaterializeOccurrence(source);

        var before = ItemSnapshot.Capture(source);

        _isBatchUpdatingItem = true;
        try
        {
            RecordingItemHelper.ApplyRecordedSegment(source, title, start, end);
        }
        finally
        {
            _isBatchUpdatingItem = false;
        }

        var after = ItemSnapshot.Capture(source);
        if (!before.IsSameAs(after))
        {
            edits.Add(new ModifyItemEdit(source, before, after, "記録"));
        }
    }

    private RecordingItemMetadata CaptureRecordingMetadata(
        ScheduleItem? source,
        TodoItem? todo)
    {
        return new RecordingItemMetadata(
            _recordingColorCode
                ?? RecordingCategory?.ColorCode
                ?? CategoryInfo.CreateBrush("DarkOrange").ToString(),
            _recordingCategoryId ?? RecordingCategory?.Id,
            // 開始時に決めたコードをそのまま使う（未設定で始めた記録に既定を付けない）
            _recordingProjectCodeId,
            todo?.Id ?? source?.TodoId,
            source?.RoutineId,
            source?.Id);
    }

    private void AddTodoRecordedTimeEdit(
        TodoItem? todo,
        List<(DateTime Start, DateTime End)> segments,
        ICollection<IUndoableEdit> edits)
    {
        if (todo == null || !Todos.Contains(todo)) return;

        var beforeTicks = todo.RecordedTicks;
        if (TodoTimeBlockHelper.AccumulateRecordedTime(todo, segments))
        {
            edits.Add(new ChangeTodoRecordedTimeEdit(
                todo, beforeTicks, todo.RecordedTicks, "実績時間の追加"));
        }
    }
}
