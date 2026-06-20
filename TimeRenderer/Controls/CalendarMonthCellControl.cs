using System;
using System.Globalization;
using System.Windows;
using System.Windows.Input;
using System.Windows.Media;
using Brush = System.Windows.Media.Brush;
using TimeRenderer.Models;
using TimeRenderer.ViewModels;

namespace TimeRenderer.Controls
{
    public class CalendarMonthCellControl : FrameworkElement
    {
        // データをバインドするための依存関係プロパティ
        public static readonly DependencyProperty CellDataProperty =
            DependencyProperty.Register("CellData", typeof(CalendarCellViewModel), typeof(CalendarMonthCellControl), 
                new FrameworkPropertyMetadata(null, FrameworkPropertyMetadataOptions.AffectsRender));

        public CalendarCellViewModel CellData
        {
            get => (CalendarCellViewModel)GetValue(CellDataProperty);
            set => SetValue(CellDataProperty, value);
        }

        public static readonly DependencyProperty MutedBackgroundBrushProperty =
            DependencyProperty.Register("MutedBackgroundBrush", typeof(Brush), typeof(CalendarMonthCellControl), 
                new FrameworkPropertyMetadata(null, FrameworkPropertyMetadataOptions.AffectsRender));

        public static readonly DependencyProperty TextSecondaryBrushProperty =
            DependencyProperty.Register("TextSecondaryBrush", typeof(Brush), typeof(CalendarMonthCellControl), 
                new FrameworkPropertyMetadata(null, FrameworkPropertyMetadataOptions.AffectsRender));

        public static readonly DependencyProperty TextPrimaryBrushProperty =
            DependencyProperty.Register("TextPrimaryBrush", typeof(Brush), typeof(CalendarMonthCellControl), 
                new FrameworkPropertyMetadata(null, FrameworkPropertyMetadataOptions.AffectsRender));

        public static readonly DependencyProperty TodayBackgroundBrushProperty =
            DependencyProperty.Register("TodayBackgroundBrush", typeof(Brush), typeof(CalendarMonthCellControl), 
                new FrameworkPropertyMetadata(null, FrameworkPropertyMetadataOptions.AffectsRender));

        public static readonly DependencyProperty SundayForegroundBrushProperty =
            DependencyProperty.Register("SundayForegroundBrush", typeof(Brush), typeof(CalendarMonthCellControl), 
                new FrameworkPropertyMetadata(null, FrameworkPropertyMetadataOptions.AffectsRender));

        public static readonly DependencyProperty SaturdayForegroundBrushProperty =
            DependencyProperty.Register("SaturdayForegroundBrush", typeof(Brush), typeof(CalendarMonthCellControl), 
                new FrameworkPropertyMetadata(null, FrameworkPropertyMetadataOptions.AffectsRender));

        public static readonly DependencyProperty WeekdayForegroundBrushProperty =
            DependencyProperty.Register("WeekdayForegroundBrush", typeof(Brush), typeof(CalendarMonthCellControl), 
                new FrameworkPropertyMetadata(null, FrameworkPropertyMetadataOptions.AffectsRender));

        public Brush MutedBackgroundBrush
        {
            get => (Brush)GetValue(MutedBackgroundBrushProperty);
            set => SetValue(MutedBackgroundBrushProperty, value);
        }

        public Brush TextSecondaryBrush
        {
            get => (Brush)GetValue(TextSecondaryBrushProperty);
            set => SetValue(TextSecondaryBrushProperty, value);
        }

        public Brush TextPrimaryBrush
        {
            get => (Brush)GetValue(TextPrimaryBrushProperty);
            set => SetValue(TextPrimaryBrushProperty, value);
        }

        public Brush TodayBackgroundBrush
        {
            get => (Brush)GetValue(TodayBackgroundBrushProperty);
            set => SetValue(TodayBackgroundBrushProperty, value);
        }

        public Brush SundayForegroundBrush
        {
            get => (Brush)GetValue(SundayForegroundBrushProperty);
            set => SetValue(SundayForegroundBrushProperty, value);
        }

        public Brush SaturdayForegroundBrush
        {
            get => (Brush)GetValue(SaturdayForegroundBrushProperty);
            set => SetValue(SaturdayForegroundBrushProperty, value);
        }

        public Brush WeekdayForegroundBrush
        {
            get => (Brush)GetValue(WeekdayForegroundBrushProperty);
            set => SetValue(WeekdayForegroundBrushProperty, value);
        }

        // クリックなどのイベント用
        public static readonly RoutedEvent CellClickedEvent = EventManager.RegisterRoutedEvent(
            "CellClicked", RoutingStrategy.Bubble, typeof(RoutedEventHandler), typeof(CalendarMonthCellControl));

        public event RoutedEventHandler CellClicked
        {
            add { AddHandler(CellClickedEvent, value); }
            remove { RemoveHandler(CellClickedEvent, value); }
        }

        // スケジュールアイテムがクリックされた時のイベント
        public static readonly RoutedEvent ItemClickedEvent = EventManager.RegisterRoutedEvent(
            "ItemClicked", RoutingStrategy.Bubble, typeof(EventHandler<ScheduleItemClickedEventArgs>), typeof(CalendarMonthCellControl));

        public event EventHandler<ScheduleItemClickedEventArgs> ItemClicked
        {
            add { AddHandler(ItemClickedEvent, value); }
            remove { RemoveHandler(ItemClickedEvent, value); }
        }

        // スケジュールアイテムが右クリックされた時のイベント
        public static readonly RoutedEvent ItemRightClickedEvent = EventManager.RegisterRoutedEvent(
            "ItemRightClicked", RoutingStrategy.Bubble, typeof(EventHandler<ScheduleItemClickedEventArgs>), typeof(CalendarMonthCellControl));

        public event EventHandler<ScheduleItemClickedEventArgs> ItemRightClicked
        {
            add { AddHandler(ItemRightClickedEvent, value); }
            remove { RemoveHandler(ItemRightClickedEvent, value); }
        }

        // テキスト描画用のTypefaceキャッシュ
        private static readonly Typeface _dayTypeface = new(new System.Windows.Media.FontFamily("Segoe UI"), FontStyles.Normal, FontWeights.SemiBold, FontStretches.Normal);
        private static readonly Typeface _itemTypeface = new(new System.Windows.Media.FontFamily("Segoe UI"), FontStyles.Normal, FontWeights.Normal, FontStretches.Normal);

        private static readonly System.Windows.Media.Brush _mutedBackgroundBrush = new SolidColorBrush(System.Windows.Media.Color.FromRgb(249, 250, 251)); // #F9FAFB
        private static readonly System.Windows.Media.Brush _textSecondaryBrush = new SolidColorBrush(System.Windows.Media.Color.FromRgb(107, 114, 128));   // #6B7280
        private static readonly System.Windows.Media.Brush _textPrimaryBrush = new SolidColorBrush(System.Windows.Media.Color.FromRgb(17, 24, 39));       // #111827
        private static readonly System.Windows.Media.Brush _todayBackgroundBrush = new SolidColorBrush(System.Windows.Media.Color.FromRgb(239, 246, 255));   // #EFF6FF
        private static readonly System.Windows.Media.Brush _sundayForegroundBrush = new SolidColorBrush(System.Windows.Media.Color.FromRgb(220, 38, 38));    // #DC2626
        private static readonly System.Windows.Media.Brush _saturdayForegroundBrush = new SolidColorBrush(System.Windows.Media.Color.FromRgb(37, 99, 235));  // #2563EB
        private static readonly System.Windows.Media.Brush _weekdayForegroundBrush = new SolidColorBrush(System.Windows.Media.Color.FromRgb(31, 41, 55));    // #1F2937

        static CalendarMonthCellControl()
        {
            _mutedBackgroundBrush.Freeze();
            _textSecondaryBrush.Freeze();
            _textPrimaryBrush.Freeze();
            _todayBackgroundBrush.Freeze();
            _sundayForegroundBrush.Freeze();
            _saturdayForegroundBrush.Freeze();
            _weekdayForegroundBrush.Freeze();
        }

        protected override void OnRender(DrawingContext dc)
        {
            base.OnRender(dc);
            
            var data = CellData;
            if (data == null) return;

            var width = ActualWidth;
            var height = ActualHeight;
            if (width <= 0 || height <= 0) return;

            // 1. セルの背景描画
            System.Windows.Media.Brush bgBrush = System.Windows.Media.Brushes.Transparent;
            if (!data.IsCurrentMonth)
            {
                bgBrush = MutedBackgroundBrush ?? _mutedBackgroundBrush;
            }
            else if (data.IsToday)
            {
                bgBrush = TodayBackgroundBrush ?? _todayBackgroundBrush;
            }
            
            // 下線と右側の境界線を自前で描くか、親のBorderに任せるか。今回は親にBorderがある前提だが、ここで背景だけ敷く。
            dc.DrawRectangle(bgBrush, null, new Rect(0, 0, width, height));

            // 2. 日付テキストの描画
            System.Windows.Media.Brush fgBrush = WeekdayForegroundBrush ?? _weekdayForegroundBrush;
            if (!data.IsCurrentMonth) fgBrush = TextSecondaryBrush ?? _textSecondaryBrush;
            else if (data.DayOfWeek == DayOfWeek.Sunday) fgBrush = SundayForegroundBrush ?? _sundayForegroundBrush;
            else if (data.DayOfWeek == DayOfWeek.Saturday) fgBrush = SaturdayForegroundBrush ?? _saturdayForegroundBrush;

            var dayFormattedText = new FormattedText(
                data.DayText,
                CultureInfo.CurrentUICulture,
                System.Windows.FlowDirection.LeftToRight,
                _dayTypeface,
                12, // FontSize
                fgBrush,
                VisualTreeHelper.GetDpi(this).PixelsPerDip);

            dc.DrawText(dayFormattedText, new System.Windows.Point(4, 4));

            // 3. スケジュールアイテムの描画処理
            if (data.DailyItems == null || data.DailyItems.Count == 0) return;

            double currentY = dayFormattedText.Height + 8; // 日付テキストの下部から開始
            double itemHeight = 18; // アイテムの描画高さ
            double margin = 2; // アイテム間のマージン
            double padding = 4; // アイテム内のパディング

            int maxItems = (int)((height - currentY) / (itemHeight + margin));
            int displayCount = Math.Min(data.DailyItems.Count, maxItems);

            // スペースが足りず省略表記を行う場合
            bool hasMoreItems = data.DailyItems.Count > maxItems;
            if (hasMoreItems && maxItems > 0)
            {
                displayCount = maxItems - 1; // 省略テキストの分1つ減らす
            }

            for (int i = 0; i < displayCount; i++)
            {
                var item = data.DailyItems[i];
                DrawScheduleItem(dc, item, new Rect(2, currentY, width - 4, itemHeight), padding);
                currentY += itemHeight + margin;
            }

            // 省略テキストを描画
            if (hasMoreItems && displayCount >= 0)
            {
                var moreText = $"+{data.DailyItems.Count - displayCount} 件";
                var moreFormattedText = new FormattedText(
                    moreText,
                    CultureInfo.CurrentUICulture,
                    System.Windows.FlowDirection.LeftToRight,
                    _itemTypeface,
                    11,
                    TextSecondaryBrush ?? _textSecondaryBrush,
                    VisualTreeHelper.GetDpi(this).PixelsPerDip);

                dc.DrawText(moreFormattedText, new System.Windows.Point(4, currentY));
            }
        }

        private void DrawScheduleItem(DrawingContext dc, ScheduleItem item, Rect rect, double padding)
        {
            // アイテムの背景を描画（角丸）
            System.Windows.Media.Brush itemBg = item.BackgroundColor;
            // 枠線はとりあえず無し
            dc.DrawRoundedRectangle(itemBg, null, rect, 4, 4);

            // アイテムのテキストを描画
            var itemText = new FormattedText(
                item.Title,
                CultureInfo.CurrentUICulture,
                System.Windows.FlowDirection.LeftToRight,
                _itemTypeface,
                11,
                _textPrimaryBrush, // 予定背景はパステルカラー固定のため、文字色は常に暗い色で固定
                VisualTreeHelper.GetDpi(this).PixelsPerDip)
            {
                // テキスト省略設定
                MaxTextWidth = Math.Max(0, rect.Width - (padding * 2)),
                MaxTextHeight = rect.Height,
                Trimming = TextTrimming.CharacterEllipsis
            };

            // テキストの中心合わせ (Y軸)
            double textY = rect.Y + (rect.Height - itemText.Height) / 2;
            dc.DrawText(itemText, new System.Windows.Point(rect.X + padding, textY));
        }

        private ScheduleItem? GetItemAtPosition(System.Windows.Point pos)
        {
            var data = CellData;
            if (data == null || data.DailyItems == null || data.DailyItems.Count == 0) return null;

            double width = ActualWidth;
            double height = ActualHeight;

            var dayFormattedText = new FormattedText("0", CultureInfo.CurrentUICulture, System.Windows.FlowDirection.LeftToRight, _dayTypeface, 12, System.Windows.Media.Brushes.Black, 1);
            double currentY = dayFormattedText.Height + 8;
            double itemHeight = 18;
            double margin = 2;

            int maxItems = (int)((height - currentY) / (itemHeight + margin));
            int displayCount = Math.Min(data.DailyItems.Count, maxItems);
            if (data.DailyItems.Count > maxItems && maxItems > 0) displayCount = maxItems - 1;

            for (int i = 0; i < displayCount; i++)
            {
                Rect itemRect = new(2, currentY, width - 4, itemHeight);
                if (itemRect.Contains(pos))
                {
                    return data.DailyItems[i];
                }
                currentY += itemHeight + margin;
            }

            return null;
        }

        // マウスホバー時のカーソル変更
        protected override void OnMouseMove(System.Windows.Input.MouseEventArgs e)
        {
            base.OnMouseMove(e);
            
            var item = GetItemAtPosition(e.GetPosition(this));
            if (item != null)
            {
                Cursor = System.Windows.Input.Cursors.Hand;
            }
            else
            {
                Cursor = System.Windows.Input.Cursors.Arrow;
            }
        }

        // ヒットテストロジック（マウスクリック時の要素特定用）
        protected override void OnMouseLeftButtonDown(MouseButtonEventArgs e)
        {
            base.OnMouseLeftButtonDown(e);

            var data = CellData;
            if (data == null) return;

            // ダブルクリック時のみアクションを発火する
            if (e.ClickCount == 2)
            {
                var item = GetItemAtPosition(e.GetPosition(this));
                if (item != null)
                {
                    var args = new ScheduleItemClickedEventArgs(ItemClickedEvent, this, item);
                    RaiseEvent(args);
                    e.Handled = true;
                }
                else
                {
                    RaiseEvent(new RoutedEventArgs(CellClickedEvent, this));
                    e.Handled = true;
                }
            }
        }

        protected override void OnMouseRightButtonDown(MouseButtonEventArgs e)
        {
            base.OnMouseRightButtonDown(e);

            var item = GetItemAtPosition(e.GetPosition(this));
            if (item != null)
            {
                var args = new ScheduleItemClickedEventArgs(ItemRightClickedEvent, this, item);
                RaiseEvent(args);
                e.Handled = true;
            }
        }
    }

    public class ScheduleItemClickedEventArgs(RoutedEvent routedEvent, object source, ScheduleItem item) : RoutedEventArgs(routedEvent, source)
    {
        public ScheduleItem Item { get; } = item;
    }
}
