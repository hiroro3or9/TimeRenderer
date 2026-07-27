using System.Windows;
using Brush = System.Windows.Media.Brush;
using Brushes = System.Windows.Media.Brushes;
using ComboBox = System.Windows.Controls.ComboBox;
using MessageBox = System.Windows.MessageBox;
using SelectionChangedEventArgs = System.Windows.Controls.SelectionChangedEventArgs;

using TimeRenderer.Models;

namespace TimeRenderer.Views.Dialogs
{
    /// <summary>
    /// 定期予定（ルーティン）の追加・編集用ダイアログ。
    /// 曜日指定（毎週・N週ごと）または日付指定（毎月・Nヶ月ごと）で
    /// 予定を自動生成するテンプレートを作成する。
    /// </summary>
    public partial class RoutineEditDialog : Window
    {
        /// <summary>
        /// 色選択肢を表すヘルパークラス
        /// </summary>
        public record ColorOption(string Name, Brush Brush, string? CategoryId);

        /// <summary>
        /// 繰り返し種別・間隔・日付のコンボボックス項目（表示名と値の対）
        /// </summary>
        public record OptionItem(string Label, int Value);

        /// <summary>選択できる週の間隔（1=毎週, 2=隔週, …）</summary>
        private static readonly int[] WeekIntervals = [1, 2, 3, 4, 6, 8];

        /// <summary>選択できる月の間隔（1=毎月, 2=隔月, …）</summary>
        private static readonly int[] MonthIntervals = [1, 2, 3, 4, 6, 12];

        /// <summary>
        /// 編集対象の定期予定（ダイアログ結果）
        /// </summary>
        public RoutineScheduleItem? ResultRoutine { get; private set; }

        private readonly List<ColorOption> _colorOptions;
        private readonly string _routineId;

        /// <summary>
        /// コンストラクタ。既存のルーティンを渡すと編集モード、nullなら新規追加モード。
        /// </summary>
        /// <param name="categories">選択可能なカテゴリ一覧（null/空の場合は既定値を使用）</param>
        /// <param name="titleSuggestions">タイトル入力欄のドロップダウン候補</param>
        public RoutineEditDialog(RoutineScheduleItem? existingRoutine = null, IReadOnlyList<CategoryInfo>? categories = null, IReadOnlyList<string>? titleSuggestions = null)
        {
            InitializeComponent();

            _routineId = existingRoutine?.Id ?? Guid.NewGuid().ToString("N");

            TitleCombo.ItemsSource = titleSuggestions ?? [];

            // カテゴリ（名前付きの色）の選択肢を初期化
            List<CategoryInfo> source = (categories == null || categories.Count == 0)
                ? CategoryInfo.CreateDefaults()
                : [.. categories];
            _colorOptions = [.. source.Select(c => new ColorOption(c.Name, c.Brush, c.Id))];
            ColorCombo.ItemsSource = _colorOptions;

            // 繰り返し種別・間隔・日付の選択肢を初期化
            RecurrenceCombo.ItemsSource = new List<OptionItem>
            {
                new("毎週（曜日で指定）", (int)RecurrenceType.Weekly),
                new("毎月（日付で指定）", (int)RecurrenceType.MonthlyByDate),
            };
            WeekIntervalCombo.ItemsSource = WeekIntervals.Select(i => new OptionItem(WeekIntervalLabel(i), i)).ToList();
            MonthIntervalCombo.ItemsSource = MonthIntervals.Select(i => new OptionItem(MonthIntervalLabel(i), i)).ToList();
            DayOfMonthCombo.ItemsSource = Enumerable.Range(1, 31)
                .Select(d => new OptionItem(d == 31 ? "31日（末日）" : $"{d}日", d)).ToList();

            // 時間コンボボックスを初期化（0〜23時、0〜55分を5分刻み）
            StartHourCombo.ItemsSource = Enumerable.Range(0, 24).Select(h => h.ToString("D2")).ToList();
            EndHourCombo.ItemsSource = Enumerable.Range(0, 24).Select(h => h.ToString("D2")).ToList();
            var minuteOptions = Enumerable.Range(0, 12).Select(m => (m * 5).ToString("D2")).ToList();
            StartMinuteCombo.ItemsSource = minuteOptions;
            EndMinuteCombo.ItemsSource = minuteOptions;

            if (existingRoutine != null)
            {
                // 編集モード：既存値をフォームに設定
                TitleCombo.Text = existingRoutine.Title;

                SelectOption(RecurrenceCombo, (int)existingRoutine.Recurrence);
                SelectOption(WeekIntervalCombo, existingRoutine.Interval);
                SelectOption(MonthIntervalCombo, existingRoutine.Interval);
                SelectOption(DayOfMonthCombo, existingRoutine.DayOfMonth);

                MonCheck.IsChecked = existingRoutine.DaysOfWeek.Contains(DayOfWeek.Monday);
                TueCheck.IsChecked = existingRoutine.DaysOfWeek.Contains(DayOfWeek.Tuesday);
                WedCheck.IsChecked = existingRoutine.DaysOfWeek.Contains(DayOfWeek.Wednesday);
                ThuCheck.IsChecked = existingRoutine.DaysOfWeek.Contains(DayOfWeek.Thursday);
                FriCheck.IsChecked = existingRoutine.DaysOfWeek.Contains(DayOfWeek.Friday);
                SatCheck.IsChecked = existingRoutine.DaysOfWeek.Contains(DayOfWeek.Saturday);
                SunCheck.IsChecked = existingRoutine.DaysOfWeek.Contains(DayOfWeek.Sunday);

                // 旧データ（開始日なし）は当日を初期値にする
                StartDatePicker.SelectedDate = existingRoutine.StartDate == default
                    ? DateTime.Today
                    : existingRoutine.StartDate.Date;

                StartHourCombo.SelectedItem = existingRoutine.StartTime.Hours.ToString("D2");
                StartMinuteCombo.SelectedItem = (existingRoutine.StartTime.Minutes / 5 * 5).ToString("D2");
                EndHourCombo.SelectedItem = existingRoutine.EndTime.Hours.ToString("D2");
                EndMinuteCombo.SelectedItem = (existingRoutine.EndTime.Minutes / 5 * 5).ToString("D2");

                // カテゴリを選択（ID一致を優先し、旧データは色一致でフォールバック）
                var matchingColor =
                    (existingRoutine.CategoryId != null
                        ? _colorOptions.FirstOrDefault(c => c.CategoryId == existingRoutine.CategoryId)
                        : null)
                    ?? _colorOptions.FirstOrDefault(c => c.Brush.ToString() == existingRoutine.ColorCode);
                ColorCombo.SelectedItem = matchingColor ?? _colorOptions[0];

                AutoStartCheckBox.IsChecked = existingRoutine.IsAutoStart;
                ForceStartCheckBox.IsChecked = existingRoutine.IsForceStart;
                EnabledCheckBox.IsChecked = existingRoutine.IsEnabled;
            }
            else
            {
                // 新規モード：デフォルト値を設定（開始日は作成日＝当日）
                var now = DateTime.Now;
                SelectOption(RecurrenceCombo, (int)RecurrenceType.Weekly);
                SelectOption(WeekIntervalCombo, 1);
                SelectOption(MonthIntervalCombo, 1);
                SelectOption(DayOfMonthCombo, now.Day);
                StartDatePicker.SelectedDate = now.Date;
                StartHourCombo.SelectedItem = now.Hour.ToString("D2");
                StartMinuteCombo.SelectedItem = (now.Minute / 5 * 5).ToString("D2");
                var endHour = (now.Hour + 1) % 24;
                EndHourCombo.SelectedItem = endHour.ToString("D2");
                EndMinuteCombo.SelectedItem = (now.Minute / 5 * 5).ToString("D2");
                ColorCombo.SelectedItem = _colorOptions[0];
                EnabledCheckBox.IsChecked = true;
            }

            UpdateRecurrencePanels();
        }

        private static string WeekIntervalLabel(int interval) => interval switch
        {
            1 => "毎週",
            2 => "隔週（2週ごと）",
            _ => $"{interval}週ごと",
        };

        private static string MonthIntervalLabel(int interval) => interval switch
        {
            1 => "毎月",
            2 => "隔月（2ヶ月ごと）",
            _ => $"{interval}ヶ月ごと",
        };

        /// <summary>値が一致する選択肢を選ぶ。一致するものが無ければ先頭を選ぶ</summary>
        private static void SelectOption(ComboBox combo, int value)
        {
            var items = (List<OptionItem>)combo.ItemsSource;
            combo.SelectedItem = items.FirstOrDefault(o => o.Value == value) ?? items[0];
        }

        private static int SelectedValue(ComboBox combo) => ((OptionItem)combo.SelectedItem).Value;

        private void RecurrenceCombo_SelectionChanged(object sender, SelectionChangedEventArgs e)
            => UpdateRecurrencePanels();

        /// <summary>選択中の繰り返し種別に応じて、曜日指定／日付指定の入力欄を切り替える</summary>
        private void UpdateRecurrencePanels()
        {
            // ItemsSource 設定に伴う選択変更でも呼ばれるため、未初期化の状態を避ける
            if (WeeklyPanel == null || MonthlyPanel == null || RecurrenceCombo.SelectedItem == null) return;

            var isMonthly = SelectedValue(RecurrenceCombo) == (int)RecurrenceType.MonthlyByDate;
            WeeklyPanel.Visibility = isMonthly ? Visibility.Collapsed : Visibility.Visible;
            MonthlyPanel.Visibility = isMonthly ? Visibility.Visible : Visibility.Collapsed;
        }

        private void OkButton_Click(object sender, RoutedEventArgs e)
        {
            // バリデーション
            if (string.IsNullOrWhiteSpace(TitleCombo.Text))
            {
                MessageBox.Show("タイトルを入力してください。", "入力エラー", MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }

            var recurrence = (RecurrenceType)SelectedValue(RecurrenceCombo);
            var isMonthly = recurrence == RecurrenceType.MonthlyByDate;

            var days = new List<DayOfWeek>();
            if (MonCheck.IsChecked == true) days.Add(DayOfWeek.Monday);
            if (TueCheck.IsChecked == true) days.Add(DayOfWeek.Tuesday);
            if (WedCheck.IsChecked == true) days.Add(DayOfWeek.Wednesday);
            if (ThuCheck.IsChecked == true) days.Add(DayOfWeek.Thursday);
            if (FriCheck.IsChecked == true) days.Add(DayOfWeek.Friday);
            if (SatCheck.IsChecked == true) days.Add(DayOfWeek.Saturday);
            if (SunCheck.IsChecked == true) days.Add(DayOfWeek.Sunday);

            // 日付指定のときは曜日を使わないため、未選択でもよい
            if (!isMonthly && days.Count == 0)
            {
                MessageBox.Show("曜日を1つ以上選択してください。", "入力エラー", MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }

            if (StartDatePicker.SelectedDate == null)
            {
                MessageBox.Show("開始日を選択してください。", "入力エラー", MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }

            if (StartHourCombo.SelectedItem == null || StartMinuteCombo.SelectedItem == null ||
                EndHourCombo.SelectedItem == null || EndMinuteCombo.SelectedItem == null)
            {
                MessageBox.Show("時刻を選択してください。", "入力エラー", MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }

            var startTime = new TimeSpan(
                int.Parse((string)StartHourCombo.SelectedItem),
                int.Parse((string)StartMinuteCombo.SelectedItem),
                0);
            var endTime = new TimeSpan(
                int.Parse((string)EndHourCombo.SelectedItem),
                int.Parse((string)EndMinuteCombo.SelectedItem),
                0);

            if (endTime <= startTime)
            {
                MessageBox.Show("終了時刻は開始時刻より後にしてください。", "入力エラー", MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }

            var selectedColor = (ColorOption?)ColorCombo.SelectedItem;

            ResultRoutine = new RoutineScheduleItem
            {
                Id = _routineId,
                Title = TitleCombo.Text.Trim(),
                Recurrence = recurrence,
                Interval = isMonthly ? SelectedValue(MonthIntervalCombo) : SelectedValue(WeekIntervalCombo),
                DaysOfWeek = days,
                DayOfMonth = SelectedValue(DayOfMonthCombo),
                StartDate = StartDatePicker.SelectedDate.Value.Date,
                StartTime = startTime,
                EndTime = endTime,
                ColorCode = selectedColor?.Brush.ToString() ?? Brushes.Lavender.ToString(),
                CategoryId = selectedColor?.CategoryId,
                IsAutoStart = AutoStartCheckBox.IsChecked ?? false,
                IsForceStart = (AutoStartCheckBox.IsChecked ?? false) && (ForceStartCheckBox.IsChecked ?? false),
                IsEnabled = EnabledCheckBox.IsChecked ?? true
            };

            DialogResult = true;
            Close();
        }

        private void CancelButton_Click(object sender, RoutedEventArgs e) => DialogResult = false;
    }
}
