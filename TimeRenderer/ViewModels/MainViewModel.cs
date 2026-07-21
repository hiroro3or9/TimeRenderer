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

/// <summary>
/// メイン画面の ViewModel。責務ごとに partial で分割している。
///
/// - MainViewModel.cs                 : 表示モード・現在時刻・記録タイマー・変更監視の中核
/// - .Commands.cs                     : 追加・編集・削除・記録開始/停止などのコマンド
/// - .Data.cs                         : 設定/予定データ/メモの読み書き（デバウンス保存・破損保護）
/// - .Layout.cs                       : 日/週/月/スプリントの表示日計算とセグメント再レイアウト
/// - .Timeline.cs / .TimelineDecorations.cs / .TimelineViewport.cs : スプリントタイムラインの計算
/// - .Stats.cs                        : 統計ビューの集計
/// - .Away.cs                         : 離席検知と記録への反映
/// - .Undo.cs                         : 取り消し・やり直し
/// - .Search.cs / .Selection.cs / .Categories.cs / .Titles.cs / .Routines.cs : 各機能
///
/// 共有の enum / record（ViewMode, TimerOption, TimelineGroupMode, AwayHandlingMode など）は
/// ViewModels/ 直下の独立ファイルにある。
/// </summary>
public partial class MainViewModel : INotifyPropertyChanged
{
    private readonly bool _isInitialized = false;

    /// <summary>LoadData 実行中フラグ（再計算・保存の抑止用）</summary>
    private bool _isLoadingData;

    /// <summary>アイテムの複数プロパティ一括更新中フラグ（変更ごとの再計算・保存の抑止用）</summary>
    private bool _isBatchUpdatingItem;

    private TransitionDirection _transitionDirection = TransitionDirection.Forward;
    public TransitionDirection TransitionDirection
    {
        get => _transitionDirection;
        set => SetProperty(ref _transitionDirection, value);
    }



    public IReadOnlyList<ViewModeOption> ViewModeOptions { get; } =
    [
        new(ViewMode.Day, "日"),
        new(ViewMode.Week, "週"),
        new(ViewMode.Month, "月"),
        new(ViewMode.Sprint, "スプリント"),
        new(ViewMode.SprintTimeline, "タイムライン"),
        new(ViewMode.Stats, "統計"),
    ];

    /// <summary>現在の表示モードに対応するドロップダウン選択項目</summary>
    public ViewModeOption SelectedViewModeOption
    {
        get
        {
            foreach (var option in ViewModeOptions)
            {
                if (option.Mode == CurrentViewMode) return option;
            }
            return ViewModeOptions[0];
        }
        set
        {
            if (value != null)
            {
                CurrentViewMode = value.Mode;
            }
        }
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

    private IReadOnlyList<ScheduleSegment> _standardItems = [];
    /// <summary>週/日ビュー描画用のセグメント一覧（日またぎアイテムは日単位に分割済み）</summary>
    public IReadOnlyList<ScheduleSegment> StandardItems
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
                EnsureRoutineOccurrences(value);
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
                OnPropertyChanged(nameof(ShowAwayBanner));
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
                NotifyViewModeDependents();
                SaveSettings();
            }
        }
    }

    /// <summary>CurrentViewMode に依存する表示用プロパティの変更通知をまとめて発行する</summary>
    private void NotifyViewModeDependents()
    {
        OnPropertyChanged(nameof(DateDisplay));
        OnPropertyChanged(nameof(SelectedViewModeOption));
        OnPropertyChanged(nameof(IsDayMode));
        OnPropertyChanged(nameof(IsWeekMode));
        OnPropertyChanged(nameof(IsMonthMode));
        OnPropertyChanged(nameof(IsSprintMode));
        OnPropertyChanged(nameof(IsSprintTimelineMode));
        OnPropertyChanged(nameof(IsStatsMode));
        OnPropertyChanged(nameof(IsTimeRangeSettingsVisible));
        OnPropertyChanged(nameof(IsSprintSettingsVisible));
        OnPropertyChanged(nameof(IsDayOfWeekSettingsVisible));
    }

    public bool IsDayMode => CurrentViewMode == ViewMode.Day;
    public bool IsWeekMode => CurrentViewMode == ViewMode.Week;
    public bool IsMonthMode => CurrentViewMode == ViewMode.Month;
    public bool IsSprintMode => CurrentViewMode == ViewMode.Sprint;
    public bool IsSprintTimelineMode => CurrentViewMode == ViewMode.SprintTimeline;
    public bool IsStatsMode => CurrentViewMode == ViewMode.Stats;
    public bool IsTimeRangeSettingsVisible => CurrentViewMode == ViewMode.Day || CurrentViewMode == ViewMode.Week;
    public bool IsSprintSettingsVisible => CurrentViewMode == ViewMode.Sprint || CurrentViewMode == ViewMode.SprintTimeline;
    public bool IsDayOfWeekSettingsVisible => CurrentViewMode != ViewMode.SprintTimeline && CurrentViewMode != ViewMode.Stats;

    public DateTime CurrentWeekStart => Converters.DateTimeHelper.GetStartOfWeek(CurrentDate);

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
            else if (CurrentViewMode == ViewMode.Stats)
            {
                return GetStatsRangeDisplay();
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

    public MainViewModel(Services.IDialogService dialogService)
    {
        _dialogService = dialogService;
        InitializeCommands();
        InitializeCategoryCommands();
        InitializeStatsCommands();
        InitializeSearchCommands();
        InitializeTitleCommands();
        InitializeRoutineCommands();
        InitializeWorkDayCommands();
        InitializeUndo();
        InitializeAwayDetection();
        LoadCategories(null); // 既定カテゴリで初期化（LoadSettings で上書きされる）
        LoadPinnedTitles(null); // 既定の定型タイトルで初期化（LoadSettings で上書きされる）

        ScheduleItems = [];
        ScheduleItems.CollectionChanged += OnScheduleItemsChanged;

        _selectedTimerOption = TimerOptions[0];

        CurrentDate = DateTime.Today;

        InitializeTimeLabels();
        UpdateVisibleDays();
        LoadData();
        LoadSettings();
        LoadMemos();
        LoadWorkDays(); // 予定データの読み込み後（未退勤の自動締めが作業記録を参照するため）
        UpdateMemoTextForCurrentWeek();
        StartClock();

        _isInitialized = true;
    }



    /// <summary>リマインダー・自動開始チェックを最後に実行した時刻（10秒間隔の間引き用）</summary>
    private DateTime _lastReminderCheck = DateTime.MinValue;

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

            // タイムラインの現在時刻ライン（内部で1分程度に間引かれる）
            UpdateTimelineNowLine(CurrentTime);

            // リマインダー・自動開始のチェックは10秒間隔に間引く（時計表示の500msごとには不要）
            if (CurrentTime - _lastReminderCheck >= TimeSpan.FromSeconds(10))
            {
                _lastReminderCheck = CurrentTime;
                CheckReminders(CurrentTime);
                UpdateWorkDayTick(CurrentTime); // 勤務時間の表示更新と、日付またぎの自動締め
            }
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

                // 消えたアイテムを選択したままにしない。
                // 残すと Enter や Delete が「もう存在しないアイテム」に対して働く
                if (ReferenceEquals(item, SelectedItem)) SelectedItem = null;
            }
        }

        if (_isLoadingData) return; // ロード中は再計算・保存を抑止（LoadData 完了時に一括実行）
        if (IsApplyingUndo) return; // 取り消し・やり直しの適用中は AfterUndoRedo でまとめて実行

        RecalculateLayout();
        SaveData();
    }

    private void OnScheduleItemPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        // 表示専用のプロパティは再計算・保存の対象外にする。
        // - ColumnIndex: RecalculateLayout 内で書き換えられる（再帰防止）
        // - IsSelected : 選択のたびにレイアウトを作り直すと、
        //                クリックした要素がツリーから外れてドラッグ開始に失敗する。
        //                データでもないので保存も不要
        // - ToolTipText: Title/StartTime などの変更に伴って発火する派生通知。
        //                元のプロパティ側で既に再計算されるため二重に走らせない
        if (e.PropertyName is nameof(ScheduleItem.ColumnIndex)
                           or nameof(ScheduleItem.IsSelected)
                           or nameof(ScheduleItem.ToolTipText)) return;
        if (_isBatchUpdatingItem) return; // EditCommand 等の一括更新中は完了時にまとめて処理
        if (IsApplyingUndo) return;       // 取り消し・やり直しの適用中は AfterUndoRedo でまとめて実行

        // タイトル・色などの変更も月ビュー（独自描画セル）や週ビューのセグメントへ反映する必要がある
        RecalculateLayout();
        SaveData();
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

