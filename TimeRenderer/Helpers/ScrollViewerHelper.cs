using System;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Shapes;
using System.Windows.Threading;

using Color = System.Windows.Media.Color;
using Cursors = System.Windows.Input.Cursors;
using HorizontalAlignment = System.Windows.HorizontalAlignment;
using VerticalAlignment = System.Windows.VerticalAlignment;
using KeyEventArgs = System.Windows.Input.KeyEventArgs;
using MouseEventArgs = System.Windows.Input.MouseEventArgs;
using Point = System.Windows.Point;

namespace TimeRenderer.Helpers
{
    /// <summary>
    /// ScrollViewer に対するホイールクリック（中ボタン）によるオートスクロール（ブラウザ風スクロール）機能を提供するヘルパークラスです。
    /// </summary>
    public static class ScrollViewerHelper
    {
        // 添付プロパティ: 中ボタンスクロールを有効にするか
        public static readonly DependencyProperty EnableMiddleButtonScrollProperty =
            DependencyProperty.RegisterAttached(
                "EnableMiddleButtonScroll",
                typeof(bool),
                typeof(ScrollViewerHelper),
                new PropertyMetadata(false, OnEnableMiddleButtonScrollChanged));

        public static bool GetEnableMiddleButtonScroll(DependencyObject obj)
        {
            return (bool)obj.GetValue(EnableMiddleButtonScrollProperty);
        }

        public static void SetEnableMiddleButtonScroll(DependencyObject obj, bool value)
        {
            obj.SetValue(EnableMiddleButtonScrollProperty, value);
        }

        private static void OnEnableMiddleButtonScrollChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
        {
            if (d is ScrollViewer scrollViewer)
            {
                bool isEnabled = (bool)e.NewValue;
                if (isEnabled)
                {
                    scrollViewer.PreviewMouseDown += ScrollViewer_PreviewMouseDown;
                }
                else
                {
                    scrollViewer.PreviewMouseDown -= ScrollViewer_PreviewMouseDown;
                    DisableAutoScroll(scrollViewer);
                }
            }
        }

        // 内部状態保持用の添付プロパティ
        private static readonly DependencyProperty AutoScrollStateProperty =
            DependencyProperty.RegisterAttached(
                "AutoScrollState",
                typeof(AutoScrollState),
                typeof(ScrollViewerHelper),
                new PropertyMetadata(null));

        private static AutoScrollState? GetAutoScrollState(DependencyObject obj)
        {
            return (AutoScrollState?)obj.GetValue(AutoScrollStateProperty);
        }

        private static void SetAutoScrollState(DependencyObject obj, AutoScrollState? value)
        {
            obj.SetValue(AutoScrollStateProperty, value);
        }

        private static void ScrollViewer_PreviewMouseDown(object sender, MouseButtonEventArgs e)
        {
            if (sender is ScrollViewer scrollViewer)
            {
                var state = GetAutoScrollState(scrollViewer);

                if (e.ChangedButton == MouseButton.Middle)
                {
                    e.Handled = true;

                    if (state != null)
                    {
                        // 既に有効なら停止（トグル動作）
                        DisableAutoScroll(scrollViewer);
                    }
                    else
                    {
                        // オートスクロール開始
                        EnableAutoScroll(scrollViewer, e);
                    }
                }
                else
                {
                    // 左クリックや右クリックがあった場合はオートスクロールを終了する
                    if (state != null)
                    {
                        DisableAutoScroll(scrollViewer);
                        e.Handled = true;
                    }
                }
            }
        }

        private static void EnableAutoScroll(ScrollViewer scrollViewer, MouseButtonEventArgs e)
        {
            var window = Window.GetWindow(scrollViewer);
            if (window == null) return;

            // スクロール領域に対するマウス位置と、スクリーン相対座標を取得
            var startPoint = e.GetPosition(scrollViewer);
            var screenPoint = scrollViewer.PointToScreen(startPoint);

            // インジケーター用のポップアップを動的に生成
            var popup = CreateIndicatorPopup(scrollViewer);
            
            // ポップアップをマウス位置の中心に配置 (32x32ピクセルなので16ピクセル引く)
            popup.HorizontalOffset = screenPoint.X - 16;
            popup.VerticalOffset = screenPoint.Y - 16;
            popup.IsOpen = true;

            var state = new AutoScrollState
            {
                StartPoint = startPoint,
                ScreenStartPoint = screenPoint,
                IndicatorPopup = popup,
                Timer = new DispatcherTimer(DispatcherPriority.Background),
                IsDragMode = true // 初期は中ボタンが押された状態の「ドラッグモード」
            };

            // 10ms間隔でスクロール処理を定期更新
            state.Timer.Interval = TimeSpan.FromMilliseconds(10);
            state.Timer.Tick += (s, ev) => UpdateScroll(scrollViewer, state);
            state.Timer.Start();

            // 一時的に必要なイベントハンドラを購読
            scrollViewer.PreviewMouseUp += ScrollViewer_PreviewMouseUp;
            scrollViewer.MouseMove += ScrollViewer_MouseMove;
            scrollViewer.PreviewKeyDown += ScrollViewer_PreviewKeyDown;
            scrollViewer.LostMouseCapture += ScrollViewer_LostMouseCapture;

            // マウスをキャプチャして領域外への移動も検知可能にする
            scrollViewer.CaptureMouse();

            SetAutoScrollState(scrollViewer, state);
            UpdateCursor(scrollViewer);
        }

        private static void DisableAutoScroll(ScrollViewer scrollViewer)
        {
            var state = GetAutoScrollState(scrollViewer);
            if (state != null)
            {
                state.Timer.Stop();
                scrollViewer.PreviewMouseUp -= ScrollViewer_PreviewMouseUp;
                scrollViewer.MouseMove -= ScrollViewer_MouseMove;
                scrollViewer.PreviewKeyDown -= ScrollViewer_PreviewKeyDown;
                scrollViewer.LostMouseCapture -= ScrollViewer_LostMouseCapture;

                if (scrollViewer.IsMouseCaptured)
                {
                    scrollViewer.ReleaseMouseCapture();
                }

                state.IndicatorPopup.IsOpen = false;

                scrollViewer.Cursor = Cursors.Arrow; // カーソルを通常に戻す
                SetAutoScrollState(scrollViewer, null);
            }
        }

        private static void ScrollViewer_PreviewMouseUp(object sender, MouseButtonEventArgs e)
        {
            if (e.ChangedButton == MouseButton.Middle && sender is ScrollViewer scrollViewer)
            {
                var state = GetAutoScrollState(scrollViewer);
                if (state != null)
                {
                    e.Handled = true;

                    // ドラッグしていた距離を計算
                    var currentPoint = e.GetPosition(scrollViewer);
                    double distance = (currentPoint - state.StartPoint).Length;

                    // 中クリックしてすぐ離した（ドラッグしていない）場合はトグルモードに移行
                    if (state.IsDragMode && distance < 5)
                    {
                        state.IsDragMode = false;
                    }
                    else
                    {
                        // 長押しドラッグで離した場合はその時点でスクロール終了
                        DisableAutoScroll(scrollViewer);
                    }
                }
            }
        }

        private static void ScrollViewer_MouseMove(object sender, MouseEventArgs e)
        {
            if (sender is ScrollViewer scrollViewer)
            {
                var state = GetAutoScrollState(scrollViewer);
                if (state != null)
                {
                    var currentPoint = e.GetPosition(scrollViewer);
                    state.CurrentOffset = currentPoint - state.StartPoint;
                    UpdateCursor(scrollViewer);
                }
            }
        }

        private static void ScrollViewer_PreviewKeyDown(object sender, KeyEventArgs e)
        {
            if (sender is ScrollViewer scrollViewer)
            {
                // エスケープキーなど、何らかのキー入力があったら終了
                DisableAutoScroll(scrollViewer);
            }
        }

        private static void ScrollViewer_LostMouseCapture(object sender, MouseEventArgs e)
        {
            if (sender is ScrollViewer scrollViewer)
            {
                DisableAutoScroll(scrollViewer);
            }
        }

        private static void UpdateCursor(ScrollViewer scrollViewer)
        {
            // 現在のスクロール可能状態に応じてカーソル画像を変更
            bool canScrollV = scrollViewer.ScrollableHeight > 0;
            bool canScrollH = scrollViewer.ScrollableWidth > 0;

            if (canScrollV && canScrollH)
            {
                scrollViewer.Cursor = Cursors.ScrollAll;
            }
            else if (canScrollV)
            {
                scrollViewer.Cursor = Cursors.ScrollNS;
            }
            else if (canScrollH)
            {
                scrollViewer.Cursor = Cursors.ScrollWE;
            }
            else
            {
                scrollViewer.Cursor = Cursors.No;
            }
        }

        private static void UpdateScroll(ScrollViewer scrollViewer, AutoScrollState state)
        {
            var offset = state.CurrentOffset;
            double dx = offset.X;
            double dy = offset.Y;

            // スクロールを開始するデッドゾーン（反応しない遊び領域）
            const double DeadZone = 8.0;

            // 基準点からの距離に基づいてスクロール量を加算 (0.15 は速度調節係数)
            if (scrollViewer.ScrollableWidth > 0 && Math.Abs(dx) > DeadZone)
            {
                double speedX = (dx - Math.Sign(dx) * DeadZone) * 0.15;
                scrollViewer.ScrollToHorizontalOffset(scrollViewer.HorizontalOffset + speedX);
            }

            if (scrollViewer.ScrollableHeight > 0 && Math.Abs(dy) > DeadZone)
            {
                double speedY = (dy - Math.Sign(dy) * DeadZone) * 0.15;
                scrollViewer.ScrollToVerticalOffset(scrollViewer.VerticalOffset + speedY);
            }
        }

        // 動的なポップアップ図形（インジケーター）の構築
        private static Popup CreateIndicatorPopup(ScrollViewer scrollViewer)
        {
            var popup = new Popup
            {
                AllowsTransparency = true,
                Placement = PlacementMode.Absolute,
                Width = 32,
                Height = 32,
                IsHitTestVisible = false // ポップアップ自体がマウスイベントを遮らないようにする
            };

            var grid = new Grid
            {
                Width = 32,
                Height = 32
            };

            // 外円（半透明の背景）
            var ellipse = new Ellipse
            {
                Fill = new SolidColorBrush(Color.FromArgb(160, 240, 240, 240)),
                Stroke = new SolidColorBrush(Color.FromArgb(180, 100, 100, 100)),
                StrokeThickness = 1.5,
                Width = 28,
                Height = 28,
                HorizontalAlignment = HorizontalAlignment.Center,
                VerticalAlignment = VerticalAlignment.Center
            };
            grid.Children.Add(ellipse);

            // 中心点（ドット）
            var dot = new Ellipse
            {
                Fill = new SolidColorBrush(Color.FromArgb(255, 60, 60, 60)),
                Width = 4,
                Height = 4,
                HorizontalAlignment = HorizontalAlignment.Center,
                VerticalAlignment = VerticalAlignment.Center
            };
            grid.Children.Add(dot);

            bool canScrollV = scrollViewer.ScrollableHeight > 0;
            bool canScrollH = scrollViewer.ScrollableWidth > 0;

            // スクロール可能な方向に応じた方向矢印を描画
            if (canScrollV)
            {
                // 上矢印
                var upArrow = new Path
                {
                    Data = Geometry.Parse("M 16,5 L 12,10 L 20,10 Z"),
                    Fill = new SolidColorBrush(Color.FromArgb(255, 60, 60, 60))
                };
                // 下矢印
                var downArrow = new Path
                {
                    Data = Geometry.Parse("M 16,27 L 12,22 L 20,22 Z"),
                    Fill = new SolidColorBrush(Color.FromArgb(255, 60, 60, 60))
                };
                grid.Children.Add(upArrow);
                grid.Children.Add(downArrow);
            }

            if (canScrollH)
            {
                // 左矢印
                var leftArrow = new Path
                {
                    Data = Geometry.Parse("M 5,16 L 10,12 L 10,20 Z"),
                    Fill = new SolidColorBrush(Color.FromArgb(255, 60, 60, 60))
                };
                // 右矢印
                var rightArrow = new Path
                {
                    Data = Geometry.Parse("M 27,16 L 22,12 L 22,20 Z"),
                    Fill = new SolidColorBrush(Color.FromArgb(255, 60, 60, 60))
                };
                grid.Children.Add(leftArrow);
                grid.Children.Add(rightArrow);
            }

            popup.Child = grid;
            return popup;
        }
    }

    /// <summary>
    /// オートスクロールの状態を保持する内部クラス
    /// </summary>
    internal class AutoScrollState
    {
        public Point StartPoint { get; set; }
        public Point ScreenStartPoint { get; set; }
        public Vector CurrentOffset { get; set; }
        public required Popup IndicatorPopup { get; set; }
        public required DispatcherTimer Timer { get; set; }
        public bool IsDragMode { get; set; }
    }
}
