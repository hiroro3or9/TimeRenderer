using System.Collections.Generic;
using System.Windows;
using TextBox = System.Windows.Controls.TextBox;

using TimeRenderer.ViewModels;

namespace TimeRenderer.Views.Dialogs
{
    /// <summary>
    /// 記録開始時の設定を行うダイアログ
    /// </summary>
    public partial class RecordingStartDialog : Window
    {
        // 入力された作業タイトル
        public string InputText { get; set; } = string.Empty;

        // 選択肢となるタイマーオプションリスト
        public List<MainViewModel.TimerOption> TimerOptions { get; set; } = [];

        // 選択されたタイマーオプション
        public MainViewModel.TimerOption? SelectedTimerOption { get; set; }

        // タイトル入力欄のドロップダウン候補（定型タイトル＋直近1か月のタイトル）
        public IReadOnlyList<string> TitleSuggestions { get; } = [];

        public RecordingStartDialog(
            string defaultTitle,
            List<MainViewModel.TimerOption> timerOptions,
            MainViewModel.TimerOption defaultOption,
            IReadOnlyList<string>? titleSuggestions = null)
        {
            InitializeComponent();

            InputText = defaultTitle;
            TimerOptions = timerOptions;
            SelectedTimerOption = defaultOption;
            TitleSuggestions = titleSuggestions ?? [];

            DataContext = this;

            // ロード時に入力ボックスにフォーカスを当て、テキストを全選択する
            Loaded += (s, e) =>
            {
                InputCombo.Focus();
                // 編集可能コンボボックスの内部テキストボックスを取得して全選択する
                if (InputCombo.Template.FindName("PART_EditableTextBox", InputCombo) is TextBox innerTextBox)
                {
                    innerTextBox.SelectAll();
                }
            };
        }

        // 開始ボタンクリック時の処理
        private void OkButton_Click(object sender, RoutedEventArgs e)
        {
            DialogResult = true;
        }
    }
}
