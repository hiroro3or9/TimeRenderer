using System;
using System.Globalization;
using System.Windows.Data;
using TimeRenderer.Helpers;

namespace TimeRenderer.Converters
{
    public class DayOfWeekToBrushConverter : IMultiValueConverter
    {
        public string SaturdayBrushKey { get; set; } = "PrimaryBrush";
        public string SundayBrushKey { get; set; } = "DangerBrush";
        public string DefaultBrushKey { get; set; } = "TextPrimaryBrush";

        public object Convert(object[] values, Type targetType, object parameter, CultureInfo culture)
        {
            if (values.Length < 1 || values[0] is not DateTime date)
                return ThemeHelper.GetBrush(DefaultBrushKey, System.Windows.Media.Brushes.Black);
            string key = date.DayOfWeek switch
            {
                DayOfWeek.Saturday => SaturdayBrushKey,
                DayOfWeek.Sunday => SundayBrushKey,
                _ => DefaultBrushKey
            };
            return ThemeHelper.GetBrush(key, System.Windows.Media.Brushes.Black);
        }

        public object[] ConvertBack(object value, Type[] targetTypes, object parameter, CultureInfo culture)
            => throw new NotImplementedException();
    }
}
