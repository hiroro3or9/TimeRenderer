using UserControl = System.Windows.Controls.UserControl;

namespace TimeRenderer.Views
{
    /// <summary>
    /// 設定パネル（右スライドイン・オーバーレイ）。
    /// 外観、表示、通知、勤務などの一般設定を扱う。データ管理は ManagementPanel が担当する。
    /// </summary>
    public partial class SettingsPanel : UserControl
    {
        public SettingsPanel()
        {
            InitializeComponent();
        }

        private void SettingsSearchBox_TextChanged(
            object sender,
            System.Windows.Controls.TextChangedEventArgs e)
        {
            // TextBox は後続のセクションより先に生成されるため、初期化途中のイベントは無視する。
            if (AppearanceSettingsSection is null) return;

            var query = SettingsSearchBox.Text.Trim();
            bool searching = query.Length > 0;
            SettingsSearchClearButton.Visibility = searching
                ? System.Windows.Visibility.Visible
                : System.Windows.Visibility.Collapsed;

            bool appearance = Matches(query, "外観", "ダーク", "テーマ", "配色");
            bool display = Matches(query, "表示", "時間", "時刻", "曜日", "刻み", "ドラッグ", "カレンダー");
            bool away = Matches(query, "離席", "中断", "無操作", "スリープ", "ロック", "除外");
            bool appUsage = Matches(query, "アプリ", "使用", "前面", "ウィンドウ", "記録", "プライバシー");
            bool todo = Matches(query, "todo", "通知", "先送り", "クイック", "期限", "まとめ", "完了", "アーカイブ");
            bool work = Matches(query, "勤務", "出勤", "退勤", "ふりかえり", "終了", "自動締め");

            ApplySearchResult(AppearanceSettingsSection, appearance, searching);
            ApplySearchResult(DisplaySettingsSection, display, searching);
            ApplySearchResult(AwaySettingsSection, away, searching);
            ApplySearchResult(AppUsageSettingsSection, appUsage, searching);
            ApplySearchResult(TodoSettingsSection, todo, searching);
            ApplySearchResult(WorkSettingsSection, work, searching);

            NoSettingsResultsText.Visibility = searching
                && !(appearance || display || away || appUsage || todo || work)
                    ? System.Windows.Visibility.Visible
                    : System.Windows.Visibility.Collapsed;
        }

        private static bool Matches(string query, params string[] keywords)
        {
            if (query.Length == 0) return true;

            foreach (var keyword in keywords)
            {
                if (keyword.Contains(query, System.StringComparison.OrdinalIgnoreCase)
                    || query.Contains(keyword, System.StringComparison.OrdinalIgnoreCase))
                {
                    return true;
                }
            }

            return false;
        }

        private static void ApplySearchResult(
            System.Windows.Controls.Expander section,
            bool isMatch,
            bool searching)
        {
            section.Visibility = isMatch
                ? System.Windows.Visibility.Visible
                : System.Windows.Visibility.Collapsed;
            if (searching && isMatch)
            {
                section.IsExpanded = true;
            }
        }

        private void SettingsSearchClearButton_Click(object sender, System.Windows.RoutedEventArgs e)
        {
            SettingsSearchBox.Clear();
            SettingsSearchBox.Focus();
        }
    }
}
