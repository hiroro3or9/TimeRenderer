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
            // リマインダー発生時、アプリが非アクティブならトレイのバルーン通知でも知らせる
            viewModel.PendingReminders.CollectionChanged += PendingReminders_CollectionChanged;

            // 各ビュー（DayWeekView / TimelineView 等）は自身の Loaded で VM のイベントを購読する
            SetupNotifyIcon();

            // 検索/フィルタのポップアップをトグルボタンの右端に揃えて表示する
            SearchFlyout.CustomPopupPlacementCallback = PlaceDropdownRightAligned;
            FilterPopup.CustomPopupPlacementCallback = PlaceDropdownRightAligned;
        }

        /// <summary>
        /// ポップアップをターゲット（トグルボタン）の下・右端揃えで配置する。
        /// 固定オフセットと違い、画面端での自動補正と干渉しない。
        /// </summary>
        private static System.Windows.Controls.Primitives.CustomPopupPlacement[] PlaceDropdownRightAligned(
            System.Windows.Size popupSize, System.Windows.Size targetSize, System.Windows.Point offset)
        {
            return
            [
                new System.Windows.Controls.Primitives.CustomPopupPlacement(
                    new System.Windows.Point(targetSize.Width - popupSize.Width, targetSize.Height + 4),
                    System.Windows.Controls.Primitives.PopupPrimaryAxis.Horizontal)
            ];
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

            // バルーン通知クリックでウィンドウ表示（リマインダーから記録開始ボタンへ誘導）
            _notifyIcon.BalloonTipClicked += (_, _) => ShowWindow();

            // コンテキストメニュー
            WinForms.ContextMenuStrip contextMenu = new();
            
            // 表示
            contextMenu.Items.Add("表示", null, (_, _) => ShowWindow());
            
            // 記録開始/停止（トレイからはダイアログを出さずに即開始する）
            _recordMenuItem = new("記録開始");
            _recordMenuItem.Click += (_, _) => ViewModel.QuickToggleRecording();
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
            else if (e.PropertyName == nameof(MainViewModel.AutoStartNotice))
            {
                // 自動記録開始をアプリ外でも知らせる（アプリを見ていないときこそ必要な通知）
                if (!string.IsNullOrEmpty(ViewModel.AutoStartNotice) && !IsActive)
                {
                    ShowTrayBalloon("自動記録開始", ViewModel.AutoStartNotice);
                }
            }
        }

        /// <summary>
        /// リマインダー追加時：アプリが非アクティブ（最小化・トレイ常駐・背面）なら
        /// トレイのバルーン通知でも知らせる。クリックでウィンドウが開き、バナーから記録開始できる。
        /// </summary>
        private void PendingReminders_CollectionChanged(object? sender, System.Collections.Specialized.NotifyCollectionChangedEventArgs e)
        {
            if (e.Action != System.Collections.Specialized.NotifyCollectionChangedAction.Add || e.NewItems == null) return;
            if (IsActive) return; // アプリを見ているときはアプリ内バナーで十分

            foreach (ScheduleItem item in e.NewItems)
            {
                ShowTrayBalloon($"「{item.Title}」の開始時刻です", "クリックすると記録を開始できます");
            }
        }

        /// <summary>トレイのバルーン通知（Windows 10/11 ではトースト通知として表示される）</summary>
        private void ShowTrayBalloon(string title, string text)
        {
            if (_notifyIcon is { Visible: true })
            {
                _notifyIcon.ShowBalloonTip(5000, title, text, WinForms.ToolTipIcon.Info);
            }
        }


        private void UpdateContextMenu()
        {
            if (_recordMenuItem != null)
            {
                // UIスレッドで実行
                Dispatcher.Invoke(() =>
                {
                    // 登録に成功したグローバルホットキーをメニューに併記する（診断も兼ねる）
                    var hotkeySuffix = RegisteredHotkeyText != null ? $" ({RegisteredHotkeyText})" : "";
                    if (ViewModel.IsRecording)
                    {
                        _recordMenuItem.Text = "■ 停止" + hotkeySuffix;
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
                        _recordMenuItem.Text = "● 記録開始" + hotkeySuffix;
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
                // トレイへ隠すだけで終了しないが、ユーザーの区切りではあるので
                // デバウンス中の未保存分をここで確定させておく
                if (DataContext is MainViewModel hiding)
                {
                    hiding.FlushMemoSave();
                    hiding.FlushDataSave();
                }

                e.Cancel = true;
                Hide();
                return;
            }
            base.OnClosing(e);
        }

        protected override void OnClosed(EventArgs e)
        {
            UnregisterGlobalHotkey();

            if (_notifyIcon != null)
            {
                _notifyIcon.Visible = false;
                _notifyIcon.Dispose();
            }

            if (DataContext is MainViewModel vm)
            {
                 vm.FlushMemoSave(); // デバウンス中の未保存メモを書き込む
                 vm.FlushDataSave(); // デバウンス中の未保存の予定データを書き込む
                 vm.PropertyChanged -= ViewModel_PropertyChanged;
                 vm.PendingReminders.CollectionChanged -= PendingReminders_CollectionChanged;
                 vm.DisposeAwayDetection(); // 監視タイマーと SystemEvents の購読を解除する
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
                
                MenuItem resumeItem = new() { Header = "この内容で記録開始" };
                resumeItem.Click += (s, args) =>
                {
                    if (ViewModel.StartRecordingFromItemCommand.CanExecute(e.Item))
                    {
                        ViewModel.StartRecordingFromItemCommand.Execute(e.Item);
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
                contextMenu.Items.Add(resumeItem);
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
        /// 検索ボックスに再フォーカスした際、入力が残っていれば結果ポップアップを開き直す。
        /// </summary>
        // ===== 検索フライアウト / 色フィルタ ポップアップ =====

        // StaysOpen=False のポップアップは「外側クリック」で先に閉じるため、
        // トグルボタン自身をクリックして閉じようとすると
        // 閉じる → 同じクリックで再チェック → 即再オープン、となり閉じられない。
        // 直前にポップアップが閉じた時刻を覚えておき、その直後のクリックを無効化する。
        private DateTime _searchFlyoutClosedAt;
        private DateTime _filterPopupClosedAt;

        private static bool JustClosed(DateTime closedAt) =>
            (DateTime.UtcNow - closedAt).TotalMilliseconds < 250;

        private void SearchFlyout_Closed(object? sender, EventArgs e) => _searchFlyoutClosedAt = DateTime.UtcNow;
        private void FilterPopup_Closed(object? sender, EventArgs e) => _filterPopupClosedAt = DateTime.UtcNow;

        private void SearchToggle_PreviewMouseLeftButtonDown(object sender, MouseButtonEventArgs e)
        {
            if (JustClosed(_searchFlyoutClosedAt)) e.Handled = true;
        }

        private void FilterToggle_PreviewMouseLeftButtonDown(object sender, MouseButtonEventArgs e)
        {
            if (JustClosed(_filterPopupClosedAt)) e.Handled = true;
        }

        /// <summary>検索トグルON：フライアウトを開いて入力フォーカスを移す</summary>
        private void SearchToggle_Checked(object sender, RoutedEventArgs e)
        {
            Dispatcher.BeginInvoke(new Action(() =>
            {
                SearchTextBox.Focus();
                SearchTextBox.SelectAll();
            }), System.Windows.Threading.DispatcherPriority.Input);
        }

        /// <summary>Esc キーで検索フライアウトを閉じる</summary>
        private void SearchFlyout_PreviewKeyDown(object sender, System.Windows.Input.KeyEventArgs e)
        {
            if (e.Key == Key.Escape)
            {
                SearchToggle.IsChecked = false;
                e.Handled = true;
            }
        }

        /// <summary>検索結果クリック：ジャンプ実行後にフライアウトを閉じる</summary>
        private void SearchResultButton_Click(object sender, RoutedEventArgs e)
        {
            // コマンド（ジャンプ）の実行が終わってから閉じる
            Dispatcher.BeginInvoke(new Action(() => SearchToggle.IsChecked = false));
        }

    }
}