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

    public (string Title, TimerOption SelectedOption)? ShowRecordingStartDialog(
        string defaultTitle,
        List<TimerOption> timerOptions,
        TimerOption defaultOption,
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

    public WorkDayEditResult? ShowWorkDayEditDialog(
        System.DateTime date, System.DateTime? start, System.DateTime? end, bool canDelete)
    {
        WorkDayEditDialog dialog = new(date, start, end, canDelete)
        {
            Owner = owner
        };

        // トレイへ隠れている状態から呼ばれても操作できるようにする
        if (!owner.IsVisible) owner.Show();

        return dialog.ShowDialog() == true ? dialog.Result : null;
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

    public bool ShowAwayReviewDialog(
        string recordTitle,
        System.DateTime recordStart,
        System.DateTime recordEnd,
        IReadOnlyList<AwayPeriod> awayPeriods)
    {
        AwayReviewDialog dialog = new(recordTitle, recordStart, recordEnd, awayPeriods)
        {
            Owner = owner
        };

        // ウィンドウがトレイへ隠れている状態でも確認できるよう、必要なら表示に戻す
        if (!owner.IsVisible) owner.Show();

        return dialog.ShowDialog() == true && dialog.ShouldExclude;
    }
}
