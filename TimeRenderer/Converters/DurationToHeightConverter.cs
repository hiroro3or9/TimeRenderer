using System;
using System.Globalization;
using System.Windows.Data;
using TimeRenderer.Models;

namespace TimeRenderer.Converters;

public class DurationToHeightConverter : IValueConverter
{
    private const string ParameterAddExtension = "ADD_EXTENSION";
    public double PixelsPerHour { get; set; } = Helpers.LayoutConstants.PixelsPerHour;

    public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
    {
        double hours = value switch
        {
            double h => h,
            ScheduleItem item => item.DurationHours,
            TimeSpan span => span.TotalHours,
            _ => 0.0
        };

        double pixels = hours * PixelsPerHour;

        // L字型の足の部分を追加するためのパラメータ
        if (parameter as string == ParameterAddExtension)
        {
            pixels += Helpers.LayoutConstants.PixelsPerHour * 0.25; // 15分相当の足
        }

        return pixels;
    }

    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
    {
        throw new NotImplementedException();
    }
}
