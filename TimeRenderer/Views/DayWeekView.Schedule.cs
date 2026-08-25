using System;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;

using Cursors = System.Windows.Input.Cursors;
using MouseEventArgs = System.Windows.Input.MouseEventArgs;
using MouseButtonEventArgs = System.Windows.Input.MouseButtonEventArgs;
using KeyEventArgs = System.Windows.Input.KeyEventArgs;
using ContextMenu = System.Windows.Controls.ContextMenu;
using MenuItem = System.Windows.Controls.MenuItem;

using TimeRenderer.Models;

namespace TimeRenderer.Views
{
    /// <summary>
    /// 日/週ビューの操作をタイムラインと揃えるための処理。
    ///
    /// - キーボード: ↑↓ 選択移動 / ←→ 日付移動 / Enter 編集 / F2 名称変更 / Delete 削除 / Esc 選択解除
    /// - キーボード（Ctrl 併用）: Ctrl+D 複製 / Ctrl+C コピー / Ctrl+V 貼り付け
    /// - 空き領域の縦ドラッグ: その時間帯で新規作成（バー上でそのままタイトルを入力する）
    /// - 空き領域の右クリック: コピー済みの内容をその時刻へ貼り付け
    ///
    /// 予定バーのドラッグ（移動・伸縮・複製）は DayWeekView.Drag.cs が担当する。
    /// こちらは「何もない場所」を起点にした操作だけを扱う。
    /// </summary>
    public partial class DayWeekView
    {
        /// <summary>この距離を超えて動いたら範囲ドラッグとみなす</summary>
        private const double RangeDragThresholdY = 5.0;

        /// <summary>新規作成の刻み幅（分）。設定で変更できる</summary>
        private int RangeSnapMinutes => ViewModel.SnapMinutes;

        private Grid? _rangeSurface;
        private bool _rangeDragActive;
        private bool _rangeDragStarted;
        private double _rangeStartY;
        private DateTime _rangeAnchor;

        /// <summary>右クリックした位置の時刻（「ここに貼り付け」の貼り付け先）</summary>
        private DateTime? _contextMenuTime;

        private static void Execute(System.Windows.Input.ICommand command)
        {
            if (command.CanExecute(null)) command.Execute(null);
        }

        // ===== キーボード =====

        /// <summary>
        /// 日/週ビューのキーボード操作。
        /// 時間軸が縦なので、選択の移動は上下、日付の移動は左右に割り当てる。
        /// </summary>
        private void ScheduleView_PreviewKeyDown(object sender, KeyEventArgs e)
        {
            // 日/週ビュー以外が手前に出ているときは何もしない
            // （この ScrollViewer は他のビューに覆われても木構造には残るため）
            if (!ViewModel.IsDayMode && !ViewModel.IsWeekMode) return;

            // テキスト入力中は横取りしない（インライン入力中もここで抜ける）
            if (Keyboard.FocusedElement is System.Windows.Controls.Primitives.TextBoxBase) return;

            if (Keyboard.Modifiers.HasFlag(ModifierKeys.Control))
            {
                // Ctrl+Z / Ctrl+Y はウィンドウ側で処理済み。ここでは複製・コピー系だけを見る
                switch (e.Key)
                {
                    case Key.D:
                        ViewModel.DuplicateItemCommand.Execute(null);
                        break;
                    case Key.C:
                        ViewModel.CopyItemCommand.Execute(null);
                        break;
                    case Key.V:
                        ViewModel.PasteItemCommand.Execute(GetPointerTime());
                        break;
                    default:
                        return; // その他の Ctrl 併用キーは既存のショートカットへ渡す
                }

                e.Handled = true;
                return;
            }

            switch (e.Key)
            {
                case Key.Up:
                    ViewModel.MoveSelectionCommand.Execute(-1);
                    break;
                case Key.Down:
                    ViewModel.MoveSelectionCommand.Execute(1);
                    break;

                case Key.Left:
                    Execute(ViewModel.PreviousCommand);
                    break;
                case Key.Right:
                    Execute(ViewModel.NextCommand);
                    break;

                case Key.Enter:
                    ViewModel.EditSelectedCommand.Execute(null);
                    break;

                case Key.F2:
                    if (ViewModel.SelectedItem is { } selected) StartInlineRename(selected);
                    break;

                case Key.Delete:
                    ViewModel.DeleteSelectedCommand.Execute(null);
                    break;
                case Key.Escape:
                    ViewModel.ClearSelectionCommand.Execute(null);
                    break;

                case Key.T:
                    Execute(ViewModel.TodayCommand);
                    break;

                default:
                    return;
            }

            e.Handled = true;
        }

        /// <summary>
        /// マウスが描画面の上にあれば、その位置の時刻を返す（貼り付け先に使う）。
        /// 外にあるときは null を返し、貼り付け先の判断は ViewModel に委ねる。
        /// </summary>
        private DateTime? GetPointerTime()
        {
            var surface = FindScheduleSurface();
            if (surface == null || surface.ActualWidth <= 0) return null;

            var pos = Mouse.GetPosition(surface);
            if (pos.X < 0 || pos.Y < 0 || pos.X > surface.ActualWidth || pos.Y > surface.ActualHeight)
            {
                return null;
            }

            var date = ResolveDateFromX(surface, pos.X);
            if (date == null) return null;

            return date.Value.AddHours(SnapHours(pos.Y, 60.0 / RangeSnapMinutes));
        }

        // ===== 空き領域の縦ドラッグで新規作成 =====

        /// <summary>
        /// 背景の押下。ダブルクリックは1時間の予定を作り、
        /// 縦にドラッグした場合はその範囲で作る。どちらもバー上でタイトルを直接入力する。
        /// </summary>
        private void ScheduleBackground_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
        {
            if (sender is not Grid grid || grid.ActualWidth <= 0) return;

            // 予定バー上のクリックは対象外（バー側の編集・ドラッグを優先）
            if (e.OriginalSource is DependencyObject src && FindScheduleItemRoot(src) != null) return;

            // インライン入力中なら、まず入力を確定させる
            if (IsInlineEditing) CloseInlineEditor(commit: true);

            MainScrollViewer?.Focus();

            var pos = e.GetPosition(grid);
            var date = ResolveDateFromX(grid, pos.X);
            if (date == null) return;

            if (e.ClickCount == 2)
            {
                CancelRangeDrag();

                // 刻み幅で丸めた開始時刻から、従来どおり1時間の予定を作る
                var start = date.Value.AddHours(SnapHours(pos.Y, 60.0 / RangeSnapMinutes));
                StartInlineCreate(grid, start, start.AddHours(1));
                e.Handled = true;
                return;
            }

            // 選択を外し、範囲ドラッグの候補として記録する
            ViewModel.ClearSelectionCommand.Execute(null);

            _rangeSurface = grid;
            _rangeDragActive = true;
            _rangeDragStarted = false;
            _rangeStartY = pos.Y;
            _rangeAnchor = date.Value.AddHours(SnapHours(pos.Y, 60.0 / RangeSnapMinutes));
        }

        private void ScheduleBackground_MouseMove(object sender, MouseEventArgs e)
        {
            if (!_rangeDragActive || _rangeSurface == null) return;

            if (e.LeftButton != MouseButtonState.Pressed)
            {
                CancelRangeDrag();
                return;
            }

            double y = e.GetPosition(_rangeSurface).Y;

            if (!_rangeDragStarted)
            {
                if (Math.Abs(y - _rangeStartY) < RangeDragThresholdY) return;

                _rangeDragStarted = true;
                Mouse.Capture(_rangeSurface);
                Mouse.OverrideCursor = Cursors.Cross;
            }

            UpdateRangeDragPreview(y);
        }

        private void ScheduleBackground_MouseLeftButtonUp(object sender, MouseButtonEventArgs e)
        {
            if (!_rangeDragActive) return;

            bool started = _rangeDragStarted;
            double y = _rangeSurface != null ? e.GetPosition(_rangeSurface).Y : 0;
            var surface = _rangeSurface;

            CancelRangeDrag();

            if (!started || surface == null) return;

            CommitRangeDrag(surface, y);
        }

        private Border? _rangePreview;

        /// <summary>
        /// ドラッグ中の範囲を示す矩形を用意する。
        ///
        /// 日/週ビューの描画面は DataTemplate の中にあり、x:Name を付けても
        /// コードビハインドからは参照できない。そのため矩形は実行時に生成して
        /// 描画面へ足す。日付を移動すると描画面ごと作り直されるので、
        /// 親が変わっていたら作り直す。
        /// </summary>
        private Border EnsureRangePreview(Grid surface)
        {
            if (_rangePreview != null && surface.Children.Contains(_rangePreview))
            {
                return _rangePreview;
            }

            _rangePreview = new Border
            {
                IsHitTestVisible = false,
                VerticalAlignment = System.Windows.VerticalAlignment.Top,
                HorizontalAlignment = System.Windows.HorizontalAlignment.Stretch,
                Opacity = 0.35,
                BorderThickness = new Thickness(1),
                Visibility = Visibility.Collapsed
            };

            _rangePreview.SetResourceReference(Border.BackgroundProperty, "PrimaryBrush");
            _rangePreview.SetResourceReference(Border.BorderBrushProperty, "TextPrimaryBrush");

            surface.Children.Add(_rangePreview);
            return _rangePreview;
        }

        /// <summary>ドラッグ中の範囲を半透明の矩形で示す</summary>
        private void UpdateRangeDragPreview(double y)
        {
            if (_rangeSurface == null) return;

            var preview = EnsureRangePreview(_rangeSurface);

            double top = Math.Min(_rangeStartY, y);
            double height = Math.Abs(y - _rangeStartY);

            // 週ビューではドラッグ中の列だけを塗る
            var days = ViewModel.VisibleDays;
            if (days.Count > 1 && _rangeSurface.ActualWidth > 0)
            {
                double columnWidth = _rangeSurface.ActualWidth / days.Count;
                int column = days.ToList().FindIndex(d => d.Date == _rangeAnchor.Date);
                if (column >= 0)
                {
                    preview.HorizontalAlignment = System.Windows.HorizontalAlignment.Left;
                    preview.Width = columnWidth;
                    preview.Margin = new Thickness(column * columnWidth, top, 0, 0);
                    preview.Height = height;
                    preview.Visibility = Visibility.Visible;
                    return;
                }
            }

            preview.HorizontalAlignment = System.Windows.HorizontalAlignment.Stretch;
            preview.Width = double.NaN;
            preview.Margin = new Thickness(0, top, 0, 0);
            preview.Height = height;
            preview.Visibility = Visibility.Visible;
        }

        private void CommitRangeDrag(Grid surface, double y)
        {
            // 日付はドラッグ開始時の列で固定する
            // （週ビューで縦にドラッグする間、横に少し動いても日が変わらないように）
            var baseDate = _rangeAnchor.Date;

            var dropped = baseDate.AddHours(SnapHours(y, 60.0 / RangeSnapMinutes));

            var start = _rangeAnchor <= dropped ? _rangeAnchor : dropped;
            var end = _rangeAnchor <= dropped ? dropped : _rangeAnchor;

            if (end <= start) end = start.AddMinutes(RangeSnapMinutes);

            StartInlineCreate(surface, start, end);
        }

        private void CancelRangeDrag()
        {
            if (_rangeDragStarted)
            {
                Mouse.Capture(null);
                Mouse.OverrideCursor = null;
            }

            if (_rangePreview != null)
            {
                _rangePreview.Visibility = Visibility.Collapsed;
                _rangePreview.Height = 0;
            }

            _rangeDragActive = false;
            _rangeDragStarted = false;
            _rangeSurface = null;
        }

        // ===== 空き領域の右クリック（貼り付け） =====

        /// <summary>
        /// 右クリックした時刻を控え、コピー済みの内容が無いときは項目を無効にする。
        /// 描画面は DataTemplate の中にあり x:Name を付けられないため、
        /// メニュー項目はここで辿って状態を変える。
        /// </summary>
        private void ScheduleBackground_ContextMenuOpening(object sender, ContextMenuEventArgs e)
        {
            if (sender is not Grid grid || grid.ActualWidth <= 0)
            {
                _contextMenuTime = null;
                return;
            }

            var pos = Mouse.GetPosition(grid);
            var date = ResolveDateFromX(grid, pos.X);
            _contextMenuTime = date?.AddHours(SnapHours(pos.Y, 60.0 / RangeSnapMinutes));

            if (grid.ContextMenu is ContextMenu menu)
            {
                foreach (var entry in menu.Items)
                {
                    if (entry is not MenuItem menuItem) continue;
                    menuItem.IsEnabled = ViewModel.HasClipboardItem && _contextMenuTime != null;
                    menuItem.Header = ViewModel.HasClipboardItem
                        ? $"ここに貼り付け（{ViewModel.ClipboardItemTitle}）"
                        : "ここに貼り付け";
                }
            }
        }

        private void PasteHereMenuItem_Click(object sender, RoutedEventArgs e)
        {
            if (_contextMenuTime is not { } at) return;
            if (ViewModel.PasteItemCommand.CanExecute(at)) ViewModel.PasteItemCommand.Execute(at);
        }

        // ===== ToDo のドロップ（作業時間の確保） =====

        private void ScheduleBackground_DragOver(object sender, System.Windows.DragEventArgs e)
        {
            e.Effects = e.Data.GetDataPresent(typeof(TodoItem))
                ? System.Windows.DragDropEffects.Copy
                : System.Windows.DragDropEffects.None;
            e.Handled = true;
        }

        /// <summary>
        /// ToDo を落とした位置から予定を作る。
        /// 長さは ViewModel 側で決める（見積もり時間、無ければ1時間）。
        /// </summary>
        private void ScheduleBackground_Drop(object sender, System.Windows.DragEventArgs e)
        {
            if (sender is not Grid grid || grid.ActualWidth <= 0) return;
            if (e.Data.GetData(typeof(TodoItem)) is not TodoItem todo) return;

            var pos = e.GetPosition(grid);
            var date = ResolveDateFromX(grid, pos.X);
            if (date == null) return;

            var start = date.Value.AddHours(SnapHours(pos.Y, 60.0 / RangeSnapMinutes));
            ViewModel.BlockTimeForTodo(todo, start);
            e.Handled = true;
        }

        // ===== 座標の解決 =====

        /// <summary>X座標から日付列を求める（週ビューでは列が日付に対応する）</summary>
        private DateTime? ResolveDateFromX(Grid grid, double x)
        {
            var days = ViewModel.VisibleDays;
            if (days.Count == 0 || grid.ActualWidth <= 0) return null;

            double columnWidth = grid.ActualWidth / days.Count;
            int column = Math.Clamp((int)(x / columnWidth), 0, days.Count - 1);
            return days[column].Date;
        }

        /// <summary>
        /// Y座標を時刻（時間単位）へ変換し、指定の刻みでスナップする。
        /// </summary>
        /// <param name="stepsPerHour">1時間あたりの分割数（2なら30分、4なら15分刻み）</param>
        private double SnapHours(double y, double stepsPerHour)
        {
            double hours = (y / PixelsPerHour) + ViewModel.DisplayStartHour;
            double snapped = Math.Floor(hours * stepsPerHour) / stepsPerHour;
            return Math.Clamp(snapped, 0, 24 - (1.0 / stepsPerHour));
        }
    }
}
