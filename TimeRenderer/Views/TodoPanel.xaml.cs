using System.Windows;
using System.Windows.Media;
using ButtonBase = System.Windows.Controls.Primitives.ButtonBase;
using ContextMenu = System.Windows.Controls.ContextMenu;
using DataObject = System.Windows.DataObject;
using DragDropEffects = System.Windows.DragDropEffects;
using DragEventArgs = System.Windows.DragEventArgs;
using ICommand = System.Windows.Input.ICommand;
using Key = System.Windows.Input.Key;
using KeyEventArgs = System.Windows.Input.KeyEventArgs;
using Keyboard = System.Windows.Input.Keyboard;
using MenuItem = System.Windows.Controls.MenuItem;
using ModifierKeys = System.Windows.Input.ModifierKeys;
using MouseButtonEventArgs = System.Windows.Input.MouseButtonEventArgs;
using MouseButtonState = System.Windows.Input.MouseButtonState;
using MouseEventArgs = System.Windows.Input.MouseEventArgs;
using Point = System.Windows.Point;
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

        private MainViewModel? _subscribedViewModel;

        // ドラッグ並べ替えの開始判定用（しきい値を超えるまでは通常のクリックとして扱う）
        private Point _dragStart;
        private TodoItem? _dragCandidate;

        public TodoPanel()
        {
            InitializeComponent();
            Loaded += OnLoaded;
            Unloaded += OnUnloaded;
        }

        private void OnLoaded(object sender, RoutedEventArgs e)
        {
            if (_subscribedViewModel == null && DataContext is MainViewModel vm)
            {
                _subscribedViewModel = vm;
                vm.QuickAddTodoFocusRequested += OnQuickAddFocusRequested;
            }
        }

        private void OnUnloaded(object sender, RoutedEventArgs e)
        {
            _subscribedViewModel?.QuickAddTodoFocusRequested -= OnQuickAddFocusRequested;
            _subscribedViewModel = null;
        }

        /// <summary>
        /// Ctrl+T：即時追加欄へ入力を移す。
        /// パネルが閉じていた場合は開く幅アニメーションの途中なので、
        /// レイアウトが確定してからフォーカスを移す。
        /// </summary>
        private void OnQuickAddFocusRequested(object? sender, EventArgs e)
        {
            Dispatcher.BeginInvoke(
                new Action(() =>
                {
                    QuickAddTextBox.Focus();
                    QuickAddTextBox.SelectAll();
                }),
                System.Windows.Threading.DispatcherPriority.Loaded);
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

            _dragCandidate = null; // ダブルクリックをドラッグ開始と取り違えない
            Execute(ViewModel.EditTodoCommand, todo);
            e.Handled = true;
        }

        // ===== キーボード操作 =====

        /// <summary>
        /// 一覧のキー操作。上下キーによる選択移動は ListBox 自身が行うので、
        /// ここでは選択中の ToDo に対する操作だけを受ける。
        /// </summary>
        private void TodoList_PreviewKeyDown(object sender, KeyEventArgs e)
        {
            // 入力欄（サブタスクの追加）で打っている最中は、一覧の操作として横取りしない。
            // Space が完了の切り替えになると、サブタスク名に空白が打てなくなる
            if (e.OriginalSource is System.Windows.Controls.TextBox) return;

            if (ViewModel.SelectedTodo is not { } todo) return;

            var ctrl = Keyboard.Modifiers.HasFlag(ModifierKeys.Control);

            switch (e.Key)
            {
                case Key.Space:
                    Execute(ViewModel.ToggleTodoCompletedCommand, todo);
                    break;

                case Key.Enter:
                    Execute(ViewModel.EditTodoCommand, todo);
                    break;

                case Key.Delete:
                    Execute(ViewModel.DeleteTodoCommand, todo);
                    break;

                case Key.T when !ctrl:
                    Execute(ViewModel.TogglePlannedTodayCommand, todo);
                    break;

                // Ctrl+↑↓ は並べ替え。修飾なしの↑↓は ListBox の選択移動に任せる
                case Key.Up when ctrl:
                    Execute(ViewModel.MoveTodoUpCommand, todo);
                    break;

                case Key.Down when ctrl:
                    Execute(ViewModel.MoveTodoDownCommand, todo);
                    break;

                default:
                    return;
            }

            e.Handled = true;
        }

        // ===== サブタスク =====

        /// <summary>三角を押してサブタスクの一覧を開閉する</summary>
        private void ExpandButton_Click(object sender, RoutedEventArgs e)
        {
            if (sender is FrameworkElement { DataContext: TodoItem todo }) ViewModel.ToggleTodoExpanded(todo);
        }

        /// <summary>メニューからの追加：一覧を開いて入力欄へフォーカスを移す</summary>
        private void AddSubtaskMenuItem_Click(object sender, RoutedEventArgs e)
        {
            if (ResolveMenuTodo(sender) is not { } todo) return;

            todo.IsExpanded = true;
            ViewModel.SelectedTodo = todo;

            // 入力欄は展開して初めて作られるため、レイアウトの確定を待つ
            Dispatcher.BeginInvoke(
                new Action(() => FindSubtaskAddBox(todo)?.Focus()),
                System.Windows.Threading.DispatcherPriority.Loaded);
        }

        private void SubtaskCheck_Click(object sender, RoutedEventArgs e)
        {
            if (sender is not FrameworkElement { DataContext: TodoSubtask subtask } element) return;
            if (FindParentTodo(element) is not { } parent) return;

            ViewModel.ToggleSubtask(parent, subtask);
        }

        private void SubtaskDelete_Click(object sender, RoutedEventArgs e)
        {
            if (sender is not FrameworkElement { DataContext: TodoSubtask subtask } element) return;
            if (FindParentTodo(element) is not { } parent) return;

            ViewModel.RemoveSubtask(parent, subtask);
        }

        /// <summary>サブタスクの追加欄：Enter で1件足して、続けて打てるように空に戻す</summary>
        private void SubtaskAddBox_KeyDown(object sender, KeyEventArgs e)
        {
            if (e.Key != Key.Enter) return;
            if (sender is not FrameworkElement { DataContext: TodoItem todo }) return;

            ViewModel.AddSubtask(todo);
            e.Handled = true;
        }

        /// <summary>
        /// サブタスクの行から、それが属する ToDo を探す。
        /// 行自身の DataContext は TodoSubtask なので、TodoItem を持つ親まで遡る。
        /// </summary>
        private static TodoItem? FindParentTodo(DependencyObject node)
        {
            for (var n = node; n is Visual; n = VisualTreeHelper.GetParent(n))
            {
                if (n is FrameworkElement { DataContext: TodoItem todo }) return todo;
            }
            return null;
        }

        /// <summary>展開中の行から、その ToDo のサブタスク追加欄を探す</summary>
        private System.Windows.Controls.TextBox? FindSubtaskAddBox(TodoItem todo)
        {
            if (TodoList.ItemContainerGenerator.ContainerFromItem(todo) is not DependencyObject container) return null;

            return FindDescendant<System.Windows.Controls.TextBox>(container, "SubtaskAddBox");
        }

        private static T? FindDescendant<T>(DependencyObject root, string name) where T : FrameworkElement
        {
            for (int i = 0; i < VisualTreeHelper.GetChildrenCount(root); i++)
            {
                var child = VisualTreeHelper.GetChild(root, i);
                if (child is T { } typed && typed.Name == name) return typed;

                if (FindDescendant<T>(child, name) is { } found) return found;
            }
            return null;
        }

        // ===== ドラッグ並べ替え =====

        /// <summary>
        /// ドラッグの開始候補を控える。
        /// チェックボックスや記録開始ボタンの上から始まった操作は、その操作のものとして扱う。
        /// </summary>
        private void TodoRow_PreviewMouseLeftButtonDown(object sender, MouseButtonEventArgs e)
        {
            _dragCandidate = null;

            if (sender is not FrameworkElement element || element.DataContext is not TodoItem todo) return;
            if (IsInteractiveChild(e.OriginalSource as DependencyObject)) return;

            _dragStart = e.GetPosition(this);
            _dragCandidate = todo;
        }

        /// <summary>しきい値を超えて動いたらドラッグを始める（軽いクリックで並びが変わらないように）</summary>
        private void TodoRow_PreviewMouseMove(object sender, MouseEventArgs e)
        {
            if (_dragCandidate is not { } moved) return;
            if (e.LeftButton != MouseButtonState.Pressed)
            {
                _dragCandidate = null;
                return;
            }

            var diff = e.GetPosition(this) - _dragStart;
            if (Math.Abs(diff.X) < SystemParameters.MinimumHorizontalDragDistance &&
                Math.Abs(diff.Y) < SystemParameters.MinimumVerticalDragDistance) return;

            _dragCandidate = null;
            DragDrop.DoDragDrop((DependencyObject)sender, new DataObject(typeof(TodoItem), moved), DragDropEffects.Move);
        }

        private void TodoRow_DragOver(object sender, DragEventArgs e)
        {
            e.Effects = e.Data.GetDataPresent(typeof(TodoItem)) ? DragDropEffects.Move : DragDropEffects.None;
            e.Handled = true;
        }

        private void TodoRow_Drop(object sender, DragEventArgs e)
        {
            _dragCandidate = null;

            if (sender is not FrameworkElement element || element.DataContext is not TodoItem target) return;
            if (e.Data.GetData(typeof(TodoItem)) is not TodoItem moved) return;

            ViewModel.MoveTodoTo(moved, target);
            e.Handled = true;
        }

        /// <summary>
        /// 行の中のボタン類（チェックボックス・記録開始）から始まった操作か。
        /// 行の枠（RowRoot）まで遡っても見つからなければ、行そのものを掴んだとみなす。
        /// </summary>
        private static bool IsInteractiveChild(DependencyObject? source)
        {
            for (var node = source; node is Visual; node = VisualTreeHelper.GetParent(node))
            {
                if (node is ButtonBase) return true;
                if (node is System.Windows.Controls.Border { Name: "RowRoot" }) return false;
            }
            return false;
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

        private void PlanTodayMenuItem_Click(object sender, RoutedEventArgs e) =>
            ExecuteOnMenuTarget(sender, ViewModel.TogglePlannedTodayCommand);

        private void MoveUpMenuItem_Click(object sender, RoutedEventArgs e) =>
            ExecuteOnMenuTarget(sender, ViewModel.MoveTodoUpCommand);

        private void MoveDownMenuItem_Click(object sender, RoutedEventArgs e) =>
            ExecuteOnMenuTarget(sender, ViewModel.MoveTodoDownCommand);

        private void DeleteTodoMenuItem_Click(object sender, RoutedEventArgs e) =>
            ExecuteOnMenuTarget(sender, ViewModel.DeleteTodoCommand);

        /// <summary>メニューを開いた要素から対象の ToDo を取り出す</summary>
        private static TodoItem? ResolveMenuTodo(object sender)
        {
            if (sender is MenuItem menuItem &&
                menuItem.Parent is ContextMenu contextMenu &&
                contextMenu.PlacementTarget is FrameworkElement { DataContext: TodoItem todo })
            {
                return todo;
            }
            return null;
        }

        /// <summary>メニューを開いた要素から対象の ToDo を解決してコマンドを実行する</summary>
        private static void ExecuteOnMenuTarget(object sender, ICommand command)
        {
            if (ResolveMenuTodo(sender) is { } todo) Execute(command, todo);
        }

        private static void Execute(ICommand command, TodoItem todo)
        {
            if (command.CanExecute(todo)) command.Execute(todo);
        }
    }
}
