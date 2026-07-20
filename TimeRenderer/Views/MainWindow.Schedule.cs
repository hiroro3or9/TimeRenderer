using System;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;

using Cursors = System.Windows.Input.Cursors;
using MouseEventArgs = System.Windows.Input.MouseEventArgs;
using MouseButtonEventArgs = System.Windows.Input.MouseButtonEventArgs;
using KeyEventArgs = System.Windows.Input.KeyEventArgs;

using TimeRenderer.Models;

namespace TimeRenderer.Views
{
    /// <summary>
    /// 日/週ビューの操作をタイムラインと揃えるための処理。
    ///
    /// - キーボード: ↑↓ 選択移動 / ←→ 日付移動 / Enter 編集 / Delete 削除 / Esc 選択解除
    /// - 空き領域の縦ドラッグ: その時間帯で新規作成
    ///
    /// 予定バーのドラッグ（移動・伸縮）は MainWindow.Drag.cs が担当する。
    /// こちらは「何もない場所」を起点にした操作だけを扱う。
    /// </summary>
    public partial class MainWindow
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

            // テキスト入力中は横取りしない
            if (Keyboard.FocusedElement is System.Windows.Controls.Primitives.TextBoxBase) return;
            if (Keyboard.Modifiers.HasFlag(ModifierKeys.Control)) return;

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

        // ===== 空き領域の縦ドラッグで新規作成 =====

        /// <summary>
        /// 背景の押下。ダブルクリックは従来どおり1時間の予定を作り、
        /// 縦にドラッグした場合はその範囲で作る。
        /// </summary>
        private void ScheduleBackground_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
        {
            if (sender is not Grid grid || grid.ActualWidth <= 0) return;

            // 予定バー上のクリックは対象外（バー側の編集・ドラッグを優先）
            if (e.OriginalSource is DependencyObject src && FindScheduleItemRoot(src) != null) return;

            MainScrollViewer?.Focus();

            var pos = e.GetPosition(grid);
            var date = ResolveDateFromX(grid, pos.X);
            if (date == null) return;

            if (e.ClickCount == 2)
            {
                CancelRangeDrag();

                // 刻み幅で丸めた開始時刻から、従来どおり1時間の予定を作る
                var start = date.Value.AddHours(SnapHours(pos.Y, 60.0 / RangeSnapMinutes));
                if (ViewModel.AddScheduleItemAtTimeCommand.CanExecute(start))
                {
                    ViewModel.AddScheduleItemAtTimeCommand.Execute(start);
                }
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

            CommitRangeDrag(y);
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

        private void CommitRangeDrag(double y)
        {
            // 日付はドラッグ開始時の列で固定する
            // （週ビューで縦にドラッグする間、横に少し動いても日が変わらないように）
            var baseDate = _rangeAnchor.Date;

            var dropped = baseDate.AddHours(SnapHours(y, 60.0 / RangeSnapMinutes));

            var start = _rangeAnchor <= dropped ? _rangeAnchor : dropped;
            var end = _rangeAnchor <= dropped ? dropped : _rangeAnchor;

            if (end <= start) end = start.AddMinutes(RangeSnapMinutes);

            var range = (start, end);
            if (ViewModel.AddScheduleItemInRangeCommand.CanExecute(range))
            {
                ViewModel.AddScheduleItemInRangeCommand.Execute(range);
            }
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
