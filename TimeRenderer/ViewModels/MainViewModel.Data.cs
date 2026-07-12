using System;
using System.Collections.Generic;
using System.Windows.Threading;

using TimeRenderer.Models;
using TimeRenderer.Helpers;
using TimeRenderer.Services;

namespace TimeRenderer.ViewModels;

public partial class MainViewModel
{
    private Dictionary<DateTime, string> _weeklyMemos = [];

    // メモはキーストロークごとに保存せず、入力が止まってから書き込む（デバウンス）
    private DispatcherTimer? _memoSaveTimer;
    private bool _hasPendingMemoSave;

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
                if (string.IsNullOrEmpty(value))
                {
                    _weeklyMemos.Remove(CurrentWeekStart);
                }
                else
                {
                    _weeklyMemos[CurrentWeekStart] = value;
                }
                ScheduleMemoSave();
            }
        }
    }

    private void ScheduleMemoSave()
    {
        _hasPendingMemoSave = true;
        if (_memoSaveTimer == null)
        {
            _memoSaveTimer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(1) };
            _memoSaveTimer.Tick += (_, _) => FlushMemoSave();
        }
        _memoSaveTimer.Stop();
        _memoSaveTimer.Start();
    }

    /// <summary>保留中のメモ保存を即時実行する（アプリ終了時などに呼ぶ）</summary>
    public void FlushMemoSave()
    {
        _memoSaveTimer?.Stop();
        if (_hasPendingMemoSave)
        {
            _hasPendingMemoSave = false;
            SaveMemos();
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
            EnabledDaysOfWeek = EnabledDaysOfWeek,
            Categories = [.. Categories]
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

            // 設定ファイルが壊れていても不正な enum 値にならないよう検証する
            _currentViewMode = Enum.IsDefined(typeof(ViewMode), settings.ViewMode)
                ? (ViewMode)settings.ViewMode
                : ViewMode.Day;
            OnPropertyChanged(nameof(CurrentViewMode));
            NotifyViewModeDependents();

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

            LoadCategories(settings.Categories);

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
            OnPropertyChanged(nameof(EnabledDayHeaders));

            UpdateVisibleDays();
        }
    }


    private void SaveData()
    {
        // 初期化中・ロード中の保存を抑止する
        // （起動時の Clear で空リストがファイルに書き込まれ、データ消失の危険があった）
        if (!_isInitialized || _isLoadingData) return;

        Services.FilePersistenceService.SaveData(ScheduleItems);
    }

    private void LoadData()
    {
        var items = Services.FilePersistenceService.LoadData();

        _isLoadingData = true;
        try
        {
            // Clear は Reset イベントのため OldItems が渡らず、購読解除は手動で行う
            foreach (var old in ScheduleItems)
            {
                old.PropertyChanged -= OnScheduleItemPropertyChanged;
            }
            ScheduleItems.Clear();
            foreach (var item in items)
            {
                ScheduleItems.Add(item);
            }
        }
        finally
        {
            _isLoadingData = false;
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
