using System.Windows.Resources;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using Drawing = System.Drawing;
using Drawing2D = System.Drawing.Drawing2D;
using WinForms = System.Windows.Forms;

namespace TimeRenderer.Helpers
{
    /// <summary>
    /// アプリ固有のアイコン (Assets/AppIcon.ico) を扱うヘルパー。
    /// </summary>
    internal static partial class AppIconHelper
    {
        private const string IconResourceUri = "pack://application:,,,/Assets/AppIcon.ico";

        /// <summary>
        /// タスクトレイ用のアイコンを生成する。
        /// AppIcon.ico は 16/24/32/48/64/256px のフレームを持つので、
        /// 現在の DPI でのトレイ推奨サイズに最も近いフレームが選ばれる。
        /// 読み込みに失敗した場合はシステムアイコンにフォールバックする。
        /// </summary>
        public static Drawing.Icon CreateTrayIcon()
            => CreateIcon(WinForms.SystemInformation.SmallIconSize);

        /// <summary>タスクバー用に、トレイより高解像度のアイコンを生成する。</summary>
        public static Drawing.Icon CreateWindowIcon()
            => CreateIcon(WinForms.SystemInformation.IconSize);

        private static Drawing.Icon CreateIcon(Drawing.Size size)
        {
            try
            {
                StreamResourceInfo? info = System.Windows.Application.GetResourceStream(new Uri(IconResourceUri));
                if (info?.Stream is { } stream)
                {
                    using (stream)
                    {
                        return new Drawing.Icon(stream, size);
                    }
                }
            }
            catch (Exception)
            {
                // リソースが見つからない・壊れている場合はフォールバックする
            }

            return (Drawing.Icon)Drawing.SystemIcons.Application.Clone();
        }

        /// <summary>
        /// 元のアイコンへ勤務・記録状態を重ねたアイコンを生成する。
        /// 出勤中は緑の外周、記録中は右下の赤いドットで区別する。
        /// </summary>
        public static Drawing.Icon CreateStatusIcon(Drawing.Icon baseIcon, bool isWorking, bool isRecording)
        {
            try
            {
                using Drawing.Bitmap bitmap = baseIcon.ToBitmap();
                using Drawing.Graphics graphics = Drawing.Graphics.FromImage(bitmap);
                graphics.SmoothingMode = Drawing2D.SmoothingMode.AntiAlias;
                graphics.CompositingQuality = Drawing2D.CompositingQuality.HighQuality;
                graphics.PixelOffsetMode = Drawing2D.PixelOffsetMode.HighQuality;

                if (isWorking)
                {
                    DrawWorkingOutline(graphics, bitmap.Size);
                }

                if (isRecording)
                {
                    DrawRecordingBadge(graphics, bitmap.Size);
                }

                nint iconHandle = bitmap.GetHicon();
                try
                {
                    using Drawing.Icon handleIcon = Drawing.Icon.FromHandle(iconHandle);
                    return (Drawing.Icon)handleIcon.Clone();
                }
                finally
                {
                    _ = DestroyIcon(iconHandle);
                }
            }
            catch (Exception)
            {
                // 状態表示の生成に失敗しても、トレイアイコン自体は表示し続ける
                return (Drawing.Icon)baseIcon.Clone();
            }
        }

        private static void DrawWorkingOutline(Drawing.Graphics graphics, Drawing.Size iconSize)
        {
            float shortestSide = Math.Min(iconSize.Width, iconSize.Height);
            float lineWidth = Math.Max(1.2f, shortestSide * 0.055f);
            float inset = (lineWidth / 2f) + Math.Max(0.25f, shortestSide * 0.01f);
            float cornerRadius = shortestSide * 0.18f;
            Drawing.RectangleF bounds = new(
                inset,
                inset,
                iconSize.Width - (inset * 2f),
                iconSize.Height - (inset * 2f));

            using Drawing2D.GraphicsPath path = CreateRoundedRectangle(bounds, cornerRadius);
            using Drawing.Pen pen = new(Drawing.Color.FromArgb(240, 34, 197, 94), lineWidth)
            {
                LineJoin = Drawing2D.LineJoin.Round
            };
            graphics.DrawPath(pen, path);
        }

        private static void DrawRecordingBadge(Drawing.Graphics graphics, Drawing.Size iconSize)
        {
            float shortestSide = Math.Min(iconSize.Width, iconSize.Height);
            float badgeDiameter = Math.Max(6.5f, shortestSide * 0.39f);
            float margin = Math.Max(0.6f, shortestSide * 0.035f);
            float borderWidth = Math.Max(1.1f, shortestSide * 0.065f);
            Drawing.RectangleF badgeBounds = new(
                iconSize.Width - badgeDiameter - margin,
                iconSize.Height - badgeDiameter - margin,
                badgeDiameter,
                badgeDiameter);

            Drawing.RectangleF shadowBounds = badgeBounds;
            shadowBounds.Offset(0, Math.Max(0.5f, shortestSide * 0.02f));
            shadowBounds.Inflate(Math.Max(0.2f, shortestSide * 0.01f), Math.Max(0.2f, shortestSide * 0.01f));
            using Drawing.Brush shadowBrush = new Drawing.SolidBrush(Drawing.Color.FromArgb(90, 0, 0, 0));
            using Drawing.Brush borderBrush = new Drawing.SolidBrush(Drawing.Color.White);
            using Drawing.Brush statusBrush = new Drawing.SolidBrush(Drawing.Color.FromArgb(245, 220, 38, 38));
            graphics.FillEllipse(shadowBrush, shadowBounds);
            graphics.FillEllipse(borderBrush, badgeBounds);
            graphics.FillEllipse(statusBrush, Drawing.RectangleF.Inflate(badgeBounds, -borderWidth, -borderWidth));
        }

        private static Drawing2D.GraphicsPath CreateRoundedRectangle(Drawing.RectangleF bounds, float radius)
        {
            float diameter = radius * 2f;
            Drawing2D.GraphicsPath path = new();
            path.AddArc(bounds.Left, bounds.Top, diameter, diameter, 180, 90);
            path.AddArc(bounds.Right - diameter, bounds.Top, diameter, diameter, 270, 90);
            path.AddArc(bounds.Right - diameter, bounds.Bottom - diameter, diameter, diameter, 0, 90);
            path.AddArc(bounds.Left, bounds.Bottom - diameter, diameter, diameter, 90, 90);
            path.CloseFigure();
            return path;
        }

        /// <summary>System.Drawing.Icon を WPF のウィンドウアイコンへ変換する。</summary>
        public static ImageSource? CreateImageSource(Drawing.Icon icon)
        {
            try
            {
                BitmapSource source = Imaging.CreateBitmapSourceFromHIcon(
                    icon.Handle,
                    Int32Rect.Empty,
                    BitmapSizeOptions.FromEmptyOptions());
                source.Freeze();
                return source;
            }
            catch (Exception)
            {
                return null;
            }
        }

        [LibraryImport("user32.dll", SetLastError = true)]
        [return: MarshalAs(UnmanagedType.Bool)]
        private static partial bool DestroyIcon(nint hIcon);
    }
}
