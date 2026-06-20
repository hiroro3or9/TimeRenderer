using System;
using System.Collections.Generic;

using TimeRenderer.Models;
using TimeRenderer.Helpers;
using TimeRenderer.Services;

namespace TimeRenderer.ViewModels;

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
            if (SetProperty(ref _isMemoPanelVisible, value))
            {
                SaveSettings();
            }
        }
    }

    private bool _isSettingsPanelVisible = false;
    public bool IsSettingsPanelVisible
    {
        get => _isSettingsPanelVisible;
        set
        {
            if (SetProperty(ref _isSettingsPanelVisible, value))
            {
                SaveSettings();
            }
        }
    }

    private bool _isDarkMode = false;
    public bool IsDarkMode
    {
        get => _isDarkMode;
        set
        {
            if (_isDarkMode != value)
            {
                _isDarkMode = value;
                App.ApplyTheme(value);
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
            if (SetProperty(ref _isMemoEditMode, value))
            {
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
            if (SetProperty(ref _memoText, value))
            {
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

    private List<DayOfWeek> _enabledDaysOfWeek =
    [
        DayOfWeek.Monday,
        DayOfWeek.Tuesday,
        DayOfWeek.Wednesday,
        DayOfWeek.Thursday,
        DayOfWeek.Friday,
        DayOfWeek.Saturday,
        DayOfWeek.Sunday
    ];

    public List<DayOfWeek> EnabledDaysOfWeek
    {
        get => _enabledDaysOfWeek;
        set
        {
            if (SetProperty(ref _enabledDaysOfWeek, value))
            {
                NotifyShowDaysProperties();
                OnPropertyChanged(nameof(EnabledDaysCount));
                OnPropertyChanged(nameof(EnabledDayNames));
                OnPropertyChanged(nameof(EnabledDayHeaders));
                UpdateVisibleDays();
                SaveSettings();
            }
        }
    }

    public int EnabledDaysCount => _enabledDaysOfWeek.Count;

    private void SetDayEnabled(DayOfWeek day, bool enabled)
    {
        var list = new List<DayOfWeek>(_enabledDaysOfWeek);
        if (enabled)
        {
            if (!list.Contains(day))
            {
                list.Add(day);
                // 曜日の順序を維持（月～日）
                DayOfWeek[] order = [DayOfWeek.Monday, DayOfWeek.Tuesday, DayOfWeek.Wednesday, DayOfWeek.Thursday, DayOfWeek.Friday, DayOfWeek.Saturday, DayOfWeek.Sunday];
                list = [.. order.Where(d => list.Contains(d))];
            }
        }
        else
        {
            if (list.Contains(day))
            {
                // 最低1つの曜日は表示した状態を維持する
                if (list.Count > 1)
                {
                    list.Remove(day);
                }
                else
                {
                    // チェック状態を元に戻すために通知
                    NotifyShowDaysProperties();
                    return;
                }
            }
        }
        EnabledDaysOfWeek = list;
    }

    private void NotifyShowDaysProperties()
    {
        OnPropertyChanged(nameof(ShowMonday));
        OnPropertyChanged(nameof(ShowTuesday));
        OnPropertyChanged(nameof(ShowWednesday));
        OnPropertyChanged(nameof(ShowThursday));
        OnPropertyChanged(nameof(ShowFriday));
        OnPropertyChanged(nameof(ShowSaturday));
        OnPropertyChanged(nameof(ShowSunday));
    }

    public bool ShowMonday { get => EnabledDaysOfWeek.Contains(DayOfWeek.Monday); set => SetDayEnabled(DayOfWeek.Monday, value); }
    public bool ShowTuesday { get => EnabledDaysOfWeek.Contains(DayOfWeek.Tuesday); set => SetDayEnabled(DayOfWeek.Tuesday, value); }
    public bool ShowWednesday { get => EnabledDaysOfWeek.Contains(DayOfWeek.Wednesday); set => SetDayEnabled(DayOfWeek.Wednesday, value); }
    public bool ShowThursday { get => EnabledDaysOfWeek.Contains(DayOfWeek.Thursday); set => SetDayEnabled(DayOfWeek.Thursday, value); }
    public bool ShowFriday { get => EnabledDaysOfWeek.Contains(DayOfWeek.Friday); set => SetDayEnabled(DayOfWeek.Friday, value); }
    public bool ShowSaturday { get => EnabledDaysOfWeek.Contains(DayOfWeek.Saturday); set => SetDayEnabled(DayOfWeek.Saturday, value); }
    public bool ShowSunday { get => EnabledDaysOfWeek.Contains(DayOfWeek.Sunday); set => SetDayEnabled(DayOfWeek.Sunday, value); }

    private void SaveSettings()
    {
        if (!_isInitialized) return;

        AppSettings settings = new()
        {
            IsMemoPanelVisible = IsMemoPanelVisible,
            IsSettingsPanelVisible = IsSettingsPanelVisible,
            IsMemoEditMode = IsMemoEditMode,
            ViewMode = (int)CurrentViewMode,
            DisplayStartHour = DisplayStartHour,
            DisplayEndHour = DisplayEndHour,
            IsDarkMode = IsDarkMode,
            ManualSprints = ManualSprints,
            EnabledDaysOfWeek = EnabledDaysOfWeek
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

            _isSettingsPanelVisible = settings.IsSettingsPanelVisible;
            OnPropertyChanged(nameof(IsSettingsPanelVisible));

            _isMemoEditMode = settings.IsMemoEditMode;
            OnPropertyChanged(nameof(IsMemoEditMode));

            _currentViewMode = (ViewMode)settings.ViewMode;
            OnPropertyChanged(nameof(CurrentViewMode));
            OnPropertyChanged(nameof(IsDayMode));
            OnPropertyChanged(nameof(IsWeekMode));
            OnPropertyChanged(nameof(IsMonthMode));
            OnPropertyChanged(nameof(IsSprintMode));
            OnPropertyChanged(nameof(IsSprintTimelineMode));
            OnPropertyChanged(nameof(DateDisplay));

            _displayStartHour = Math.Clamp(settings.DisplayStartHour, 0, 23);
            _displayEndHour = Math.Clamp(settings.DisplayEndHour, _displayStartHour + 1, 24);
            OnPropertyChanged(nameof(DisplayStartHour));
            OnPropertyChanged(nameof(DisplayEndHour));
            OnPropertyChanged(nameof(ScheduleGridHeight));
            InitializeTimeLabels();

            _isDarkMode = settings.IsDarkMode;
            App.ApplyTheme(_isDarkMode);
            OnPropertyChanged(nameof(IsDarkMode));

            _manualSprints = settings.ManualSprints ?? [];
            OnPropertyChanged(nameof(ManualSprints));

            if (settings.EnabledDaysOfWeek != null && settings.EnabledDaysOfWeek.Count > 0)
            {
                _enabledDaysOfWeek = settings.EnabledDaysOfWeek;
            }
            else
                _enabledDaysOfWeek =
                [
                    DayOfWeek.Monday,
                    DayOfWeek.Tuesday,
                    DayOfWeek.Wednesday,
                    DayOfWeek.Thursday,
                    DayOfWeek.Friday,
                    DayOfWeek.Saturday,
                    DayOfWeek.Sunday
                ];
            NotifyShowDaysProperties();
            OnPropertyChanged(nameof(EnabledDaysCount));
            OnPropertyChanged(nameof(EnabledDayNames));
            OnPropertyChanged(nameof(EnabledDayHeaders));
            
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
