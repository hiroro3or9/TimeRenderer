using System.Windows;
using TimeRenderer.Controls;
using TimeRenderer.Models;
using TimeRenderer.ViewModels;
using TimeRenderer.Views.Dialogs;
using MessageBox = System.Windows.MessageBox;

namespace TimeRenderer.Services;

public class DefaultDialogService(Window owner) : IDialogService
{
    public ScheduleItem? ShowScheduleEditDialog(ScheduleItem? initialItem = null, IReadOnlyList<CategoryInfo>? categories = null, IReadOnlyList<string>? titleSuggestions = null)
    {
        ScheduleEditDialog dialog = new(initialItem, categories, titleSuggestions)
        {
            Owner = owner
        };

        if (dialog.ShowDialog() == true)
        {
            return dialog.ResultItem;
        }
        return null;
    }

    public RoutineScheduleItem? ShowRoutineEditDialog(RoutineScheduleItem? initialRoutine = null, IReadOnlyList<CategoryInfo>? categories = null, IReadOnlyList<string>? titleSuggestions = null)
    {
        RoutineEditDialog dialog = new(initialRoutine, categories, titleSuggestions)
        {
            Owner = owner
        };

        if (dialog.ShowDialog() == true)
        {
            return dialog.ResultRoutine;
        }
        return null;
    }

    public (string Title, MainViewModel.TimerOption SelectedOption)? ShowRecordingStartDialog(
        string defaultTitle,
        List<MainViewModel.TimerOption> timerOptions,
        MainViewModel.TimerOption defaultOption,
        IReadOnlyList<string>? titleSuggestions = null)
    {
        RecordingStartDialog dialog = new(defaultTitle, timerOptions, defaultOption, titleSuggestions)
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

    public void ShowMessage(string message, string title)
    {
        MessageBox.Show(owner, message, title, MessageBoxButton.OK, MessageBoxImage.Warning);
    }
}
