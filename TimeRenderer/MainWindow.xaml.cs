using System.ComponentModel;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using WinForms = System.Windows.Forms;
using Drawing = System.Drawing;
using MenuItem = System.Windows.Controls.MenuItem;
using ContextMenu = System.Windows.Controls.ContextMenu;
using MessageBox = System.Windows.MessageBox;

namespace TimeRenderer
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

        public MainWindow()
        {
            InitializeComponent();
            var viewModel = new MainViewModel();
            DataContext = viewModel;
            
            // ViewModelのプロパティ変更監視
            viewModel.PropertyChanged += ViewModel_PropertyChanged;

            SetupNotifyIcon();
        }

        private void SetupNotifyIcon()
        {
            _notifyIcon = new WinForms.NotifyIcon
            {
                // アイコンはアプリケーションのアイコンがあればそれを使う、なければシステムアイコン
                Icon = Drawing.SystemIcons.Application,
                Visible = true,
                Text = "TimeRenderer"
            };

            // ダブルクリックでウィンドウ表示
            _notifyIcon.DoubleClick += (s, e) => ShowWindow();

            // コンテキストメニュー
            var contextMenu = new WinForms.ContextMenuStrip();
            
            // 表示
            contextMenu.Items.Add("表示", null, (s, e) => ShowWindow());
            
            // 記録開始/停止
            _recordMenuItem = new WinForms.ToolStripMenuItem("記録開始");
            _recordMenuItem.Click += (s, e) =>
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
            contextMenu.Items.Add("終了", null, (s, e) => 
            {
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
                        _notifyIcon.Text = $"TimeRenderer - 素早く記録中 ({ViewModel.RecordingDuration:hh\\:mm\\:ss})"; 
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

        protected override void OnClosed(EventArgs e)
        {
            if (_notifyIcon != null)
            {
                _notifyIcon.Visible = false;
                _notifyIcon.Dispose();
            }
            
            if (DataContext is MainViewModel vm)
            {
                 vm.PropertyChanged -= ViewModel_PropertyChanged;
            }

            base.OnClosed(e);
        }

        /// <summary>
        /// 「＋追加」ボタンのクリックイベント。新規追加ダイアログを開く。
        /// </summary>
        private void AddButton_Click(object sender, RoutedEventArgs e)
        {
            var dialog = new ScheduleEditDialog()
            {
                Owner = this
            };

            if (dialog.ShowDialog() == true && dialog.ResultItem != null)
            {
                ViewModel.ScheduleItems.Add(dialog.ResultItem);
            }
        }

        /// <summary>
        /// スケジュールアイテムのダブルクリックイベント。編集ダイアログを開く。
        /// </summary>
        private void ScheduleItem_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
        {
            // ダブルクリックの場合のみ編集ダイアログを開く
            if (e.ClickCount == 2 && sender is FrameworkElement element && element.DataContext is ScheduleItem item)
            {
                OpenEditDialog(item);
                e.Handled = true;
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
                element.DataContext is ScheduleItem item)
            {
                OpenEditDialog(item);
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
                element.DataContext is ScheduleItem item)
            {
                var result = MessageBox.Show(
                    $"「{item.Title}」を削除しますか？",
                    "削除確認",
                    MessageBoxButton.YesNo,
                    MessageBoxImage.Question);

                if (result == MessageBoxResult.Yes)
                {
                    ViewModel.ScheduleItems.Remove(item);
                }
            }
        }

        /// <summary>
        /// 編集ダイアログを開き、結果を既存アイテムに反映する
        /// </summary>
        private void OpenEditDialog(ScheduleItem item)
        {
            var dialog = new ScheduleEditDialog(item)
            {
                Owner = this
            };

            if (dialog.ShowDialog() == true && dialog.ResultItem != null)
            {
                // 既存アイテムのプロパティを更新（INotifyPropertyChanged により自動反映）
                item.Title = dialog.ResultItem.Title;
                item.Content = dialog.ResultItem.Content;
                item.StartTime = dialog.ResultItem.StartTime;
                item.EndTime = dialog.ResultItem.EndTime;
                item.IsAllDay = dialog.ResultItem.IsAllDay;
                item.BackgroundColor = dialog.ResultItem.BackgroundColor;
            }
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