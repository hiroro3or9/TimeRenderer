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

    // ============================================================
    // 設定の保存と読込。
    // 設定項目を追加するときは AppSettings / BuildSettings / ApplySettings
    // の3箇所を必ずセットで更新すること（Build と Apply は対称になるよう並べている）。
    // ============================================================

    private void SaveSettings()
    {
        if (!_isInitialized) return;
        Services.SettingsService.SaveSettings(BuildSettings());
    }

    /// <summary>現在のVM状態から設定スナップショットを作る（ApplySettings と対称）</summary>
    private AppSettings BuildSettings() => new()
    {
        IsMemoPanelVisible = IsMemoPanelVisible,
        IsSettingsPanelVisible = IsSettingsPanelVisible,
        IsMemoEditMode = IsMemoEditMode,
        ViewMode = (int)CurrentViewMode,
        DisplayStartHour = DisplayStartHour,
        DisplayEndHour = DisplayEndHour,
        IsDarkMode = IsDarkMode,
        TimelinePixelsPerDay = TimelinePixelsPerDay,
        TimelineGroupMode = (int)CurrentTimelineGroupMode,
        TimelineSprintCount = TimelineSprintCount,
        IsAwayDetectionEnabled = IsAwayDetectionEnabled,
        AwayThresholdMinutes = AwayThresholdMinutes,
        AwayHandlingMode = (int)CurrentAwayHandlingMode,
        SnapMinutes = SnapMinutes,
        ManualSprints = ManualSprints,
        EnabledDaysOfWeek = EnabledDaysOfWeek,
        Categories = [.. Categories],
        PinnedTitles = [.. PinnedTitles.Select(t => t.Text)],
        RoutineSchedules = Routines
    };

    private void LoadSettings()
    {
        var settings = Services.SettingsService.LoadSettings();
        if (settings != null)
        {
            ApplySettings(settings);
        }
    }

    /// <summary>設定スナップショットをVMへ反映する（BuildSettings と対称）</summary>
    private void ApplySettings(AppSettings settings)
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

        // 設定ファイルが壊れていても極端な倍率にならないようクランプする
        _timelinePixelsPerDay = Math.Clamp(
            settings.TimelinePixelsPerDay <= 0 ? Helpers.TimelineScale.DefaultPixelsPerDay : settings.TimelinePixelsPerDay,
            Helpers.TimelineScale.MinPixelsPerDay,
            Helpers.TimelineScale.MaxPixelsPerDay);
        OnPropertyChanged(nameof(TimelinePixelsPerDay));
        OnPropertyChanged(nameof(TimelineZoomText));

        _timelineGroupMode = Enum.IsDefined(typeof(TimelineGroupMode), settings.TimelineGroupMode)
            ? (TimelineGroupMode)settings.TimelineGroupMode
            : TimelineGroupMode.Packed;
        OnPropertyChanged(nameof(CurrentTimelineGroupMode));
        OnPropertyChanged(nameof(SelectedTimelineGroupModeOption));
        OnPropertyChanged(nameof(IsTimelineCategoryMode));
        OnPropertyChanged(nameof(TimelineLabelColumnWidth));

        _timelineSprintCount = Math.Clamp(
            settings.TimelineSprintCount <= 0 ? 5 : settings.TimelineSprintCount, 1, 25);
        OnPropertyChanged(nameof(TimelineSprintCount));
        OnPropertyChanged(nameof(SelectedTimelineSpanOption));

        _isAwayDetectionEnabled = settings.IsAwayDetectionEnabled;
        _awayThresholdMinutes = Math.Clamp(
            settings.AwayThresholdMinutes <= 0 ? 10 : settings.AwayThresholdMinutes, 1, 240);
        OnPropertyChanged(nameof(IsAwayDetectionEnabled));
        OnPropertyChanged(nameof(AwayThresholdMinutes));

        _awayHandlingMode = Enum.IsDefined(typeof(AwayHandlingMode), settings.AwayHandlingMode)
            ? (AwayHandlingMode)settings.AwayHandlingMode
            : AwayHandlingMode.Ask;
        OnPropertyChanged(nameof(CurrentAwayHandlingMode));
        OnPropertyChanged(nameof(SelectedAwayHandlingOption));

        ApplyAwaySettings();

        _snapMinutes = Math.Clamp(settings.SnapMinutes <= 0 ? 15 : settings.SnapMinutes, 1, 60);
        OnPropertyChanged(nameof(SnapMinutes));

        _manualSprints = settings.ManualSprints ?? [];
        OnPropertyChanged(nameof(ManualSprints));

        LoadCategories(settings.Categories);
        LoadPinnedTitles(settings.PinnedTitles);

        _routines = settings.RoutineSchedules ?? [];
        OnPropertyChanged(nameof(Routines));

        _enabledDaysOfWeek = (settings.EnabledDaysOfWeek is { Count: > 0 } days)
            ? days
            :
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
        EnsureRoutineOccurrences(CurrentDate);
    }


    private DispatcherTimer? _dataSaveTimer;
    private bool _hasPendingDataSave;

    /// <summary>
    /// 予定データの保存を予約する。
    ///
    /// 変更のたびに全件を書き出すと、編集が続く間ずっとファイルを書き換え続けることになり、
    /// 破損の窓もディスクI/Oも増える。短い間隔でまとめて1回にする。
    /// アプリ終了時は <see cref="FlushDataSave"/> で確実に書き出す。
    /// </summary>
    private void SaveData()
    {
        // 初期化中・ロード中の保存を抑止する
        // （起動時の Clear で空リストがファイルに書き込まれ、データ消失の危険があった）
        if (!_isInitialized || _isLoadingData) return;

        // 読み込みに失敗している状態で保存すると、
        // 壊れたファイルの上に「空のデータ」を確定させてしまう
        if (IsDataLoadFailed) return;

        _hasPendingDataSave = true;

        if (_dataSaveTimer == null)
        {
            _dataSaveTimer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(700) };
            _dataSaveTimer.Tick += (_, _) => FlushDataSave();
        }

        _dataSaveTimer.Stop();
        _dataSaveTimer.Start();
    }

    /// <summary>保留中の予定データ保存を即時実行する（アプリ終了時などに呼ぶ）</summary>
    public void FlushDataSave()
    {
        _dataSaveTimer?.Stop();
        if (!_hasPendingDataSave) return;

        _hasPendingDataSave = false;
        Services.FilePersistenceService.SaveData(ScheduleItems);
    }

    private bool _isDataLoadFailed;
    /// <summary>
    /// 予定データを読み込めなかったか。
    /// この間は保存を止める。空の状態を書き出すと、
    /// 壊れたファイルの上に「記録が無い」という結果を確定させてしまうため。
    /// </summary>
    public bool IsDataLoadFailed
    {
        get => _isDataLoadFailed;
        private set
        {
            if (SetProperty(ref _isDataLoadFailed, value))
            {
                OnPropertyChanged(nameof(HasDataNotice));
            }
        }
    }

    private string? _dataNotice;
    /// <summary>読み込みの異常をユーザーへ知らせるメッセージ（正常時は null）</summary>
    public string? DataNotice
    {
        get => _dataNotice;
        private set
        {
            if (SetProperty(ref _dataNotice, value))
            {
                OnPropertyChanged(nameof(HasDataNotice));
            }
        }
    }

    public bool HasDataNotice => !string.IsNullOrEmpty(DataNotice);

    /// <summary>通知バナーを閉じる（読み込み失敗の場合は保存停止も解除しない）</summary>
    public RelayCommand DismissDataNoticeCommand => _dismissDataNoticeCommand ??=
        new RelayCommand(_ => DataNotice = null);
    private RelayCommand? _dismissDataNoticeCommand;

    /// <summary>データフォルダをエクスプローラーで開く（バックアップを手動で確認するため）</summary>
    public RelayCommand OpenDataFolderCommand => _openDataFolderCommand ??=
        new RelayCommand(_ =>
        {
            try
            {
                System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo
                {
                    FileName = Services.JsonFileRepository.DataDirectory,
                    UseShellExecute = true
                });
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"Open data folder failed: {ex.Message}");
            }
        });
    private RelayCommand? _openDataFolderCommand;

    private void LoadData()
    {
        var result = Services.FilePersistenceService.LoadData();

        _isLoadingData = true;
        try
        {
            // Clear は Reset イベントのため OldItems が渡らず、購読解除は手動で行う
            foreach (var old in ScheduleItems)
            {
                old.PropertyChanged -= OnScheduleItemPropertyChanged;
            }
            ScheduleItems.Clear();
            foreach (var item in result.Items)
            {
                ScheduleItems.Add(item);
            }
        }
        finally
        {
            _isLoadingData = false;
        }

        ApplyLoadStatus(result);

        // 履歴が指しているインスタンスは読み込みで入れ替わっているため捨てる
        ClearUndoHistory();

        RecalculateLayout();
    }

    /// <summary>読み込み結果に応じて、通知と保存の可否を決める</summary>
    private void ApplyLoadStatus(Services.FilePersistenceService.ScheduleLoadResult result)
    {
        switch (result.Status)
        {
            case Services.LoadStatus.RecoveredFromBackup:
                // 復元できているので保存は続行してよい
                IsDataLoadFailed = false;
                DataNotice = result.Message;
                break;

            case Services.LoadStatus.Failed:
                // ここで保存を許すと、壊れたファイルを空データで確定させてしまう
                IsDataLoadFailed = true;
                DataNotice = (result.Message ?? "予定データを読み込めませんでした。") +
                             "\nデータを保護するため、このセッションでは保存を停止しています。" +
                             "\nデータフォルダのバックアップ（.bak）を確認してください。";
                break;

            default:
                IsDataLoadFailed = false;
                DataNotice = null;
                break;
        }
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
