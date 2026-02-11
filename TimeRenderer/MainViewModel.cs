using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Runtime.CompilerServices;
using System.Windows.Input;
using System.Windows.Media;
using Brushes = System.Windows.Media.Brushes;
using System.Windows.Threading;
using System.Linq;
using System.Collections.Generic;
using System.IO;
using System.Text.Json;

namespace TimeRenderer
{
    public class MainViewModel : INotifyPropertyChanged
    {
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
                    SaveSettings(); // 変更ごとに保存（頻度が高い場合はデバウンス検討）
                }
            }
        }

        public ICommand ToggleMemoPanelCommand { get; }
        public ICommand ToggleRecordingCommand { get; }

        public ObservableCollection<ScheduleItem> ScheduleItems { get; set; }
        public ObservableCollection<string> TimeLabels { get; set; }
        
        // 表示中の日付リスト（ヘッダー用）
        public ObservableCollection<DateTime> VisibleDays { get; set; }

        private static readonly JsonSerializerOptions _jsonOptions = new() { WriteIndented = true };

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
                    _currentDate = value;
                    OnPropertyChanged();
                    UpdateVisibleDays();
                    OnPropertyChanged(nameof(CurrentWeekStart));
                    OnPropertyChanged(nameof(DateDisplay));
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

        public MainViewModel()
        {
            ToggleMemoPanelCommand = new RelayCommand(_ => IsMemoPanelVisible = !IsMemoPanelVisible);
            LoadSettings();



            ScheduleItems = [];
            ScheduleItems.CollectionChanged += OnScheduleItemsChanged;

            StandardItems = [];
            AllDayItems = [];

            TimeLabels = [];
            VisibleDays = [];

            CurrentDate = DateTime.Today;
            CurrentViewMode = ViewMode.Day; // 初期は日表示

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
            TodayCommand = new RelayCommand(_ => CurrentDate = DateTime.Today);
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
            LoadData();
            StartClock();
        }

        private void Navigate(int amount)
        {
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
            for (int i = 0; i <= 24; i++)
            {
                TimeLabels.Add($"{i}:00");
            }
        }

        private void LoadSampleData()
        {
            // 今日を基準にサンプルデータを作成
            var baseDate = DateTime.Today;
            var baseTime = baseDate.AddHours(9); 

            ScheduleItems.Add(new ScheduleItem
            {
                Title = "朝会",
                StartTime = baseTime,
                EndTime = baseTime.AddMinutes(30),
                Content = "定例",
                BackgroundColor = Brushes.LightBlue
            });

            // 明日の予定（週間表示確認用）
            var tomorrowBase = baseDate.AddDays(1).AddHours(14);
            ScheduleItems.Add(new ScheduleItem
            {
                Title = "週次レビュー",
                StartTime = tomorrowBase,
                EndTime = tomorrowBase.AddHours(1.5),
                Content = "進捗確認",
                BackgroundColor = Brushes.LightGreen
            });
            
            // 昨日の予定
            var yesterdayBase = baseDate.AddDays(-1).AddHours(10);
            ScheduleItems.Add(new ScheduleItem
            {
                Title = "顧客訪問",
                StartTime = yesterdayBase,
                EndTime = yesterdayBase.AddHours(2),
                Content = "直行",
                BackgroundColor = Brushes.LightPink
            });

            // 重なりテスト用
            ScheduleItems.Add(new ScheduleItem
            {
                Title = "重複会議A",
                StartTime = baseTime.AddHours(1),
                EndTime = baseTime.AddHours(2),
                Content = "重複テスト",
                BackgroundColor = Brushes.Orange
            });
            ScheduleItems.Add(new ScheduleItem
            {
                Title = "重複会議B",
                StartTime = baseTime.AddHours(1.5),
                EndTime = baseTime.AddHours(2.5),
                Content = "重複テスト",
                BackgroundColor = Brushes.Purple
            });
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

        private void SaveSettings()
        {
            try
            {
                var settings = new AppSettings
                {
                    IsMemoPanelVisible = IsMemoPanelVisible,
                    IsMemoEditMode = IsMemoEditMode,
                    MemoText = MemoText
                };
                var jsonString = JsonSerializer.Serialize(settings, _jsonOptions);
                 File.WriteAllText("appsettings.json", jsonString);
             }
             catch (Exception ex)
             {
                 System.Diagnostics.Debug.WriteLine($"Save settings failed: {ex.Message}");
             }
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
            if (File.Exists("appsettings.json"))
            {
                try
                {
                    var jsonString = File.ReadAllText("appsettings.json");
                    var settings = JsonSerializer.Deserialize<AppSettings>(jsonString);
                    if (settings != null)
                    {
                        // プロパティセット時にSaveSettingsが走らないようにフィールドにセットするか、ロード中はフラグを立てる等の対策が必要だが、
                        // ここでは簡易的にプロパティを通してセットし、即保存されても問題ないとする（無駄だが害はない）。
                        // より良くするならバッキングフィールドにセットしてOnPropertyChangedだけ呼ぶ。
                        _isMemoPanelVisible = settings.IsMemoPanelVisible;
                        OnPropertyChanged(nameof(IsMemoPanelVisible));

                        _memoText = settings.MemoText;
                        OnPropertyChanged(nameof(MemoText));

                        _isMemoEditMode = settings.IsMemoEditMode;
                        OnPropertyChanged(nameof(IsMemoEditMode));
                    }
                }
                catch (Exception ex)
                {
                    System.Diagnostics.Debug.WriteLine($"Load settings failed: {ex.Message}");
                }
            }
        }



        private void SaveData()
        {
            try
            {
                var jsonString = JsonSerializer.Serialize(ScheduleItems, _jsonOptions);
                File.WriteAllText("schedules.json", jsonString);
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"Save failed: {ex.Message}");
            }
        }

        private void LoadData()
        {
            if (File.Exists("schedules.json"))
            {
                try
                {
                    var jsonString = File.ReadAllText("schedules.json");
                    var items = JsonSerializer.Deserialize<ObservableCollection<ScheduleItem>>(jsonString);
                    if (items != null)
                    {
                        ScheduleItems.Clear();
                        foreach (var item in items)
                        {
                            ScheduleItems.Add(item);
                        }
                    }
                }
                catch (Exception ex)
                {
                    System.Diagnostics.Debug.WriteLine($"Load failed: {ex.Message}");
                    LoadSampleData(); // 読み込み失敗時はサンプルデータ
                }
            }
            else
            {
                LoadSampleData();
            }
            
            // レイアウト再計算（読み込み後）
            RecalculateLayout();
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
                CalculateClustersAndAssignColumns(sortedItems);
            }
        }

        private static void CalculateClustersAndAssignColumns(List<ScheduleItem> sortedItems)
        {
            if (sortedItems.Count == 0) return;

            var clusters = new List<List<ScheduleItem>>();
            var currentCluster = new List<ScheduleItem>();
            DateTime clusterEndTime = DateTime.MinValue;

            foreach (var item in sortedItems)
            {
                if (currentCluster.Count == 0)
                {
                    currentCluster.Add(item);
                    clusterEndTime = item.EndTime;
                }
                else
                {
                    if (item.StartTime < clusterEndTime)
                    {
                        currentCluster.Add(item);
                        if (item.EndTime > clusterEndTime) clusterEndTime = item.EndTime;
                    }
                    else
                    {
                        clusters.Add(currentCluster);
                        currentCluster = [item];
                        clusterEndTime = item.EndTime;
                    }
                }
            }
            if (currentCluster.Count > 0) clusters.Add(currentCluster);

            foreach (var cluster in clusters)
            {
                AssignColumnsToCluster(cluster);
            }
        }

        private static void AssignColumnsToCluster(List<ScheduleItem> cluster)
        {
            var columnEndTimes = new List<DateTime>();
            foreach (var item in cluster)
            {
                int assignedColumn = -1;
                for (int i = 0; i < columnEndTimes.Count; i++)
                {
                    if (columnEndTimes[i] <= item.StartTime)
                    {
                        assignedColumn = i;
                        columnEndTimes[i] = item.EndTime;
                        break;
                    }
                }
                if (assignedColumn == -1)
                {
                    assignedColumn = columnEndTimes.Count;
                    columnEndTimes.Add(item.EndTime);
                }
                item.ColumnIndex = assignedColumn;
            }

            int maxColIndex = columnEndTimes.Count - 1;
            foreach (var item in cluster)
            {
                item.MaxColumnIndex = maxColIndex;
            }
        }
        public event PropertyChangedEventHandler? PropertyChanged;
        protected void OnPropertyChanged([CallerMemberName] string? propertyName = null)
        {
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
        }
    }
}
