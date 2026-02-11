using System;
using System.Globalization;
using System.Windows.Data;

namespace TimeRenderer
{
    public class TimeToPositionConverter : IValueConverter
    {
        public double PixelsPerHour { get; set; } = 60.0;

        public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
        {
            if (value is DateTime time)
            {
                // Assuming the schedule shows one day, from 0:00 to 24:00
                // We'll map the time of day to the position.
                var timeOfDay = time.TimeOfDay;
                return timeOfDay.TotalHours * PixelsPerHour;
            }
            return 0.0;
        }

        public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
        {
            throw new NotImplementedException();
        }
    }

    public class DurationToHeightConverter : IValueConverter
    {
        public double PixelsPerHour { get; set; } = 60.0;

        public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
        {
            double hours = 0.0;
            if (value is double h) hours = h;
            else if (value is ScheduleItem item) hours = item.DurationHours;
            else if (value is TimeSpan span) hours = span.TotalHours;

            double pixels = hours * PixelsPerHour;

            // L字型の足の部分を追加するためのパラメータ
            if (parameter is string p && p == "ADD_EXTENSION")
            {
                pixels += 15.0; // 15分相当 (60px/h * 0.25h)
            }

            return pixels;
        }

        public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
        {
            throw new NotImplementedException();
        }
    }

    public class DateToPagePositionConverter : IMultiValueConverter
    {
        public object Convert(object[] values, Type targetType, object parameter, CultureInfo culture)
        {
            // values[0]: StartTime (DateTime)
            // values[1]: ColumnIndex (int)
            // values[2]: MaxColumnIndex (int)
            // values[3]: CurrentDate (DateTime)
            // values[4]: ViewMode (Day/Week)
            // values[5]: Canvas ActualWidth (double)

            if (values.Length < 6 || values[5] is not double actualWidth)
                return 0.0;

            if (values[0] is not DateTime itemStartTime) return 0.0;
            var itemDate = itemStartTime.Date;

            bool isAllDay = false;
            // 7番目のパラメータ(IsAllDay)があれば取得
            if (values.Length > 6 && values[6] is bool b)
            {
                isAllDay = b;
            }
            
            // 終日イベントの場合は、ColumnIndex は縦位置用なので横位置計算には使わない（常に0扱い）
            int columnIndex = (values[1] is int c && !isAllDay) ? c : 0;
            int maxColumnIndex = (values[2] is int m && !isAllDay) ? m : 0;
            int totalColumns = maxColumnIndex + 1;

            var baseDate = values[3] is DateTime d ? d.Date : DateTime.Today;
            var mode = values[4] is MainViewModel.ViewMode viewMode ? viewMode : MainViewModel.ViewMode.Day;
            
            // パラメータが "WIDTH" の場合は幅を返す、それ以外はX座標を返す
            bool isWidth = parameter as string == "WIDTH";

            if (mode == MainViewModel.ViewMode.Day)
            {
                // 1日表示モード：日付が一致しない場合は非表示
                if (itemDate.Date != baseDate.Date)
                {
                    return isWidth ? 0.0 : -10000.0;
                }

                double columnWidth = actualWidth / totalColumns;

                if (isWidth) return columnWidth;
                return columnIndex * columnWidth;
            }
            else // Week Mode
            {
                // baseDate は CurrentDate なので、週の開始日（月曜日）を計算する
                var diff = (7 + (baseDate.DayOfWeek - DayOfWeek.Monday)) % 7;
                var weekStart = baseDate.AddDays(-1 * diff).Date;
                
                var dayDiff = (itemDate - weekStart).TotalDays;
                
                // 週の範囲外なら表示しない（幅0）
                if (dayDiff < 0 || dayDiff >= 7)
                {
                    return isWidth ? 0.0 : -10000.0; // 画面外へ
                }

                double dayColumnWidth = actualWidth / 7.0;
                double itemWidth = dayColumnWidth / totalColumns;

                if (isWidth) return itemWidth;
                return (dayDiff * dayColumnWidth) + (columnIndex * itemWidth);
            }
        }

        public object[] ConvertBack(object value, Type[] targetTypes, object parameter, CultureInfo culture)
        {
            throw new NotImplementedException();
        }
    }

    public class IndexToTopMarginConverter : IValueConverter
    {
        public double ItemHeight { get; set; } = 24.0;
        public double MarginTop { get; set; } = 2.0;

        public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
        {
            if (value is int index)
            {
                return (index * ItemHeight) + MarginTop;
            }
            return MarginTop;
        }

        public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
        {
            throw new NotImplementedException();
        }
    }
}
