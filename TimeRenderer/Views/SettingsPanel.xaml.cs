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
    }
}
