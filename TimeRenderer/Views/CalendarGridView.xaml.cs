using System.Windows;
using UserControl = System.Windows.Controls.UserControl;

namespace TimeRenderer.Views
{
    /// <summary>
    /// 月ビュー / スプリントビュー共通のカレンダーグリッド。
    /// セルのクリックイベント（CellClicked / ItemClicked / ItemRightClicked）は
    /// ルーティングイベントとしてバブルするため、親要素側で
    /// controls:CalendarMonthCellControl.ItemClicked="..." の形式で購読する。
    /// </summary>
    public partial class CalendarGridView : UserControl
    {
        /// <summary>グリッドの行数（月ビュー: 6、スプリントビュー: 週数に応じて可変）</summary>
        public static readonly DependencyProperty RowsProperty =
            DependencyProperty.Register(nameof(Rows), typeof(int), typeof(CalendarGridView), new PropertyMetadata(6));

        public int Rows
        {
            get => (int)GetValue(RowsProperty);
            set => SetValue(RowsProperty, value);
        }

        public CalendarGridView()
        {
            InitializeComponent();
        }
    }
}
