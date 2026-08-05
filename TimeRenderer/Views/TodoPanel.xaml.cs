using System.Windows;
using ContextMenu = System.Windows.Controls.ContextMenu;
using ICommand = System.Windows.Input.ICommand;
using Key = System.Windows.Input.Key;
using KeyEventArgs = System.Windows.Input.KeyEventArgs;
using MenuItem = System.Windows.Controls.MenuItem;
using MouseButtonEventArgs = System.Windows.Input.MouseButtonEventArgs;
using UserControl = System.Windows.Controls.UserControl;

using TimeRenderer.Models;
using TimeRenderer.ViewModels;

namespace TimeRenderer.Views
{
    /// <summary>
    /// ToDo パネル（一覧・即時追加・記録開始）。
    /// 開閉は IsTodoPanelVisible による幅アニメーションで行う（XAML 内のトリガー参照）。
    ///
    /// コンテキストメニューは視覚ツリーの外にあり RelativeSource でVMへ辿れないため、
    /// 予定アイテムのメニューと同じくコードビハインドで対象を解決してコマンドを実行する。
    /// </summary>
    public partial class TodoPanel : UserControl
    {
        private MainViewModel ViewModel => (MainViewModel)DataContext;

        public TodoPanel()
        {
            InitializeComponent();
        }

        /// <summary>即時追加：Enter で1件追加して入力欄を空に戻す</summary>
        private void QuickAddTextBox_KeyDown(object sender, KeyEventArgs e)
        {
            if (e.Key != Key.Enter) return;

            if (ViewModel.AddQuickTodoCommand.CanExecute(null))
            {
                ViewModel.AddQuickTodoCommand.Execute(null);
            }
            e.Handled = true;
        }

        /// <summary>行のダブルクリックで編集ダイアログを開く</summary>
        private void TodoRow_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
        {
            if (e.ClickCount != 2) return;
            if (sender is not FrameworkElement element || element.DataContext is not TodoItem todo) return;

            Execute(ViewModel.EditTodoCommand, todo);
            e.Handled = true;
        }

        // ===== コンテキストメニュー =====

        private void EditTodoMenuItem_Click(object sender, RoutedEventArgs e) =>
            ExecuteOnMenuTarget(sender, ViewModel.EditTodoCommand);

        private void StartRecordingMenuItem_Click(object sender, RoutedEventArgs e) =>
            ExecuteOnMenuTarget(sender, ViewModel.StartRecordingFromTodoCommand);

        private void DueTodayMenuItem_Click(object sender, RoutedEventArgs e) =>
            ExecuteOnMenuTarget(sender, ViewModel.SetTodoDueTodayCommand);

        private void DueTomorrowMenuItem_Click(object sender, RoutedEventArgs e) =>
            ExecuteOnMenuTarget(sender, ViewModel.SetTodoDueTomorrowCommand);

        private void ClearDueMenuItem_Click(object sender, RoutedEventArgs e) =>
            ExecuteOnMenuTarget(sender, ViewModel.ClearTodoDueCommand);

        private void DeleteTodoMenuItem_Click(object sender, RoutedEventArgs e) =>
            ExecuteOnMenuTarget(sender, ViewModel.DeleteTodoCommand);

        /// <summary>メニューを開いた要素から対象の ToDo を解決してコマンドを実行する</summary>
        private static void ExecuteOnMenuTarget(object sender, ICommand command)
        {
            if (sender is MenuItem menuItem &&
                menuItem.Parent is ContextMenu contextMenu &&
                contextMenu.PlacementTarget is FrameworkElement element &&
                element.DataContext is TodoItem todo)
            {
                Execute(command, todo);
            }
        }

        private static void Execute(ICommand command, TodoItem todo)
        {
            if (command.CanExecute(todo)) command.Execute(todo);
        }
    }
}
