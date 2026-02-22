using System.Windows;
using System.Windows.Media;
using Brush = System.Windows.Media.Brush;
using Brushes = System.Windows.Media.Brushes;
using MessageBox = System.Windows.MessageBox;

namespace TimeRenderer
{
    /// <summary>
    /// スケジュールアイテムの追加・編集用ダイアログ。
    /// </summary>
    public partial class ScheduleEditDialog : Window
    {
        /// <summary>
        /// 色選択肢を表すヘルパークラス
        /// </summary>
        public class ColorOption
        {
            public string Name { get; set; } = string.Empty;
            public Brush Brush { get; set; } = Brushes.White;
        }

        /// <summary>
        /// 編集対象のスケジュールアイテム（ダイアログ結果）
        /// </summary>
        public ScheduleItem? ResultItem { get; private set; }

        private readonly List<ColorOption> _colorOptions;

        /// <summary>
        /// コンストラクタ。既存アイテムを渡すと編集モード、nullなら新規追加モード。
        /// </summary>
        public ScheduleEditDialog(ScheduleItem? existingItem = null)
        {
            InitializeComponent();

            // 色の選択肢を初期化
            _colorOptions =
            [
                new() { Name = "ライトブルー", Brush = Brushes.LightBlue },
                new() { Name = "ライトグリーン", Brush = Brushes.LightGreen },
                new() { Name = "ライトピンク", Brush = Brushes.LightPink },
                new() { Name = "ライトイエロー", Brush = Brushes.LightYellow },
                new() { Name = "ライトグレー", Brush = Brushes.LightGray },
                new() { Name = "ライトコーラル", Brush = Brushes.LightCoral },
                new() { Name = "ラベンダー", Brush = Brushes.Lavender },
                new() { Name = "ライトシアン", Brush = Brushes.LightCyan },
            ];
            ColorCombo.ItemsSource = _colorOptions;

            // 時間コンボボックスを初期化（0〜23時、0〜55分を5分刻み）
            StartHourCombo.ItemsSource = Enumerable.Range(0, 24).Select(h => h.ToString("D2")).ToList();
            EndHourCombo.ItemsSource = Enumerable.Range(0, 24).Select(h => h.ToString("D2")).ToList();
            var minutes = Enumerable.Range(0, 12).Select(m => (m * 5).ToString("D2")).ToList();
            StartMinuteCombo.ItemsSource = minutes;
            EndMinuteCombo.ItemsSource = minutes;

            if (existingItem != null)
            {
                // 編集モード：既存値をフォームに設定
                TitleTextBox.Text = existingItem.Title;
                ContentTextBox.Text = existingItem.Content;
                DatePicker.SelectedDate = existingItem.StartTime.Date;
                AllDayCheckBox.IsChecked = existingItem.IsAllDay;

                StartHourCombo.SelectedItem = existingItem.StartTime.Hour.ToString("D2");
                StartMinuteCombo.SelectedItem = (existingItem.StartTime.Minute / 5 * 5).ToString("D2");
                EndHourCombo.SelectedItem = existingItem.EndTime.Hour.ToString("D2");
                EndMinuteCombo.SelectedItem = (existingItem.EndTime.Minute / 5 * 5).ToString("D2");

                // 色を選択
                var matchingColor = _colorOptions.FirstOrDefault(c => c.Brush.ToString() == existingItem.BackgroundColor.ToString());
                ColorCombo.SelectedItem = matchingColor ?? _colorOptions[0];
            }
            else
            {
                // 新規モード：デフォルト値を設定
                var now = DateTime.Now;
                DatePicker.SelectedDate = now.Date;
                AllDayCheckBox.IsChecked = false;
                
                StartHourCombo.SelectedItem = now.Hour.ToString("D2");
                StartMinuteCombo.SelectedItem = (now.Minute / 5 * 5).ToString("D2");
                var endHour = (now.Hour + 1) % 24;
                EndHourCombo.SelectedItem = endHour.ToString("D2");
                EndMinuteCombo.SelectedItem = (now.Minute / 5 * 5).ToString("D2");
                ColorCombo.SelectedItem = _colorOptions[0];
            }

            UpdateTimePanelState();
        }

        private void OkButton_Click(object sender, RoutedEventArgs e)
        {
            // バリデーション
            if (string.IsNullOrWhiteSpace(TitleTextBox.Text))
            {
                MessageBox.Show("タイトルを入力してください。", "入力エラー", MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }

            if (DatePicker.SelectedDate == null)
            {
                MessageBox.Show("日付を選択してください。", "入力エラー", MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }

            var date = DatePicker.SelectedDate.Value;
            var isAllDay = AllDayCheckBox.IsChecked ?? false;
            DateTime startTime;
            DateTime endTime;

            if (isAllDay)
            {
                // 終日の場合、時間は 0:00 - 翌0:00 とする
                startTime = date.Date;
                endTime = date.Date.AddDays(1);
            }
            else
            {
                if (StartHourCombo.SelectedItem == null || StartMinuteCombo.SelectedItem == null ||
                    EndHourCombo.SelectedItem == null || EndMinuteCombo.SelectedItem == null)
                {
                    MessageBox.Show("時刻を選択してください。", "入力エラー", MessageBoxButton.OK, MessageBoxImage.Warning);
                    return;
                }

                var startHour = int.Parse((string)StartHourCombo.SelectedItem);
                var startMinute = int.Parse((string)StartMinuteCombo.SelectedItem);
                var endHour = int.Parse((string)EndHourCombo.SelectedItem);
                var endMinute = int.Parse((string)EndMinuteCombo.SelectedItem);

                startTime = date.AddHours(startHour).AddMinutes(startMinute);
                endTime = date.AddHours(endHour).AddMinutes(endMinute);

                // 終了時刻が開始時刻より前の場合、翌日とする
                if (endTime <= startTime)
                {
                    // 明らかに開始より終了が早い（例: 23:00 -> 01:00）場合は翌日とみなす
                    // 単に同じ時刻等の場合はエラーにする手もあるが、ここでは翌日扱いが自然
                    endTime = endTime.AddDays(1);
                }
            }

            var selectedColor = (ColorOption?)ColorCombo.SelectedItem;

            ResultItem = new ScheduleItem
            {
                Title = TitleTextBox.Text.Trim(),
                Content = ContentTextBox.Text.Trim(),
                StartTime = startTime,
                EndTime = endTime,
                IsAllDay = isAllDay,
                BackgroundColor = selectedColor?.Brush ?? Brushes.LightBlue
            };

            DialogResult = true;
            Close();
        }

        private void CancelButton_Click(object sender, RoutedEventArgs e)
        {
            DialogResult = false;
            Close();
        }

        private void AllDayCheckBox_Changed(object sender, RoutedEventArgs e)
        {
            UpdateTimePanelState();
        }

        private void UpdateTimePanelState()
        {
            // チェックボックスの状態に応じて時刻入力パネルの有効/無効を切り替え
            bool isTimeEnabled = !(AllDayCheckBox.IsChecked ?? false);
            StartTimePanel?.IsEnabled = isTimeEnabled;
            EndTimePanel?.IsEnabled = isTimeEnabled;
        }
    }
}
