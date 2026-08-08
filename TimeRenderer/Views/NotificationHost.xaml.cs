using System;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using TimeRenderer.Models;
using TimeRenderer.ViewModels;
using Button = System.Windows.Controls.Button;
using ContextMenu = System.Windows.Controls.ContextMenu;
using MenuItem = System.Windows.Controls.MenuItem;
using UserControl = System.Windows.Controls.UserControl;

namespace TimeRenderer.Views;

/// <summary>
/// メインコンテンツ上に通知を重ねて表示する。通知固有のメニュー操作だけを扱う。
/// </summary>
public partial class NotificationHost : UserControl
{
    public NotificationHost()
    {
        InitializeComponent();
    }

    private MainViewModel? ViewModel => DataContext as MainViewModel;

    private void TodoSnoozeMenuButton_Click(object sender, RoutedEventArgs e)
    {
        if (sender is not Button { ContextMenu: { } menu } button) return;

        menu.PlacementTarget = button;
        menu.Placement = PlacementMode.Bottom;
        menu.IsOpen = true;
    }

    private void TodoSnoozeMenuItem_Click(object sender, RoutedEventArgs e)
    {
        if (sender is not MenuItem item) return;
        if (item.Parent is not ContextMenu menu) return;
        if (menu.PlacementTarget is not FrameworkElement { DataContext: TodoItem todo }) return;
        if (ViewModel is not { } viewModel) return;

        if (item.Tag as string == "tomorrow")
        {
            viewModel.SnoozeTodoReminderUntilTomorrow(todo);
            return;
        }

        if (int.TryParse(item.Tag as string, out var minutes))
        {
            viewModel.SnoozeTodoReminder(todo, TimeSpan.FromMinutes(minutes));
        }
    }
}
