using System;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Runtime.CompilerServices;
using System.Windows.Threading;
using System.Media;
using TimeRenderer.Controls;

namespace TimeRenderer;

public partial class MainViewModel : INotifyPropertyChanged
{
    private readonly bool _isInitialized = false;

    private TransitionDirection _transitionDirection = TransitionDirection.Forward;
    public TransitionDirection TransitionDirection
    {
        get => _transitionDirection;
        set
        {
            if (_transitionDirection != value)
            {
                _transitionDirection = value;
                OnPropertyChanged();
            }
        }
    }

    public enum ViewMode
    {
        Day,
        Week
    }

    public static List<int> StartHourOptions => [.. Enumerable.Range(0, 24)];
    public static List<int> EndHourOptions => [.. Enumerable.Range(1, 24)];

    public ObservableCollection<ScheduleItem> ScheduleItems { get; set; }
    public ObservableCollection<string> TimeLabels { get; set; }
    public ObservableCollection<DateTime> VisibleDays { get; set; }

    public ObservableCollection<ScheduleItem> StandardItems { get; set; }
    public ObservableCollection<ScheduleItem> AllDayItems { get; set; }

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
                OnPropertyChanged();
                UpdateVisibleDays();
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
        set
        {
            if (_selectedTimerOption != value)
            {
                _selectedTimerOption = value;
                OnPropertyChanged();
            }
        }
    }

    private bool _isCountdownMode;
    public bool IsCountdownMode
    {
        get => _isCountdownMode;
        set
        {
            if (_isCountdownMode != value)
            {
                _isCountdownMode = value;
                OnPropertyChanged();
            }
        }
    }

    private TimeSpan? _countdownRemaining;
    public TimeSpan? CountdownRemaining
    {
        get => _countdownRemaining;
        set
        {
            if (_countdownRemaining != value)
            {
                _countdownRemaining = value;
                OnPropertyChanged();
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
            if (_isRecording != value)
            {
                _isRecording = value;
                OnPropertyChanged();
                OnPropertyChanged(nameof(RecordingDurationText));
                UpdateRecordingCommandState();
            }
        }
    }

    private DateTime? _recordingStartTime;
    public DateTime? RecordingStartTime
    {
        get => _recordingStartTime;
        set
        {
            if (_recordingStartTime != value)
            {
                _recordingStartTime = value;
                OnPropertyChanged();
            }
        }
    }

    private TimeSpan _recordingDuration;
    public TimeSpan RecordingDuration
    {
        get => _recordingDuration;
        set
        {
            if (_recordingDuration != value)
            {
                _recordingDuration = value;
                OnPropertyChanged();
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
        set
        {
            if (_recordingTitle != value)
            {
                _recordingTitle = value;
                OnPropertyChanged();
            }
        }
    }

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
                SaveSettings(); 
            }
        }
    }

    public bool IsDayMode => CurrentViewMode == ViewMode.Day;
    public bool IsWeekMode => CurrentViewMode == ViewMode.Week;

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
            else
            {
                var start = CurrentWeekStart;
                var end = start.AddDays(6);
                if (start.Month == end.Month)
                    return $"{start:yyyy年M月d日} - {end:d日}";
                else
                    return $"{start:yyyy年M月d日} - {end:M月d日}";
            }
        }
    }

    private DateTime _currentTime;
    public DateTime CurrentTime
    {
        get => _currentTime;
        set
        {
            if (_currentTime != value)
            {
                _currentTime = value;
                OnPropertyChanged();
            }
        }
    }

    private ScheduleItem? _selectedItem;
    public ScheduleItem? SelectedItem
    {
        get => _selectedItem;
        set
        {
            if (_selectedItem != value)
            {
                _selectedItem = value;
                OnPropertyChanged();
            }
        }
    }

    public MainViewModel(Services.IDialogService dialogService)
    {
        _dialogService = dialogService;
        InitializeCommands();

        ScheduleItems = [];
        ScheduleItems.CollectionChanged += OnScheduleItemsChanged;

        StandardItems = [];
        AllDayItems = [];

        TimeLabels = [];
        VisibleDays = [];

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

    public event PropertyChangedEventHandler? PropertyChanged;
    protected void OnPropertyChanged([CallerMemberName] string? propertyName = null)
    {
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
    }
}
