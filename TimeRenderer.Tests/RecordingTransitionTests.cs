using System.Reflection;

using TimeRenderer.Models;
using TimeRenderer.ViewModels;

using TUnit.Assertions;
using TUnit.Assertions.Extensions;
using TUnit.Core;

namespace TimeRenderer.Tests;

public class RecordingTransitionTests
{
    private static readonly DateTime Start = new(2026, 8, 30, 9, 0, 0);

    [Test]
    [Arguments(false)]
    [Arguments(true)]
    public async Task ミニバーの停止は離席確認前に計測状態を解除する(bool excludeAway)
    {
        var time = new ManualTimeProvider(Start);
        var dialogs = new TestDialogService { AwayReviewResult = excludeAway };
        var vm = new MainViewModel(dialogs, time, startRuntime: false);
        vm.QuickToggleRecording();
        time.Advance(TimeSpan.FromMinutes(60));
        AddAway(vm, Start.AddMinutes(10), Start.AddMinutes(30));

        bool? recordingDuringReview = null;
        DateTime? startDuringReview = Start;
        string? titleDuringReview = null;
        TimeSpan? durationDuringReview = null;
        dialogs.OnAwayReview = () =>
        {
            recordingDuringReview = vm.IsRecording;
            startDuringReview = vm.RecordingStartTime;
            titleDuringReview = vm.RecordingTitle;
            durationDuringReview = vm.RecordingDuration;
            time.Advance(TimeSpan.FromMinutes(5));
        };

        // MiniRecordingBar.xaml の停止ボタンと同じコマンド。
        vm.ToggleRecordingCommand.Execute(null);

        await Assert.That(recordingDuringReview).IsFalse();
        await Assert.That(startDuringReview).IsNull();
        await Assert.That(titleDuringReview).IsEqualTo(string.Empty);
        await Assert.That(durationDuringReview).IsEqualTo(TimeSpan.Zero);
        await Assert.That(vm.IsRecording).IsFalse();
        await Assert.That(vm.ScheduleItems.Sum(i => (i.EndTime - i.StartTime).TotalMinutes))
            .IsEqualTo(excludeAway ? 40d : 60d);
        await Assert.That(vm.ScheduleItems.Max(i => i.EndTime)).IsEqualTo(Start.AddHours(1));
        vm.UndoCommand.Execute(null);
        await Assert.That(vm.ScheduleItems).IsEmpty();
    }

    [Test]
    public async Task 強制自動開始は離席確認を待たず次を開始し前の記録の属性と積算を保持する()
    {
        var time = new ManualTimeProvider(Start);
        var dialogs = new TestDialogService { AwayReviewResult = true };
        var vm = new MainViewModel(dialogs, time, startRuntime: false);
        var todo = new TodoItem { Title = "前の作業" };
        vm.Todos.Add(todo);
        var previous = new ScheduleItem
        {
            Kind = ScheduleItemKind.Planned, Title = todo.Title,
            StartTime = Start, EndTime = Start.AddHours(1),
            ColorCode = "#FF123456", CategoryId = "previous-category",
            ProjectCodeId = "previous-project", TodoId = todo.Id,
        };
        var next = new ScheduleItem
        {
            Kind = ScheduleItemKind.Planned, Title = "次の予定",
            StartTime = Start.AddHours(1), EndTime = Start.AddHours(2),
            ColorCode = "#FF654321", CategoryId = "next-category",
            ProjectCodeId = "next-project", AutoStartRecording = true,
            ForceStartRecording = true,
        };
        vm.ScheduleItems.Add(previous);
        vm.ScheduleItems.Add(next);
        vm.StartRecordingFromItemCommand.Execute(previous);
        time.Advance(TimeSpan.FromHours(1));
        AddAway(vm, Start.AddMinutes(10), Start.AddMinutes(30));

        string? titleDuringReview = null;
        DateTime? startDuringReview = null;
        bool? recordingDuringReview = null;
        dialogs.OnAwayReview = () =>
        {
            titleDuringReview = vm.RecordingTitle;
            startDuringReview = vm.RecordingStartTime;
            recordingDuringReview = vm.IsRecording;
            time.Advance(TimeSpan.FromMinutes(5));
            AddAway(vm, Start.AddMinutes(61), Start.AddMinutes(63));
        };

        Invoke(vm, "CheckReminders", Start.AddHours(1));

        await Assert.That(recordingDuringReview).IsTrue();
        await Assert.That(titleDuringReview).IsEqualTo(next.Title);
        await Assert.That(startDuringReview).IsEqualTo(Start.AddHours(1));
        await Assert.That(vm.IsRecording).IsTrue();
        await Assert.That(vm.RecordingTitle).IsEqualTo(next.Title);
        await Assert.That(vm.RecordingStartTime).IsEqualTo(Start.AddHours(1));
        await Assert.That(todo.RecordedDuration).IsEqualTo(TimeSpan.FromMinutes(40));
        var extraSegment = vm.ScheduleItems.Single(i => i.SourcePlanId == previous.Id);
        await Assert.That(extraSegment.ColorCode).IsEqualTo(previous.ColorCode);
        await Assert.That(extraSegment.CategoryId).IsEqualTo(previous.CategoryId);
        await Assert.That(extraSegment.ProjectCodeId).IsEqualTo(previous.ProjectCodeId);
        await Assert.That(extraSegment.TodoId).IsEqualTo(todo.Id);

        int nextReviews = 0;
        dialogs.OnAwayReview = () => nextReviews++;
        time.Advance(TimeSpan.FromMinutes(5));
        vm.ToggleRecordingCommand.Execute(null);

        await Assert.That(nextReviews).IsEqualTo(1);
        await Assert.That(vm.IsRecording).IsFalse();
        var nextSegments = vm.ScheduleItems.Where(i => i.Id == next.Id || i.SourcePlanId == next.Id).ToList();
        await Assert.That(nextSegments.Sum(i => (i.EndTime - i.StartTime).TotalMinutes)).IsEqualTo(8d);
        await Assert.That(nextSegments.All(i => i.ProjectCodeId == "next-project")).IsTrue();
        await Assert.That(todo.RecordedDuration).IsEqualTo(TimeSpan.FromMinutes(40));
    }

    [Test]
    public async Task 離席確認中に開始した記録を確認終了後にリセットしない()
    {
        var time = new ManualTimeProvider(Start);
        var dialogs = new TestDialogService();
        var vm = new MainViewModel(dialogs, time, startRuntime: false);
        vm.QuickToggleRecording();
        time.Advance(TimeSpan.FromHours(1));
        AddAway(vm, Start.AddMinutes(10), Start.AddMinutes(30));
        var next = new TodoItem { Title = "確認中に始めた作業" };
        vm.Todos.Add(next);
        dialogs.OnAwayReview = () => vm.StartRecordingFromTodoCommand.Execute(next);

        vm.ToggleRecordingCommand.Execute(null);

        await Assert.That(vm.IsRecording).IsTrue();
        await Assert.That(vm.RecordingTitle).IsEqualTo(next.Title);
        await Assert.That(vm.RecordingStartTime).IsEqualTo(Start.AddHours(1));
        time.Advance(TimeSpan.FromMinutes(10));
        vm.ToggleRecordingCommand.Execute(null);
        await Assert.That(next.RecordedDuration).IsEqualTo(TimeSpan.FromMinutes(10));
    }

    private static void AddAway(MainViewModel vm, DateTime start, DateTime end) =>
        Invoke(vm, "OnAwayDetected", null, new AwayPeriod(start, end, AwayReason.Idle));

    private static void Invoke(MainViewModel vm, string name, params object?[] arguments) =>
        typeof(MainViewModel).GetMethod(name, BindingFlags.Instance | BindingFlags.NonPublic)!
            .Invoke(vm, arguments);
}
