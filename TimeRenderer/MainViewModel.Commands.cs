using System;
using System.Windows.Input;
using System.Windows.Media;
using TimeRenderer.Services;
using Brushes = System.Windows.Media.Brushes;

namespace TimeRenderer;

public partial class MainViewModel
{
    private readonly IDialogService _dialogService;

    public ICommand ToggleMemoPanelCommand { get; private set; } = null!;
    public ICommand ToggleSettingsPanelCommand { get; private set; } = null!;
    public ICommand ToggleRecordingCommand { get; private set; } = null!;
    public ICommand DeleteCommand { get; private set; } = null!;
    public ICommand AddCommand { get; private set; } = null!;
    public ICommand EditCommand { get; private set; } = null!;
    public ICommand PreviousCommand { get; private set; } = null!;
    public ICommand NextCommand { get; private set; } = null!;
    public ICommand TodayCommand { get; private set; } = null!;
    public ICommand ChangeViewModeCommand { get; private set; } = null!;

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

        AddCommand = new RelayCommand(_ =>
        {
            var newItem = _dialogService.ShowScheduleEditDialog();
            if (newItem != null)
            {
                ScheduleItems.Add(newItem);
            }
        });

        EditCommand = new RelayCommand(
            param =>
            {
                if (param is ScheduleItem item)
                {
                    var editedItem = _dialogService.ShowScheduleEditDialog(item);
                    if (editedItem != null)
                    {
                        item.Title = editedItem.Title;
                        item.Content = editedItem.Content;
                        item.StartTime = editedItem.StartTime;
                        item.EndTime = editedItem.EndTime;
                        item.IsAllDay = editedItem.IsAllDay;
                        item.BackgroundColor = editedItem.BackgroundColor;
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
    }

    private void Navigate(int amount)
    {
        TransitionDirection = amount > 0 ? Controls.TransitionDirection.Forward : Controls.TransitionDirection.Backward;
        CurrentDate = CurrentViewMode == ViewMode.Day ? CurrentDate.AddDays(amount) : CurrentDate.AddDays(amount * 7);
    }

    private void ToggleRecording()
    {
        if (IsRecording)
        {
            if (RecordingStartTime.HasValue)
            {
                var endTime = DateTime.Now;
                var startTime = RecordingStartTime.Value;
                
                var title = string.IsNullOrWhiteSpace(RecordingTitle) ? $"作業ログ {startTime:HH:mm}" : RecordingTitle;

                ScheduleItem newItem = new()
                {
                    Title = title,
                    Content = $"記録時間: {(endTime - startTime):hh\\:mm}",
                    StartTime = startTime,
                    EndTime = endTime,
                    BackgroundColor = Brushes.DarkOrange,
                    ColumnIndex = 0
                };
                
                ScheduleItems.Add(newItem);
                RecalculateLayout();
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
            var inputText = _dialogService.ShowTextInputDialog(defaultTitle);
            if (inputText != null) // Cancel以外（OK押下時）は開始
            {
                RecordingTitle = string.IsNullOrWhiteSpace(inputText) ? defaultTitle : inputText;
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

    private static void UpdateRecordingCommandState()
    {
        // ToggleRecordingCommand.RaiseCanExecuteChanged();
    }
}
