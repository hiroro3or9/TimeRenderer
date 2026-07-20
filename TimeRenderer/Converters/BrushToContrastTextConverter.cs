using System;
using System.Globalization;
using System.Windows.Data;
using System.Windows.Media;
using Color = System.Windows.Media.Color;

namespace TimeRenderer.Converters;

/// <summary>
/// 背景ブラシの輝度から、読みやすい文字色（黒 or 白）を選ぶ。
///
/// 従来は予定バーの文字色を #1E293B に固定していたため、
/// 濃いカテゴリ色（DarkOrange 等）やダークモードで文字が潰れていた。
/// </summary>
public class BrushToContrastTextConverter : IValueConverter
{
    /// <summary>この輝度を超えたら黒文字にする（0-255）</summary>
    private const double Threshold = 140.0;

    private static readonly SolidColorBrush DarkText = CreateFrozen(Color.FromRgb(0x1E, 0x29, 0x3B));
    private static readonly SolidColorBrush LightText = CreateFrozen(Color.FromRgb(0xF8, 0xFA, 0xFC));

    // 副次的なテキスト（メモ・時刻）用。主テキストと同じ側の色を薄くして使う
    private static readonly SolidColorBrush DarkMutedText = CreateFrozen(Color.FromArgb(0xB0, 0x1E, 0x29, 0x3B));
    private static readonly SolidColorBrush LightMutedText = CreateFrozen(Color.FromArgb(0xC0, 0xF8, 0xFA, 0xFC));

    /// <summary>ConverterParameter に指定すると、控えめな文字色を返す</summary>
    private const string MutedParameter = "Muted";

    public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
    {
        bool muted = parameter as string == MutedParameter;

        var color = ExtractColor(value);
        if (color is not Color c) return muted ? DarkMutedText : DarkText;

        // ITU-R BT.601 の輝度式。人間の視感度に合わせた重み付け
        double luminance = (0.299 * c.R) + (0.587 * c.G) + (0.114 * c.B);

        // 半透明の場合は明るい背景に載る前提で、実効輝度を白側に寄せて評価する
        if (c.A < 255)
        {
            double alpha = c.A / 255.0;
            luminance = (luminance * alpha) + (255.0 * (1.0 - alpha));
        }

        bool useDark = luminance > Threshold;
        return muted
            ? (useDark ? DarkMutedText : LightMutedText)
            : (useDark ? DarkText : LightText);
    }

    private static Color? ExtractColor(object value) => value switch
    {
        SolidColorBrush solid => solid.Color,
        Color color => color,
        // グラデーション等は最初のストップの色で近似する
        GradientBrush gradient when gradient.GradientStops.Count > 0 => gradient.GradientStops[0].Color,
        _ => null
    };

    private static SolidColorBrush CreateFrozen(Color color)
    {
        var brush = new SolidColorBrush(color);
        brush.Freeze();
        return brush;
    }

    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
        => throw new NotImplementedException();
}
