using System;
using System.Windows.Input;
using System.Windows.Media;
using TimeRenderer.Services;
using Brushes = System.Windows.Media.Brushes;

using TimeRenderer.Models;
using TimeRenderer.Helpers;

namespace TimeRenderer.ViewModels;

public partial class MainViewModel
{
    private readonly IDialogService _dialogService;

    public ICommand ToggleMemoPanelCommand { get; private set; } = null!;
    public ICommand ToggleSettingsPanelCommand { get; private set; } = null!;
    public ICommand ToggleRecordingCommand { get; private set; } = null!;
    public ICommand StartRecordingFromItemCommand { get; private set; } = null!;
    public ICommand DeleteCommand { get; private set; } = null!;
    public ICommand AddCommand { get; private set; } = null!;
    public ICommand AddScheduleItemAtDateCommand { get; private set; } = null!;
    public ICommand AddScheduleItemAtTimeCommand { get; private set; } = null!;
    public ICommand EditCommand { get; private set; } = null!;
    public ICommand PreviousCommand { get; private set; } = null!;
    public ICommand NextCommand { get; private set; } = null!;
    public ICommand TodayCommand { get; private set; } = null!;
    public ICommand ChangeViewModeCommand { get; private set; } = null!;
    public ICommand ShowAddSprintFormCommand { get; private set; } = null!;
    public ICommand HideAddSprintFormCommand { get; private set; } = null!;
    public ICommand SaveNewSprintCommand { get; private set; } = null!;
    public ICommand DeleteManualSprintCommand { get; private set; } = null!;
    public ICommand EditManualSprintCommand { get; private set; } = null!;

    private void InitializeCommands()
    {
        ToggleMemoPanelCommand = new RelayCommand(_ => IsMemoPanelVisible = !IsMemoPanelVisible);
        ToggleSettingsPanelCommand = new RelayCommand(_ => IsSettingsPanelVisible = !IsSettingsPanelVisible);

        DeleteCommand = new RelayCommand(
            param =>
            {
                if (param is ScheduleItem item)
                {
                    if (_dialogService.ShowConfirmationDialog($"「{item.Title}」を削除しますか？", "削除確認"))
                    {
                        ScheduleItems.Remove(item);
                    }
                }
            },
            param => param is ScheduleItem
        );

        AddCommand = new RelayCommand(_ => AddViaDialog(null));

        AddScheduleItemAtDateCommand = new RelayCommand(dateObj =>
        {
            if (dateObj is DateTime date)
            {
                AddViaDialog(new ScheduleItem
                {
                    StartTime = date.Date.AddHours(9),
                    EndTime = date.Date.AddHours(10),
                    Title = "新しい予定",
                    ColorCode = Categories.FirstOrDefault()?.ColorCode ?? Brushes.LightBlue.ToString(),
                    CategoryId = Categories.FirstOrDefault()?.Id
                });
            }
        });

        // 日/週ビューの空白部分ダブルクリック用：時刻付きの日時から予定を追加する
        AddScheduleItemAtTimeCommand = new RelayCommand(dateObj =>
        {
            if (dateObj is DateTime start)
            {
                AddViaDialog(new ScheduleItem
                {
                    StartTime = start,
                    EndTime = start.AddHours(1),
                    Title = "新しい予定",
                    ColorCode = Categories.FirstOrDefault()?.ColorCode ?? Brushes.LightBlue.ToString(),
                    CategoryId = Categories.FirstOrDefault()?.Id
                });
            }
        });

        EditCommand = new RelayCommand(
            param =>
            {
                if (param is ScheduleItem item)
                {
                    var editedItem = _dialogService.ShowScheduleEditDialog(item, [.. Categories], GetTitleSuggestions());
                    if (editedItem != null)
                    {
                        // プロパティごとの再計算・保存を避け、一括更新後に1回だけ実行する
                        _isBatchUpdatingItem = true;
                        try
                        {
                            item.Title = editedItem.Title;
                            item.Content = editedItem.Content;
                            item.StartTime = editedItem.StartTime;
                            item.EndTime = editedItem.EndTime;
                            item.IsAllDay = editedItem.IsAllDay;
                            item.BackgroundColor = editedItem.BackgroundColor;
                            item.CategoryId = editedItem.CategoryId;
                            item.RemindAtStart = editedItem.RemindAtStart;
                            item.AutoStartRecording = editedItem.AutoStartRecording;
                            item.ForceStartRecording = editedItem.ForceStartRecording;
                        }
                        finally
                        {
                            _isBatchUpdatingItem = false;
                        }
                        RecalculateLayout();
                        SaveData();
                    }
                }
            },
            param => param is ScheduleItem
        );

        PreviousCommand = new RelayCommand(_ => Navigate(-1));
        NextCommand = new RelayCommand(_ => Navigate(1));
        TodayCommand = new RelayCommand(_ =>
        {
            if (CurrentDate < DateTime.Today)
                TransitionDirection = Controls.TransitionDirection.Forward;
            else if (CurrentDate > DateTime.Today)
                TransitionDirection = Controls.TransitionDirection.Backward;

            CurrentDate = DateTime.Today;
        });

        ChangeViewModeCommand = new RelayCommand(param =>
        {
            if (param is ViewMode mode)
            {
                CurrentViewMode = mode;
            }
            else if (param is string modeStr && Enum.TryParse<ViewMode>(modeStr, out var m))
            {
                CurrentViewMode = m;
            }
        });

        ToggleRecordingCommand = new RelayCommand(_ => ToggleRecording());

        StartRecordingFromItemCommand = new RelayCommand(
            param =>
            {
                if (param is ScheduleItem item)
                {
                    StartRecordingFromItem(item);
                }
            },
            param => param is ScheduleItem
        );

        ShowAddSprintFormCommand = new RelayCommand(_ =>
        {
            EditingSprint = null;
            var latest = ManualSprints.OrderBy(x => x.StartDate).LastOrDefault();
            if (latest != null)
            {
                NewSprintStartDate = latest.EndDate.AddDays(1).Date;
                NewSprintEndDate = latest.EndDate.AddDays(14).Date;
                if (latest.Name.StartsWith("Sprint ") && int.TryParse(latest.Name.AsSpan(7), out var num))
                {
                    NewSprintName = $"Sprint {num + 1}";
                }
                else
                {
                    NewSprintName = "Sprint";
                }
            }
            else
            {
                var current = Helpers.SprintHelper.GetSprintForDate(ManualSprints, DateTime.Today);
                NewSprintStartDate = current.StartDate;
                NewSprintEndDate = current.EndDate;
                NewSprintName = "Sprint 1";
            }
            IsAddSprintFormVisible = true;
        });

        HideAddSprintFormCommand = new RelayCommand(_ =>
        {
            IsAddSprintFormVisible = false;
            NewSprintName = string.Empty;
            NewSprintStartDate = null;
            NewSprintEndDate = null;
            EditingSprint = null;
        });

        SaveNewSprintCommand = new RelayCommand(
            _ =>
            {
                if (string.IsNullOrWhiteSpace(NewSprintName))
                {
                    _dialogService.ShowMessage("スプリント名を入力してください。", "入力エラー");
                    return;
                }
                if (!NewSprintStartDate.HasValue || !NewSprintEndDate.HasValue)
                {
                    _dialogService.ShowMessage("開始日と終了日を設定してください。", "入力エラー");
                    return;
                }
                if (NewSprintStartDate.Value.Date > NewSprintEndDate.Value.Date)
                {
                    _dialogService.ShowMessage("開始日は終了日以前である必要があります。", "入力エラー");
                    return;
                }

                // 期間重複チェック (編集中のスプリント自身は除外)
                bool isOverlapped = ManualSprints.Any(x =>
                    x.Id != EditingSprint?.Id && (
                    (NewSprintStartDate.Value.Date >= x.StartDate.Date && NewSprintStartDate.Value.Date <= x.EndDate.Date) ||
                    (NewSprintEndDate.Value.Date >= x.StartDate.Date && NewSprintEndDate.Value.Date <= x.EndDate.Date) ||
                    (x.StartDate.Date >= NewSprintStartDate.Value.Date && x.StartDate.Date <= NewSprintEndDate.Value.Date))
                );

                if (isOverlapped)
                {
                    _dialogService.ShowMessage("既存のスプリントと期間が重複しています。", "入力エラー");
                    return;
                }

                if (EditingSprint != null)
                {
                    // 編集モード：既存スプリントを置換
                    var list = new List<SprintInfo>(ManualSprints);
                    var index = list.FindIndex(x => x.Id == EditingSprint.Id);
                    if (index >= 0)
                    {
                        list[index] = new SprintInfo
                        {
                            Id = EditingSprint.Id,
                            Name = NewSprintName,
                            StartDate = NewSprintStartDate.Value.Date,
                            EndDate = NewSprintEndDate.Value.Date,
                            IsManual = true
                        };
                        ManualSprints = list;
                    }
                }
                else
                {
                    // 新規追加モード
                    var sprint = new SprintInfo
                    {
                        Name = NewSprintName,
                        StartDate = NewSprintStartDate.Value.Date,
                        EndDate = NewSprintEndDate.Value.Date,
                        IsManual = true
                    };

                    var list = new List<SprintInfo>(ManualSprints) { sprint };
                    ManualSprints = list;
                }

                HideAddSprintFormCommand.Execute(null);
            }
        );

        DeleteManualSprintCommand = new RelayCommand(
            param =>
            {
                if (param is SprintInfo sprint)
                {
                    if (_dialogService.ShowConfirmationDialog($"スプリント「{sprint.Name}」を削除しますか？\n削除すると自動分割の計算に戻ります。", "削除確認"))
                    {
                        var list = new List<SprintInfo>(ManualSprints);
                        list.Remove(sprint);
                        ManualSprints = list;
                    }
                }
            },
            param => param is SprintInfo
        );

        EditManualSprintCommand = new RelayCommand(
            param =>
            {
                if (param is SprintInfo sprint)
                {
                    EditingSprint = sprint;
                    NewSprintName = sprint.Name;
                    NewSprintStartDate = sprint.StartDate;
                    NewSprintEndDate = sprint.EndDate;
                    IsAddSprintFormVisible = true;
                }
            },
            param => param is SprintInfo
        );
    }

    private void Navigate(int amount)
    {
        TransitionDirection = amount > 0 ? Controls.TransitionDirection.Forward : Controls.TransitionDirection.Backward;
        CurrentDate = CurrentViewMode switch
        {
            ViewMode.Day => GetNextActiveDay(CurrentDate, amount),
            ViewMode.Week => CurrentDate.AddDays(amount * 7),
            ViewMode.Month => CurrentDate.AddMonths(amount),
            ViewMode.Sprint => GetAdjacentSprintDate(amount),
            ViewMode.SprintTimeline => GetAdjacentSprintDate(amount),
            ViewMode.Stats => StatsPeriod switch
            {
                StatsPeriodMode.Month => CurrentDate.AddMonths(amount),
                StatsPeriodMode.Sprint => GetAdjacentSprintDate(amount),
                _ => CurrentDate.AddDays(amount * 7)
            },
            _ => CurrentDate
        };
    }

    /// <summary>
    /// 表示対象の曜日のみを考慮して、次のアクティブな日を取得します
    /// </summary>
    private DateTime GetNextActiveDay(DateTime date, int amount)
    {
        var next = date;
        var step = amount > 0 ? 1 : -1;
        var absAmount = Math.Abs(amount);
        
        for (int i = 0; i < absAmount; i++)
        {
            do
            {
                next = next.AddDays(step);
            } while (EnabledDaysOfWeek.Count > 0 && !EnabledDaysOfWeek.Contains(next.DayOfWeek));
        }
        return next;
    }


    private DateTime GetAdjacentSprintDate(int amount)
    {
        var currentSprint = Helpers.SprintHelper.GetSprintForDate(ManualSprints, CurrentDate);
        var sprints = Helpers.SprintHelper.GetSprintsForRange(ManualSprints, currentSprint.StartDate.AddMonths(-3), currentSprint.EndDate.AddMonths(3));
        int idx = sprints.FindIndex(s => s.StartDate.Date == currentSprint.StartDate.Date);
        if (idx >= 0)
        {
            int targetIdx = Math.Clamp(idx + amount, 0, sprints.Count - 1);
            return sprints[targetIdx].StartDate;
        }
        return CurrentDate.AddDays(amount * 14);
    }

    /// <summary>
    /// 「この内容で記録開始」用：記録アイテム作成時に使う色（null なら既定の記録カテゴリ色）
    /// </summary>
    private string? _recordingColorCode;

    /// <summary>「この内容で記録開始」用：記録アイテムに引き継ぐカテゴリID</summary>
    private string? _recordingCategoryId;

    /// <summary>
    /// 予定アイテムから記録を開始した場合の「消費」対象。
    /// 停止時にこのアイテム自体を実績（実際の開始〜終了時刻）に更新し、新規アイテムは作らない。
    /// null の場合は従来どおり停止時に新規アイテムを作成する。
    /// </summary>
    private ScheduleItem? _recordingSourceItem;

    /// <summary>
    /// 選択したアイテムと同じタイトル・色で新しい記録を開始する。
    /// 記録中だった場合は現在の記録を保存してから開始する。
    /// </summary>
    /// <param name="consumeItem">
    /// true の場合、渡された予定アイテム自体を記録の実績として使う（停止時に時刻を上書きし、別アイテムを作らない）。
    /// リマインダー通知・自動開始からの記録開始で使用する。
    /// </param>
    private void StartRecordingFromItem(ScheduleItem item, bool consumeItem = false)
    {
        if (IsRecording)
        {
            ToggleRecording(); // 現在の記録を停止・保存
        }

        RecordingTitle = item.Title;
        _recordingColorCode = item.ColorCode;
        _recordingCategoryId = item.CategoryId ?? ResolveCategory(item)?.Id;
        _recordingSourceItem = consumeItem ? item : null;
        IsRecording = true;
        RecordingStartTime = DateTime.Now;
        RecordingDuration = TimeSpan.Zero;
        IsCountdownMode = false;
        CountdownRemaining = null;
    }

    private void ToggleRecording()
    {
        if (IsRecording)
        {
            if (RecordingStartTime.HasValue)
            {
                var endTime = DateTime.Now;
                var startTime = RecordingStartTime.Value;
                var duration = endTime - startTime;

                var title = string.IsNullOrWhiteSpace(RecordingTitle) ? $"作業ログ {startTime:HH:mm}" : RecordingTitle;
                // TimeSpan の "hh" は24時間で桁落ちするため総時間数で表記する
                var durationText = $"記録時間: {(int)duration.TotalHours}:{duration.Minutes:D2}";

                var source = _recordingSourceItem;
                _recordingSourceItem = null;

                if (source != null && ScheduleItems.Contains(source))
                {
                    // 予定アイテムから開始した記録：予定自体を実績に更新する（別アイテムを作らない）
                    _isBatchUpdatingItem = true;
                    try
                    {
                        source.Title = title;
                        source.StartTime = startTime;
                        source.EndTime = endTime;
                        // ユーザーが予定に書いたメモは残し、空の場合のみ記録時間を書き込む
                        if (string.IsNullOrWhiteSpace(source.Content))
                        {
                            source.Content = durationText;
                        }
                    }
                    finally
                    {
                        _isBatchUpdatingItem = false;
                    }
                    RecalculateLayout();
                    SaveData();
                }
                else
                {
                    ScheduleItem newItem = new()
                    {
                        Title = title,
                        Content = durationText,
                        StartTime = startTime,
                        EndTime = endTime,
                        ColorCode = _recordingColorCode ?? RecordingCategory?.ColorCode ?? Brushes.DarkOrange.ToString(),
                        CategoryId = _recordingCategoryId ?? RecordingCategory?.Id,
                        ColumnIndex = 0
                    };

                    ScheduleItems.Add(newItem);
                }
            }

            IsRecording = false;
            RecordingStartTime = null;
            RecordingDuration = TimeSpan.Zero;
            RecordingTitle = "";
            _recordingColorCode = null;
            _recordingCategoryId = null;
            _recordingSourceItem = null;
            IsCountdownMode = false;
            CountdownRemaining = null;
        }
        else
        {
            _recordingColorCode = null;
            _recordingCategoryId = null;
            _recordingSourceItem = null;
            string defaultTitle = $"作業ログ {DateTime.Now:HH:mm}";
            var result = _dialogService.ShowRecordingStartDialog(defaultTitle, TimerOptions, SelectedTimerOption, GetTitleSuggestions());
            if (result != null) // Cancel以外（OK押下時）は開始
            {
                RecordingTitle = string.IsNullOrWhiteSpace(result.Value.Title) ? defaultTitle : result.Value.Title;
                SelectedTimerOption = result.Value.SelectedOption;
                IsRecording = true;
                RecordingStartTime = DateTime.Now;
                RecordingDuration = TimeSpan.Zero;
                
                if (SelectedTimerOption != null && SelectedTimerOption.Minutes > 0)
                {
                    IsCountdownMode = true;
                    CountdownRemaining = TimeSpan.FromMinutes(SelectedTimerOption.Minutes);
                }
                else
                {
                    IsCountdownMode = false;
                    CountdownRemaining = null;
                }
            }
        }
    }

    /// <summary>編集ダイアログを表示し、確定されたアイテムを追加する</summary>
    private void AddViaDialog(ScheduleItem? template)
    {
        var result = _dialogService.ShowScheduleEditDialog(template, [.. Categories], GetTitleSuggestions());
        if (result != null)
        {
            ScheduleItems.Add(result);
        }
    }
}
