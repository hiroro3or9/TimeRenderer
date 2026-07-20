using UserControl = System.Windows.Controls.UserControl;

namespace TimeRenderer.Views
{
    /// <summary>
    /// 統計ビュー。カテゴリ別作業時間と日別の推移を表示する。
    /// ロジックはすべて MainViewModel.Stats.cs 側にあり、こちらはバインディングのみ。
    /// </summary>
    public partial class StatsView : UserControl
    {
        public StatsView()
        {
            InitializeComponent();
        }
    }
}
