using UserControl = System.Windows.Controls.UserControl;

namespace TimeRenderer.Views
{
    /// <summary>
    /// 設定パネル（右スライドイン・オーバーレイ）。
    /// 一般・分類・定期予定・スプリントの各タブを持つ。ロジックはバインディングのみ。
    /// </summary>
    public partial class SettingsPanel : UserControl
    {
        public SettingsPanel()
        {
            InitializeComponent();
        }
    }
}
