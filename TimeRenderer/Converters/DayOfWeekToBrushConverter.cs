using System;
using System.Globalization;
using System.Windows.Data;
using Media = System.Windows.Media;

namespace TimeRenderer.Converters
{
    public class DayOfWeekToBrushConverter : IValueConverter
    {
        public Media.Brush SaturdayBrush { get; set; } = Media.Brushes.Blue;
        public Media.Brush SundayBrush { get; set; } = Media.Brushes.Red;
        public Media.Brush DefaultBrush { get; set; } = Media.Brushes.Black;

        public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
        {
            if (value is DateTime date)
            {
                return date.DayOfWeek switch
                {
                    DayOfWeek.Saturday => SaturdayBrush,
                    DayOfWeek.Sunday => SundayBrush,
                    _ => DefaultBrush
                };
            }
            return DefaultBrush;
        }

        public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture) => throw new NotImplementedException();
    }
}
