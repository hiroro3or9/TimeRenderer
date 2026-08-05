using System.Windows;
using MessageBox = System.Windows.MessageBox;
using MouseButtonEventArgs = System.Windows.Input.MouseButtonEventArgs;

using TimeRenderer.Models;

namespace TimeRenderer.Views.Dialogs
{
    /// <summary>
    /// 未完了の ToDo から1件選ぶダイアログ。
    /// 記録漏れの帯を「どの ToDo の作業だったか」で埋めるために使う。
    /// </summary>
    public partial class TodoPickerDialog : Window
    {
        /// <summary>選ばれた ToDo（キャンセル時は null のまま）</summary>
        public TodoItem? SelectedTodo { get; private set; }

        /// <param name="message">何を選ぶのかの説明（対象の時間帯を書く）</param>
        /// <param name="todos">選択肢（未完了のものだけを渡すこと）</param>
        public TodoPickerDialog(string message, IReadOnlyList<TodoItem> todos)
        {
            InitializeComponent();

            MessageText.Text = message;
            TodoList.ItemsSource = todos;

            // 1件しか無いなら選んだ状態で開く（そのまま Enter で確定できる）
            if (todos.Count > 0) TodoList.SelectedIndex = 0;

            Loaded += (_, _) => TodoList.Focus();
        }

        private void TodoList_MouseDoubleClick(object sender, MouseButtonEventArgs e)
        {
            if (TodoList.SelectedItem is TodoItem) Confirm();
        }

        private void OkButton_Click(object sender, RoutedEventArgs e) => Confirm();

        private void CancelButton_Click(object sender, RoutedEventArgs e) => DialogResult = false;

        private void Confirm()
        {
            if (TodoList.SelectedItem is not TodoItem todo)
            {
                MessageBox.Show("ToDo を選んでください。", "選択エラー", MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }

            SelectedTodo = todo;
            DialogResult = true;
        }
    }
}
