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

        // ToDo がクリックされた時のイベント（編集を開く導線）
        public static readonly RoutedEvent TodoClickedEvent = EventManager.RegisterRoutedEvent(
            "TodoClicked", RoutingStrategy.Bubble, typeof(EventHandler<TodoClickedEventArgs>), typeof(CalendarMonthCellControl));

        public event EventHandler<TodoClickedEventArgs> TodoClicked
        {
            add { AddHandler(TodoClickedEvent, value); }
            remove { RemoveHandler(TodoClickedEvent, value); }
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
        private static readonly System.Windows.Media.Brush _todoOverdueBrush = new SolidColorBrush(System.Windows.Media.Color.FromRgb(220, 38, 38));        // #DC2626

        static CalendarMonthCellControl()
        {
            _mutedBackgroundBrush.Freeze();
            _textSecondaryBrush.Freeze();
            _textPrimaryBrush.Freeze();
            _todayBackgroundBrush.Freeze();
            _sundayForegroundBrush.Freeze();
            _saturdayForegroundBrush.Freeze();
            _weekdayForegroundBrush.Freeze();
            _todoOverdueBrush.Freeze();
        }

        // アイテム描画のレイアウト定数（OnRender とヒットテストで共有）
        private const double DayFontSize = 12;
        private const double ItemFontSize = 11;
        private const double ItemHeight = 18;
        private const double ItemMargin = 2;
        private const double ItemPadding = 4;

        /// <summary>
        /// セルに積む1行。予定か ToDo のどちらか一方を持つ。
        /// 描画とヒットテストで同じ並びを使うため、行の組み立てを1か所にまとめている。
        /// </summary>
        private readonly record struct CellRow(ScheduleItem? Item, TodoItem? Todo);

        /// <summary>予定を先に、その下へ ToDo を続けた行の一覧</summary>
        private List<CellRow> BuildRows()
        {
            var rows = new List<CellRow>();
            var data = CellData;
            if (data == null) return rows;

            if (data.DailyItems != null)
            {
                foreach (var item in data.DailyItems) rows.Add(new CellRow(item, null));
            }
            if (data.DailyTodos != null)
            {
                foreach (var todo in data.DailyTodos) rows.Add(new CellRow(null, todo));
            }
            return rows;
        }

        /// <summary>
        /// アイテム描画領域のレイアウトを計算する。
        /// OnRender と GetRowAtPosition で同一ロジック・同一DPIを使用する（高DPIでのクリック判定ずれ防止）。
        /// </summary>
        private (double StartY, int DisplayCount, bool HasMore) GetItemLayout(double height, int rowCount)
        {
            var data = CellData;
            if (data == null) return (0, 0, false);

            double pixelsPerDip = VisualTreeHelper.GetDpi(this).PixelsPerDip;
            var dayText = new FormattedText(
                data.DayText,
                CultureInfo.CurrentUICulture,
                System.Windows.FlowDirection.LeftToRight,
                _dayTypeface,
                DayFontSize,
                System.Windows.Media.Brushes.Black,
                pixelsPerDip);

            double startY = dayText.Height + 8;
            int maxItems = (int)((height - startY) / (ItemHeight + ItemMargin));
            int displayCount = Math.Clamp(rowCount, 0, Math.Max(0, maxItems));
            bool hasMore = rowCount > maxItems;
            if (hasMore && maxItems > 0)
            {
                displayCount = maxItems - 1; // 省略テキストの分1つ減らす
            }
            return (startY, displayCount, hasMore);
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
                DayFontSize,
                fgBrush,
                VisualTreeHelper.GetDpi(this).PixelsPerDip);

            dc.DrawText(dayFormattedText, new System.Windows.Point(4, 4));

            // 3. 予定と ToDo の描画処理（予定が先、その下に ToDo）
            var rows = BuildRows();
            if (rows.Count == 0) return;

            var (currentY, displayCount, hasMoreItems) = GetItemLayout(height, rows.Count);

            for (int i = 0; i < displayCount; i++)
            {
                var rect = new Rect(2, currentY, width - 4, ItemHeight);
                var row = rows[i];

                if (row.Item is { } item) DrawScheduleItem(dc, item, rect, ItemPadding);
                else if (row.Todo is { } todo) DrawTodo(dc, todo, rect, ItemPadding);

                currentY += ItemHeight + ItemMargin;
            }

            // 省略テキストを描画
            if (hasMoreItems)
            {
                var moreText = $"+{rows.Count - displayCount} 件";
                var moreFormattedText = new FormattedText(
                    moreText,
                    CultureInfo.CurrentUICulture,
                    System.Windows.FlowDirection.LeftToRight,
                    _itemTypeface,
                    ItemFontSize,
                    TextSecondaryBrush ?? _textSecondaryBrush,
                    VisualTreeHelper.GetDpi(this).PixelsPerDip);

                dc.DrawText(moreFormattedText, new System.Windows.Point(4, currentY));
            }
        }

        private void DrawScheduleItem(DrawingContext dc, ScheduleItem item, Rect rect, double padding)
        {
            // 予定は枠線、実績はカテゴリ色の塗りで描き分ける。
            var itemBg = item.IsPlanned
                ? MutedBackgroundBrush ?? _mutedBackgroundBrush
                : item.BackgroundColor;
            System.Windows.Media.Pen? itemPen = null;
            if (item.IsPlanned)
            {
                itemPen = new System.Windows.Media.Pen(item.BackgroundColor, 1.5);
                itemPen.Freeze();
            }
            dc.DrawRoundedRectangle(itemBg, itemPen, rect, 4, 4);

            // アイテムのテキストを描画
            var itemText = new FormattedText(
                item.Title,
                CultureInfo.CurrentUICulture,
                System.Windows.FlowDirection.LeftToRight,
                _itemTypeface,
                ItemFontSize,
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

        /// <summary>
        /// ToDo を描く。予定と同じ塗りつぶしの箱にすると作業記録と混ざるため、
        /// 日/週ビューのチップと同じく枠線だけにして「まだやっていないこと」だと分かるようにする。
        /// </summary>
        private void DrawTodo(DrawingContext dc, TodoItem todo, Rect rect, double padding)
        {
            var stroke = todo.IsOverdue ? _todoOverdueBrush : todo.Brush;
            var pen = new System.Windows.Media.Pen(stroke, todo.IsHighPriority ? 2 : 1);
            pen.Freeze();

            dc.DrawRoundedRectangle(MutedBackgroundBrush ?? _mutedBackgroundBrush, pen, rect, ItemHeight / 2, ItemHeight / 2);

            var todoText = new FormattedText(
                todo.Title,
                CultureInfo.CurrentUICulture,
                System.Windows.FlowDirection.LeftToRight,
                _itemTypeface,
                ItemFontSize,
                todo.IsOverdue ? _todoOverdueBrush : (TextPrimaryBrush ?? _textPrimaryBrush),
                VisualTreeHelper.GetDpi(this).PixelsPerDip)
            {
                MaxTextWidth = Math.Max(0, rect.Width - (padding * 3)),
                MaxTextHeight = rect.Height,
                Trimming = TextTrimming.CharacterEllipsis
            };

            double textY = rect.Y + (rect.Height - todoText.Height) / 2;
            dc.DrawText(todoText, new System.Windows.Point(rect.X + (padding * 1.5), textY));
        }

        /// <summary>指定位置にある行（予定または ToDo）を返す</summary>
        private CellRow? GetRowAtPosition(System.Windows.Point pos)
        {
            var rows = BuildRows();
            if (rows.Count == 0) return null;

            double width = ActualWidth;
            var (currentY, displayCount, _) = GetItemLayout(ActualHeight, rows.Count);

            for (int i = 0; i < displayCount; i++)
            {
                Rect itemRect = new(2, currentY, width - 4, ItemHeight);
                if (itemRect.Contains(pos)) return rows[i];
                currentY += ItemHeight + ItemMargin;
            }

            return null;
        }

        private ScheduleItem? GetItemAtPosition(System.Windows.Point pos) => GetRowAtPosition(pos)?.Item;

        // マウスホバー時のカーソル変更
        protected override void OnMouseMove(System.Windows.Input.MouseEventArgs e)
        {
            base.OnMouseMove(e);

            Cursor = GetRowAtPosition(e.GetPosition(this)) != null
                ? System.Windows.Input.Cursors.Hand
                : System.Windows.Input.Cursors.Arrow;
        }

        // ヒットテストロジック（マウスクリック時の要素特定用）
        protected override void OnMouseLeftButtonDown(MouseButtonEventArgs e)
        {
            base.OnMouseLeftButtonDown(e);

            var data = CellData;
            if (data == null) return;

            // ダブルクリック時のみアクションを発火する
            if (e.ClickCount != 2) return;

            switch (GetRowAtPosition(e.GetPosition(this)))
            {
                case { Item: { } item }:
                    RaiseEvent(new ScheduleItemClickedEventArgs(ItemClickedEvent, this, item));
                    break;

                case { Todo: { } todo }:
                    RaiseEvent(new TodoClickedEventArgs(TodoClickedEvent, this, todo));
                    break;

                default:
                    RaiseEvent(new RoutedEventArgs(CellClickedEvent, this));
                    break;
            }
            e.Handled = true;
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

    public class TodoClickedEventArgs(RoutedEvent routedEvent, object source, TodoItem todo) : RoutedEventArgs(routedEvent, source)
    {
        public TodoItem Todo { get; } = todo;
    }
}
