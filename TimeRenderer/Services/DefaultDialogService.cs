using System.Windows;
using TimeRenderer.Controls;
using TimeRenderer.Models;
using TimeRenderer.ViewModels;
using TimeRenderer.Views.Dialogs;
using MessageBox = System.Windows.MessageBox;

namespace TimeRenderer.Services;

public class DefaultDialogService(Window owner) : IDialogService
{
    public ScheduleItem? ShowScheduleEditDialog(
        ScheduleItem? initialItem = null,
        IReadOnlyList<CategoryInfo>? categories = null,
        IReadOnlyList<string>? titleSuggestions = null,
        IReadOnlyList<ProjectCodeInfo>? projectCodes = null,
        ProjectCodeInfo? defaultProjectCode = null)
    {
        ScheduleEditDialog dialog = new(initialItem, categories, titleSuggestions, projectCodes, defaultProjectCode)
        {
            Owner = owner
        };

        if (dialog.ShowDialog() == true)
        {
            return dialog.ResultItem;
        }
        return null;
    }

    public RoutineScheduleItem? ShowRoutineEditDialog(
        RoutineScheduleItem? initialRoutine = null,
        IReadOnlyList<CategoryInfo>? categories = null,
        IReadOnlyList<string>? titleSuggestions = null,
        IReadOnlyList<ProjectCodeInfo>? projectCodes = null,
        ProjectCodeInfo? defaultProjectCode = null)
    {
        RoutineEditDialog dialog = new(
            initialRoutine, categories, titleSuggestions, projectCodes, defaultProjectCode)
        {
            Owner = owner
        };

        if (dialog.ShowDialog() == true)
        {
            return dialog.ResultRoutine;
        }
        return null;
    }

    public TodoItem? ShowTodoEditDialog(
        TodoItem? initialTodo = null,
        IReadOnlyList<CategoryInfo>? categories = null,
        IReadOnlyList<string>? titleSuggestions = null,
        TodoEstimateStats? estimateStats = null)
    {
        TodoEditDialog dialog = new(initialTodo, categories, titleSuggestions, estimateStats)
        {
            Owner = owner
        };

        if (dialog.ShowDialog() == true)
        {
            return dialog.ResultTodo;
        }
        return null;
    }

    public TodoItem? ShowTodoPickerDialog(string message, IReadOnlyList<TodoItem> todos)
    {
        TodoPickerDialog dialog = new(message, todos)
        {
            Owner = owner
        };

        return dialog.ShowDialog() == true ? dialog.SelectedTodo : null;
    }

    public (string Title, TimerOption SelectedOption, string? ProjectCodeId)? ShowRecordingStartDialog(
        string defaultTitle,
        List<TimerOption> timerOptions,
        TimerOption defaultOption,
        IReadOnlyList<string>? titleSuggestions = null,
        IReadOnlyList<ProjectCodeInfo>? projectCodes = null,
        ProjectCodeInfo? defaultProjectCode = null)
    {
        RecordingStartDialog dialog = new(
            defaultTitle, timerOptions, defaultOption, titleSuggestions, projectCodes, defaultProjectCode)
        {
            Owner = owner
        };

        if (dialog.ShowDialog() == true)
        {
            return (
                dialog.InputText,
                dialog.SelectedTimerOption ?? defaultOption,
                dialog.SelectedProjectCode?.Id);
        }
        return null;
    }

    public WorkDayEditResult? ShowWorkDayEditDialog(
        System.DateTime date, System.DateTime? start, System.DateTime? end, bool canDelete, string note)
    {
        WorkDayEditDialog dialog = new(date, start, end, canDelete, note)
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

    public RoutineScope? ShowRoutineScopeDialog(string message, string title)
    {
        RoutineScopeDialog dialog = new(message, title)
        {
            Owner = owner
        };

        return dialog.ShowDialog() == true ? dialog.Result : null;
    }

    public void ShowMessage(string message, string title)
    {
        MessageBox.Show(owner, message, title, MessageBoxButton.OK, MessageBoxImage.Warning);
    }

    public WorkEndReviewResult ShowWorkEndReviewDialog(
        System.DateTime date,
        System.DateTime start,
        System.DateTime end,
        System.TimeSpan recorded,
        int completedCount,
        IReadOnlyList<WorkEndCarryOver> candidates,
        IReadOnlyList<GitCommit> commits,
        string initialNote)
    {
        WorkEndReviewDialog dialog = new(
            date, start, end, recorded, completedCount, candidates, commits, initialNote)
        {
            Owner = owner
        };

        // トレイのメニューから退勤した場合はウィンドウが隠れているため、表示に戻す
        if (!owner.IsVisible) owner.Show();

        // 一言は閉じ方に関わらず受け取る（「そのまま閉じる」で入力を捨てない）
        var confirmed = dialog.ShowDialog() == true;
        return new WorkEndReviewResult(confirmed ? dialog.CarriedOver : [], dialog.Note);
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

    public void ShowAppUsageDialog(
        string itemTitle,
        System.DateTime rangeStart,
        System.DateTime rangeEnd,
        IReadOnlyList<AppUsageStat> stats)
    {
        AppUsageDialog dialog = new(itemTitle, rangeStart, rangeEnd, stats)
        {
            Owner = owner
        };

        if (!owner.IsVisible) owner.Show();

        dialog.ShowDialog();
    }

    public GapFillResult? ShowGapFillDialog(
        System.DateTime start,
        System.DateTime end,
        GapFillSuggestion suggestion,
        IReadOnlyList<CategoryInfo> categories,
        IReadOnlyList<ProjectCodeInfo> projectCodes)
    {
        GapFillDialog dialog = new(start, end, suggestion, categories, projectCodes)
        {
            Owner = owner
        };

        if (!owner.IsVisible) owner.Show();

        return dialog.ShowDialog() == true ? dialog.Result : null;
    }

    public string? ShowFolderPicker(string description)
    {
        // WPF に相当するものが無いため WinForms のものを使う。
        // csproj で UseWindowsForms を有効にしてあるので追加の参照は要らない
        using var dialog = new System.Windows.Forms.FolderBrowserDialog
        {
            Description = description,
            UseDescriptionForTitle = true,
            ShowNewFolderButton = false,
        };

        return dialog.ShowDialog() == System.Windows.Forms.DialogResult.OK
            ? dialog.SelectedPath
            : null;
    }
}
