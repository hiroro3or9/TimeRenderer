using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using Cursors = System.Windows.Input.Cursors;
using Point = System.Windows.Point;
using MouseEventArgs = System.Windows.Input.MouseEventArgs;
using MouseButtonEventArgs = System.Windows.Input.MouseButtonEventArgs;

using TimeRenderer.Models;

namespace TimeRenderer.Views
{
    /// <summary>
    /// 日/週ビューの予定バーのドラッグ操作（移動・リサイズ）。
    /// - バー中央を上下ドラッグ: 時刻変更（刻み幅は設定で変更可能）
    /// - 別の日の列へドラッグ: 日付変更
    /// - バーの上端/下端をつまむ: 開始/終了時刻の伸縮
    /// ドラッグ中は VM の UpdateItemTimesPreview で再レイアウトのみ行い、
    /// マウスアップ時に CommitItemDrag で1回だけ保存する。
    /// </summary>
    public partial class DayWeekView
    {
        private enum DragMode { None, Move, ResizeTop, ResizeBottom }

        private const double PixelsPerHour = Helpers.LayoutConstants.PixelsPerHour;
        private const double DragThresholdPx = 4.0;   // この距離を超えて動いたらドラッグ開始
        /// <summary>時刻の丸め単位（設定で変更できる）</summary>
        private int SnapMinutes => ViewModel.SnapMinutes;

        /// <summary>ドラッグで作れる最小の長さ。刻み幅と同じにする</summary>
        private TimeSpan MinDragDuration => TimeSpan.FromMinutes(SnapMinutes);

        private ScheduleItem? _dragItem;
        private DragMode _dragMode = DragMode.None;
        private bool _dragStarted;
        private Point _dragStartPos;            // ドラッグ開始位置（Canvas座標）
        private Canvas? _dragCanvas;            // 予定バーを載せているCanvas（再レイアウト後も生存）
        private DateTime _dragOrigStart;
        private DateTime _dragOrigEnd;
        private int _dragOrigColumn = -1;       // 掴んだセグメントの列インデックス

        /// <summary>コンストラクタから呼ぶ：ビュー全体のドラッグ用トンネルイベントを購読する</summary>
        private void InitializeDragHandlers()
        {
            PreviewMouseMove += View_PreviewMouseMoveForDrag;
            PreviewMouseLeftButtonUp += View_PreviewMouseLeftButtonUpForDrag;
        }

        /// <summary>
        /// アイテム上でのマウス押下時に「ドラッグ候補」として記録する。
        /// 実際のドラッグ開始は閾値を超えて動いてから（クリック・ダブルクリックと共存させるため）。
        /// </summary>
        private void BeginPotentialDrag(FrameworkElement element, ScheduleSegment segment, MouseButtonEventArgs e)
        {
            var canvas = FindAncestor<Canvas>(element);
            if (canvas == null) return;

            _dragCanvas = canvas;
            _dragItem = segment.Item;
            _dragOrigStart = segment.Item.StartTime;
            _dragOrigEnd = segment.Item.EndTime;
            _dragStartPos = e.GetPosition(canvas);
            _dragStarted = false;
            _dragMode = GetZone(element, e.GetPosition(element).Y);

            // 掴んだセグメントの日付が表示上どの列かを調べる（週ビューの日付変更用）
            _dragOrigColumn = -1;
            var segmentDate = segment.StartTime.Date;
            for (int i = 0; i < ViewModel.VisibleDays.Count; i++)
            {
                if (ViewModel.VisibleDays[i].Date == segmentDate)
                {
                    _dragOrigColumn = i;
                    break;
                }
            }
        }

        /// <summary>要素内のY座標からドラッグ種別を判定する（上端/下端はリサイズ）</summary>
        private static DragMode GetZone(FrameworkElement element, double y)
        {
            double h = element.ActualHeight;
            if (h <= 0) return DragMode.Move;

            double topZone = Math.Min(8, h / 3);
            // 下端はL字型の15px拡張部を含めて判定する
            double bottomZone = Math.Min(23, h / 3);

            if (y <= topZone) return DragMode.ResizeTop;
            if (y >= h - bottomZone) return DragMode.ResizeBottom;
            return DragMode.Move;
        }

        private void View_PreviewMouseMoveForDrag(object sender, MouseEventArgs e)
        {
            if (_dragItem == null || _dragCanvas == null)
            {
                // ドラッグ中でなければ、予定バー上のホバー位置に応じてカーソルだけ更新する
                UpdateHoverCursor(e);
                return;
            }

            if (e.LeftButton != MouseButtonState.Pressed)
            {
                EndDrag(commit: false);
                return;
            }

            var pos = e.GetPosition(_dragCanvas);

            if (!_dragStarted)
            {
                if (Math.Abs(pos.X - _dragStartPos.X) < DragThresholdPx &&
                    Math.Abs(pos.Y - _dragStartPos.Y) < DragThresholdPx)
                {
                    return;
                }
                _dragStarted = true;

                // 取り消し用に、ドラッグ開始前の状態を控える
                if (_dragItem != null) ViewModel.BeginItemDragUndo(_dragItem);

                // キャプチャ先の canvas に直接イベントを購読する
                // （キャプチャ後のマウスイベントはキャプチャ要素に確実に届くため）
                _dragCanvas.MouseMove += DragCanvas_MouseMove;
                _dragCanvas.MouseLeftButtonUp += DragCanvas_MouseLeftButtonUp;
                _dragCanvas.LostMouseCapture += DragCanvas_LostMouseCapture;
                Mouse.Capture(_dragCanvas);

                Mouse.OverrideCursor = _dragMode == DragMode.Move ? Cursors.SizeAll : Cursors.SizeNS;
            }

            ProcessDragMove(pos);
        }

        /// <summary>ドラッグ中（キャプチャ後）のマウス移動：canvas 側で受け取る</summary>
        private void DragCanvas_MouseMove(object sender, MouseEventArgs e)
        {
            if (!_dragStarted || _dragCanvas == null) return;
            ProcessDragMove(e.GetPosition(_dragCanvas));
        }

        /// <summary>ドラッグ中のマウスアップ：canvas 側で受け取り、確定する</summary>
        private void DragCanvas_MouseLeftButtonUp(object sender, MouseButtonEventArgs e)
        {
            EndDrag(commit: true);
        }

        /// <summary>キャプチャが外部要因で失われた場合（Alt+Tab等）：現在の状態で確定する</summary>
        private void DragCanvas_LostMouseCapture(object sender, MouseEventArgs e)
        {
            if (_dragStarted)
            {
                EndDrag(commit: true);
            }
        }

        /// <summary>現在のマウス位置（canvas座標）から新しい時刻を計算して反映する</summary>
        private void ProcessDragMove(Point pos)
        {
            if (_dragItem == null || _dragCanvas == null) return;

            double deltaHours = (pos.Y - _dragStartPos.Y) / PixelsPerHour;

            DateTime newStart = _dragOrigStart;
            DateTime newEnd = _dragOrigEnd;

            switch (_dragMode)
            {
                case DragMode.Move:
                    newStart = SnapTime(_dragOrigStart.AddHours(deltaHours));

                    // 列をまたいだら日付を変更する（非表示曜日を考慮して実際の日付差で加算）
                    int cols = ViewModel.VisibleDays.Count;
                    if (cols > 1 && _dragOrigColumn >= 0 && _dragCanvas.ActualWidth > 0)
                    {
                        double colWidth = _dragCanvas.ActualWidth / cols;
                        int newCol = Math.Clamp((int)(pos.X / colWidth), 0, cols - 1);
                        var dayDiff = ViewModel.VisibleDays[newCol].Date - ViewModel.VisibleDays[_dragOrigColumn].Date;
                        newStart = newStart.Add(dayDiff);
                    }
                    newEnd = newStart + (_dragOrigEnd - _dragOrigStart);
                    break;

                case DragMode.ResizeTop:
                    newStart = SnapTime(_dragOrigStart.AddHours(deltaHours));
                    if (newStart > _dragOrigEnd - MinDragDuration)
                    {
                        newStart = _dragOrigEnd - MinDragDuration;
                    }
                    break;

                case DragMode.ResizeBottom:
                    newEnd = SnapTime(_dragOrigEnd.AddHours(deltaHours));
                    if (newEnd < _dragOrigStart + MinDragDuration)
                    {
                        newEnd = _dragOrigStart + MinDragDuration;
                    }
                    break;

                default:
                    return;
            }

            ViewModel.UpdateItemTimesPreview(_dragItem, newStart, newEnd);
        }

        private void View_PreviewMouseLeftButtonUpForDrag(object sender, MouseButtonEventArgs e)
        {
            if (_dragItem == null) return;
            EndDrag(commit: _dragStarted);
        }

        /// <summary>
        /// ドラッグ状態を終了する。commit=true なら変更を保存し、
        /// false ならプレビューで動かした時刻を元に戻す。
        /// </summary>
        private void EndDrag(bool commit)
        {
            bool started = _dragStarted;
            var item = _dragItem;
            var canvas = _dragCanvas;
            var origStart = _dragOrigStart;
            var origEnd = _dragOrigEnd;

            // 先にフラグを下ろし、ハンドラを解除してからキャプチャを解放する
            // （Capture(null) が LostMouseCapture を発火して再入するのを防ぐ）
            _dragStarted = false;
            if (canvas != null)
            {
                canvas.MouseMove -= DragCanvas_MouseMove;
                canvas.MouseLeftButtonUp -= DragCanvas_MouseLeftButtonUp;
                canvas.LostMouseCapture -= DragCanvas_LostMouseCapture;
            }

            if (started)
            {
                Mouse.Capture(null);
                Mouse.OverrideCursor = null;
            }

            _dragItem = null;
            _dragCanvas = null;
            _dragMode = DragMode.None;
            _dragOrigColumn = -1;

            if (item == null || !started) return;

            if (commit)
            {
                ViewModel.CommitItemDrag();
            }
            else
            {
                // キャンセル：プレビューで変更した時刻を元に戻す
                ViewModel.UpdateItemTimesPreview(item, origStart, origEnd);
                ViewModel.ClearItemDragUndo(); // 取り消したドラッグは履歴に残さない
            }
        }

        /// <summary>設定された刻み幅に丸める</summary>
        private DateTime SnapTime(DateTime t)
        {
            int step = SnapMinutes;
            double minutes = Math.Round(t.TimeOfDay.TotalMinutes / step) * step;
            return t.Date.AddMinutes(minutes);
        }

        /// <summary>
        /// アイテム上のホバー位置に応じてカーソルを切り替える（端＝上下リサイズ、中央＝手のひら）。
        /// XAMLのイベント配線を使わず、マウス位置の要素から予定バーを特定する。
        /// </summary>
        private void UpdateHoverCursor(MouseEventArgs e)
        {
            if (e.OriginalSource is not DependencyObject source) return;

            var element = FindScheduleItemRoot(source);
            if (element == null) return;

            var zone = GetZone(element, e.GetPosition(element).Y);
            element.Cursor = zone == DragMode.Move ? Cursors.Hand : Cursors.SizeNS;
        }

        /// <summary>
        /// マウス下の要素から、予定バーのテンプレートルート（DataContext が ScheduleSegment の Grid）を探す。
        /// バーの外（Canvas 以上）に達したら null。
        /// </summary>
        private static FrameworkElement? FindScheduleItemRoot(DependencyObject source)
        {
            var node = source;
            while (node != null)
            {
                if (node is Canvas || node is Window) return null; // バーの外に出た
                if (node is Grid grid && grid.DataContext is ScheduleSegment) return grid;
                node = GetParentSafe(node);
            }
            return null;
        }

        /// <summary>
        /// Visual/Visual3D はビジュアルツリー、Run 等の ContentElement は論理ツリーで親をたどる。
        /// （TextBlock 内の Run は Visual ではないため VisualTreeHelper.GetParent が例外を投げる）
        /// </summary>
        private static DependencyObject? GetParentSafe(DependencyObject node) => node switch
        {
            System.Windows.Media.Media3D.Visual3D or Visual => VisualTreeHelper.GetParent(node),
            FrameworkContentElement fce => fce.Parent,
            _ => LogicalTreeHelper.GetParent(node)
        };

        private static T? FindAncestor<T>(DependencyObject current) where T : DependencyObject
        {
            var node = GetParentSafe(current);
            while (node != null)
            {
                if (node is T match) return match;
                node = GetParentSafe(node);
            }
            return null;
        }
    }
}
