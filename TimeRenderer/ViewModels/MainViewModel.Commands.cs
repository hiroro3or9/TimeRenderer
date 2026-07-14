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
    public ICommand DeleteCommand { get; private set; } = null!;
    public ICommand AddCommand { get; private set; } = null!;
    public ICommand AddScheduleItemAtDateCommand { get; private set; } = null!;
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
                    ColorCode = Categories.FirstOrDefault()?.ColorCode ?? Brushes.LightBlue.ToString()
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

                ScheduleItem newItem = new()
                {
                    Title = title,
                    // TimeSpan の "hh" は24時間で桁落ちするため総時間数で表記する
                    Content = $"記録時間: {(int)duration.TotalHours}:{duration.Minutes:D2}",
                    StartTime = startTime,
                    EndTime = endTime,
                    ColorCode = RecordingCategory?.ColorCode ?? Brushes.DarkOrange.ToString(),
                    ColumnIndex = 0
                };

                ScheduleItems.Add(newItem);
            }

            IsRecording = false;
            RecordingStartTime = null;
            RecordingDuration = TimeSpan.Zero;
            RecordingTitle = "";
            IsCountdownMode = false;
            CountdownRemaining = null;
        }
        else
        {
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
