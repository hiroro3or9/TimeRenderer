using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Runtime.CompilerServices;
using System.Windows.Input;
using System.Windows.Media;
using Brushes = System.Windows.Media.Brushes;
using System.Windows.Threading;
using System.Linq;
using System.Collections.Generic;
using TimeRenderer.Services;
using TimeRenderer.Helpers;
using TimeRenderer.Controls;

namespace TimeRenderer
{
    public class MainViewModel : INotifyPropertyChanged
    {
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

        // メモパネル関連
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
                    SaveSettings(); // 状態保存
                }
            }
        }

        private Dictionary<DateTime, string> _weeklyMemos = [];

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
                    // 週ごとのメモを更新して保存
                    _weeklyMemos[CurrentWeekStart] = value;
                    SaveMemos(); 
                }
            }
        }

        public ICommand ToggleMemoPanelCommand { get; }
        public ICommand ToggleRecordingCommand { get; }

        // 表示時間範囲の設定
        private int _displayStartHour = 0;
        public int DisplayStartHour
        {
            get => _displayStartHour;
            set
            {
                // 値の範囲制限（0～EndHour-1）
                var clamped = Math.Clamp(value, 0, _displayEndHour - 1);
                if (_displayStartHour != clamped)
                {
                    _displayStartHour = clamped;
                    OnPropertyChanged();
                    OnPropertyChanged(nameof(ScheduleGridHeight));
                    InitializeTimeLabels();
                    SaveSettings();
                }
            }
        }

        private int _displayEndHour = 24;
        public int DisplayEndHour
        {
            get => _displayEndHour;
            set
            {
                // 値の範囲制限（StartHour+1～24）
                var clamped = Math.Clamp(value, _displayStartHour + 1, 24);
                if (_displayEndHour != clamped)
                {
                    _displayEndHour = clamped;
                    OnPropertyChanged();
                    OnPropertyChanged(nameof(ScheduleGridHeight));
                    InitializeTimeLabels();
                    SaveSettings();
                }
            }
        }

        // 表示時間数に基づくグリッド高さ（1時間=60px）
        public double ScheduleGridHeight => (_displayEndHour - _displayStartHour) * 60.0;

        // 時間選択肢（ComboBox用）
        public static List<int> StartHourOptions => [.. Enumerable.Range(0, 24)];
        public static List<int> EndHourOptions => [.. Enumerable.Range(1, 24)];

        public ObservableCollection<ScheduleItem> ScheduleItems { get; set; }
        public ObservableCollection<string> TimeLabels { get; set; }
        
        // 表示中の日付リスト（ヘッダー用）
        public ObservableCollection<DateTime> VisibleDays { get; set; }



        // UIバインディング用：通常イベント（時刻指定あり）
        public ObservableCollection<ScheduleItem> StandardItems { get; set; }
        // UIバインディング用：終日イベント
        public ObservableCollection<ScheduleItem> AllDayItems { get; set; }

        private double _allDayPanelHeight = 30; // 初期値
        public double AllDayPanelHeight
        {
            get => _allDayPanelHeight;
            set
            {
                if (_allDayPanelHeight != value)
                {
                    _allDayPanelHeight = value;
                    OnPropertyChanged();
                }
            }
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
                    OnPropertyChanged();
                    UpdateVisibleDays();
                    OnPropertyChanged(nameof(CurrentWeekStart));
                    OnPropertyChanged(nameof(DateDisplay));

                    // 週が変わったらメモを読み込む
                    if (CurrentWeekStart != oldWeekStart)
                    {
                        UpdateMemoTextForCurrentWeek();
                    }
                }
            }
        }

        // 記録機能
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

        public string RecordingDurationText => IsRecording ? $"■ 停止 ({RecordingDuration:hh\\:mm\\:ss})" : "● 記録開始";

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

        // 週の開始日（月曜日とする）
        public DateTime CurrentWeekStart
        {
            get
            {
                var diff = (7 + (CurrentDate.DayOfWeek - DayOfWeek.Monday)) % 7;
                return CurrentDate.AddDays(-1 * diff).Date;
            }
        }

        private void UpdateMemoTextForCurrentWeek()
        {
            if (_weeklyMemos.TryGetValue(CurrentWeekStart, out var memo))
            {
                _memoText = memo;
            }
            else
            {
                _memoText = "";
            }
            OnPropertyChanged(nameof(MemoText));
        }

        // 画面上部に表示する日付文字列
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

        public ICommand DeleteCommand { get; }
        public ICommand PreviousCommand { get; }
        public ICommand NextCommand { get; }
        public ICommand TodayCommand { get; }
        public ICommand ChangeViewModeCommand { get; }

        private readonly FilePersistenceService _fileService;
        private readonly SettingsService _settingsService;

        public MainViewModel()
        {
            _fileService = new FilePersistenceService();
            _settingsService = new SettingsService();
            ToggleMemoPanelCommand = new RelayCommand(_ => IsMemoPanelVisible = !IsMemoPanelVisible);

            ScheduleItems = [];
            ScheduleItems.CollectionChanged += OnScheduleItemsChanged;

            StandardItems = [];
            AllDayItems = [];

            TimeLabels = [];
            VisibleDays = [];

            CurrentDate = DateTime.Today;
            // CurrentViewMode = ViewMode.Day; // Remove this as it overwrites loaded settings

            // コマンドの初期化
            DeleteCommand = new RelayCommand(
                param =>
                {
                    if (param is ScheduleItem item)
                    {
                        ScheduleItems.Remove(item);
                    }
                },
                param => param is ScheduleItem
            );

            PreviousCommand = new RelayCommand(_ => Navigate(-1));
            NextCommand = new RelayCommand(_ => Navigate(1));
            TodayCommand = new RelayCommand(_ =>
            {
                // 現在表示中の日付と今日を比較してアニメーション方向を決定
                if (CurrentDate < DateTime.Today)
                    TransitionDirection = TransitionDirection.Forward;
                else if (CurrentDate > DateTime.Today)
                    TransitionDirection = TransitionDirection.Backward;

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

            InitializeTimeLabels();
            UpdateVisibleDays();
            LoadData(); // Generate sample data first if needed
            LoadSettings(); // Add this missing call!
            LoadMemos(); // Load memos after data loading (though independent)
            UpdateMemoTextForCurrentWeek(); // Set initial memo text
            StartClock();

            _isInitialized = true;
        }

        private void Navigate(int amount)
        {
            TransitionDirection = amount > 0 ? TransitionDirection.Forward : TransitionDirection.Backward;

            if (CurrentViewMode == ViewMode.Day)
            {
                CurrentDate = CurrentDate.AddDays(amount);
            }
            else
            {
                CurrentDate = CurrentDate.AddDays(amount * 7);
            }
        }

        private void UpdateVisibleDays()
        {
            VisibleDays.Clear();
            if (CurrentViewMode == ViewMode.Day)
            {
                VisibleDays.Add(CurrentDate);
            }
            else
            {
                var start = CurrentWeekStart;
                for (int i = 0; i < 7; i++)
                {
                    VisibleDays.Add(start.AddDays(i));
                }
            }
        }

        private void StartClock()
        {
            CurrentTime = DateTime.Now;
            var timer = new DispatcherTimer
            {
                Interval = TimeSpan.FromSeconds(1)
            };
            timer.Tick += (s, e) => 
            {
                CurrentTime = DateTime.Now;
                if (IsRecording && RecordingStartTime.HasValue)
                {
                    RecordingDuration = CurrentTime - RecordingStartTime.Value;
                }
            };
            timer.Start();
        }

        private void InitializeTimeLabels()
        {
            TimeLabels.Clear();
            for (int i = _displayStartHour; i <= _displayEndHour; i++)
            {
                TimeLabels.Add($"{i}:00");
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
            // 表示用プロパティ以外が変更された場合に保存
            if (e.PropertyName != nameof(ScheduleItem.ColumnIndex) && 
                e.PropertyName != nameof(ScheduleItem.MaxColumnIndex))
            {
                SaveData();
            }
        }

        private readonly bool _isInitialized = false;

        private void SaveSettings()
        {
            if (!_isInitialized) return;

            var settings = new AppSettings
            {
                IsMemoPanelVisible = IsMemoPanelVisible,
                IsMemoEditMode = IsMemoEditMode,
                ViewMode = (int)CurrentViewMode,
                DisplayStartHour = _displayStartHour,
                DisplayEndHour = _displayEndHour
            };
            SettingsService.SaveSettings(settings);
        }

        private void ToggleRecording()
        {
            if (IsRecording)
            {
                // Stop Recording
                if (RecordingStartTime.HasValue)
                {
                    var endTime = DateTime.Now;
                    var startTime = RecordingStartTime.Value;
                    
                    var title = string.IsNullOrWhiteSpace(RecordingTitle) ? $"作業ログ {startTime:HH:mm}" : RecordingTitle;

                    var newItem = new ScheduleItem
                    {
                        Title = title,
                        Content = $"記録時間: {(endTime - startTime):hh\\:mm}",
                        StartTime = startTime,
                        EndTime = endTime,
                        BackgroundColor = Brushes.DarkOrange, // 記録用カラー
                        ColumnIndex = 0
                    };
                    
                    ScheduleItems.Add(newItem);
                    RecalculateLayout();
                }

                IsRecording = false;
                RecordingStartTime = null;
                RecordingDuration = TimeSpan.Zero;
                RecordingTitle = "";
            }
            else
            {
                // Start Recording
                // 入力ダイアログを表示
                var dialog = new SimpleTextInputDialog
                {
                    // メインウィンドウをオーナーにする（Application.Current.MainWindow）
                    Owner = System.Windows.Application.Current.MainWindow
                };

                if (dialog.ShowDialog() == true)
                {
                    RecordingTitle = dialog.InputText;
                    
                    IsRecording = true;
                    RecordingStartTime = DateTime.Now;
                    RecordingDuration = TimeSpan.Zero;
                }
            }
        }

        private static void UpdateRecordingCommandState()
        {
            // 必要に応じてコマンドの有効/無効を切り替える処理
            // ToggleRecordingCommand.RaiseCanExecuteChanged();
        }

        private void LoadSettings()
        {
            var settings = SettingsService.LoadSettings();
            if (settings != null)
            {
                _isMemoPanelVisible = settings.IsMemoPanelVisible;
                OnPropertyChanged(nameof(IsMemoPanelVisible));

                _isMemoEditMode = settings.IsMemoEditMode;
                OnPropertyChanged(nameof(IsMemoEditMode));

                // ViewModeの反映
                var newMode = (ViewMode)settings.ViewMode;
                _currentViewMode = newMode;
                OnPropertyChanged(nameof(CurrentViewMode));
                OnPropertyChanged(nameof(IsDayMode));
                OnPropertyChanged(nameof(IsWeekMode));
                OnPropertyChanged(nameof(DateDisplay));

                // 表示時間範囲の反映
                _displayStartHour = Math.Clamp(settings.DisplayStartHour, 0, 23);
                _displayEndHour = Math.Clamp(settings.DisplayEndHour, _displayStartHour + 1, 24);
                OnPropertyChanged(nameof(DisplayStartHour));
                OnPropertyChanged(nameof(DisplayEndHour));
                OnPropertyChanged(nameof(ScheduleGridHeight));
                InitializeTimeLabels();
                
                // ビューモード変更に伴い、表示日付を更新する
                UpdateVisibleDays();
            }
        }



        private void SaveData()
        {
            FilePersistenceService.SaveData(ScheduleItems);
        }

        private void LoadData()
        {
            var items = FilePersistenceService.LoadData();
            ScheduleItems.Clear();
            foreach (var item in items)
            {
                ScheduleItems.Add(item);
            }
            
            // レイアウト再計算（読み込み後）
            RecalculateLayout();
        }

        private void SaveMemos()
        {
            FilePersistenceService.SaveMemos(_weeklyMemos);
        }

        private void LoadMemos()
        {
            _weeklyMemos = FilePersistenceService.LoadMemos();
        }

        private void RecalculateLayout()
        {
            // まずUI用コレクションをクリア
            StandardItems.Clear();
            AllDayItems.Clear();

            // 振り分け
            foreach (var item in ScheduleItems)
            {
                if (item.IsAllDay)
                {
                    // 終日イベントも日付ごとの位置計算が必要かもしれないが、
                    // コンバーターで行うため、ここではColumnIndexは使わない（常に0でよい、あるいは重なり計算するか）
                    // 今回はシンプルに重ねずリスト表示するなら計算不要だが、
                    // 横軸（日付）の位置合わせは必要。
                    // DateToPagePositionConverter は ColumnIndex=0 で動作するはず。
                    AllDayItems.Add(item);
                }
                else
                {
                    StandardItems.Add(item);
                }
            }

            // 終日イベントの重なり計算（簡易版：日ごとにスタック）
            var allDayGrouped = AllDayItems.GroupBy(x => x.StartTime.Date);
            int maxStackIndex = 0;

            foreach (var group in allDayGrouped)
            {
                int index = 0;
                // 日付ごとに、タイトル順などでソートしてインデックスを振る
                foreach (var item in group.OrderBy(x => x.Title)) // Title順などで安定させる
                {
                    item.ColumnIndex = index;
                    index++;
                }
                if (index > maxStackIndex) maxStackIndex = index;
            }

            // パネルの高さを計算 (1行あたり26px程度 + マージン)
            // アイテム高さ22 + マージン2 = 24pxピッチ + 余白
            AllDayPanelHeight = Math.Max(30, (maxStackIndex * 24) + 6);

            // 通常イベントの重なり計算
            var grouped = StandardItems.GroupBy(x => x.StartTime.Date);

            foreach (var group in grouped)
            {
                var sortedItems = group.OrderBy(x => x.StartTime).ThenByDescending(x => x.EndTime).ToList();
                ScheduleLayoutHelper.CalculateClustersAndAssignColumns(sortedItems);
            }
        }

        public event PropertyChangedEventHandler? PropertyChanged;
        protected void OnPropertyChanged([CallerMemberName] string? propertyName = null)
        {
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
        }
    }
}
