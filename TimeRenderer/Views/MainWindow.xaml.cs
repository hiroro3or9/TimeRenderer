using System.ComponentModel;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using WinForms = System.Windows.Forms;
using Drawing = System.Drawing;
using MenuItem = System.Windows.Controls.MenuItem;
using ContextMenu = System.Windows.Controls.ContextMenu;
using MessageBox = System.Windows.MessageBox;

using TimeRenderer.ViewModels;
using TimeRenderer.Models;
using TimeRenderer.Services;

namespace TimeRenderer.Views
{
    /// <summary>
    /// MainWindow のコードビハインド。
    /// スケジュールアイテムの追加・編集・削除のUIイベントを処理する。
    /// </summary>
    public partial class MainWindow : Window
    {
        private MainViewModel ViewModel => (MainViewModel)DataContext;
        private WinForms.NotifyIcon _notifyIcon = null!;
        private WinForms.ToolStripMenuItem _recordMenuItem = null!;
        private bool _isExiting = false;

        public MainWindow()
        {
            InitializeComponent();
            var dialogService = new Services.DefaultDialogService(this);
            MainViewModel viewModel = new(dialogService);
            DataContext = viewModel;
            
            // ViewModelのプロパティ変更監視
            viewModel.PropertyChanged += ViewModel_PropertyChanged;
            // 検索結果ジャンプ時のスクロール要求を購読
            viewModel.ScrollToTimeRequested += OnScrollToTimeRequested;

            SetupNotifyIcon();
        }

        private void SetupNotifyIcon()
        {
            _notifyIcon = new()
            {
                // アイコンはアプリケーションのアイコンがあればそれを使う、なければシステムアイコン
                Icon = Drawing.SystemIcons.Application,
                Visible = true,
                Text = "TimeRenderer"
            };

            // ダブルクリックでウィンドウ表示
            _notifyIcon.DoubleClick += (_, _) => ShowWindow();

            // コンテキストメニュー
            WinForms.ContextMenuStrip contextMenu = new();
            
            // 表示
            contextMenu.Items.Add("表示", null, (_, _) => ShowWindow());
            
            // 記録開始/停止
            _recordMenuItem = new("記録開始");
            _recordMenuItem.Click += (_, _) =>
            {
                if (ViewModel.ToggleRecordingCommand.CanExecute(null))
                {
                    ViewModel.ToggleRecordingCommand.Execute(null);
                }
            };
            contextMenu.Items.Add(_recordMenuItem);
            
            // セパレーター
            contextMenu.Items.Add(new WinForms.ToolStripSeparator());

            // 終了
            contextMenu.Items.Add("終了", null, (_, _) =>
            {
                // 記録中の場合は記録を保存してから終了する（黙って破棄しない）
                if (ViewModel.IsRecording && ViewModel.ToggleRecordingCommand.CanExecute(null))
                {
                    ViewModel.ToggleRecordingCommand.Execute(null);
                }

                _isExiting = true;
                _notifyIcon.Visible = false; // アイコンを消してから終了
                System.Windows.Application.Current.Shutdown();
            });

            _notifyIcon.ContextMenuStrip = contextMenu;
        }

        private void ViewModel_PropertyChanged(object? sender, PropertyChangedEventArgs e)
        {
            if (e.PropertyName == nameof(MainViewModel.IsRecording) || e.PropertyName == nameof(MainViewModel.RecordingDurationText))
            {
                UpdateContextMenu();
            }
        }


        private void UpdateContextMenu()
        {
            if (_recordMenuItem != null)
            {
                // UIスレッドで実行
                Dispatcher.Invoke(() =>
                {
                    if (ViewModel.IsRecording)
                    {
                        _recordMenuItem.Text = "■ 停止";
                        if (ViewModel.IsCountdownMode && ViewModel.CountdownRemaining.HasValue)
                        {
                            _notifyIcon.Text = $"TimeRenderer - 作業中 (残り {ViewModel.CountdownRemaining.Value:hh\\:mm\\:ss})"; 
                        }
                        else
                        {
                            _notifyIcon.Text = $"TimeRenderer - 素早く記録中 ({ViewModel.RecordingDuration:hh\\:mm\\:ss})"; 
                        }
                    }
                    else
                    {
                        _recordMenuItem.Text = "● 記録開始";
                        _notifyIcon.Text = "TimeRenderer";
                    }
                });
            }
        }

        private void ShowWindow()
        {
            Show();
            WindowState = WindowState.Normal;
            Activate();
        }

        protected override void OnStateChanged(EventArgs e)
        {
            if (WindowState == WindowState.Minimized)
            {
                // 最小化時はタスクバーから消す（トレイ常駐）
                Hide();
            }
            base.OnStateChanged(e);
        }

        protected override void OnClosing(CancelEventArgs e)
        {
            if (!_isExiting)
            {
                e.Cancel = true;
                Hide();
                return;
            }
            base.OnClosing(e);
        }

        protected override void OnClosed(EventArgs e)
        {
            if (_notifyIcon != null)
            {
                _notifyIcon.Visible = false;
                _notifyIcon.Dispose();
            }

            if (DataContext is MainViewModel vm)
            {
                 vm.FlushMemoSave(); // デバウンス中の未保存メモを書き込む
                 vm.PropertyChanged -= ViewModel_PropertyChanged;
                 vm.ScrollToTimeRequested -= OnScrollToTimeRequested;
            }

            base.OnClosed(e);
        }

        /// <summary>
        /// UI要素の DataContext から編集・削除対象の ScheduleItem を取り出す。
        /// 週/日ビューは日またぎ分割のため ScheduleSegment が DataContext になる。
        /// </summary>
        private static ScheduleItem? ResolveScheduleItem(object? dataContext) => dataContext switch
        {
            ScheduleItem item => item,
            ScheduleSegment segment => segment.Item,
            _ => null
        };

        /// <summary>
        /// 「＋追加」ボタンのクリックイベント。
        /// </summary>
        private void AddButton_Click(object sender, RoutedEventArgs e)
        {
            if (ViewModel.AddCommand.CanExecute(null))
            {
                ViewModel.AddCommand.Execute(null);
            }
        }

        /// <summary>
        /// スケジュールアイテムのダブルクリックイベント。(週表示/日表示用)
        /// </summary>
        private void ScheduleItem_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
        {
            if (e.ClickCount == 2 && sender is FrameworkElement element &&
                ResolveScheduleItem(element.DataContext) is ScheduleItem item)
            {
                if (ViewModel.EditCommand.CanExecute(item))
                {
                    ViewModel.EditCommand.Execute(item);
                }
                e.Handled = true;
            }
        }

        /// <summary>
        /// 月表示のカレンダーセル内、スケジュールアイテムがクリックされた時のイベント
        /// </summary>
        private void MonthCell_ItemClicked(object sender, Controls.ScheduleItemClickedEventArgs e)
        {
            if (ViewModel.EditCommand.CanExecute(e.Item))
            {
                ViewModel.EditCommand.Execute(e.Item);
            }
        }

        /// <summary>
        /// 月表示のカレンダーセル内、スケジュールアイテムが右クリックされた時のイベント
        /// </summary>
        private void MonthCell_ItemRightClicked(object sender, Controls.ScheduleItemClickedEventArgs e)
        {
            if (sender is FrameworkElement element)
            {
                ContextMenu contextMenu = new();
                
                MenuItem editItem = new() { Header = "編集" };
                editItem.Click += (s, args) => 
                {
                    if (ViewModel.EditCommand.CanExecute(e.Item))
                    {
                        ViewModel.EditCommand.Execute(e.Item);
                    }
                };
                
                MenuItem deleteItem = new() { Header = "削除" };
                deleteItem.Click += (s, args) => 
                {
                    if (ViewModel.DeleteCommand.CanExecute(e.Item))
                    {
                        ViewModel.DeleteCommand.Execute(e.Item);
                    }
                };

                contextMenu.Items.Add(editItem);
                contextMenu.Items.Add(deleteItem);
                
                contextMenu.IsOpen = true;
            }
        }

        /// <summary>
        /// 月表示のカレンダーのセル自体（空白部分）がダブルクリックされた時のイベント
        /// </summary>
        private void MonthCell_Clicked(object sender, RoutedEventArgs e)
        {
            // ルーティングイベントを CalendarGridView 上で購読しているため、
            // sender ではなく e.Source からセルを特定する
            if (e.Source is Controls.CalendarMonthCellControl cell && cell.CellData != null)
            {
                if (ViewModel.AddScheduleItemAtDateCommand.CanExecute(cell.CellData.Date))
                {
                    ViewModel.AddScheduleItemAtDateCommand.Execute(cell.CellData.Date);
                }
            }
        }

        /// <summary>
        /// コンテキストメニュー「編集」のクリックイベント
        /// </summary>
        private void EditMenuItem_Click(object sender, RoutedEventArgs e)
        {
            if (sender is MenuItem menuItem &&
                menuItem.Parent is ContextMenu contextMenu &&
                contextMenu.PlacementTarget is FrameworkElement element &&
                ResolveScheduleItem(element.DataContext) is ScheduleItem item)
            {
                if (ViewModel.EditCommand.CanExecute(item))
                {
                    ViewModel.EditCommand.Execute(item);
                }
            }
        }

        /// <summary>
        /// コンテキストメニュー「削除」のクリックイベント
        /// </summary>
        private void DeleteMenuItem_Click(object sender, RoutedEventArgs e)
        {
            if (sender is MenuItem menuItem &&
                menuItem.Parent is ContextMenu contextMenu &&
                contextMenu.PlacementTarget is FrameworkElement element &&
                ResolveScheduleItem(element.DataContext) is ScheduleItem item)
            {
                if (ViewModel.DeleteCommand.CanExecute(item))
                {
                    ViewModel.DeleteCommand.Execute(item);
                }
            }
        }

        /// <summary>
        /// 検索ボックスに再フォーカスした際、入力が残っていれば結果ポップアップを開き直す。
        /// </summary>
        private void SearchTextBox_GotKeyboardFocus(object sender, KeyboardFocusChangedEventArgs e)
        {
            // フォーカスを与えたクリック自体が「ポップアップ外のクリック」と誤判定され、
            // 開いた直後に即閉じてしまう（StaysOpen=False の競合）。
            // クリック（入力イベント）の処理が終わってから開くよう遅延させる。
            Dispatcher.BeginInvoke(new Action(() => ViewModel.ReopenSearchResultsIfAny()),
                System.Windows.Threading.DispatcherPriority.Background);
        }

        /// <summary>
        /// 検索結果から日ビューへジャンプした後、該当時刻が画面中央に来るようスクロールする。
        /// ビュー切替・レイアウト確定後に実行する必要があるため Dispatcher で遅延させる。
        /// </summary>
        private void OnScrollToTimeRequested(object? sender, DateTime time)
        {
            Dispatcher.BeginInvoke(new Action(() =>
            {
                const double pixelsPerHour = 60.0;
                double currentY = ((time.Hour - ViewModel.DisplayStartHour) * pixelsPerHour) + (time.Minute * (pixelsPerHour / 60.0));
                double targetOffset = currentY - (MainScrollViewer.ViewportHeight / 2);
                if (targetOffset < 0) targetOffset = 0;
                MainScrollViewer.ScrollToVerticalOffset(targetOffset);
            }), System.Windows.Threading.DispatcherPriority.Loaded);
        }

        private void Window_Loaded(object sender, RoutedEventArgs e)
        {
             // 起動時に現在時刻までスクロール
             var now = DateTime.Now;
             
             // 1時間 = 60px、表示開始時刻を基準にオフセット計算
             double pixelsPerHour = 60.0;
             double currentY = ((now.Hour - ViewModel.DisplayStartHour) * pixelsPerHour) + (now.Minute * (pixelsPerHour / 60.0));
             
             // 画面の中央に持ってくる
             double targetOffset = currentY - (MainScrollViewer.ViewportHeight / 2);
             
             if (targetOffset < 0) targetOffset = 0;
             
             MainScrollViewer.ScrollToVerticalOffset(targetOffset);
        }
    }
}