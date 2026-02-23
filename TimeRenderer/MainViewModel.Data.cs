using System;
using System.Collections.Generic;

namespace TimeRenderer;

public partial class MainViewModel
{
    private Dictionary<DateTime, string> _weeklyMemos = [];

    // メモパネル関連プロパティ
    private bool _isMemoPanelVisible = true;
    public bool IsMemoPanelVisible
    {
        get => _isMemoPanelVisible;
        set
        {
            if (_isMemoPanelVisible != value)
            {
                _isMemoPanelVisible = value;
                OnPropertyChanged();
                SaveSettings();
            }
        }
    }

    private bool _isMemoEditMode = true;
    public bool IsMemoEditMode
    {
        get => _isMemoEditMode;
        set
        {
            if (_isMemoEditMode != value)
            {
                _isMemoEditMode = value;
                OnPropertyChanged();
                SaveSettings();
            }
        }
    }

    private string _memoText = "";
    public string MemoText
    {
        get => _memoText;
        set
        {
            if (_memoText != value)
            {
                _memoText = value;
                OnPropertyChanged();
                _weeklyMemos[CurrentWeekStart] = value;
                SaveMemos();
            }
        }
    }

    private void UpdateMemoTextForCurrentWeek()
    {
        _memoText = _weeklyMemos.TryGetValue(CurrentWeekStart, out var memo) ? memo : "";
        OnPropertyChanged(nameof(MemoText));
    }

    private void SaveSettings()
    {
        if (!_isInitialized) return;

        AppSettings settings = new()
        {
            IsMemoPanelVisible = IsMemoPanelVisible,
            IsMemoEditMode = IsMemoEditMode,
            ViewMode = (int)CurrentViewMode,
            DisplayStartHour = DisplayStartHour,
            DisplayEndHour = DisplayEndHour
        };
        Services.SettingsService.SaveSettings(settings);
    }

    private void LoadSettings()
    {
        var settings = Services.SettingsService.LoadSettings();
        if (settings != null)
        {
            _isMemoPanelVisible = settings.IsMemoPanelVisible;
            OnPropertyChanged(nameof(IsMemoPanelVisible));

            _isMemoEditMode = settings.IsMemoEditMode;
            OnPropertyChanged(nameof(IsMemoEditMode));

            _currentViewMode = (ViewMode)settings.ViewMode;
            OnPropertyChanged(nameof(CurrentViewMode));
            OnPropertyChanged(nameof(IsDayMode));
            OnPropertyChanged(nameof(IsWeekMode));
            OnPropertyChanged(nameof(DateDisplay));

            _displayStartHour = Math.Clamp(settings.DisplayStartHour, 0, 23);
            _displayEndHour = Math.Clamp(settings.DisplayEndHour, _displayStartHour + 1, 24);
            OnPropertyChanged(nameof(DisplayStartHour));
            OnPropertyChanged(nameof(DisplayEndHour));
            OnPropertyChanged(nameof(ScheduleGridHeight));
            InitializeTimeLabels();
            
            UpdateVisibleDays();
        }
    }

    private void SaveData()
    {
        Services.FilePersistenceService.SaveData(ScheduleItems);
    }

    private void LoadData()
    {
        var items = Services.FilePersistenceService.LoadData();
        ScheduleItems.Clear();
        foreach (var item in items)
        {
            ScheduleItems.Add(item);
        }
        RecalculateLayout();
    }

    private void SaveMemos()
    {
        Services.FilePersistenceService.SaveMemos(_weeklyMemos);
    }

    private void LoadMemos()
    {
        _weeklyMemos = Services.FilePersistenceService.LoadMemos();
    }
}
