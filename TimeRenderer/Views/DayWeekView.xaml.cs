using System.Windows;
using System.Windows.Controls;
using MouseButtonEventArgs = System.Windows.Input.MouseButtonEventArgs;
using UserControl = System.Windows.Controls.UserControl;

using TimeRenderer.Models;
using TimeRenderer.ViewModels;

namespace TimeRenderer.Views
{
    /// <summary>
    /// 日/週ビュー（曜日ヘッダー・終日イベント・時間グリッド）。
    ///
    /// - 予定バーのドラッグ（移動・伸縮）: DayWeekView.Drag.cs
    /// - キーボード操作・空き領域ドラッグでの新規作成: DayWeekView.Schedule.cs
    ///
    /// DataContext は MainWindow から継承した MainViewModel を前提にする。
    /// </summary>
    public partial class DayWeekView : UserControl
    {
        private MainViewModel ViewModel => (MainViewModel)DataContext;

        private MainViewModel? _subscribedViewModel;
        private bool _initialScrollDone;

        public DayWeekView()
        {
            InitializeComponent();
            InitializeDragHandlers();
            Loaded += OnLoaded;
            Unloaded += OnUnloaded;
        }

        private void OnLoaded(object sender, RoutedEventArgs e)
        {
            // 検索結果ジャンプ時のスクロール要求を購読する
            if (_subscribedViewModel == null && DataContext is MainViewModel vm)
            {
                _subscribedViewModel = vm;
                vm.ScrollToTimeRequested += OnScrollToTimeRequested;
            }

            // 起動時に現在時刻までスクロール
            if (!_initialScrollDone)
            {
                _initialScrollDone = true;
                ScrollDayViewToTime(DateTime.Now);
            }
        }

        private void OnUnloaded(object sender, RoutedEventArgs e)
        {
            _subscribedViewModel?.ScrollToTimeRequested -= OnScrollToTimeRequested;
            _subscribedViewModel = null;
        }

        // ===== スクロール =====

        /// <summary>指定時刻が画面の縦中央に来るようスクロールする</summary>
        private void ScrollDayViewToTime(DateTime time)
        {
            double pixelsPerHour = Helpers.LayoutConstants.PixelsPerHour;
            double y = ((time.Hour - ViewModel.DisplayStartHour) * pixelsPerHour)
                     + (time.Minute * (pixelsPerHour / 60.0));
            double targetOffset = Math.Max(0, y - (MainScrollViewer.ViewportHeight / 2));
            MainScrollViewer.ScrollToVerticalOffset(targetOffset);
        }

        /// <summary>
        /// 検索結果から日ビューへジャンプした後、該当時刻が画面中央に来るようスクロールする。
        /// ビュー切替・レイアウト確定後に実行する必要があるため Dispatcher で遅延させる。
        /// </summary>
        private void OnScrollToTimeRequested(object? sender, DateTime time)
        {
            Dispatcher.BeginInvoke(new Action(() => ScrollDayViewToTime(time)),
                System.Windows.Threading.DispatcherPriority.Loaded);
        }

        // ===== 予定バーのクリック =====

        /// <summary>
        /// スケジュールアイテムのマウス押下イベント。
        /// ダブルクリックで編集、シングルクリックはドラッグ操作の候補として記録する。
        /// </summary>
        private void ScheduleItem_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
        {
            if (e.ClickCount == 2 && sender is FrameworkElement element &&
                ScheduleItemMenu.ResolveScheduleItem(element.DataContext) is ScheduleItem item)
            {
                EndDrag(commit: false); // 1クリック目で記録したドラッグ候補を破棄
                if (ViewModel.EditCommand.CanExecute(item))
                {
                    ViewModel.EditCommand.Execute(item);
                }
                e.Handled = true;
            }
            else if (e.ClickCount == 1 && sender is FrameworkElement el &&
                     el.DataContext is ScheduleSegment segment)
            {
                // ドラッグ候補の登録を先に済ませる。
                // 選択より後にすると、選択に伴う再レイアウトで el がツリーから外れ、
                // 親 Canvas を辿れずドラッグが登録されないことがある
                BeginPotentialDrag(el, segment, e);

                // クリックで選択する（キーボード操作の起点になる）
                ViewModel.SelectedItem = segment.Item;
                MainScrollViewer?.Focus();
            }
        }

        // ===== 出勤・退勤マーカー =====

        /// <summary>マーカーのラベルをクリック：その日の勤務時間を編集する</summary>
        private void WorkDayMarker_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
        {
            if (sender is FrameworkElement element && element.DataContext is WorkDayMarker marker)
            {
                ExecuteWorkDayCommand(ViewModel.EditWorkDayCommand, marker);
                e.Handled = true; // 背景の範囲ドラッグ（予定の新規作成）を始めさせない
            }
        }

        private void WorkDayEditMenuItem_Click(object sender, RoutedEventArgs e) =>
            ExecuteWorkDayMenuCommand(sender, ViewModel.EditWorkDayCommand);

        private void WorkDayDeleteMenuItem_Click(object sender, RoutedEventArgs e) =>
            ExecuteWorkDayMenuCommand(sender, ViewModel.DeleteWorkDayCommand);

        /// <summary>メニューを開いた要素から対象のマーカーを解決してコマンドを実行する</summary>
        private static void ExecuteWorkDayMenuCommand(object sender, System.Windows.Input.ICommand command)
        {
            if (sender is System.Windows.Controls.MenuItem menuItem &&
                menuItem.Parent is System.Windows.Controls.ContextMenu contextMenu &&
                contextMenu.PlacementTarget is FrameworkElement element &&
                element.DataContext is WorkDayMarker marker)
            {
                ExecuteWorkDayCommand(command, marker);
            }
        }

        private static void ExecuteWorkDayCommand(System.Windows.Input.ICommand command, WorkDayMarker marker)
        {
            if (command.CanExecute(marker)) command.Execute(marker);
        }

        // ===== ToDo チップ（終日行） =====

        /// <summary>チップのダブルクリックで ToDo の編集ダイアログを開く</summary>
        private void TodoChip_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
        {
            if (e.ClickCount != 2) return;
            if (sender is not FrameworkElement element || element.DataContext is not TodoChip chip) return;

            ExecuteTodoCommand(ViewModel.EditTodoCommand, chip.Todo);
            e.Handled = true;
        }

        /// <summary>チップから直接完了にする（パネルを開かずに片付けられるようにする）</summary>
        private void TodoChipCompleteMenuItem_Click(object sender, RoutedEventArgs e) =>
            ExecuteTodoMenuCommand(sender, ViewModel.ToggleTodoCompletedCommand);

        private void TodoChipRecordMenuItem_Click(object sender, RoutedEventArgs e) =>
            ExecuteTodoMenuCommand(sender, ViewModel.StartRecordingFromTodoCommand);

        private void TodoChipEditMenuItem_Click(object sender, RoutedEventArgs e) =>
            ExecuteTodoMenuCommand(sender, ViewModel.EditTodoCommand);

        private void TodoChipDueTomorrowMenuItem_Click(object sender, RoutedEventArgs e) =>
            ExecuteTodoMenuCommand(sender, ViewModel.SetTodoDueTomorrowCommand);

        private void TodoChipClearDueMenuItem_Click(object sender, RoutedEventArgs e) =>
            ExecuteTodoMenuCommand(sender, ViewModel.ClearTodoDueCommand);

        private void TodoChipDeleteMenuItem_Click(object sender, RoutedEventArgs e) =>
            ExecuteTodoMenuCommand(sender, ViewModel.DeleteTodoCommand);

        /// <summary>メニューを開いたチップから対象の ToDo を取り出す</summary>
        private static TodoItem? ResolveTodoFromMenu(object sender)
        {
            if (sender is System.Windows.Controls.MenuItem menuItem &&
                menuItem.Parent is System.Windows.Controls.ContextMenu contextMenu &&
                contextMenu.PlacementTarget is FrameworkElement element &&
                element.DataContext is TodoChip chip)
            {
                return chip.Todo;
            }
            return null;
        }

        private static void ExecuteTodoMenuCommand(object sender, System.Windows.Input.ICommand command)
        {
            if (ResolveTodoFromMenu(sender) is { } todo) ExecuteTodoCommand(command, todo);
        }

        private static void ExecuteTodoCommand(System.Windows.Input.ICommand command, TodoItem todo)
        {
            if (command.CanExecute(todo)) command.Execute(todo);
        }

        // ===== 記録漏れの帯 =====

        /// <summary>帯を右クリック →「ToDo から埋める」：その時間幅の実績を ToDo から作る</summary>
        private void FillGapMenuItem_Click(object sender, RoutedEventArgs e)
        {
            if (TryGetGap(sender, out var gap)) ViewModel.FillGapFromTodoPicker(gap);
        }

        /// <summary>その時間帯に使っていたアプリを手がかりに、記録を作り直す</summary>
        private void FillGapFromAppUsageMenuItem_Click(object sender, RoutedEventArgs e)
        {
            if (TryGetGap(sender, out var gap)) ViewModel.FillGapFromAppUsage(gap);
        }

        /// <summary>コンテキストメニューの発生元から、対象の未記録の帯を取り出す</summary>
        private static bool TryGetGap(object sender, out UnrecordedGap gap)
        {
            if (sender is System.Windows.Controls.MenuItem menuItem &&
                menuItem.Parent is System.Windows.Controls.ContextMenu contextMenu &&
                contextMenu.PlacementTarget is FrameworkElement { DataContext: UnrecordedGap found })
            {
                gap = found;
                return true;
            }

            gap = null!;
            return false;
        }

        // ===== コンテキストメニュー =====

        private void EditMenuItem_Click(object sender, RoutedEventArgs e) =>
            ScheduleItemMenu.ExecuteOnMenuTarget(sender, ViewModel.EditCommand);

        private void ResumeRecordingMenuItem_Click(object sender, RoutedEventArgs e) =>
            ScheduleItemMenu.ExecuteOnMenuTarget(sender, ViewModel.StartRecordingFromItemCommand);

        private void AppUsageMenuItem_Click(object sender, RoutedEventArgs e) =>
            ScheduleItemMenu.ExecuteOnMenuTarget(sender, ViewModel.ShowAppUsageCommand);

        private void DeleteMenuItem_Click(object sender, RoutedEventArgs e) =>
            ScheduleItemMenu.ExecuteOnMenuTarget(sender, ViewModel.DeleteCommand);
    }
}
