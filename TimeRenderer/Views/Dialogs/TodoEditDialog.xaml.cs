using System.Collections.ObjectModel;
using System.Windows;
using System.Windows.Media;
using Brush = System.Windows.Media.Brush;
using Brushes = System.Windows.Media.Brushes;
using MessageBox = System.Windows.MessageBox;

using TimeRenderer.Models;
using TimeRenderer.ViewModels;

namespace TimeRenderer.Views.Dialogs
{
    /// <summary>
    /// ToDo の追加・編集用ダイアログ。
    ///
    /// 予定と違って時刻を持たないため、入力するのは期限日・優先度・カテゴリ・メモだけ。
    /// 完了状態と記録済み時間は一覧側で変わるものなので、ここでは扱わない。
    /// </summary>
    public partial class TodoEditDialog : Window
    {
        /// <summary>カテゴリ選択肢（色つき）</summary>
        public record ColorOption(string Name, Brush Brush, string? CategoryId, string ColorCode);

        /// <summary>見積もり時間の選択肢</summary>
        public record EstimateOption(string Label, int Minutes);

        /// <summary>繰り返しの単位の選択肢</summary>
        public record RecurrenceOption(string Label, TodoRecurrenceUnit Unit);

        /// <summary>
        /// 通知のタイミングの選択肢。
        /// OffsetDays が null なら絶対日時、値があれば「期限の N 日前」（0 は当日）。
        /// </summary>
        public record RemindTimingOption(string Label, int? OffsetDays);

        /// <summary>
        /// 相対指定の刻み。「前日」までは1日単位、そこから先は3日・1週間と粗くする
        /// （4日前と5日前を選び分けたい場面はまず無い）。
        /// </summary>
        private static readonly List<RemindTimingOption> RemindTimingOptions =
        [
            new("日時を指定", null),
            new("期限の当日", 0),
            new("期限の前日", 1),
            new("期限の2日前", 2),
            new("期限の3日前", 3),
            new("期限の1週間前", 7),
        ];

        /// <summary>
        /// 見積もりの選択肢。刻んだ選択肢にすることで、入力の手間と精度のつり合いを取る
        /// （分単位で正確に見積もっても、実績と比べる用途では意味がない）。
        /// </summary>
        private static readonly List<EstimateOption> EstimateOptions =
        [
            new("なし", 0),
            new("15分", 15),
            new("30分", 30),
            new("45分", 45),
            new("1時間", 60),
            new("1時間30分", 90),
            new("2時間", 120),
            new("3時間", 180),
            new("4時間", 240),
            new("6時間", 360),
            new("8時間", 480),
        ];

        private static readonly List<RecurrenceOption> RecurrenceOptions =
        [
            new("しない", TodoRecurrenceUnit.None),
            new("日ごと", TodoRecurrenceUnit.Day),
            new("週ごと", TodoRecurrenceUnit.Week),
            new("月ごと", TodoRecurrenceUnit.Month),
        ];

        /// <summary>入力結果（キャンセル時は null のまま）</summary>
        public TodoItem? ResultTodo { get; private set; }

        private readonly List<ColorOption> _colorOptions;
        private readonly TodoItem? _existingTodo;
        private readonly TodoEstimateStats _estimateStats;

        /// <summary>
        /// 編集中のサブタスク。元のインスタンスを直接いじると、
        /// キャンセルしても変更が残ってしまうため複製を編集して OK 時に返す。
        /// </summary>
        private readonly ObservableCollection<TodoSubtask> _subtasks = [];

        /// <summary>
        /// 5分刻みの分の選択肢。既存の通知時刻が5分刻みでない場合はその値も残す
        /// （スヌーズで作られた任意の分を、編集しただけで勝手に丸めないため）。
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
        /// コンストラクタ。既存の ToDo を渡すと編集モード、null なら新規追加モード。
        /// </summary>
        /// <param name="existingTodo">編集対象。新規追加時は null</param>
        /// <param name="categories">カテゴリ一覧（null・空の場合は既定値を使用）</param>
        /// <param name="titleSuggestions">タイトル入力欄のドロップダウン候補</param>
        /// <param name="estimateStats">見積もり欄に添える、過去の実績の傾向</param>
        public TodoEditDialog(
            TodoItem? existingTodo = null,
            IReadOnlyList<CategoryInfo>? categories = null,
            IReadOnlyList<string>? titleSuggestions = null,
            TodoEstimateStats? estimateStats = null)
        {
            InitializeComponent();

            _existingTodo = existingTodo;
            _estimateStats = estimateStats ?? TodoEstimateStats.Empty;
            Title = existingTodo == null ? "ToDo の追加" : "ToDo の編集";

            TitleCombo.ItemsSource = titleSuggestions ?? [];

            List<CategoryInfo> source = (categories == null || categories.Count == 0)
                ? CategoryInfo.CreateDefaults()
                : [.. categories];
            _colorOptions = [.. source.Select(c => new ColorOption(c.Name, c.Brush, c.Id, c.ColorCode))];

            // どのカテゴリにも一致しない色を持つ ToDo は、その色も選択肢として残す
            if (existingTodo != null &&
                _colorOptions.All(c => c.CategoryId != existingTodo.CategoryId || existingTodo.CategoryId == null) &&
                _colorOptions.All(c => c.ColorCode != existingTodo.ColorCode))
            {
                _colorOptions.Add(new ColorOption("（現在の色）", existingTodo.Brush, existingTodo.CategoryId, existingTodo.ColorCode));
            }
            ColorCombo.ItemsSource = _colorOptions;

            RemindTimingCombo.ItemsSource = RemindTimingOptions;
            RemindHourCombo.ItemsSource = Enumerable.Range(0, 24).Select(h => h.ToString("D2")).ToList();
            RemindMinuteCombo.ItemsSource = BuildMinuteOptions(existingTodo?.RemindAt?.Minute);

            EstimateCombo.ItemsSource = BuildEstimateOptions(existingTodo?.EstimatedMinutes);
            RecurrenceCombo.ItemsSource = RecurrenceOptions;
            RecurrenceIntervalCombo.ItemsSource = Enumerable.Range(1, 12).ToList();

            foreach (var subtask in existingTodo?.Subtasks ?? [])
            {
                _subtasks.Add(subtask.Clone());
            }
            SubtaskList.ItemsSource = _subtasks;

            if (existingTodo != null)
            {
                TitleCombo.Text = existingTodo.Title;
                ContentTextBox.Text = existingTodo.Content;
                DueDatePicker.SelectedDate = existingTodo.DueDate;

                SelectPriority(existingTodo.Priority);
                ApplyReminder(existingTodo.RemindAt, existingTodo.DueDate, existingTodo.RemindOffsetDays);
                ApplyEstimate(existingTodo.EstimatedMinutes);
                ApplyRecurrence(
                    existingTodo.Recurrence,
                    existingTodo.RecurrenceInterval,
                    existingTodo.RecurrenceDaysOfWeek,
                    existingTodo.RecurrenceFromCompletion);

                // 見積もりの横に、これまでこの ToDo で記録した時間を出す（見直しの手がかりになる）
                if (existingTodo.HasRecorded) RecordedText.Text = $"記録済み {existingTodo.RecordedDisplay}";

                // カテゴリを選択（ID一致を優先し、旧データは色一致でフォールバック）
                var matching =
                    (existingTodo.CategoryId != null
                        ? _colorOptions.FirstOrDefault(c => c.CategoryId == existingTodo.CategoryId)
                        : null)
                    ?? _colorOptions.FirstOrDefault(c => c.ColorCode == existingTodo.ColorCode);
                ColorCombo.SelectedItem = matching ?? _colorOptions[0];
            }
            else
            {
                // 新規は「期限なし・通知なし・見積もりなし・繰り返しなし・標準」で始める。
                // 決まっていない段階でも置けることを優先する
                SelectPriority(TodoPriority.Normal);
                ApplyReminder(null, null, null);
                ApplyEstimate(0);
                ApplyRecurrence(TodoRecurrenceUnit.None, 1, [], false);
                ColorCombo.SelectedItem = _colorOptions[0];
            }

            UpdateAccuracyHint();
            Loaded += (_, _) => TitleCombo.Focus();
        }

        /// <summary>
        /// 選んだカテゴリの「見積もりに対する実績」の傾向を出す。
        /// 見積もっているその瞬間に目に入らないと、次の見積もりに反映されないため、
        /// 統計ビューではなくここに置く。
        /// </summary>
        private void UpdateAccuracyHint()
        {
            // InitializeComponent 中の SelectionChanged では、まだ他の要素が作られていない
            if (AccuracyText == null) return;

            var categoryId = ((ColorOption?)ColorCombo.SelectedItem)?.CategoryId;
            var accuracy = _estimateStats.For(categoryId);

            if (accuracy == null)
            {
                AccuracyText.Visibility = Visibility.Collapsed;
                return;
            }

            AccuracyText.Text = accuracy.Display;
            AccuracyText.Visibility = Visibility.Visible;
            AccuracyText.SetResourceReference(
                System.Windows.Controls.TextBlock.ForegroundProperty,
                accuracy.IsOverrunning ? "TodoHighPriorityBrush" : "TextSecondaryBrush");
        }

        private void ColorCombo_SelectionChanged(object sender, System.Windows.Controls.SelectionChangedEventArgs e) =>
            UpdateAccuracyHint();

        /// <summary>
        /// 見積もりの選択肢。既存の値が選択肢に無い場合（設定を刻み直した後など）はその値も残す。
        /// </summary>
        private static List<EstimateOption> BuildEstimateOptions(int? exactMinutes)
        {
            var options = new List<EstimateOption>(EstimateOptions);
            if (exactMinutes is > 0 && options.All(o => o.Minutes != exactMinutes.Value))
            {
                options.Add(new EstimateOption($"{exactMinutes.Value}分", exactMinutes.Value));
                options.Sort((a, b) => a.Minutes.CompareTo(b.Minutes));
            }
            return options;
        }

        private void ApplyEstimate(int minutes)
        {
            var options = (List<EstimateOption>)EstimateCombo.ItemsSource;
            EstimateCombo.SelectedItem = options.FirstOrDefault(o => o.Minutes == minutes) ?? options[0];
        }

        private void ApplyRecurrence(
            TodoRecurrenceUnit unit, int interval, IReadOnlyList<DayOfWeek> days, bool fromCompletion)
        {
            RecurrenceCombo.SelectedItem = RecurrenceOptions.FirstOrDefault(o => o.Unit == unit) ?? RecurrenceOptions[0];
            RecurrenceIntervalCombo.SelectedItem = Math.Clamp(interval, 1, 12);
            RecurrenceFromCompletionCheckBox.IsChecked = fromCompletion;

            foreach (var (check, day) in DayChecks())
            {
                check.IsChecked = days.Contains(day);
            }

            UpdateRecurrenceState();
        }

        /// <summary>曜日のチェックと DayOfWeek の対応（月曜始まり）</summary>
        private IEnumerable<(System.Windows.Controls.CheckBox Check, DayOfWeek Day)> DayChecks()
        {
            yield return (DayMonCheck, DayOfWeek.Monday);
            yield return (DayTueCheck, DayOfWeek.Tuesday);
            yield return (DayWedCheck, DayOfWeek.Wednesday);
            yield return (DayThuCheck, DayOfWeek.Thursday);
            yield return (DayFriCheck, DayOfWeek.Friday);
            yield return (DaySatCheck, DayOfWeek.Saturday);
            yield return (DaySunCheck, DayOfWeek.Sunday);
        }

        private List<DayOfWeek> ReadRecurrenceDays()
        {
            // 曜日は週ごとのときだけ意味を持つ。他の単位では空にして持ち越さない
            if (ReadRecurrenceUnit() != TodoRecurrenceUnit.Week) return [];

            return [.. DayChecks().Where(x => x.Check.IsChecked == true).Select(x => x.Day)];
        }

        private void RecurrenceCombo_SelectionChanged(object sender, System.Windows.Controls.SelectionChangedEventArgs e) =>
            UpdateRecurrenceState();

        private void RecurrenceDay_Changed(object sender, RoutedEventArgs e) => UpdateRecurrenceState();

        // ===== サブタスク =====

        private void SubtaskAddBox_KeyDown(object sender, System.Windows.Input.KeyEventArgs e)
        {
            if (e.Key != System.Windows.Input.Key.Enter) return;

            AddSubtaskFromBox();
            e.Handled = true;
        }

        private void SubtaskAddButton_Click(object sender, RoutedEventArgs e) => AddSubtaskFromBox();

        private void AddSubtaskFromBox()
        {
            var title = SubtaskAddBox.Text.Trim();
            if (title.Length == 0) return;

            _subtasks.Add(new TodoSubtask { Title = title });

            // 続けて何件でも打ち込めるように空へ戻す
            SubtaskAddBox.Clear();
            SubtaskAddBox.Focus();
        }

        private void SubtaskDelete_Click(object sender, RoutedEventArgs e)
        {
            if (sender is FrameworkElement { DataContext: TodoSubtask subtask }) _subtasks.Remove(subtask);
        }

        /// <summary>繰り返さない場合は間隔などの入力を無効にし、説明文も切り替える</summary>
        private void UpdateRecurrenceState()
        {
            // InitializeComponent 中の SelectionChanged では、まだ他の要素が作られていない
            if (RecurrenceIntervalCombo == null || RecurrenceFromCompletionCheckBox == null
                || RecurrenceHint == null || RecurrenceDaysPanel == null) return;

            var unit = ((RecurrenceOption?)RecurrenceCombo.SelectedItem)?.Unit ?? TodoRecurrenceUnit.None;
            var enabled = unit != TodoRecurrenceUnit.None;
            var isWeekly = unit == TodoRecurrenceUnit.Week;

            RecurrenceIntervalCombo.IsEnabled = enabled;
            RecurrenceFromCompletionCheckBox.IsEnabled = enabled;
            RecurrenceHint.Visibility = enabled ? Visibility.Visible : Visibility.Collapsed;

            // 曜日を決められるのは週ごとのときだけ
            RecurrenceDaysPanel.Visibility = isWeekly ? Visibility.Visible : Visibility.Collapsed;

            // 曜日を決めた場合はその曜日に来ること自体が起点なので、完了日基準は使わない
            var hasDays = isWeekly && DayChecks().Any(x => x.Check.IsChecked == true);
            RecurrenceFromCompletionCheckBox.IsEnabled = enabled && !hasDays;

            RecurrenceHint.Text = hasDays
                ? "完了すると、指定した曜日のうち次に来る日を期限にした次回分が作られます。"
                : "完了すると次回分が自動で作られます。期限日から数えるので、遅れて完了しても曜日や日付はずれません。";
        }

        private TodoRecurrenceUnit ReadRecurrenceUnit() =>
            ((RecurrenceOption?)RecurrenceCombo.SelectedItem)?.Unit ?? TodoRecurrenceUnit.None;

        /// <summary>
        /// 通知欄へ初期値を入れる。未設定なら、チェックを入れたときにそのまま使える値を置いておく
        /// （期限日の既定時刻。期限も無ければ今日の既定時刻）。
        /// </summary>
        private void ApplyReminder(DateTime? remindAt, DateTime? dueDate, int? offsetDays)
        {
            RemindCheckBox.IsChecked = remindAt.HasValue;

            var value = remindAt ?? (dueDate ?? DateTime.Today).Date.AddHours(TodoItem.DefaultRemindHour);
            RemindDatePicker.SelectedDate = value.Date;
            RemindHourCombo.SelectedItem = value.Hour.ToString("D2");
            RemindMinuteCombo.SelectedItem = value.Minute.ToString("D2");

            // 期限が無ければ相対の基準が無いので、絶対日時の指定に倒す
            var timing = dueDate.HasValue
                ? RemindTimingOptions.FirstOrDefault(o => o.OffsetDays == offsetDays)
                : null;
            RemindTimingCombo.SelectedItem = timing ?? RemindTimingOptions[0];

            UpdateRemindTiming();
        }

        /// <summary>
        /// 通知を有効にしたとき、日付が空なら期限日（無ければ今日）で埋める。
        /// チェックを入れただけで通知できる状態にしておく。
        /// </summary>
        private void RemindCheckBox_Checked(object sender, RoutedEventArgs e)
        {
            if (RemindDatePicker.SelectedDate == null)
            {
                RemindDatePicker.SelectedDate = (DueDatePicker.SelectedDate ?? DateTime.Today).Date;
                RemindHourCombo.SelectedItem ??= TodoItem.DefaultRemindHour.ToString("D2");
                RemindMinuteCombo.SelectedItem ??= "00";
            }

            UpdateRemindTiming();
        }

        private void RemindTimingCombo_SelectionChanged(
            object sender, System.Windows.Controls.SelectionChangedEventArgs e) => UpdateRemindTiming();

        /// <summary>
        /// 期限を変えたら、相対指定の通知日も追随させる。
        /// 期限を外した場合は基準が無くなるので、絶対日時の指定へ戻す。
        /// </summary>
        private void DueDatePicker_SelectedDateChanged(
            object sender, System.Windows.Controls.SelectionChangedEventArgs e)
        {
            // InitializeComponent 中は他の要素がまだ作られていない
            if (RemindTimingCombo == null) return;

            if (DueDatePicker.SelectedDate == null && ReadRemindOffsetDays() != null)
            {
                RemindTimingCombo.SelectedItem = RemindTimingOptions[0];
            }

            UpdateRemindTiming();
        }

        private int? ReadRemindOffsetDays() =>
            ((RemindTimingOption?)RemindTimingCombo?.SelectedItem)?.OffsetDays;

        /// <summary>
        /// タイミングの選択に合わせて、日付欄の見せ方と説明を切り替える。
        /// 相対指定のときは日付を自分で選べても意味がないので、計算結果を出すだけにする。
        /// </summary>
        private void UpdateRemindTiming()
        {
            if (RemindTimingCombo == null || RemindHint == null) return;

            var offset = ReadRemindOffsetDays();
            var due = DueDatePicker.SelectedDate?.Date;

            var isRelative = offset.HasValue;
            RemindDatePicker.Visibility = isRelative ? Visibility.Collapsed : Visibility.Visible;
            RemindComputedText.Visibility = isRelative ? Visibility.Visible : Visibility.Collapsed;

            if (isRelative && due is { } dueDate)
            {
                var date = dueDate.AddDays(-offset!.Value);
                RemindDatePicker.SelectedDate = date;
                RemindComputedText.Text = date.ToString("yyyy/M/d (ddd)");
            }

            RemindHint.Text = (isRelative, due) switch
            {
                (true, not null) => "期限を変えると、通知日も一緒に動きます。",
                (true, null) => "期限が未設定のため、この指定は使えません。期限を決めてください。",
                _ => "期限とは別に、思い出したい日時を指定します。「期限の前日」などにすると期限に追随します。",
            };
        }

        /// <summary>通知欄の入力から通知日時を組み立てる（無効・未入力なら null）</summary>
        private DateTime? ReadRemindAt()
        {
            if (RemindCheckBox.IsChecked != true) return null;
            if (RemindDatePicker.SelectedDate is not { } date) return null;

            var hour = int.TryParse(RemindHourCombo.SelectedItem as string, out var h)
                ? h
                : TodoItem.DefaultRemindHour;
            var minute = int.TryParse(RemindMinuteCombo.SelectedItem as string, out var m) ? m : 0;

            return date.Date.AddHours(hour).AddMinutes(minute);
        }

        /// <summary>
        /// 相対指定として保存する日数（絶対日時なら null）。
        /// 期限が無いときは基準が無いので相対にはしない。
        /// </summary>
        private int? ReadRemindOffsetForResult()
        {
            if (RemindCheckBox.IsChecked != true) return null;
            if (DueDatePicker.SelectedDate == null) return null;

            return ReadRemindOffsetDays();
        }

        private void SelectPriority(TodoPriority priority)
        {
            PriorityHighRadio.IsChecked = priority == TodoPriority.High;
            PriorityNormalRadio.IsChecked = priority == TodoPriority.Normal;
            PriorityLowRadio.IsChecked = priority == TodoPriority.Low;
        }

        private TodoPriority ReadPriority()
        {
            if (PriorityHighRadio.IsChecked == true) return TodoPriority.High;
            if (PriorityLowRadio.IsChecked == true) return TodoPriority.Low;
            return TodoPriority.Normal;
        }

        private void TodayButton_Click(object sender, RoutedEventArgs e) =>
            DueDatePicker.SelectedDate = DateTime.Today;

        private void TomorrowButton_Click(object sender, RoutedEventArgs e) =>
            DueDatePicker.SelectedDate = DateTime.Today.AddDays(1);

        private void ClearDueButton_Click(object sender, RoutedEventArgs e) =>
            DueDatePicker.SelectedDate = null;

        private void OkButton_Click(object sender, RoutedEventArgs e)
        {
            if (string.IsNullOrWhiteSpace(TitleCombo.Text))
            {
                MessageBox.Show("タイトルを入力してください。", "入力エラー", MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }

            var selectedColor = (ColorOption?)ColorCombo.SelectedItem;

            ResultTodo = new TodoItem
            {
                // 編集時は元の識別子・作成日時・記録済み時間・完了状態を引き継ぐ
                // （呼び出し側は値だけを実体へ移すが、単体で使われても壊れないようにしておく）
                Id = _existingTodo?.Id ?? Guid.NewGuid().ToString("N"),
                CreatedAt = _existingTodo?.CreatedAt ?? DateTime.Now,
                RecordedTicks = _existingTodo?.RecordedTicks ?? 0,
                SortOrder = _existingTodo?.SortOrder ?? 0,
                IsCompleted = _existingTodo?.IsCompleted ?? false,
                CompletedAt = _existingTodo?.CompletedAt,

                Title = TitleCombo.Text.Trim(),
                Content = ContentTextBox.Text.Trim(),
                DueDate = DueDatePicker.SelectedDate?.Date,
                RemindAt = ReadRemindAt(),
                RemindOffsetDays = ReadRemindOffsetForResult(),
                Priority = ReadPriority(),
                CategoryId = selectedColor?.CategoryId,
                ColorCode = selectedColor?.ColorCode ?? Brushes.LightBlue.ToString(),
                EstimatedMinutes = ((EstimateOption?)EstimateCombo.SelectedItem)?.Minutes ?? 0,
                Recurrence = ReadRecurrenceUnit(),
                RecurrenceInterval = RecurrenceIntervalCombo.SelectedItem is int interval ? interval : 1,
                RecurrenceDaysOfWeek = ReadRecurrenceDays(),
                RecurrenceFromCompletion = RecurrenceFromCompletionCheckBox.IsChecked ?? false,
                PlannedOn = _existingTodo?.PlannedOn,
                // 空のまま追加された行は捨てる（入力途中で OK を押した場合）
                Subtasks = [.. _subtasks.Where(s => !string.IsNullOrWhiteSpace(s.Title))],
            };

            DialogResult = true;
        }

        private void CancelButton_Click(object sender, RoutedEventArgs e) => DialogResult = false;
    }
}
