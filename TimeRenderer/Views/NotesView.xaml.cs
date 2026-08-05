using UserControl = System.Windows.Controls.UserControl;

namespace TimeRenderer.Views
{
    /// <summary>
    /// ふりかえり一覧ビュー（全期間・月ごと）。
    /// 表示は MainViewModel.NoteGroups へのバインディングだけで済むため、コードビハインドは持たない。
    /// </summary>
    public partial class NotesView : UserControl
    {
        public NotesView()
        {
            InitializeComponent();
        }
    }
}
