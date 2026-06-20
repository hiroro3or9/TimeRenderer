using System;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Runtime.CompilerServices;
using System.Windows.Input;
using System.Windows.Threading;
using System.Media;
using TimeRenderer.Controls;

using TimeRenderer.Models;
using TimeRenderer.Helpers;
using TimeRenderer.Services;

namespace TimeRenderer.ViewModels;

public partial class MainViewModel : INotifyPropertyChanged
{
    private readonly bool _isInitialized = false;

    private TransitionDirection _transitionDirection = TransitionDirection.Forward;
    public TransitionDirection TransitionDirection
    {
        get => _transitionDirection;
        set => SetProperty(ref _transitionDirection, value);
    }



    public enum ViewMode
    {
        Day,
        Week,
        Month,
        Sprint,
        SprintTimeline
    }

    public static List<int> StartHourOptions => [.. Enumerable.Range(0, 24)];
    public static List<int> EndHourOptions => [.. Enumerable.Range(1, 24)];

    public ObservableCollection<ScheduleItem> ScheduleItems { get; set; }
    private IReadOnlyList<string> _timeLabels = [];
    public IReadOnlyList<string> TimeLabels
    {
        get => _timeLabels;
        set => SetProperty(ref _timeLabels, value);
    }

    private IReadOnlyList<DateTime> _visibleDays = [];
    public IReadOnlyList<DateTime> VisibleDays
    {
        get => _visibleDays;
        set => SetProperty(ref _visibleDays, value);
    }

    private IReadOnlyList<ScheduleItem> _standardItems = [];
    public IReadOnlyList<ScheduleItem> StandardItems
    {
        get => _standardItems;
        set => SetProperty(ref _standardItems, value);
    }

    private IReadOnlyList<ScheduleItem> _allDayItems = [];
    public IReadOnlyList<ScheduleItem> AllDayItems
    {
        get => _allDayItems;
        set => SetProperty(ref _allDayItems, value);
    }

    private IReadOnlyDictionary<DateTime, List<ScheduleItem>> _dailyScheduleItems = new Dictionary<DateTime, List<ScheduleItem>>();
    public IReadOnlyDictionary<DateTime, List<ScheduleItem>> DailyScheduleItems
    {
        get => _dailyScheduleItems;
        private set => SetProperty(ref _dailyScheduleItems, value);
    }

    private IReadOnlyList<CalendarCellViewModel> _calendarCells = [];
    public IReadOnlyList<CalendarCellViewModel> CalendarCells
    {
        get => _calendarCells;
        private set => SetProperty(ref _calendarCells, value);
    }

    private DateTime _currentDate;
    public DateTime CurrentDate
    {
        get => _currentDate;
        set
        {
            if (_currentDate != value)
            {
                var oldWeekStart = CurrentWeekStart;
                _currentDate = value;
                UpdateVisibleDays();
                OnPropertyChanged();
                OnPropertyChanged(nameof(CurrentWeekStart));
                OnPropertyChanged(nameof(DateDisplay));

                if (CurrentWeekStart != oldWeekStart)
                {
                    UpdateMemoTextForCurrentWeek();
                }
            }
        }
    }

    public record TimerOption(string Name, int Minutes);

    public List<TimerOption> TimerOptions { get; } = [
        new("カウントアップ", 0),
        new("15分", 15),
        new("30分", 30),
        new("45分", 45),
        new("60分", 60)
    ];

    private TimerOption _selectedTimerOption = null!;
    public TimerOption SelectedTimerOption
    {
        get => _selectedTimerOption;
        set => SetProperty(ref _selectedTimerOption, value);
    }

    private bool _isCountdownMode;
    public bool IsCountdownMode
    {
        get => _isCountdownMode;
        set => SetProperty(ref _isCountdownMode, value);
    }

    private TimeSpan? _countdownRemaining;
    public TimeSpan? CountdownRemaining
    {
        get => _countdownRemaining;
        set
        {
            if (SetProperty(ref _countdownRemaining, value))
            {
                OnPropertyChanged(nameof(RecordingDurationText));
            }
        }
    }

    private bool _isRecording;
    public bool IsRecording
    {
        get => _isRecording;
        set
        {
            if (SetProperty(ref _isRecording, value))
            {
                OnPropertyChanged(nameof(RecordingDurationText));
                UpdateRecordingCommandState();
            }
        }
    }

    private DateTime? _recordingStartTime;
    public DateTime? RecordingStartTime
    {
        get => _recordingStartTime;
        set => SetProperty(ref _recordingStartTime, value);
    }

    private TimeSpan _recordingDuration;
    public TimeSpan RecordingDuration
    {
        get => _recordingDuration;
        set
        {
            if (SetProperty(ref _recordingDuration, value))
            {
                OnPropertyChanged(nameof(RecordingDurationText));
            }
        }
    }

    public string RecordingDurationText => IsRecording
        ? (IsCountdownMode && CountdownRemaining.HasValue
            ? $"■ 停止 (残り {CountdownRemaining.Value:hh\\:mm\\:ss})"
            : $"■ 停止 ({RecordingDuration:hh\\:mm\\:ss})")
        : "● 記録開始";

    private string _recordingTitle = "";
    public string RecordingTitle
    {
        get => _recordingTitle;
        set => SetProperty(ref _recordingTitle, value);
    }

    private List<SprintInfo> _manualSprints = [];
    public List<SprintInfo> ManualSprints
    {
        get => _manualSprints;
        set
        {
            if (SetProperty(ref _manualSprints, value))
            {
                UpdateVisibleDays();
                SaveSettings();
            }
        }
    }

    private IReadOnlyList<SprintInfo> _timelineSprints = [];
    public IReadOnlyList<SprintInfo> TimelineSprints
    {
        get => _timelineSprints;
        set => SetProperty(ref _timelineSprints, value);
    }

    private bool _isAddSprintFormVisible;
    public bool IsAddSprintFormVisible
    {
        get => _isAddSprintFormVisible;
        set => SetProperty(ref _isAddSprintFormVisible, value);
    }

    private string _newSprintName = string.Empty;
    public string NewSprintName
    {
        get => _newSprintName;
        set => SetProperty(ref _newSprintName, value);
    }

    private DateTime? _newSprintStartDate;
    public DateTime? NewSprintStartDate
    {
        get => _newSprintStartDate;
        set => SetProperty(ref _newSprintStartDate, value);
    }

    private DateTime? _newSprintEndDate;
    public DateTime? NewSprintEndDate
    {
        get => _newSprintEndDate;
        set => SetProperty(ref _newSprintEndDate, value);
    }

    private SprintInfo? _editingSprint;
    public SprintInfo? EditingSprint
    {
        get => _editingSprint;
        set
        {
            if (SetProperty(ref _editingSprint, value))
            {
                OnPropertyChanged(nameof(FormTitle));
            }
        }
    }

    public string FormTitle => EditingSprint == null ? "スプリントを追加" : "スプリントを編集";

    private ViewMode _currentViewMode;
    public ViewMode CurrentViewMode
    {
        get => _currentViewMode;
        set
        {
            if (_currentViewMode != value)
            {
                _currentViewMode = value;
                OnPropertyChanged();
                UpdateVisibleDays();
                OnPropertyChanged(nameof(DateDisplay));
                OnPropertyChanged(nameof(IsDayMode));
                OnPropertyChanged(nameof(IsWeekMode));
                OnPropertyChanged(nameof(IsMonthMode));
                OnPropertyChanged(nameof(IsSprintMode));
                OnPropertyChanged(nameof(IsSprintTimelineMode));
                OnPropertyChanged(nameof(IsTimeRangeSettingsVisible));
                OnPropertyChanged(nameof(IsSprintSettingsVisible));
                OnPropertyChanged(nameof(IsDayOfWeekSettingsVisible));
                SaveSettings();
            }
        }
    }

    public bool IsDayMode => CurrentViewMode == ViewMode.Day;
    public bool IsWeekMode => CurrentViewMode == ViewMode.Week;
    public bool IsMonthMode => CurrentViewMode == ViewMode.Month;
    public bool IsSprintMode => CurrentViewMode == ViewMode.Sprint;
    public bool IsSprintTimelineMode => CurrentViewMode == ViewMode.SprintTimeline;
    public bool IsTimeRangeSettingsVisible => CurrentViewMode == ViewMode.Day || CurrentViewMode == ViewMode.Week;
    public bool IsSprintSettingsVisible => CurrentViewMode == ViewMode.Sprint || CurrentViewMode == ViewMode.SprintTimeline;
    public bool IsDayOfWeekSettingsVisible => CurrentViewMode != ViewMode.SprintTimeline;

    public DateTime CurrentWeekStart
    {
        get
        {
            var diff = (7 + (CurrentDate.DayOfWeek - DayOfWeek.Monday)) % 7;
            return CurrentDate.AddDays(-1 * diff).Date;
        }
    }

    public string DateDisplay
    {
        get
        {
            if (CurrentViewMode == ViewMode.Day)
            {
                return CurrentDate.ToString("yyyy年M月d日 (ddd)");
            }
            else if (CurrentViewMode == ViewMode.Week)
            {
                var start = CurrentWeekStart;
                var end = start.AddDays(6);
                if (start.Month == end.Month)
                    return $"{start:yyyy年M月d日} - {end:d日}";
                else
                    return $"{start:yyyy年M月d日} - {end:M月d日}";
            }
            else if (CurrentViewMode == ViewMode.Month)
            {
                return CurrentDate.ToString("yyyy年M月");
            }
            else if (CurrentViewMode == ViewMode.Sprint)
            {
                var sprint = Helpers.SprintHelper.GetSprintForDate(ManualSprints, CurrentDate);
                return $"{sprint.Name} ({sprint.StartDate:yyyy/MM/dd} - {sprint.EndDate:MM/dd})";
            }
            else // SprintTimeline
            {
                // タイムラインモード時は表示範囲を表示
                var sprint = Helpers.SprintHelper.GetSprintForDate(ManualSprints, CurrentDate);
                return $"スプリントタイムライン (起点: {sprint.Name})";
            }
        }
    }

    private DateTime _currentTime;
    public DateTime CurrentTime
    {
        get => _currentTime;
        set => SetProperty(ref _currentTime, value);
    }

    private ScheduleItem? _selectedItem;
    public ScheduleItem? SelectedItem
    {
        get => _selectedItem;
        set => SetProperty(ref _selectedItem, value);
    }

    public MainViewModel(Services.IDialogService dialogService)
    {
        _dialogService = dialogService;
        InitializeCommands();

        ScheduleItems = [];
        ScheduleItems.CollectionChanged += OnScheduleItemsChanged;

        _selectedTimerOption = TimerOptions[0];

        CurrentDate = DateTime.Today;

        InitializeTimeLabels();
        UpdateVisibleDays();
        LoadData();
        LoadSettings();
        LoadMemos();
        UpdateMemoTextForCurrentWeek();
        StartClock();

        _isInitialized = true;
    }



    private void StartClock()
    {
        CurrentTime = DateTime.Now;
        DispatcherTimer timer = new()
        {
            Interval = TimeSpan.FromMilliseconds(500)
        };
        timer.Tick += (s, e) =>
        {
            CurrentTime = DateTime.Now;
            if (IsRecording && RecordingStartTime.HasValue)
            {
                RecordingDuration = CurrentTime - RecordingStartTime.Value;

                if (IsCountdownMode && CountdownRemaining.HasValue)
                {
                    var targetDuration = TimeSpan.FromMinutes(SelectedTimerOption.Minutes);
                    var remaining = targetDuration - RecordingDuration;
                    if (remaining <= TimeSpan.Zero)
                    {
                        CountdownRemaining = TimeSpan.Zero;
                        SystemSounds.Exclamation.Play();
                        ToggleRecording();
                    }
                    else
                    {
                        CountdownRemaining = remaining;
                    }
                }
            }
        };
        timer.Start();
    }

        private void AddScheduleItemAtDate(DateTime date)
        {
            var newItem = new ScheduleItem
            {
                StartTime = date.Date.AddHours(9),
                EndTime = date.Date.AddHours(10),
                Title = "新しい予定",
                BackgroundColor = System.Windows.Media.Brushes.LightBlue
            };

            var result = _dialogService.ShowScheduleEditDialog(newItem);
            if (result != null)
            {
                ScheduleItems.Add(result);
                // OnScheduleItemsChanged 経由で Layout の更新等が発行される
            }
        }

    private void OnScheduleItemsChanged(object? sender, System.Collections.Specialized.NotifyCollectionChangedEventArgs e)
    {
        if (e.NewItems != null)
        {
            foreach (ScheduleItem item in e.NewItems)
            {
                item.PropertyChanged -= OnScheduleItemPropertyChanged;
                item.PropertyChanged += OnScheduleItemPropertyChanged;
            }
        }
        if (e.OldItems != null)
        {
            foreach (ScheduleItem item in e.OldItems)
            {
                item.PropertyChanged -= OnScheduleItemPropertyChanged;
            }
        }
        RecalculateLayout();
        SaveData();
    }

    private void OnScheduleItemPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(ScheduleItem.StartTime) || 
            e.PropertyName == nameof(ScheduleItem.EndTime) || 
            e.PropertyName == nameof(ScheduleItem.IsAllDay))
        {
            RecalculateLayout();
        }
        if (e.PropertyName != nameof(ScheduleItem.ColumnIndex) && 
            e.PropertyName != nameof(ScheduleItem.MaxColumnIndex))
        {
            SaveData();
        }
    }

    public record DayHeaderInfo(string Name, DayOfWeek DayOfWeek);

    public List<DayHeaderInfo> EnabledDayHeaders
    {
        get
        {
            var headers = new List<DayHeaderInfo>();
            var order = new[] { DayOfWeek.Monday, DayOfWeek.Tuesday, DayOfWeek.Wednesday, DayOfWeek.Thursday, DayOfWeek.Friday, DayOfWeek.Saturday, DayOfWeek.Sunday };
            foreach (var day in order)
            {
                if (EnabledDaysOfWeek.Contains(day))
                {
                    var name = day switch
                    {
                        DayOfWeek.Monday => "月",
                        DayOfWeek.Tuesday => "火",
                        DayOfWeek.Wednesday => "水",
                        DayOfWeek.Thursday => "木",
                        DayOfWeek.Friday => "金",
                        DayOfWeek.Saturday => "土",
                        DayOfWeek.Sunday => "日",
                        _ => ""
                    };
                    headers.Add(new DayHeaderInfo(name, day));
                }
            }
            return headers;
        }
    }

    public List<string> EnabledDayNames
    {
        get
        {
            var names = new List<string>();
            var order = new[] { DayOfWeek.Monday, DayOfWeek.Tuesday, DayOfWeek.Wednesday, DayOfWeek.Thursday, DayOfWeek.Friday, DayOfWeek.Saturday, DayOfWeek.Sunday };
            foreach (var day in order)
            {
                if (EnabledDaysOfWeek.Contains(day))
                {
                    names.Add(day switch
                    {
                        DayOfWeek.Monday => "月",
                        DayOfWeek.Tuesday => "火",
                        DayOfWeek.Wednesday => "水",
                        DayOfWeek.Thursday => "木",
                        DayOfWeek.Friday => "金",
                        DayOfWeek.Saturday => "土",
                        DayOfWeek.Sunday => "日",
                        _ => ""
                    });
                }
            }
            return names;
        }
    }

    public event PropertyChangedEventHandler? PropertyChanged;
    protected void OnPropertyChanged([CallerMemberName] string? propertyName = null)
    {
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
    }

    protected bool SetProperty<T>(ref T field, T value, [CallerMemberName] string? propertyName = null)
    {
        if (System.Collections.Generic.EqualityComparer<T>.Default.Equals(field, value)) return false;
        field = value;
        OnPropertyChanged(propertyName);
        return true;
    }
}

