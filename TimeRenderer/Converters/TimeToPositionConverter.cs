using System;
using System.Globalization;
using System.Windows.Data;

namespace TimeRenderer.Converters;

public class TimeToPositionConverter : IMultiValueConverter
{
    public double PixelsPerHour { get; set; } = Helpers.LayoutConstants.PixelsPerHour;
    // 後方互換のためプロパティは残すが、バインディング優先
    public double StartHour { get; set; } = 0.0;

    public object Convert(object[] values, Type targetType, object parameter, CultureInfo culture)
    {
        if (values.Length < ConverterIndices.TimeToPosition.RequiredCount || values[ConverterIndices.TimeToPosition.Time] is not DateTime time)
            return 0.0;

        double startHour = (values.Length > ConverterIndices.TimeToPosition.DisplayStartHour && values[ConverterIndices.TimeToPosition.DisplayStartHour] is int sh) ? sh : StartHour;

        return (time.TimeOfDay.TotalHours - startHour) * PixelsPerHour;
    }

    public object[] ConvertBack(object value, Type[] targetTypes, object parameter, CultureInfo culture)
    {
        throw new NotImplementedException();
    }
}
