using System.Collections.Generic;
using System.Windows;

namespace TimeRenderer
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

        public RecordingStartDialog(string defaultTitle, List<MainViewModel.TimerOption> timerOptions, MainViewModel.TimerOption defaultOption)
        {
            InitializeComponent();
            
            InputText = defaultTitle;
            TimerOptions = timerOptions;
            SelectedTimerOption = defaultOption;
            
            DataContext = this;

            // ロード時に入力ボックスにフォーカスを当て、テキストを全選択する
            Loaded += (s, e) => 
            {
                InputTextBox.Focus();
                InputTextBox.SelectAll();
            };
        }

        // 開始ボタンクリック時の処理
        private void OkButton_Click(object sender, RoutedEventArgs e)
        {
            DialogResult = true;
        }
    }
}
