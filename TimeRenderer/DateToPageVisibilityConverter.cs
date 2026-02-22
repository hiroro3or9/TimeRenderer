using System;
using System.Globalization;
using System.Windows;
using System.Windows.Data;

namespace TimeRenderer
{
    public class DateToPageVisibilityConverter : IMultiValueConverter
    {
        public object Convert(object[] values, Type targetType, object parameter, CultureInfo culture)
        {
            // values[0]: StartTime (DateTime)
            // values[1]: CurrentDate (DateTime)
            // values[2]: ViewMode (Day/Week)

            if (values.Length < 3) return Visibility.Collapsed;

            if (values[0] is not DateTime itemStartTime) return Visibility.Collapsed;
            var itemDate = itemStartTime.Date;

            var baseDate = values[1] is DateTime d ? d.Date : DateTime.Today;
            var mode = values[2] is MainViewModel.ViewMode viewMode ? viewMode : MainViewModel.ViewMode.Day;

            if (mode == MainViewModel.ViewMode.Day)
            {
                // 1日表示モード：日付が一致しない場合は非表示
                return itemDate.Date == baseDate.Date ? Visibility.Visible : Visibility.Collapsed;
            }
            else // Week Mode
            {
                // 週の開始日（月曜日）を計算する
                var diff = (7 + (baseDate.DayOfWeek - DayOfWeek.Monday)) % 7;
                var weekStart = baseDate.AddDays(-1 * diff).Date;
                var weekEnd = weekStart.AddDays(7);

                // 週の範囲内なら表示
                return (itemDate >= weekStart && itemDate < weekEnd) ? Visibility.Visible : Visibility.Collapsed;
            }
        }

        public object[] ConvertBack(object value, Type[] targetTypes, object parameter, CultureInfo culture)
        {
            throw new NotImplementedException();
        }
    }
}
