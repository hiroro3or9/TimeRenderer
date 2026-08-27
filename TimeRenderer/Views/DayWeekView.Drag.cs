using System.Collections.Generic;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using Cursors = System.Windows.Input.Cursors;
using Point = System.Windows.Point;
using MouseEventArgs = System.Windows.Input.MouseEventArgs;
using MouseButtonEventArgs = System.Windows.Input.MouseButtonEventArgs;

using TimeRenderer.Helpers;
using TimeRenderer.Models;

namespace TimeRenderer.Views
{
    /// <summary>
    /// 日/週ビューの予定バーのドラッグ操作（移動・リサイズ・複製）。
    /// - バー中央を上下ドラッグ: 時刻変更（刻み幅は設定で変更可能）
    /// - 別の日の列へドラッグ: 日付変更
    /// - バーの上端/下端をつまむ: 開始/終了時刻の伸縮
    /// - Alt を押しながら中央をドラッグ: 元の位置に写しを残して複製
    ///
    /// 時刻の決め方は「吸着 → 刻み幅」の順で見る。
    /// 先に刻み幅へ丸めてしまうと、隣の予定の端が刻み幅の格子に乗っていない場合
    /// （記録から作られた 10:23 など）に吸着の判定範囲から外れてしまうため。
    /// Alt を押している間は吸着も刻み幅も外れ、1分単位で置ける。
    ///
    /// ドラッグ中は VM の UpdateItemTimesPreview で再レイアウトのみ行い、
    /// マウスアップ時に CommitItemDrag で1回だけ保存する。
    /// </summary>
    public partial class DayWeekView
    {
        private enum DragMode { None, Move, ResizeTop, ResizeBottom }

        private const double PixelsPerHour = Helpers.LayoutConstants.PixelsPerHour;
        private const double DragThresholdPx = 4.0;   // この距離を超えて動いたらドラッグ開始

        /// <summary>吸着が効く距離（ピクセル）。広げすぎると狙った時刻に置けなくなる</summary>
        private const double MagnetTolerancePx = 6.0;

        /// <summary>Alt 併用時の刻み幅（分）。設定の刻み幅を外して細かく置くため</summary>
        private const int FineSnapMinutes = 1;

        /// <summary>時刻の丸め単位（設定で変更できる）</summary>
        private int SnapMinutes => ViewModel.SnapMinutes;

        /// <summary>ドラッグで作れる最小の長さ。そのとき効いている刻み幅と同じにする</summary>
        private static TimeSpan MinDuration(int step) => TimeSpan.FromMinutes(Math.Max(1, step));

        private ScheduleItem? _dragItem;
        private DragMode _dragMode = DragMode.None;
        private bool _dragStarted;
        private Point _dragStartPos;            // ドラッグ開始位置（Canvas座標）
        private Canvas? _dragCanvas;            // 予定バーを載せているCanvas（再レイアウト後も生存）
        private DateTime _dragOrigStart;
        private DateTime _dragOrigEnd;
        private int _dragOrigColumn = -1;       // 掴んだセグメントの列インデックス
        private bool _dragIsCopy;               // Alt 併用：元を残して複製する

        /// <summary>ドラッグ中の吸着先（日付ごと）。ドラッグの間は変わらないので使い回す</summary>
        private readonly Dictionary<DateTime, IReadOnlyList<DateTime>> _dragSnapTargets = [];

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
            _dragIsCopy = false;
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
                _dragSnapTargets.Clear();

                // Alt を押しながらの移動は「元を残して複製」。
                // 伸縮では意味を持たないため移動のときだけ見る
                _dragIsCopy = _dragMode == DragMode.Move
                              && Keyboard.Modifiers.HasFlag(ModifierKeys.Alt)
                              && ViewModel.BeginItemCopyDrag(_dragItem);

                // 取り消し用に、ドラッグ開始前の状態を控える
                if (!_dragIsCopy) ViewModel.BeginItemDragUndo(_dragItem);

                // キャプチャ先の canvas に直接イベントを購読する
                // （キャプチャ後のマウスイベントはキャプチャ要素に確実に届くため）
                _dragCanvas.MouseMove += DragCanvas_MouseMove;
                _dragCanvas.MouseLeftButtonUp += DragCanvas_MouseLeftButtonUp;
                _dragCanvas.LostMouseCapture += DragCanvas_LostMouseCapture;
                Mouse.Capture(_dragCanvas);

                Mouse.OverrideCursor = _dragMode != DragMode.Move ? Cursors.SizeNS
                    : _dragIsCopy ? Cursors.Cross
                    : Cursors.SizeAll;
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

            // Alt は「吸着も刻み幅も外して細かく置く」ための一時解除
            bool fine = Keyboard.Modifiers.HasFlag(ModifierKeys.Alt) && !_dragIsCopy;
            int step = fine ? FineSnapMinutes : SnapMinutes;

            DateTime newStart = _dragOrigStart;
            DateTime newEnd = _dragOrigEnd;
            DateTime? guide;
            switch (_dragMode)
            {
                case DragMode.Move:
                {
                    var rawStart = _dragOrigStart.AddHours(deltaHours);

                    // 列をまたいだら日付を変更する（非表示曜日を考慮して実際の日付差で加算）
                    int cols = ViewModel.VisibleDays.Count;
                    if (cols > 1 && _dragOrigColumn >= 0 && _dragCanvas.ActualWidth > 0)
                    {
                        double colWidth = _dragCanvas.ActualWidth / cols;
                        int newCol = Math.Clamp((int)(pos.X / colWidth), 0, cols - 1);
                        var dayDiff = ViewModel.VisibleDays[newCol].Date - ViewModel.VisibleDays[_dragOrigColumn].Date;
                        rawStart = rawStart.Add(dayDiff);
                    }

                    var duration = _dragOrigEnd - _dragOrigStart;
                    MagnetSnapHelper.Result? magnet = fine ? null : TrySnapRange(rawStart, rawStart + duration);

                    newStart = magnet != null ? rawStart + magnet.Offset : SnapTime(rawStart, step);
                    newEnd = newStart + duration;
                    guide = magnet?.GuideTime;
                    break;
                }

                case DragMode.ResizeTop:
                {
                    var rawStart = _dragOrigStart.AddHours(deltaHours);
                    MagnetSnapHelper.Result? magnet = fine ? null : TrySnapEdge(rawStart);

                    newStart = magnet?.GuideTime ?? SnapTime(rawStart, step);
                    guide = magnet?.GuideTime;

                    var minTop = MinDuration(step);
                    if (newStart > _dragOrigEnd - minTop)
                    {
                        newStart = _dragOrigEnd - minTop;
                        guide = null;
                    }
                    break;
                }

                case DragMode.ResizeBottom:
                {
                    var rawEnd = _dragOrigEnd.AddHours(deltaHours);
                    MagnetSnapHelper.Result? magnet = fine ? null : TrySnapEdge(rawEnd);

                    newEnd = magnet?.GuideTime ?? SnapTime(rawEnd, step);
                    guide = magnet?.GuideTime;

                    var minBottom = MinDuration(step);
                    if (newEnd < _dragOrigStart + minBottom)
                    {
                        newEnd = _dragOrigStart + minBottom;
                        guide = null;
                    }
                    break;
                }

                default:
                    return;
            }

            ShowMagnetGuide(guide);
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

            HideMagnetGuide();
            _dragSnapTargets.Clear();

            _dragItem = null;
            _dragCanvas = null;
            _dragMode = DragMode.None;
            _dragOrigColumn = -1;
            _dragIsCopy = false;

            if (item == null || !started) return;

            if (commit)
            {
                ViewModel.CommitItemDrag();
            }
            else
            {
                // キャンセル：時刻を戻し、複製ドラッグで置いた写しも取り除く
                ViewModel.CancelItemDrag(item, origStart, origEnd);
            }
        }

        // ===== 吸着 =====

        /// <summary>指定日の吸着先を得る（ドラッグ中は同じ結果になるので使い回す）</summary>
        private IReadOnlyList<DateTime> GetSnapTargetsCached(DateTime date)
        {
            var day = date.Date;
            if (_dragSnapTargets.TryGetValue(day, out var cached)) return cached;

            var targets = ViewModel.GetSnapTargets(day, _dragItem);
            _dragSnapTargets[day] = targets;
            return targets;
        }

        private TimeSpan MagnetTolerance => TimeSpan.FromHours(MagnetTolerancePx / PixelsPerHour);

        /// <summary>片方の端を吸着させる（伸縮用）</summary>
        private MagnetSnapHelper.Result? TrySnapEdge(DateTime edge)
        {
            if (!ViewModel.IsMagnetSnapEnabled) return null;
            return MagnetSnapHelper.SnapEdge(edge, GetSnapTargetsCached(edge.Date), MagnetTolerance);
        }

        /// <summary>開始・終了の近い方を吸着させる（移動用）</summary>
        private MagnetSnapHelper.Result? TrySnapRange(DateTime start, DateTime end)
        {
            if (!ViewModel.IsMagnetSnapEnabled) return null;

            var targets = GetSnapTargetsCached(start.Date);

            // 日をまたぐ場合は終了側の日付の候補も見る
            if (end.Date != start.Date)
            {
                var combined = new List<DateTime>(targets);
                combined.AddRange(GetSnapTargetsCached(end.Date));
                targets = combined;
            }

            return MagnetSnapHelper.SnapRange(start, end, targets, MagnetTolerance);
        }

        // ===== 吸着ガイド線 =====

        private Border? _magnetGuide;

        /// <summary>
        /// 吸着した時刻に細い線を出す。
        /// 線が出ているかどうかで「格子に丸めたのか、隣に揃えたのか」が区別できる。
        /// 範囲ドラッグのプレビュー矩形と同じく、描画面へ実行時に足す。
        /// </summary>
        private void ShowMagnetGuide(DateTime? time)
        {
            if (time is not { } guideTime)
            {
                HideMagnetGuide();
                return;
            }

            var surface = FindSurfaceAbove(_dragCanvas);
            if (surface == null || surface.ActualWidth <= 0)
            {
                HideMagnetGuide();
                return;
            }

            var days = ViewModel.VisibleDays;
            int column = -1;
            for (int i = 0; i < days.Count; i++)
            {
                if (days[i].Date == guideTime.Date)
                {
                    column = i;
                    break;
                }
            }
            if (column < 0 || days.Count == 0)
            {
                HideMagnetGuide();
                return;
            }

            if (_magnetGuide == null || !surface.Children.Contains(_magnetGuide))
            {
                _magnetGuide = new Border
                {
                    IsHitTestVisible = false,
                    Height = 2,
                    HorizontalAlignment = System.Windows.HorizontalAlignment.Left,
                    VerticalAlignment = System.Windows.VerticalAlignment.Top
                };
                _magnetGuide.SetResourceReference(Border.BackgroundProperty, "PrimaryBrush");
                surface.Children.Add(_magnetGuide);
            }

            double columnWidth = surface.ActualWidth / days.Count;
            double top = (guideTime.TimeOfDay.TotalHours - ViewModel.DisplayStartHour) * PixelsPerHour;

            _magnetGuide.Width = columnWidth;
            _magnetGuide.Margin = new Thickness(column * columnWidth, top - 1, 0, 0);
            _magnetGuide.Visibility = Visibility.Visible;
        }

        private void HideMagnetGuide()
        {
            _magnetGuide?.Visibility = Visibility.Collapsed;
        }

        /// <summary>設定された刻み幅に丸める</summary>
        private static DateTime SnapTime(DateTime t, int step)
        {
            if (step <= 1) return t.Date.AddMinutes(Math.Round(t.TimeOfDay.TotalMinutes));

            double minutes = Math.Round(t.TimeOfDay.TotalMinutes / step) * step;
            return t.Date.AddMinutes(minutes);
        }

        /// <summary>
        /// アイテム上のホバー位置に応じてカーソルを切り替える（端＝上下リサイズ、中央＝手のひら）。
        /// XAMLのイベント配線を使わず、マウス位置の要素から予定バーを特定する。
        /// </summary>
        private static void UpdateHoverCursor(MouseEventArgs e)
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
        private static Grid? FindScheduleItemRoot(DependencyObject source)
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
