using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Windows.Data;

namespace TimeRenderer.Converters
{
    internal static class DateTimeHelper
    {
        /// <summary>
        /// 指定された日付を含む週の開始日（月曜日）を取得します
        /// </summary>
        public static DateTime GetStartOfWeek(this DateTime date)
        {
            int diff = (7 + (date.DayOfWeek - DayOfWeek.Monday)) % 7;
            return date.AddDays(-1 * diff).Date;
        }
    }

    public class TimeToPositionConverter : IMultiValueConverter
    {
        public double PixelsPerHour { get; set; } = 60.0;
        // 後方互換のためプロパティは残すが、バインディング優先
        public double StartHour { get; set; } = 0.0;

        public object Convert(object[] values, Type targetType, object parameter, CultureInfo culture)
        {
            if (values.Length < ConverterIndices.TimeToPosition.RequiredCount || values[ConverterIndices.TimeToPosition.Time] is not DateTime time)
                return 0.0;

            double startHour = (values.Length > ConverterIndices.TimeToPosition.DisplayStartHour && values[ConverterIndices.TimeToPosition.DisplayStartHour] is int sh) ? sh : StartHour;

            return (time.TimeOfDay.TotalHours - startHour) * PixelsPerHour;
        }

        public object[] ConvertBack(object value, Type[] targetTypes, object parameter, CultureInfo culture)
        {
            throw new NotImplementedException();
        }
    }

    public class DurationToHeightConverter : IValueConverter
    {
        private const string ParameterAddExtension = "ADD_EXTENSION";
        public double PixelsPerHour { get; set; } = 60.0;

        public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
        {
            double hours = value switch
            {
                double h => h,
                ScheduleItem item => item.DurationHours,
                TimeSpan span => span.TotalHours,
                _ => 0.0
            };

            double pixels = hours * PixelsPerHour;

            // L字型の足の部分を追加するためのパラメータ
            if (parameter as string == ParameterAddExtension)
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
        private const string ParameterWidth = "WIDTH";
        private const double HiddenOutPosition = -10000.0;

        public object Convert(object[] values, Type targetType, object parameter, CultureInfo culture)
        {
            if (values.Length < ConverterIndices.DateToPagePosition.RequiredCount || 
                values[ConverterIndices.DateToPagePosition.CanvasActualWidth] is not double actualWidth || 
                values[ConverterIndices.DateToPagePosition.StartTime] is not DateTime itemStartTime)
                return 0.0;

            var itemDate = itemStartTime.Date;
            bool isAllDay = values.Length > ConverterIndices.DateToPagePosition.IsAllDay && values[ConverterIndices.DateToPagePosition.IsAllDay] is bool b && b;
            
            // 終日イベントの場合は、ColumnIndex は縦位置用なので横位置計算には使わない（常に0扱い）
            int columnIndex = (values[ConverterIndices.DateToPagePosition.ColumnIndex] is int c && !isAllDay) ? c : 0;
            int maxColumnIndex = (values[ConverterIndices.DateToPagePosition.MaxColumnIndex] is int m && !isAllDay) ? m : 0;
            int totalColumns = maxColumnIndex + 1;

            var baseDate = values[ConverterIndices.DateToPagePosition.CurrentDate] is DateTime d ? d.Date : DateTime.Today;
            var mode = values[ConverterIndices.DateToPagePosition.ViewMode] is MainViewModel.ViewMode viewMode ? viewMode : MainViewModel.ViewMode.Day;
            
            // パラメータが "WIDTH" の場合は幅を返す、それ以外はX座標を返す
            bool isWidth = parameter as string == ParameterWidth;

            if (mode == MainViewModel.ViewMode.Day)
            {
                // 1日表示モード：日付が一致しない場合は非表示
                if (itemDate != baseDate.Date)
                    return isWidth ? 0.0 : HiddenOutPosition;

                double columnWidth = actualWidth / totalColumns;
                return isWidth ? columnWidth : columnIndex * columnWidth;
            }
            else // Week Mode
            {
                // 週の開始日（月曜日）を計算
                var weekStart = baseDate.GetStartOfWeek();
                var weekEnd = weekStart.AddDays(7);
                
                // 週の範囲外なら表示しない
                if (itemDate < weekStart || itemDate >= weekEnd)
                    return isWidth ? 0.0 : HiddenOutPosition;

                var dayDiff = (itemDate - weekStart).TotalDays;
                double dayColumnWidth = actualWidth / 7.0;
                
                // itemWidth は 1日の幅 / 重なり数
                double itemWidth = dayColumnWidth / Math.Max(1, totalColumns);

                return isWidth ? itemWidth : (dayDiff * dayColumnWidth) + (columnIndex * itemWidth);
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
    public class DateToVisibleDaysConverter : IMultiValueConverter
    {
        public object Convert(object[] values, Type targetType, object parameter, CultureInfo culture)
        {
            if (values.Length < ConverterIndices.DateToVisibleDays.RequiredCount || values[ConverterIndices.DateToVisibleDays.CurrentDate] is not DateTime date)
                return new List<DateTime>();

            var mode = values[ConverterIndices.DateToVisibleDays.ViewMode] is MainViewModel.ViewMode m ? m : MainViewModel.ViewMode.Day;

            if (mode == MainViewModel.ViewMode.Day)
            {
                return new List<DateTime> { date.Date };
            }
            else
            {
                var start = date.GetStartOfWeek();
                return Enumerable.Range(0, 7).Select(i => start.AddDays(i)).ToList();
            }
        }

        public object[] ConvertBack(object value, Type[] targetTypes, object parameter, CultureInfo culture)
        {
            throw new NotImplementedException();
        }
    }
}
