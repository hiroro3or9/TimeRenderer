using System;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;

using Cursors = System.Windows.Input.Cursors;
using Point = System.Windows.Point;
using MouseEventArgs = System.Windows.Input.MouseEventArgs;
using MouseButtonEventArgs = System.Windows.Input.MouseButtonEventArgs;
using MouseWheelEventArgs = System.Windows.Input.MouseWheelEventArgs;
using KeyEventArgs = System.Windows.Input.KeyEventArgs;

using TimeRenderer.Models;
using TimeRenderer.ViewModels;

namespace TimeRenderer.Views
{
    /// <summary>
    /// スプリントタイムラインビューの操作。
    ///
    /// - Ctrl+ホイール: ズーム（カーソル位置の時刻を固定したまま倍率を変える）
    /// - Shift+ホイール: 横スクロール
    /// - バーの端をつまんで伸縮、中央をつかんで移動（スナップ単位はズーム連動）
    /// - 空き領域の横ドラッグ: その期間で新規作成
    /// - キーボード: ←→ 選択移動 / Enter 編集 / Delete 削除 / Ctrl± ズーム / Home,T 今日 / F フィット
    ///
    /// 日/週ビューのドラッグ（MainWindow.Drag.cs）は Canvas と縦軸を前提にしているため、
    /// そちらを一般化せず、横軸専用の処理としてここに独立して実装している。
    /// </summary>
    public partial class MainWindow
    {
        private const double ZoomStepFactor = 1.15;
        private const double HorizontalScrollStep = 120.0;
        private const double DragThresholdX = 4.0;

        /// <summary>この幅未満のバーは端のつまみを出さず、常に移動として扱う</summary>
        private const double ResizeHandleMinBarWidth = 24.0;

        /// <summary>バー端のつまみ幅</summary>
        private const double ResizeHandleWidth = 6.0;

        private enum TimelineDragMode { None, Move, ResizeStart, ResizeEnd, CreateRange }

        private ScheduleItem? _tlDragItem;
        private TimelineDragMode _tlDragMode = TimelineDragMode.None;
        private bool _tlDragStarted;
        private double _tlDragStartX;
        private DateTime _tlDragOrigStart;
        private DateTime _tlDragOrigEnd;
        private DateTime _tlCreateAnchor;

        // ===== 初期化 =====

        private void InitializeTimelineHandlers(MainViewModel viewModel)
        {
            viewModel.TimelineScrollToItemRequested += OnTimelineScrollToItemRequested;
            viewModel.TimelineFitToItemRequested += OnTimelineFitToItemRequested;
        }

        private void DetachTimelineHandlers(MainViewModel viewModel)
        {
            viewModel.TimelineScrollToItemRequested -= OnTimelineScrollToItemRequested;
            viewModel.TimelineFitToItemRequested -= OnTimelineFitToItemRequested;
        }

        // ===== スクロールの同期 =====

        /// <summary>
        /// 本体の横スクロールに、ルーラーと最下部のヒートバーを追従させる。
        /// それぞれ独立した ScrollViewer なので、同期しないと時間軸がずれる。
        /// </summary>
        private void TimelineBodyScroll_ScrollChanged(object sender, ScrollChangedEventArgs e)
        {
            // 表示範囲を VM へ伝えて描画対象を絞り込ませる（幅の変化だけでも通知が要る）
            ViewModel.SetTimelineViewport(e.HorizontalOffset, e.ViewportWidth);

            if (e.HorizontalChange == 0 && e.ExtentWidthChange == 0) return;

            TimelineHeaderScroll?.ScrollToHorizontalOffset(e.HorizontalOffset);
            TimelineDensityScroll?.ScrollToHorizontalOffset(e.HorizontalOffset);
        }

        private void TimelineBodyScroll_PreviewMouseWheel(object sender, MouseWheelEventArgs e)
        {
            if (sender is not ScrollViewer scrollViewer) return;

            var modifiers = Keyboard.Modifiers;

            if (modifiers.HasFlag(ModifierKeys.Control))
            {
                ZoomTimelineAtCursor(scrollViewer, e.GetPosition(scrollViewer).X,
                                     e.Delta > 0 ? ZoomStepFactor : 1.0 / ZoomStepFactor);
                e.Handled = true;
                return;
            }

            if (modifiers.HasFlag(ModifierKeys.Shift))
            {
                double delta = e.Delta > 0 ? -HorizontalScrollStep : HorizontalScrollStep;
                scrollViewer.ScrollToHorizontalOffset(scrollViewer.HorizontalOffset + delta);
                e.Handled = true;
                return;
            }

            // 修飾キーなしは縦スクロール。
            // 入れ子の ScrollViewer はホイールイベントを飲み込むことがあるため、
            // 外側（縦スクロール担当）へ明示的に転送する。
            if (TimelineVerticalScroll != null)
            {
                e.Handled = true;
                var forwarded = new MouseWheelEventArgs(e.MouseDevice, e.Timestamp, e.Delta)
                {
                    RoutedEvent = UIElement.MouseWheelEvent,
                    Source = scrollViewer
                };
                TimelineVerticalScroll.RaiseEvent(forwarded);
            }
        }

        /// <summary>
        /// 指定したビューポート内X座標の時刻を固定したままズームする。
        /// 単純に倍率だけ変えると見ていた日付が画面外へ飛ぶため、
        /// ズーム後にその時刻が同じ画面位置へ来るようスクロール位置を補正する。
        /// </summary>
        private void ZoomTimelineAtCursor(ScrollViewer scrollViewer, double cursorInViewport, double factor)
        {
            var scale = ViewModel.CurrentTimelineScale;
            if (scale == null) return;

            var anchorTime = scale.ToTime(scrollViewer.HorizontalOffset + cursorInViewport);

            ViewModel.TimelinePixelsPerDay *= factor;

            var newScale = ViewModel.CurrentTimelineScale;
            if (newScale == null) return;

            double newOffset = Math.Max(0, newScale.ToX(anchorTime) - cursorInViewport);

            // 実体化する窓は倍率変更で無効化されている。
            // 実際にスクロールする前に移動先を教えて、正しい範囲を描かせる。
            ViewModel.SetTimelineViewport(newOffset, scrollViewer.ViewportWidth);

            ScrollHorizontallyAfterLayout(scrollViewer, newOffset);
        }

        /// <summary>
        /// レイアウト更新後にスクロールする。
        /// 直後に呼ぶと ScrollableWidth が古いままで、指定位置まで動かない。
        /// </summary>
        private void ScrollHorizontallyAfterLayout(ScrollViewer scrollViewer, double offset)
        {
            Dispatcher.BeginInvoke(
                new Action(() => scrollViewer.ScrollToHorizontalOffset(Math.Max(0, offset))),
                System.Windows.Threading.DispatcherPriority.Loaded);
        }

        // ===== 選択への追従 =====

        /// <summary>選択が変わったとき、そのアイテムが画面外なら見える位置までスクロールする</summary>
        private void OnTimelineScrollToItemRequested(object? sender, ScheduleItem item)
        {
            var scale = ViewModel.CurrentTimelineScale;
            if (scale == null || TimelineBodyScroll == null) return;

            double x = scale.ToX(item.StartTime);
            double viewportWidth = TimelineBodyScroll.ViewportWidth;
            double offset = TimelineBodyScroll.HorizontalOffset;

            const double margin = 60.0;

            if (x < offset + margin)
            {
                TimelineBodyScroll.ScrollToHorizontalOffset(Math.Max(0, x - margin));
            }
            else if (x > offset + viewportWidth - margin)
            {
                TimelineBodyScroll.ScrollToHorizontalOffset(x - viewportWidth + margin);
            }
        }

        /// <summary>F キー: 選択アイテムがビューポートの6割を占めるようズームして中央に置く</summary>
        private void OnTimelineFitToItemRequested(object? sender, ScheduleItem item)
        {
            if (TimelineBodyScroll == null) return;

            double viewportWidth = TimelineBodyScroll.ViewportWidth;
            if (viewportWidth <= 0) return;

            var duration = item.EndTime - item.StartTime;
            // 極端に短いアイテムでも見やすい倍率になるよう下限を設ける
            double days = Math.Max(duration.TotalDays, 1.0 / 24.0);

            ViewModel.TimelinePixelsPerDay = viewportWidth * 0.6 / days;

            var newScale = ViewModel.CurrentTimelineScale;
            if (newScale == null) return;

            double center = (newScale.ToX(item.StartTime) + newScale.ToX(item.EndTime)) / 2.0;
            ScrollHorizontallyAfterLayout(TimelineBodyScroll, center - (viewportWidth / 2.0));
        }

        // ===== キーボード =====

        private void TimelineRoot_PreviewKeyDown(object sender, KeyEventArgs e)
        {
            bool ctrl = Keyboard.Modifiers.HasFlag(ModifierKeys.Control);

            // 修飾キー付きのショートカット（Ctrl+F の検索などと衝突させないため先に判定する）
            if (ctrl)
            {
                if (e.Key is Key.OemPlus or Key.Add)
                {
                    ZoomFromViewportCenter(ZoomStepFactor);
                }
                else if (e.Key is Key.OemMinus or Key.Subtract)
                {
                    ZoomFromViewportCenter(1.0 / ZoomStepFactor);
                }
                else
                {
                    return; // その他の Ctrl 併用キーは既存のショートカットへ渡す
                }

                e.Handled = true;
                return;
            }

            switch (e.Key)
            {
                case Key.Left:
                    ViewModel.MoveSelectionCommand.Execute(-1);
                    break;
                case Key.Right:
                    ViewModel.MoveSelectionCommand.Execute(1);
                    break;
                case Key.Enter:
                    ViewModel.EditSelectedCommand.Execute(null);
                    break;
                case Key.Delete:
                    ViewModel.DeleteSelectedCommand.Execute(null);
                    break;
                case Key.F:
                    ViewModel.TimelineFitToSelectionCommand.Execute(null);
                    break;
                case Key.Home:
                case Key.T:
                    ScrollTimelineToToday();
                    break;

                default:
                    return; // 対象外のキーは他のハンドラへ渡す
            }

            e.Handled = true;
        }

        private void ZoomFromViewportCenter(double factor)
        {
            if (TimelineBodyScroll == null) return;
            ZoomTimelineAtCursor(TimelineBodyScroll, TimelineBodyScroll.ViewportWidth / 2.0, factor);
        }

        /// <summary>今日が画面中央に来るようスクロールする（範囲外なら何もしない）</summary>
        private void ScrollTimelineToToday()
        {
            var scale = ViewModel.CurrentTimelineScale;
            if (scale == null || TimelineBodyScroll == null) return;

            var now = DateTime.Now;
            if (now < scale.Origin || now >= scale.End) return;

            double center = scale.ToX(now) - (TimelineBodyScroll.ViewportWidth / 2.0);
            TimelineBodyScroll.ScrollToHorizontalOffset(Math.Max(0, center));
        }

        // ===== ドラッグ（移動・伸縮） =====

        /// <summary>
        /// スナップ単位。ズームが粗いときに15分刻みにしても操作できないので、
        /// 1ピクセルあたりの時間量に見合った粒度へ落とす。
        /// </summary>
        private TimeSpan GetSnapUnit(double pixelsPerDay)
        {
            // ズームが粗いときは、設定より細かく刻んでも操作できないので下限を設ける
            double zoomFloorMinutes = pixelsPerDay switch
            {
                >= 240 => 0,        // 最大ズーム時は設定値をそのまま使う
                >= 60 => 60,        // 1時間
                >= 20 => 360,       // 6時間
                _ => 1440           // 1日
            };

            return TimeSpan.FromMinutes(Math.Max(ViewModel.SnapMinutes, zoomFloorMinutes));
        }

        private static DateTime SnapTimelineTime(DateTime t, TimeSpan unit)
        {
            long ticks = (long)Math.Round(t.Ticks / (double)unit.Ticks) * unit.Ticks;
            return new DateTime(ticks, t.Kind);
        }

        /// <summary>バー内のX位置から操作種別を決める（両端はつまみ、中央は移動）</summary>
        private static TimelineDragMode GetTimelineZone(TimelineBar bar, double xInBar)
        {
            if (bar.DrawWidth < ResizeHandleMinBarWidth) return TimelineDragMode.Move;
            if (xInBar <= ResizeHandleWidth) return TimelineDragMode.ResizeStart;
            if (xInBar >= bar.DrawWidth - ResizeHandleWidth) return TimelineDragMode.ResizeEnd;
            return TimelineDragMode.Move;
        }

        /// <summary>
        /// 予定バーの押下。クリックで選択、ダブルクリックで編集、ドラッグで移動・伸縮。
        /// </summary>
        private void TimelineBar_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
        {
            if (sender is not FrameworkElement element) return;
            if (element.DataContext is not TimelineBar bar) return;

            if (e.ClickCount == 2)
            {
                CancelTimelineDrag();
                ViewModel.SelectedItem = bar.Item;
                if (ViewModel.EditCommand.CanExecute(bar.Item))
                {
                    ViewModel.EditCommand.Execute(bar.Item);
                }
                e.Handled = true;
                return;
            }

            if (TimelineSurface == null) return;

            // 掴んだ位置の情報を先に確定させてから選択する
            // （選択に伴う再描画で element が入れ替わっても影響を受けないように）
            _tlDragItem = bar.Item;
            _tlDragMode = GetTimelineZone(bar, e.GetPosition(element).X);
            _tlDragOrigStart = bar.Item.StartTime;
            _tlDragOrigEnd = bar.Item.EndTime;
            _tlDragStartX = e.GetPosition(TimelineSurface).X;
            _tlDragStarted = false;

            ViewModel.SelectedItem = bar.Item;
            TimelineVerticalScroll?.Focus();

            e.Handled = true;
        }

        /// <summary>
        /// 空き領域の押下。横にドラッグするとその期間で新規作成する。
        /// </summary>
        private void TimelineSurface_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
        {
            var scale = ViewModel.CurrentTimelineScale;
            if (scale == null || TimelineSurface == null) return;

            ViewModel.SelectedItem = null;
            TimelineVerticalScroll?.Focus();

            _tlDragItem = null;
            _tlDragMode = TimelineDragMode.CreateRange;
            _tlDragStartX = e.GetPosition(TimelineSurface).X;
            _tlCreateAnchor = SnapTimelineTime(scale.ToTime(_tlDragStartX), GetSnapUnit(scale.PixelsPerDay));
            _tlDragStarted = false;
        }

        private void TimelineSurface_MouseMove(object sender, MouseEventArgs e)
        {
            if (_tlDragMode == TimelineDragMode.None) return;
            if (TimelineSurface == null) return;

            if (e.LeftButton != MouseButtonState.Pressed)
            {
                CancelTimelineDrag();
                return;
            }

            double x = e.GetPosition(TimelineSurface).X;

            if (!_tlDragStarted)
            {
                if (Math.Abs(x - _tlDragStartX) < DragThresholdX) return;

                _tlDragStarted = true;

                // ドラッグ中はレーンの組み替えと装飾の再生成を止める
                // （掴んだバーが行を飛ぶのを防ぎ、再計算量も減らす）
                if (_tlDragMode != TimelineDragMode.CreateRange)
                {
                    ViewModel.BeginTimelineDragLayout();
                    if (_tlDragItem != null) ViewModel.BeginItemDragUndo(_tlDragItem);
                }

                Mouse.Capture(TimelineSurface);
                Mouse.OverrideCursor = _tlDragMode switch
                {
                    TimelineDragMode.Move => Cursors.SizeAll,
                    TimelineDragMode.CreateRange => Cursors.Cross,
                    _ => Cursors.SizeWE
                };
            }

            if (_tlDragMode == TimelineDragMode.CreateRange)
            {
                UpdateRangePreview(x);
            }
            else
            {
                ProcessTimelineDrag(x);
            }
        }

        private void TimelineSurface_MouseLeftButtonUp(object sender, MouseButtonEventArgs e)
        {
            if (_tlDragMode == TimelineDragMode.None) return;

            bool started = _tlDragStarted;
            var mode = _tlDragMode;
            double x = TimelineSurface != null ? e.GetPosition(TimelineSurface).X : 0;

            EndTimelineDrag();

            if (!started) return;

            if (mode == TimelineDragMode.CreateRange)
            {
                CommitRangeCreation(x);
            }
            else
            {
                ViewModel.CommitItemDrag();
            }
        }

        /// <summary>ドラッグ中のマウス位置から新しい時刻を求めて反映する</summary>
        private void ProcessTimelineDrag(double x)
        {
            var scale = ViewModel.CurrentTimelineScale;
            if (scale == null || _tlDragItem == null) return;

            var snap = GetSnapUnit(scale.PixelsPerDay);
            var delta = TimeSpan.FromDays((x - _tlDragStartX) / scale.PixelsPerDay);

            var newStart = _tlDragOrigStart;
            var newEnd = _tlDragOrigEnd;

            switch (_tlDragMode)
            {
                case TimelineDragMode.Move:
                    newStart = SnapTimelineTime(_tlDragOrigStart + delta, snap);
                    newEnd = newStart + (_tlDragOrigEnd - _tlDragOrigStart);
                    break;

                case TimelineDragMode.ResizeStart:
                    newStart = SnapTimelineTime(_tlDragOrigStart + delta, snap);
                    if (newStart > _tlDragOrigEnd - snap) newStart = _tlDragOrigEnd - snap;
                    break;

                case TimelineDragMode.ResizeEnd:
                    newEnd = SnapTimelineTime(_tlDragOrigEnd + delta, snap);
                    if (newEnd < _tlDragOrigStart + snap) newEnd = _tlDragOrigStart + snap;
                    break;

                default:
                    return;
            }

            ViewModel.UpdateItemTimesPreview(_tlDragItem, newStart, newEnd);
        }

        // ===== 範囲ドラッグでの新規作成 =====

        private void UpdateRangePreview(double x)
        {
            var scale = ViewModel.CurrentTimelineScale;
            if (scale == null || TimelineRangePreview == null) return;

            double left = Math.Min(_tlDragStartX, x);
            double width = Math.Abs(x - _tlDragStartX);

            TimelineRangePreview.Margin = new Thickness(left, 0, 0, 0);
            TimelineRangePreview.Width = width;
            TimelineRangePreview.Visibility = Visibility.Visible;
        }

        private void CommitRangeCreation(double x)
        {
            var scale = ViewModel.CurrentTimelineScale;
            if (scale == null) return;

            var snap = GetSnapUnit(scale.PixelsPerDay);
            var dropped = SnapTimelineTime(scale.ToTime(x), snap);

            var start = _tlCreateAnchor <= dropped ? _tlCreateAnchor : dropped;
            var end = _tlCreateAnchor <= dropped ? dropped : _tlCreateAnchor;

            if (end <= start) end = start + snap;

            var range = (start, end);
            if (ViewModel.AddScheduleItemInRangeCommand.CanExecute(range))
            {
                ViewModel.AddScheduleItemInRangeCommand.Execute(range);
            }
        }

        // ===== ドラッグ終了 =====

        private void EndTimelineDrag()
        {
            if (_tlDragStarted)
            {
                Mouse.Capture(null);
                Mouse.OverrideCursor = null;

                // レーンと装飾を組み直す（抑制中に溜まった変更をここで反映する）
                ViewModel.EndTimelineDragLayout();
            }

            if (TimelineRangePreview != null)
            {
                TimelineRangePreview.Visibility = Visibility.Collapsed;
                TimelineRangePreview.Width = 0;
            }

            _tlDragItem = null;
            _tlDragMode = TimelineDragMode.None;
            _tlDragStarted = false;
        }

        /// <summary>ドラッグを取り消し、プレビューで動かした時刻を元に戻す</summary>
        private void CancelTimelineDrag()
        {
            var item = _tlDragItem;
            bool started = _tlDragStarted;
            var origStart = _tlDragOrigStart;
            var origEnd = _tlDragOrigEnd;
            bool wasMoveOrResize = _tlDragMode is TimelineDragMode.Move
                or TimelineDragMode.ResizeStart or TimelineDragMode.ResizeEnd;

            EndTimelineDrag();

            if (started && item != null && wasMoveOrResize)
            {
                ViewModel.UpdateItemTimesPreview(item, origStart, origEnd);
            }

            // 取り消したドラッグは履歴に残さない
            ViewModel.ClearItemDragUndo();
        }
    }
}
