using System.Windows;
using System.Windows.Media;
using Brush = System.Windows.Media.Brush;
using Brushes = System.Windows.Media.Brushes;
using MessageBox = System.Windows.MessageBox;

using TimeRenderer.Models;

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

        /// <summary>入力結果（キャンセル時は null のまま）</summary>
        public TodoItem? ResultTodo { get; private set; }

        private readonly List<ColorOption> _colorOptions;
        private readonly TodoItem? _existingTodo;

        /// <summary>
        /// コンストラクタ。既存の ToDo を渡すと編集モード、null なら新規追加モード。
        /// </summary>
        /// <param name="existingTodo">編集対象。新規追加時は null</param>
        /// <param name="categories">カテゴリ一覧（null・空の場合は既定値を使用）</param>
        /// <param name="titleSuggestions">タイトル入力欄のドロップダウン候補</param>
        public TodoEditDialog(
            TodoItem? existingTodo = null,
            IReadOnlyList<CategoryInfo>? categories = null,
            IReadOnlyList<string>? titleSuggestions = null)
        {
            InitializeComponent();

            _existingTodo = existingTodo;
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

            if (existingTodo != null)
            {
                TitleCombo.Text = existingTodo.Title;
                ContentTextBox.Text = existingTodo.Content;
                DueDatePicker.SelectedDate = existingTodo.DueDate;

                SelectPriority(existingTodo.Priority);

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
                // 新規は「期限なし・標準」で始める。決まっていない段階でも置けることを優先する
                SelectPriority(TodoPriority.Normal);
                ColorCombo.SelectedItem = _colorOptions[0];
            }

            Loaded += (_, _) => TitleCombo.Focus();
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
                IsCompleted = _existingTodo?.IsCompleted ?? false,
                CompletedAt = _existingTodo?.CompletedAt,

                Title = TitleCombo.Text.Trim(),
                Content = ContentTextBox.Text.Trim(),
                DueDate = DueDatePicker.SelectedDate?.Date,
                Priority = ReadPriority(),
                CategoryId = selectedColor?.CategoryId,
                ColorCode = selectedColor?.ColorCode ?? Brushes.LightBlue.ToString(),
            };

            DialogResult = true;
        }

        private void CancelButton_Click(object sender, RoutedEventArgs e) => DialogResult = false;
    }
}
