using System;

using TimeRenderer.Models;

namespace TimeRenderer.ViewModels;

/// <summary>作業記録セッションの開始・停止と、そのセッション中だけ保持する紐づけ情報。</summary>
public partial class MainViewModel
{
    /// <summary>記録アイテム作成時に使う色（nullなら既定の記録カテゴリ色）。</summary>
    private string? _recordingColorCode;

    /// <summary>記録アイテムに引き継ぐカテゴリID。</summary>
    private string? _recordingCategoryId;

    /// <summary>停止時に実績へ付けるプロジェクトコードID。</summary>
    private string? _recordingProjectCodeId;

    /// <summary>停止時に実績へ変換する元予定。新規記録ならnull。</summary>
    private ScheduleItem? _recordingSourceItem;

    /// <summary>選択した予定と同じ内容で記録を開始する。</summary>
    private void StartRecordingFromItem(ScheduleItem item)
    {
        if (IsRecording) StopRecording();

        // 仮想の定期予定を先に実体化し、停止時の変換対象を見失わないようにする。
        if (item.IsPlanned && item.IsVirtual) MaterializeOccurrence(item);

        RecordingTitle = item.Title;
        _recordingColorCode = item.ColorCode;
        _recordingCategoryId = item.CategoryId ?? ResolveCategory(item)?.Id;
        _recordingProjectCodeId = ResolveProjectCode(item.ProjectCodeId) is { IsActive: true } selectedProjectCode
            ? selectedProjectCode.Id
            : DefaultProjectCode?.Id;
        _recordingSourceItem = item.IsPlanned ? item : null;
        _recordingTodo = FindTodoById(item.TodoId);

        BeginRecording(useSelectedTimer: false);
    }

    /// <summary>ダイアログを表示せず、グローバルホットキーやトレイから開始・停止する。</summary>
    public void QuickToggleRecording()
    {
        if (IsRecording)
        {
            StopRecording();
            return;
        }

        ClearRecordingMetadata();
        _recordingProjectCodeId = DefaultProjectCode?.Id;
        RecordingTitle = $"作業ログ {DateTime.Now:HH:mm}";
        BeginRecording(useSelectedTimer: false);
    }

    private void ToggleRecording()
    {
        if (IsRecording)
        {
            StopRecording();
            return;
        }

        StartRecordingViaDialog();
    }

    private void StartRecordingViaDialog()
    {
        ClearRecordingMetadata();

        string defaultTitle = $"作業ログ {DateTime.Now:HH:mm}";
        var result = _dialogService.ShowRecordingStartDialog(
            defaultTitle,
            TimerOptions,
            SelectedTimerOption,
            GetTitleSuggestions(),
            ActiveProjectCodes,
            DefaultProjectCode);
        if (result == null) return;

        RecordingTitle = string.IsNullOrWhiteSpace(result.Value.Title)
            ? defaultTitle
            : result.Value.Title;
        SelectedTimerOption = result.Value.SelectedOption;
        _recordingProjectCodeId = result.Value.ProjectCodeId ?? DefaultProjectCode?.Id;
        BeginRecording(useSelectedTimer: true);
    }

    /// <summary>すべての開始経路で、前回の離席状態と表示時間を同じように初期化する。</summary>
    private void BeginRecording(bool useSelectedTimer)
    {
        ClearAwayState();
        IsRecording = true;
        RecordingStartTime = DateTime.Now;
        RecordingDuration = TimeSpan.Zero;

        IsCountdownMode = useSelectedTimer && SelectedTimerOption.Minutes > 0;
        CountdownRemaining = IsCountdownMode
            ? TimeSpan.FromMinutes(SelectedTimerOption.Minutes)
            : null;
    }

    /// <summary>現在の記録を確定し、セッション状態を必ず終了状態へ戻す。</summary>
    private void StopRecording()
    {
        if (!IsRecording) return;

        try
        {
            if (RecordingStartTime is { } startTime)
            {
                var endTime = DateTime.Now;
                var title = string.IsNullOrWhiteSpace(RecordingTitle)
                    ? $"作業ログ {startTime:HH:mm}"
                    : RecordingTitle;
                var source = _recordingSourceItem;
                var todo = _recordingTodo;

                var segments = ResolveRecordingSegments(title, startTime, endTime);
                if (segments.Count == 0)
                {
                    ShowAutoStartNotice($"「{title}」は全体が離席だったため、記録しませんでした");
                }
                else
                {
                    SaveRecordingSegments(title, source, segments, todo);
                }
            }
        }
        finally
        {
            ResetRecordingSession();
        }
    }

    private void ResetRecordingSession()
    {
        IsRecording = false;
        RecordingStartTime = null;
        RecordingDuration = TimeSpan.Zero;
        RecordingTitle = string.Empty;
        ClearRecordingMetadata();
        IsCountdownMode = false;
        CountdownRemaining = null;
        ClearAwayState();
    }

    private void ClearRecordingMetadata()
    {
        _recordingColorCode = null;
        _recordingCategoryId = null;
        _recordingProjectCodeId = null;
        _recordingSourceItem = null;
        _recordingTodo = null;
    }
}
