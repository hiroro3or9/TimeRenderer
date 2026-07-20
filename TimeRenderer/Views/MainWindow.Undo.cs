using System.Windows;
using System.Windows.Input;

using KeyEventArgs = System.Windows.Input.KeyEventArgs;
// WinForms にも同名の型があるため、WPF 側を明示する
using TextBoxBase = System.Windows.Controls.Primitives.TextBoxBase;

namespace TimeRenderer.Views
{
    /// <summary>
    /// 取り消し・やり直しのキーボードショートカット。
    ///
    /// ウィンドウ全体で Ctrl+Z / Ctrl+Y（Ctrl+Shift+Z）を拾うが、
    /// テキスト入力中はそちらの取り消しを優先する。
    /// メモ欄やタイトル入力で Ctrl+Z を押したときに予定の編集が巻き戻ると、
    /// 何が起きたか分からず被害が大きいため。
    /// </summary>
    public partial class MainWindow
    {
        private void Window_PreviewKeyDownForUndo(object sender, KeyEventArgs e)
        {
            if (!Keyboard.Modifiers.HasFlag(ModifierKeys.Control)) return;

            // テキスト入力中は TextBox 自身の取り消しに任せる
            if (Keyboard.FocusedElement is TextBoxBase) return;

            bool shift = Keyboard.Modifiers.HasFlag(ModifierKeys.Shift);

            if (e.Key == Key.Z && !shift)
            {
                Execute(ViewModel.UndoCommand);
            }
            else if (e.Key == Key.Y || (e.Key == Key.Z && shift))
            {
                Execute(ViewModel.RedoCommand);
            }
            else
            {
                return;
            }

            e.Handled = true;
        }

        private static void Execute(ICommand command)
        {
            if (command.CanExecute(null)) command.Execute(null);
        }
    }
}
