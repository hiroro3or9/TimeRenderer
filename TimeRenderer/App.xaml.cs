using System.Windows;

namespace TimeRenderer
{
    /// <summary>
    /// Interaction logic for App.xaml
    /// </summary>
    public partial class App : System.Windows.Application
    {
        /// <summary>
        /// ライト／ダークテーマを切り替える。
        /// Colors.xaml は Styles.xaml の内部に組み込まれているため、
        /// DarkColors.xaml だけを動的に追加／削除して優先度を制御する。
        /// WPF の MergedDictionaries は後に追加されたものが先に検索されるため、
        /// Add（末尾追加）することで最高優先度を確保する。
        /// </summary>
        public static void ApplyTheme(bool isDark)
        {
            var merged = System.Windows.Application.Current.Resources.MergedDictionaries;

            // 動的に追加した DarkColors.xaml をすべて削除する
            for (int i = merged.Count - 1; i >= 0; i--)
            {
                var src = merged[i].Source?.ToString() ?? "";
                if (src.Contains("DarkColors.xaml", System.StringComparison.OrdinalIgnoreCase))
                {
                    merged.RemoveAt(i);
                }
            }

            if (isDark)
            {
                // DarkColors.xaml を末尾に追加（最高優先度）することで
                // Styles.xaml 内部の Colors.xaml より優先して参照される
                var dict = new ResourceDictionary
                {
                    Source = new System.Uri("Themes/DarkColors.xaml", System.UriKind.Relative)
                };
                merged.Add(dict);
            }
            // ライトモード: DarkColors.xaml を除去するだけでよい
            // Colors.xaml は Styles.xaml 内部に常に存在しているため追加不要
        }
    }
}
