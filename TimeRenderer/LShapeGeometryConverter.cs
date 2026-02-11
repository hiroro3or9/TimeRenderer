using System;
using System.Globalization;
using System.Windows;
using System.Windows.Data;
using System.Windows.Media;
using Point = System.Windows.Point;

namespace TimeRenderer
{
    public class LShapeGeometryConverter : IMultiValueConverter
    {
        public object Convert(object[] values, Type targetType, object parameter, CultureInfo culture)
        {
            if (values.Length < 2 || !(values[0] is double width) || !(values[1] is double height))
            {
                return Geometry.Empty;
            }

            // カットする高さ (15分相当)
            // 60px/hour なので 15min = 15px
            double cutHeight = 15.0;

            // 右側の足の幅
            double legWidth = 30.0;

            // 高さがカット幅より小さい場合は単純な矩形
            if (height <= cutHeight)
            {
                return new RectangleGeometry(new Rect(0, 0, width, height));
            }

            // 幅が足の幅より小さい場合も単純な矩形
            if (width <= legWidth)
            {
                return new RectangleGeometry(new Rect(0, 0, width, height));
            }

            // L字型 (左下が欠けている)
            // (0,0) -> (W,0) -> (W,H) -> (W-LegW, H) -> (W-LegW, H-CutH) -> (0, H-CutH) -> Close

            StreamGeometry geometry = new StreamGeometry();
            using (StreamGeometryContext ctx = geometry.Open())
            {
                ctx.BeginFigure(new Point(0, 0), true, true);
                ctx.LineTo(new Point(width, 0), true, false);
                ctx.LineTo(new Point(width, height), true, false);
                ctx.LineTo(new Point(width - legWidth, height), true, false);
                ctx.LineTo(new Point(width - legWidth, height - cutHeight), true, false);
                ctx.LineTo(new Point(0, height - cutHeight), true, false);
            }
            geometry.Freeze();
            return geometry;
        }

        public object[] ConvertBack(object value, Type[] targetTypes, object parameter, CultureInfo culture)
        {
            throw new NotImplementedException();
        }
    }
}
