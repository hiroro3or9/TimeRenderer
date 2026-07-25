using System.Windows;

using TimeRenderer.Services;

namespace TimeRenderer.Views.Dialogs;

/// <summary>
/// 定期予定への操作範囲（この日のみ／定期予定全体）を選ぶダイアログ。
/// キャンセル時は Result が null のまま閉じる。
/// </summary>
public partial class RoutineScopeDialog : Window
{
    /// <summary>選ばれた範囲。キャンセル時は null</summary>
    public RoutineScope? Result { get; private set; }

    public RoutineScopeDialog(string message, string title)
    {
        InitializeComponent();
        Title = title;
        MessageText.Text = message;
    }

    private void ThisDayButton_Click(object sender, RoutedEventArgs e)
    {
        Result = RoutineScope.ThisDay;
        DialogResult = true;
    }

    private void WholeSeriesButton_Click(object sender, RoutedEventArgs e)
    {
        Result = RoutineScope.WholeSeries;
        DialogResult = true;
    }
}
