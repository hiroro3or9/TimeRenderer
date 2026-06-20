using System;
using System.Globalization;
using System.Windows.Data;
using System.Windows.Media;
using TimeRenderer.Helpers;

namespace TimeRenderer.Converters;

public class CurrentMonthBackgroundConverter : IMultiValueConverter
{
    public string CurrentMonthBrushKey { get; set; } = "SurfaceBrush";
    public string OtherMonthBrushKey   { get; set; } = "MutedBackgroundBrush";

    public object Convert(object[] values, Type targetType, object parameter, CultureInfo culture)
    {
        var currentBrush = ThemeHelper.GetBrush(CurrentMonthBrushKey, System.Windows.Media.Brushes.Transparent);
        var otherBrush   = ThemeHelper.GetBrush(OtherMonthBrushKey,   System.Windows.Media.Brushes.Transparent);

        if (values.Length < 2 || values[0] is not DateTime cellDate || values[1] is not DateTime currentDate)
            return currentBrush;

        bool isCurrentMonth = cellDate.Year == currentDate.Year && cellDate.Month == currentDate.Month;
        
        System.Windows.Media.Brush baseBrush = currentBrush;
        if (values.Length >= 3 && values[2] is System.Windows.Media.Brush b)
        {
            baseBrush = b;
        }

        return isCurrentMonth ? baseBrush : otherBrush;
    }

    public object[] ConvertBack(object value, Type[] targetTypes, object parameter, CultureInfo culture)
    {
        throw new NotImplementedException();
    }
}
