using System.Windows;
using TimeRenderer.Controls;
using MessageBox = System.Windows.MessageBox;

namespace TimeRenderer.Services;

public class DefaultDialogService(Window owner) : IDialogService
{
    public ScheduleItem? ShowScheduleEditDialog(ScheduleItem? initialItem = null)
    {
        ScheduleEditDialog dialog = new(initialItem)
        {
            Owner = owner
        };

        if (dialog.ShowDialog() == true)
        {
            return dialog.ResultItem;
        }
        return null;
    }

    public (string Title, MainViewModel.TimerOption SelectedOption)? ShowRecordingStartDialog(
        string defaultTitle,
        List<MainViewModel.TimerOption> timerOptions,
        MainViewModel.TimerOption defaultOption)
    {
        RecordingStartDialog dialog = new(defaultTitle, timerOptions, defaultOption)
        {
            Owner = owner
        };

        if (dialog.ShowDialog() == true)
        {
            return (dialog.InputText, dialog.SelectedTimerOption ?? defaultOption);
        }
        return null;
    }

    public bool ShowConfirmationDialog(string message, string title)
    {
        MessageBoxResult result = MessageBox.Show(
            owner,
            message,
            title,
            MessageBoxButton.YesNo,
            MessageBoxImage.Question);

        return result == MessageBoxResult.Yes;
    }
}
