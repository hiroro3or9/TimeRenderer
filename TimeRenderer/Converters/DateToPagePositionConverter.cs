using System;
using System.Globalization;
using System.Windows.Data;
using TimeRenderer.ViewModels;

namespace TimeRenderer.Converters;

public class DateToPagePositionConverter : IMultiValueConverter
{
    private const string ParameterWidth = "WIDTH";
    private const double HiddenOutPosition = -10000.0;

    public object Convert(object[] values, Type targetType, object parameter, CultureInfo culture)
    {
        if (values.Length < 6 || 
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

        var enabledDays = (values.Length > 7 ? values[7] : null) as IEnumerable<DayOfWeek>
            ?? [DayOfWeek.Monday, DayOfWeek.Tuesday, DayOfWeek.Wednesday, DayOfWeek.Thursday, DayOfWeek.Friday, DayOfWeek.Saturday, DayOfWeek.Sunday];

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
            
            // 週の範囲外なら表示しない、または非表示の曜日なら表示しない
            if (itemDate < weekStart || itemDate >= weekEnd || !enabledDays.Contains(itemDate.DayOfWeek))
                return isWidth ? 0.0 : HiddenOutPosition;

            // 表示されている曜日のうち、本日のインデックスを計算
            int activeDayIndex = 0;
            int totalActiveDays = 0;
            for (int i = 0; i < 7; i++)
            {
                var currentDay = weekStart.AddDays(i);
                if (enabledDays.Contains(currentDay.DayOfWeek))
                {
                    if (currentDay == itemDate)
                    {
                        activeDayIndex = totalActiveDays;
                    }
                    totalActiveDays++;
                }
            }

            if (totalActiveDays == 0) return 0.0;

            double dayColumnWidth = actualWidth / totalActiveDays;
            
            // itemWidth は 1日の幅 / 重なり数
            double itemWidth = dayColumnWidth / Math.Max(1, totalColumns);

            return isWidth ? itemWidth : (activeDayIndex * dayColumnWidth) + (columnIndex * itemWidth);
        }
    }

    public object[] ConvertBack(object value, Type[] targetTypes, object parameter, CultureInfo culture)
    {
        throw new NotImplementedException();
    }
}
