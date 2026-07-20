using UserControl = System.Windows.Controls.UserControl;

namespace TimeRenderer.Views
{
    /// <summary>
    /// 週次メモパネル（Markdown 編集/プレビュー）。
    /// 開閉は IsMemoPanelVisible による幅アニメーションで行う（XAML 内のトリガー参照）。
    /// </summary>
    public partial class MemoPanel : UserControl
    {
        public MemoPanel()
        {
            InitializeComponent();
        }
    }
}
