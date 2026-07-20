using System;
using System.Globalization;
using System.Windows;
using System.Windows.Data;
using TimeRenderer.ViewModels;

namespace TimeRenderer.Converters;

public class DateToPageVisibilityConverter : IMultiValueConverter
{
    public object Convert(object[] values, Type targetType, object parameter, CultureInfo culture)
    {
        if (values.Length < ConverterIndices.DateToPageVisibility.RequiredCount || values[ConverterIndices.DateToPageVisibility.StartTime] is not DateTime itemStartTime)
            return Visibility.Collapsed;

        var itemDate = itemStartTime.Date;
        var baseDate = values[ConverterIndices.DateToPageVisibility.CurrentDate] is DateTime d ? d.Date : DateTime.Today;
        var mode = values[ConverterIndices.DateToPageVisibility.ViewMode] is ViewMode viewMode ? viewMode : ViewMode.Day;

        if (mode == ViewMode.Day)
        {
            // 1日表示モード：日付が一致すれば表示する
            // ※曜日フィルタは適用しない（「今日」ボタン等で非表示曜日の日を開いた際に
            //   予定がすべて消えるのを防ぐ。表示中の日の予定は常に見えるべき）
            return itemDate == baseDate ? Visibility.Visible : Visibility.Collapsed;
        }
        else // Week Mode
        {
            // 非表示の曜日のアイテムは表示しない
            if (values[ConverterIndices.DateToPageVisibility.EnabledDays] is IEnumerable<DayOfWeek> enabledDays && !enabledDays.Contains(itemDate.DayOfWeek))
            {
                return Visibility.Collapsed;
            }

            // 週の開始日（月曜日）を計算する
            var weekStart = baseDate.GetStartOfWeek();
            var weekEnd = weekStart.AddDays(7);

            // 週の範囲内なら表示
            return (itemDate >= weekStart && itemDate < weekEnd) ? Visibility.Visible : Visibility.Collapsed;
        }
    }

    public object[] ConvertBack(object value, Type[] targetTypes, object parameter, CultureInfo culture) => throw new NotImplementedException();
}
