using System.Globalization;
using System.Windows.Data;
using System.Windows.Media;
using Brush = System.Windows.Media.Brush;
using Brushes = System.Windows.Media.Brushes;

namespace TimeRenderer.Converters;

/// <summary>
/// カテゴリ色を薄い背景色へ変換する。
/// 予定は色の所属だけ分かる程度に抑え、フルカラーの実績を視覚的に優先するために使う。
/// </summary>
public sealed class BrushToSubtleBackgroundConverter : IValueConverter
{
    private const double PlannedOpacity = 0.3;

    public object Convert(object value, Type targetType, object parameter, CultureInfo culture) =>
        CreateSubtleBrush(value as Brush);

    public static Brush CreateSubtleBrush(Brush? source)
    {
        if (source == null) return Brushes.Transparent;

        var result = source.CloneCurrentValue();
        result.Opacity *= PlannedOpacity;
        if (result.CanFreeze) result.Freeze();
        return result;
    }

    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture) =>
        throw new NotSupportedException();
}
