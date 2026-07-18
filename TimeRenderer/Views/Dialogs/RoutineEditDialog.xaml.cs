using System.Windows;
using Brush = System.Windows.Media.Brush;
using Brushes = System.Windows.Media.Brushes;
using MessageBox = System.Windows.MessageBox;

using TimeRenderer.Models;

namespace TimeRenderer.Views.Dialogs
{
    /// <summary>
    /// 定期予定（ルーティン）の追加・編集用ダイアログ。
    /// 指定した曜日・時刻の予定を毎週自動生成するテンプレートを作成する。
    /// </summary>
    public partial class RoutineEditDialog : Window
    {
        /// <summary>
        /// 色選択肢を表すヘルパークラス
        /// </summary>
        public record ColorOption(string Name, Brush Brush, string? CategoryId);

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

                MonCheck.IsChecked = existingRoutine.DaysOfWeek.Contains(DayOfWeek.Monday);
                TueCheck.IsChecked = existingRoutine.DaysOfWeek.Contains(DayOfWeek.Tuesday);
                WedCheck.IsChecked = existingRoutine.DaysOfWeek.Contains(DayOfWeek.Wednesday);
                ThuCheck.IsChecked = existingRoutine.DaysOfWeek.Contains(DayOfWeek.Thursday);
                FriCheck.IsChecked = existingRoutine.DaysOfWeek.Contains(DayOfWeek.Friday);
                SatCheck.IsChecked = existingRoutine.DaysOfWeek.Contains(DayOfWeek.Saturday);
                SunCheck.IsChecked = existingRoutine.DaysOfWeek.Contains(DayOfWeek.Sunday);

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
                // 新規モード：デフォルト値を設定
                var now = DateTime.Now;
                StartHourCombo.SelectedItem = now.Hour.ToString("D2");
                StartMinuteCombo.SelectedItem = (now.Minute / 5 * 5).ToString("D2");
                var endHour = (now.Hour + 1) % 24;
                EndHourCombo.SelectedItem = endHour.ToString("D2");
                EndMinuteCombo.SelectedItem = (now.Minute / 5 * 5).ToString("D2");
                ColorCombo.SelectedItem = _colorOptions[0];
                EnabledCheckBox.IsChecked = true;
            }
        }

        private void OkButton_Click(object sender, RoutedEventArgs e)
        {
            // バリデーション
            if (string.IsNullOrWhiteSpace(TitleCombo.Text))
            {
                MessageBox.Show("タイトルを入力してください。", "入力エラー", MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }

            var days = new List<DayOfWeek>();
            if (MonCheck.IsChecked == true) days.Add(DayOfWeek.Monday);
            if (TueCheck.IsChecked == true) days.Add(DayOfWeek.Tuesday);
            if (WedCheck.IsChecked == true) days.Add(DayOfWeek.Wednesday);
            if (ThuCheck.IsChecked == true) days.Add(DayOfWeek.Thursday);
            if (FriCheck.IsChecked == true) days.Add(DayOfWeek.Friday);
            if (SatCheck.IsChecked == true) days.Add(DayOfWeek.Saturday);
            if (SunCheck.IsChecked == true) days.Add(DayOfWeek.Sunday);

            if (days.Count == 0)
            {
                MessageBox.Show("曜日を1つ以上選択してください。", "入力エラー", MessageBoxButton.OK, MessageBoxImage.Warning);
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
                DaysOfWeek = days,
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
