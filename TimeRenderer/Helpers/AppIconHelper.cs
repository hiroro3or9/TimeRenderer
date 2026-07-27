using System.Windows;
using System.Windows.Resources;
using Drawing = System.Drawing;
using WinForms = System.Windows.Forms;

namespace TimeRenderer.Helpers
{
    /// <summary>
    /// アプリ固有のアイコン (Assets/AppIcon.ico) を扱うヘルパー。
    /// </summary>
    internal static class AppIconHelper
    {
        private const string IconResourceUri = "pack://application:,,,/Assets/AppIcon.ico";

        /// <summary>
        /// タスクトレイ用のアイコンを生成する。
        /// AppIcon.ico は 16/24/32/48/64/256px のフレームを持つので、
        /// 現在の DPI でのトレイ推奨サイズに最も近いフレームが選ばれる。
        /// 読み込みに失敗した場合はシステムアイコンにフォールバックする。
        /// </summary>
        public static Drawing.Icon CreateTrayIcon()
        {
            try
            {
                StreamResourceInfo? info = Application.GetResourceStream(new Uri(IconResourceUri));
                if (info?.Stream is { } stream)
                {
                    using (stream)
                    {
                        return new Drawing.Icon(stream, WinForms.SystemInformation.SmallIconSize);
                    }
                }
            }
            catch (Exception)
            {
                // リソースが見つからない・壊れている場合はフォールバックする
            }

            return (Drawing.Icon)Drawing.SystemIcons.Application.Clone();
        }
    }
}
