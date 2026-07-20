using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;

using ContextMenu = System.Windows.Controls.ContextMenu;
using MenuItem = System.Windows.Controls.MenuItem;

using TimeRenderer.Models;

namespace TimeRenderer.Views
{
    /// <summary>
    /// 予定アイテムのコンテキストメニュー（編集・この内容で記録開始・削除）の共通処理。
    /// 日/週ビューとタイムラインビューで同じメニューを使うため、ここに集約する。
    /// </summary>
    internal static class ScheduleItemMenu
    {
        /// <summary>
        /// UI要素の DataContext から対象の ScheduleItem を取り出す。
        /// 週/日ビューは日またぎ分割のため ScheduleSegment が、
        /// タイムラインは TimelineBar が DataContext になる。
        /// </summary>
        public static ScheduleItem? ResolveScheduleItem(object? dataContext) => dataContext switch
        {
            ScheduleItem item => item,
            ScheduleSegment segment => segment.Item,
            ViewModels.TimelineBar bar => bar.Item,
            _ => null
        };

        /// <summary>
        /// メニュー項目のクリックから、メニューを開いた要素の ScheduleItem を解決してコマンドを実行する。
        /// </summary>
        public static void ExecuteOnMenuTarget(object sender, ICommand command)
        {
            if (sender is MenuItem menuItem &&
                menuItem.Parent is ContextMenu contextMenu &&
                contextMenu.PlacementTarget is FrameworkElement element &&
                ResolveScheduleItem(element.DataContext) is ScheduleItem item &&
                command.CanExecute(item))
            {
                command.Execute(item);
            }
        }
    }
}
