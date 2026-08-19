using System.Windows;
using System.Windows.Media;
using Brush = System.Windows.Media.Brush;
using Brushes = System.Windows.Media.Brushes;
using MessageBox = System.Windows.MessageBox;

using TimeRenderer.Models;

namespace TimeRenderer.Views.Dialogs
{
    /// <summary>
    /// スケジュールアイテムの追加・編集用ダイアログ。
    /// </summary>
    public partial class ScheduleEditDialog : Window
    {
        /// <summary>
        /// 色選択肢を表すヘルパークラス
        /// </summary>
        public record ColorOption(string Name, Brush Brush, string? CategoryId);
        public record ProjectCodeOption(string DisplayName, string? ProjectCodeId);

        /// <summary>
        /// 編集対象のスケジュールアイテム（ダイアログ結果）
        /// </summary>
        public ScheduleItem? ResultItem { get; private set; }

        private readonly List<ColorOption> _colorOptions;
        private readonly List<ProjectCodeOption> _projectCodeOptions;
        private readonly bool _isEditMode;

        private ScheduleItemKind SelectedKind => RecordedKindRadio?.IsChecked == true
            ? ScheduleItemKind.Recorded
            : ScheduleItemKind.Planned;

        // 編集前の時刻（5分丸め・秒消失を防ぐため、変更がなければ元の値をそのまま使う）
        private readonly DateTime? _originalStartTime;
        private readonly DateTime? _originalEndTime;

        /// <summary>
        /// 5分刻みの分選択肢を生成する。既存アイテムの分が5分刻みでない場合はその値も追加する
        /// （記録機能で作られた任意の分を編集時に勝手に丸めないため）。
        /// </summary>
        private static List<string> BuildMinuteOptions(int? exactMinute = null)
        {
            var minutes = Enumerable.Range(0, 12).Select(m => m * 5).ToList();
            if (exactMinute.HasValue && !minutes.Contains(exactMinute.Value))
            {
                minutes.Add(exactMinute.Value);
                minutes.Sort();
            }
            return [.. minutes.Select(m => m.ToString("D2"))];
        }

        /// <summary>
        /// 日付＋時＋分から時刻を組み立てる。時・分が編集前と同じ場合は元の値（秒を含む）を維持する。
        /// </summary>
        private static DateTime ComposeDateTime(DateTime date, int hour, int minute, DateTime? original)
        {
            if (original.HasValue && original.Value.Hour == hour && original.Value.Minute == minute)
            {
                return date.Date.Add(original.Value.TimeOfDay);
            }
            return date.Date.AddHours(hour).AddMinutes(minute);
        }

        /// <summary>
        /// コンストラクタ。既存アイテムを渡すと編集モード、nullなら新規追加モード。
        /// </summary>
        /// <param name="categories">カテゴリ一覧（null/空の場合は既定値を使用）</param>
        /// <param name="titleSuggestions">タイトル入力欄のドロップダウン候補（定型＋直近1か月）</param>
        public ScheduleEditDialog(
            ScheduleItem? existingItem = null,
            IReadOnlyList<CategoryInfo>? categories = null,
            IReadOnlyList<string>? titleSuggestions = null,
            IReadOnlyList<ProjectCodeInfo>? projectCodes = null,
            ProjectCodeInfo? defaultProjectCode = null)
        {
            _isEditMode = existingItem != null;
            InitializeComponent();

            // タイトル候補（手入力も可能な編集可能コンボボックス）
            TitleCombo.ItemsSource = titleSuggestions ?? [];

            if (existingItem != null)
            {
                _originalStartTime = existingItem.StartTime;
                _originalEndTime = existingItem.EndTime;
            }

            // カテゴリ（名前付きの色）の選択肢を初期化
            List<CategoryInfo> source = (categories == null || categories.Count == 0)
                ? CategoryInfo.CreateDefaults()
                : [.. categories];
            _colorOptions = [.. source.Select(c => new ColorOption(c.Name, c.Brush, c.Id))];

            // 既存アイテムがどのカテゴリにも一致しない場合、その色も選択肢として残す
            if (existingItem != null &&
                _colorOptions.All(c => c.CategoryId != existingItem.CategoryId || existingItem.CategoryId == null) &&
                _colorOptions.All(c => c.Brush.ToString() != existingItem.BackgroundColor.ToString()))
            {
                _colorOptions.Add(new ColorOption("（現在の色）", existingItem.BackgroundColor, existingItem.CategoryId));
            }
            ColorCombo.ItemsSource = _colorOptions;

            _projectCodeOptions =
            [
                new("（未設定）", null),
                .. (projectCodes ?? []).Select(p => new ProjectCodeOption(p.DisplayName, p.Id))
            ];
            if (existingItem?.ProjectCodeId is { Length: > 0 } existingProjectCodeId &&
                _projectCodeOptions.All(p => p.ProjectCodeId != existingProjectCodeId))
            {
                _projectCodeOptions.Add(new ProjectCodeOption("（不明なプロジェクトコード）", existingProjectCodeId));
            }
            ProjectCodeCombo.ItemsSource = _projectCodeOptions;

            // 時間コンボボックスを初期化（0〜23時、0〜55分を5分刻み。編集時は元の分も選択肢に含める）
            StartHourCombo.ItemsSource = Enumerable.Range(0, 24).Select(h => h.ToString("D2")).ToList();
            EndHourCombo.ItemsSource = Enumerable.Range(0, 24).Select(h => h.ToString("D2")).ToList();
            StartMinuteCombo.ItemsSource = BuildMinuteOptions(existingItem?.StartTime.Minute);
            EndMinuteCombo.ItemsSource = BuildMinuteOptions(existingItem?.EndTime.Minute);

            if (existingItem != null)
            {
                // 編集モード：既存値をフォームに設定
                TitleCombo.Text = existingItem.Title;
                ContentTextBox.Text = existingItem.Content;
                DatePicker.SelectedDate = existingItem.StartTime.Date;
                AllDayCheckBox.IsChecked = existingItem.IsAllDay;

                StartHourCombo.SelectedItem = existingItem.StartTime.Hour.ToString("D2");
                StartMinuteCombo.SelectedItem = existingItem.StartTime.Minute.ToString("D2");
                EndHourCombo.SelectedItem = existingItem.EndTime.Hour.ToString("D2");
                EndMinuteCombo.SelectedItem = existingItem.EndTime.Minute.ToString("D2");

                // カテゴリを選択（ID一致を優先し、旧データは色一致でフォールバック）
                var matchingColor =
                    (existingItem.CategoryId != null
                        ? _colorOptions.FirstOrDefault(c => c.CategoryId == existingItem.CategoryId)
                        : null)
                    ?? _colorOptions.FirstOrDefault(c => c.Brush.ToString() == existingItem.BackgroundColor.ToString());
                ColorCombo.SelectedItem = matchingColor ?? _colorOptions[0];

                ProjectCodeCombo.SelectedItem = _projectCodeOptions.FirstOrDefault(
                    p => p.ProjectCodeId == existingItem.ProjectCodeId) ?? _projectCodeOptions[0];

                if (existingItem.AutoStartRecording)
                {
                    AutoStartActionRadio.IsChecked = true;
                }
                else if (existingItem.RemindAtStart)
                {
                    RemindStartActionRadio.IsChecked = true;
                }
                else
                {
                    NoStartActionRadio.IsChecked = true;
                }
                ForceStartCheckBox.IsChecked = existingItem.ForceStartRecording;
                if (existingItem.Kind == ScheduleItemKind.Recorded)
                {
                    RecordedKindRadio.IsChecked = true;
                }
                else
                {
                    PlannedKindRadio.IsChecked = true;
                }
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
                ProjectCodeCombo.SelectedItem = _projectCodeOptions.FirstOrDefault(
                    p => p.ProjectCodeId == defaultProjectCode?.Id) ?? _projectCodeOptions[0];
                PlannedKindRadio.IsChecked = true;
                NoStartActionRadio.IsChecked = true;
            }

            UpdateTimePanelState();
        }

        private void OkButton_Click(object sender, RoutedEventArgs e)
        {
            // バリデーション
            if (string.IsNullOrWhiteSpace(TitleCombo.Text))
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

                startTime = ComposeDateTime(date, startHour, startMinute, _originalStartTime);
                endTime = ComposeDateTime(date, endHour, endMinute, _originalEndTime);

                // 終了時刻が開始時刻より前の場合、翌日とする
                if (endTime <= startTime)
                {
                    // 明らかに開始より終了が早い（例: 23:00 -> 01:00）場合は翌日とみなす
                    // 単に同じ時刻等の場合はエラーにする手もあるが、ここでは翌日扱いが自然
                    endTime = endTime.AddDays(1);
                }
            }

            var selectedColor = (ColorOption?)ColorCombo.SelectedItem;
            var selectedKind = SelectedKind;
            var selectedProjectCode = (ProjectCodeOption?)ProjectCodeCombo.SelectedItem;

            ResultItem = new ScheduleItem
            {
                Kind = selectedKind,
                Title = TitleCombo.Text.Trim(),
                Content = ContentTextBox.Text.Trim(),
                StartTime = startTime,
                EndTime = endTime,
                IsAllDay = isAllDay,
                BackgroundColor = selectedColor?.Brush ?? Brushes.LightBlue,
                CategoryId = selectedColor?.CategoryId,
                ProjectCodeId = selectedProjectCode?.ProjectCodeId,
                // 終日予定は時刻の概念がないためリマインダー対象外
                RemindAtStart = selectedKind == ScheduleItemKind.Planned && !isAllDay && RemindStartActionRadio.IsChecked == true,
                AutoStartRecording = selectedKind == ScheduleItemKind.Planned && !isAllDay && AutoStartActionRadio.IsChecked == true,
                ForceStartRecording = selectedKind == ScheduleItemKind.Planned && !isAllDay &&
                    AutoStartActionRadio.IsChecked == true && ForceStartCheckBox.IsChecked == true
            };

            DialogResult = true;
            Close();
        }

        private void CancelButton_Click(object sender, RoutedEventArgs e) => DialogResult = false; // Close() is automatic when DialogResult is set in a dialog

        private void AllDayCheckBox_Changed(object sender, RoutedEventArgs e) => UpdateTimePanelState();

        private void KindRadio_Changed(object sender, RoutedEventArgs e) => UpdateTimePanelState();

        private void TimeCombo_SelectionChanged(
            object sender,
            System.Windows.Controls.SelectionChangedEventArgs e) => UpdateTimeSummary();

        private void UpdateTimePanelState()
        {
            var selectedKind = SelectedKind;
            var kindLabel = selectedKind == ScheduleItemKind.Recorded ? "実績" : "予定";
            if (DialogHeadingText != null)
            {
                DialogHeadingText.Text = $"{kindLabel}を{(_isEditMode ? "編集" : "追加")}";
            }

            // 終日は時刻を持たず、実績と終日予定は開始時の動作を持たない。
            bool isTimeEnabled = AllDayCheckBox?.IsChecked != true;
            StartTimePanel?.IsEnabled = isTimeEnabled;
            EndTimePanel?.IsEnabled = isTimeEnabled;
            if (ReminderPanel != null)
            {
                ReminderPanel.Visibility = isTimeEnabled && selectedKind == ScheduleItemKind.Planned
                    ? Visibility.Visible
                    : Visibility.Collapsed;
            }

            UpdateTimeSummary();
        }

        private void UpdateTimeSummary()
        {
            if (DurationText == null || NextDayBadge == null)
            {
                return;
            }

            if (AllDayCheckBox?.IsChecked == true)
            {
                DurationText.Text = "終日";
                NextDayBadge.Visibility = Visibility.Collapsed;
                return;
            }

            if (StartHourCombo?.SelectedItem is not string startHourText ||
                StartMinuteCombo?.SelectedItem is not string startMinuteText ||
                EndHourCombo?.SelectedItem is not string endHourText ||
                EndMinuteCombo?.SelectedItem is not string endMinuteText ||
                !int.TryParse(startHourText, out var startHour) ||
                !int.TryParse(startMinuteText, out var startMinute) ||
                !int.TryParse(endHourText, out var endHour) ||
                !int.TryParse(endMinuteText, out var endMinute))
            {
                DurationText.Text = "—";
                NextDayBadge.Visibility = Visibility.Collapsed;
                return;
            }

            var startTotalMinutes = startHour * 60 + startMinute;
            var endTotalMinutes = endHour * 60 + endMinute;
            var durationMinutes = endTotalMinutes - startTotalMinutes;
            var endsNextDay = durationMinutes <= 0;
            if (endsNextDay)
            {
                durationMinutes += 24 * 60;
            }

            var hours = durationMinutes / 60;
            var minutes = durationMinutes % 60;
            DurationText.Text = hours switch
            {
                > 0 when minutes > 0 => $"{hours}時間{minutes}分",
                > 0 => $"{hours}時間",
                _ => $"{minutes}分"
            };
            NextDayBadge.Visibility = endsNextDay ? Visibility.Visible : Visibility.Collapsed;
        }
    }
}
