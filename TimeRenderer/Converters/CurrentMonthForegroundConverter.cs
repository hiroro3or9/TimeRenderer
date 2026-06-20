using System;
using System.Globalization;
using System.Windows.Data;
using System.Windows.Media;
using TimeRenderer.Helpers;

namespace TimeRenderer.Converters;

public class CurrentMonthForegroundConverter : IMultiValueConverter
{
    public string CurrentMonthBrushKey { get; set; } = "TextPrimaryBrush";
    public string OtherMonthBrushKey   { get; set; } = "TextSecondaryBrush";

    public object Convert(object[] values, Type targetType, object parameter, CultureInfo culture)
    {
        var currentBrush = ThemeHelper.GetBrush(CurrentMonthBrushKey, System.Windows.Media.Brushes.Black);
        var otherBrush   = ThemeHelper.GetBrush(OtherMonthBrushKey,   System.Windows.Media.Brushes.Gray);

        if (values.Length < 2 || values[0] is not DateTime cellDate || values[1] is not DateTime currentDate)
            return currentBrush;

        bool isCurrentMonth = cellDate.Year == currentDate.Year && cellDate.Month == currentDate.Month;
        
        System.Windows.Media.Brush baseBrush = values.Length >= 3 && values[2] is System.Windows.Media.Brush b ? b : currentBrush;

        return isCurrentMonth ? baseBrush : otherBrush;
    }

    public object[] ConvertBack(object value, Type[] targetTypes, object parameter, CultureInfo culture)
    {
        throw new NotImplementedException();
    }
}
