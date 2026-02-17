using System;
using System.Globalization;
using System.Windows.Data;
using System.Windows.Media;

namespace TimeRenderer
{
    public class DateToBackgroundBrushConverter : IValueConverter
    {
        public System.Windows.Media.Brush TodayBrush { get; set; } = System.Windows.Media.Brushes.Transparent;
        public System.Windows.Media.Brush DefaultBrush { get; set; } = System.Windows.Media.Brushes.Transparent;

        public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
        {
            if (value is DateTime date && date.Date == DateTime.Today)
            {
                return TodayBrush;
            }
            return DefaultBrush;
        }

        public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
        {
            throw new NotImplementedException();
        }
    }
}
