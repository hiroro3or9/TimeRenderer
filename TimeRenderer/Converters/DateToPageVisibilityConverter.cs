using System;
using System.Globalization;
using System.Windows;
using System.Windows.Data;

namespace TimeRenderer.Converters;

public class DateToPageVisibilityConverter : IMultiValueConverter
{
    public object Convert(object[] values, Type targetType, object parameter, CultureInfo culture)
    {
        if (values.Length < ConverterIndices.DateToPageVisibility.RequiredCount || values[ConverterIndices.DateToPageVisibility.StartTime] is not DateTime itemStartTime)
            return Visibility.Collapsed;

        var itemDate = itemStartTime.Date;
        var baseDate = values[ConverterIndices.DateToPageVisibility.CurrentDate] is DateTime d ? d.Date : DateTime.Today;
        var mode = values[ConverterIndices.DateToPageVisibility.ViewMode] is MainViewModel.ViewMode viewMode ? viewMode : MainViewModel.ViewMode.Day;

        if (mode == MainViewModel.ViewMode.Day)
        {
            // 1日表示モード：日付が一致しない場合は非表示
            return itemDate == baseDate ? Visibility.Visible : Visibility.Collapsed;
        }
        else // Week Mode
        {
            // 週の開始日（月曜日）を計算する
            var weekStart = baseDate.GetStartOfWeek();
            var weekEnd = weekStart.AddDays(7);

            // 週の範囲内なら表示
            return (itemDate >= weekStart && itemDate < weekEnd) ? Visibility.Visible : Visibility.Collapsed;
        }
    }

    public object[] ConvertBack(object value, Type[] targetTypes, object parameter, CultureInfo culture) => throw new NotImplementedException();
}
