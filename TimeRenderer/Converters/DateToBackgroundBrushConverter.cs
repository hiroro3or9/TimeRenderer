using System;
using System.Globalization;
using System.Windows;
using System.Windows.Data;
using TimeRenderer.Helpers;

namespace TimeRenderer.Converters;

public class DateToBackgroundBrushConverter : IMultiValueConverter
{
    /// <summary>今日の背景ブラシを取得するリソースキー</summary>
    public string TodayBrushKey   { get; set; } = "PrimarySubtleBrush";
    /// <summary>それ以外の日の背景ブラシを取得するリソースキー</summary>
    public string DefaultBrushKey { get; set; } = "SurfaceBrush";

    public object Convert(object[] values, Type targetType, object parameter, CultureInfo culture)
    {
        if (values.Length < 1 || values[0] is not DateTime date)
            return System.Windows.Media.Brushes.Transparent;

        var todayBrush   = ThemeHelper.GetBrush(TodayBrushKey,   System.Windows.Media.Brushes.Transparent);
        var defaultBrush = ThemeHelper.GetBrush(DefaultBrushKey, System.Windows.Media.Brushes.Transparent);
        return date.Date == DateTime.Today ? todayBrush : defaultBrush;
    }

    public object[] ConvertBack(object value, Type[] targetTypes, object parameter, CultureInfo culture)
        => throw new NotImplementedException();
}
