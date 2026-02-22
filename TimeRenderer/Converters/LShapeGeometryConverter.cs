using System;
using System.Globalization;
using System.Windows;
using System.Windows.Data;
using System.Windows.Media;
using Point = System.Windows.Point;

namespace TimeRenderer.Converters
{
    public class LShapeGeometryConverter : IMultiValueConverter
    {
        // カットする高さ (15分相当: 60px/hour なら 15min = 15px)
        private const double CutHeight = 15.0;
        // 右側の足の幅
        private const double LegWidth = 30.0;

        public object Convert(object[] values, Type targetType, object parameter, CultureInfo culture)
        {
            if (values.Length < ConverterIndices.LShapeGeometry.RequiredCount || 
                values[ConverterIndices.LShapeGeometry.Width] is not double width || 
                values[ConverterIndices.LShapeGeometry.Height] is not double height)
            {
                return Geometry.Empty;
            }

            // 高さがカット幅より小さい、または幅が足の幅より小さい場合は単純な矩形
            if (height <= CutHeight || width <= LegWidth)
            {
                return new RectangleGeometry(new Rect(0, 0, width, height));
            }

            // L字型 (左下が欠けている)
            // (0,0) -> (W,0) -> (W,H) -> (W-LegW, H) -> (W-LegW, H-CutH) -> (0, H-CutH) -> Close

            StreamGeometry geometry = new();
            using (StreamGeometryContext ctx = geometry.Open())
            {
                ctx.BeginFigure(new Point(0, 0), true, true);
                ctx.LineTo(new Point(width, 0), true, false);
                ctx.LineTo(new Point(width, height), true, false);
                ctx.LineTo(new Point(width - LegWidth, height), true, false);
                ctx.LineTo(new Point(width - LegWidth, height - CutHeight), true, false);
                ctx.LineTo(new Point(0, height - CutHeight), true, false);
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
