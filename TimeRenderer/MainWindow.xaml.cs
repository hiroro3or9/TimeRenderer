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
            var dialogService = new Services.DefaultDialogService(this);
            MainViewModel viewModel = new(dialogService);
            DataContext = viewModel;
            
            // ViewModelのプロパティ変更監視
            viewModel.PropertyChanged += ViewModel_PropertyChanged;

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
        /// スケジュールアイテムのダブルクリックイベント。
        /// </summary>
        private void ScheduleItem_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
        {
            if (e.ClickCount == 2 && sender is FrameworkElement element && element.DataContext is ScheduleItem item)
            {
                if (ViewModel.EditCommand.CanExecute(item))
                {
                    ViewModel.EditCommand.Execute(item);
                }
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
                element.DataContext is ScheduleItem item)
            {
                if (ViewModel.DeleteCommand.CanExecute(item))
                {
                    ViewModel.DeleteCommand.Execute(item);
                }
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