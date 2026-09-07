using System;
using System.Linq;

using TimeRenderer.Models;
using TimeRenderer.ViewModels;

using TUnit.Assertions;
using TUnit.Assertions.Extensions;
using TUnit.Core;

namespace TimeRenderer.Tests;

/// <summary>
/// 実データ、OS監視、画面を使わずに、主要コマンドが同じUndo単位までつながることを確認する。
/// </summary>
public class MainViewModelIntegrationTests
{
    private static readonly DateTime Now = new(2026, 8, 30, 9, 0, 0);

    [Test]
    public async Task 軽量起動経路は実データやOS監視なしで主要機能を初期化する()
    {
        var (vm, _) = CreateViewModel();

        await Assert.That(vm.CurrentDate).IsEqualTo(Now.Date);
        await Assert.That(vm.ScheduleItems).IsEmpty();
        await Assert.That(vm.Todos).IsEmpty();
        await Assert.That(vm.IsRecording).IsFalse();
        await Assert.That(vm.AddQuickTodoCommand).IsNotNull();
        await Assert.That(vm.StartRecordingFromTodoCommand).IsNotNull();
        await Assert.That(vm.UndoCommand).IsNotNull();
    }

    [Test]
    public async Task クイック追加をコマンドから実行してUndoとRedoできる()
    {
        var (vm, _) = CreateViewModel();
        vm.NewTodoTitle = "資料整理 !高 ~30m";

        vm.AddQuickTodoCommand.Execute(null);

        await Assert.That(vm.Todos.Count).IsEqualTo(1);
        await Assert.That(vm.Todos[0].Title).IsEqualTo("資料整理");
        await Assert.That(vm.Todos[0].Priority).IsEqualTo(TodoPriority.High);
        await Assert.That(vm.Todos[0].EstimatedMinutes).IsEqualTo(30);
        await Assert.That(vm.NewTodoTitle).IsEmpty();
        await Assert.That(vm.CanUndo).IsTrue();

        vm.UndoCommand.Execute(null);
        await Assert.That(vm.Todos).IsEmpty();
        await Assert.That(vm.CanRedo).IsTrue();

        vm.RedoCommand.Execute(null);
        await Assert.That(vm.Todos.Count).IsEqualTo(1);
        await Assert.That(vm.Todos[0].Title).IsEqualTo("資料整理");
    }

    [Test]
    public async Task ToDoの時間ブロックを予定へ追加してUndoできる()
    {
        var (vm, _) = CreateViewModel();
        var todo = new TodoItem { Title = "設計", EstimatedMinutes = 45 };
        vm.Todos.Add(todo);
        var start = Now.AddHours(1);

        vm.BlockTimeForTodo(todo, start);

        await Assert.That(vm.ScheduleItems.Count).IsEqualTo(1);
        await Assert.That(vm.ScheduleItems[0].Kind).IsEqualTo(ScheduleItemKind.Planned);
        await Assert.That(vm.ScheduleItems[0].TodoId).IsEqualTo(todo.Id);
        await Assert.That(vm.ScheduleItems[0].StartTime).IsEqualTo(start);
        await Assert.That(vm.ScheduleItems[0].EndTime).IsEqualTo(start.AddMinutes(45));

        vm.UndoCommand.Execute(null);
        await Assert.That(vm.ScheduleItems).IsEmpty();
    }

    [Test]
    public async Task 記録漏れ補完は予定追加とToDo積算を一緒にUndoとRedoする()
    {
        var (vm, _) = CreateViewModel();
        var todo = new TodoItem { Title = "調査" };
        vm.Todos.Add(todo);
        var start = Now.AddHours(-2);
        var end = Now.AddMinutes(-30);

        vm.FillGapWithTodo(todo, start, end);

        await Assert.That(vm.ScheduleItems.Count).IsEqualTo(1);
        await Assert.That(vm.ScheduleItems[0].Kind).IsEqualTo(ScheduleItemKind.Recorded);
        await Assert.That(todo.RecordedDuration).IsEqualTo(TimeSpan.FromMinutes(90));

        vm.UndoCommand.Execute(null);
        await Assert.That(vm.ScheduleItems).IsEmpty();
        await Assert.That(todo.RecordedDuration).IsEqualTo(TimeSpan.Zero);

        vm.RedoCommand.Execute(null);
        await Assert.That(vm.ScheduleItems.Count).IsEqualTo(1);
        await Assert.That(todo.RecordedDuration).IsEqualTo(TimeSpan.FromMinutes(90));
    }

    [Test]
    public async Task ToDoから記録開始して停止すると実績と積算を作りUndoできる()
    {
        var (vm, time) = CreateViewModel();
        var todo = new TodoItem { Title = "実装" };
        vm.Todos.Add(todo);

        vm.StartRecordingFromTodoCommand.Execute(todo);

        await Assert.That(vm.IsRecording).IsTrue();
        await Assert.That(vm.RecordingStartTime).IsEqualTo(Now);

        time.Advance(TimeSpan.FromMinutes(90));
        vm.QuickToggleRecording();

        await Assert.That(vm.IsRecording).IsFalse();
        await Assert.That(vm.ScheduleItems.Count).IsEqualTo(1);
        await Assert.That(vm.ScheduleItems[0].Kind).IsEqualTo(ScheduleItemKind.Recorded);
        await Assert.That(vm.ScheduleItems[0].StartTime).IsEqualTo(Now);
        await Assert.That(vm.ScheduleItems[0].EndTime).IsEqualTo(Now.AddMinutes(90));
        await Assert.That(vm.ScheduleItems[0].TodoId).IsEqualTo(todo.Id);
        await Assert.That(todo.RecordedDuration).IsEqualTo(TimeSpan.FromMinutes(90));

        vm.UndoCommand.Execute(null);
        await Assert.That(vm.ScheduleItems).IsEmpty();
        await Assert.That(todo.RecordedDuration).IsEqualTo(TimeSpan.Zero);
    }

    [Test]
    public async Task ToDo通知は一度だけ積まれスヌーズで通知時刻を動かす()
    {
        var (vm, time) = CreateViewModel();
        vm.IsTodoReminderSoundEnabled = false;
        var todo = new TodoItem
        {
            Title = "連絡",
            RemindAt = Now,
        };
        vm.Todos.Add(todo);

        vm.UpdateTodoTick(Now);
        vm.UpdateTodoTick(Now.AddSeconds(10));

        await Assert.That(vm.PendingTodoReminders.Count).IsEqualTo(1);
        await Assert.That(vm.PendingTodoReminders[0]).IsSameReferenceAs(todo);

        time.Advance(TimeSpan.FromMinutes(1));
        vm.SnoozeTodoReminder(todo, TimeSpan.FromMinutes(15));

        await Assert.That(vm.PendingTodoReminders).IsEmpty();
        await Assert.That(todo.RemindAt).IsEqualTo(Now.AddMinutes(16));
        await Assert.That(todo.RemindOffsetDays).IsNull();
    }

    [Test]
    public async Task 朝のまとめ通知は渡した日付を基準に今日と超過を数える()
    {
        var (vm, _) = CreateViewModel();
        vm.IsTodoReminderSoundEnabled = false;
        vm.IsTodoDigestEnabled = true;
        vm.TodoDigestHour = 9;
        vm.Todos.Add(new TodoItem { Title = "今日", DueDate = Now.Date });
        vm.Todos.Add(new TodoItem { Title = "超過", DueDate = Now.Date.AddDays(-1) });
        vm.Todos.Add(new TodoItem { Title = "未来", DueDate = Now.Date.AddDays(1) });

        vm.UpdateTodoTick(Now);

        await Assert.That(vm.TodoDigestNotice).IsNotNull();
        await Assert.That(vm.TodoDigestNotice!).Contains("今日が期限の ToDo が 1 件");
        await Assert.That(vm.TodoDigestNotice!).Contains("期限を過ぎた ToDo が 1 件");

        var firstNotice = vm.TodoDigestNotice;
        vm.UpdateTodoTick(Now.AddHours(1));
        await Assert.That(vm.TodoDigestNotice).IsEqualTo(firstNotice);
    }

    [Test]
    public async Task 表示フィルターはカテゴリとプロジェクトコードをAND条件で予定へ適用する()
    {
        var (vm, _) = CreateViewModel();
        var category = vm.Categories[0];
        var projectCode = vm.ProjectCodes[0];
        var item = new ScheduleItem
        {
            CategoryId = category.Id,
            ColorCode = category.ColorCode,
            ProjectCodeId = projectCode.Id,
        };
        var todo = new TodoItem
        {
            CategoryId = category.Id,
            ColorCode = category.ColorCode,
        };

        await Assert.That(vm.IsItemVisible(item)).IsTrue();
        await Assert.That(vm.IsTodoVisible(todo)).IsTrue();

        projectCode.IsFilterEnabled = false;

        await Assert.That(vm.IsDisplayFilterActive).IsTrue();
        await Assert.That(vm.IsItemVisible(item)).IsFalse();
        await Assert.That(vm.IsTodoVisible(todo)).IsTrue();

        category.IsFilterEnabled = false;
        await Assert.That(vm.IsTodoVisible(todo)).IsFalse();

        vm.ResetDisplayFilterCommand.Execute(null);

        await Assert.That(vm.IsDisplayFilterActive).IsFalse();
        await Assert.That(vm.IsItemVisible(item)).IsTrue();
        await Assert.That(vm.IsTodoVisible(todo)).IsTrue();
    }

    [Test]
    public async Task 未設定または不明なプロジェクトコードのデータはフィルター中も表示する()
    {
        var (vm, _) = CreateViewModel();
        vm.ProjectCodes[0].IsFilterEnabled = false;

        var unassigned = new ScheduleItem { ProjectCodeId = null };
        var unknown = new ScheduleItem { ProjectCodeId = "missing-project" };

        await Assert.That(vm.IsItemVisible(unassigned)).IsTrue();
        await Assert.That(vm.IsItemVisible(unknown)).IsTrue();
    }

    [Test]
    public async Task 既定のプロジェクトコードを未設定にできる()
    {
        var (vm, _) = CreateViewModel();

        await Assert.That(vm.SelectableProjectCodes[0]).IsEqualTo(ProjectCodeInfo.Unassigned);
        await Assert.That(vm.DefaultProjectCode).IsNotNull();

        vm.SelectedDefaultProjectCode = ProjectCodeInfo.Unassigned;

        await Assert.That(vm.DefaultProjectCode).IsNull();
        await Assert.That(vm.SelectedDefaultProjectCode).IsEqualTo(ProjectCodeInfo.Unassigned);
    }

    [Test]
    public async Task 既定が未設定なら記録もプロジェクトコードを持たない()
    {
        var (vm, time) = CreateViewModel();
        vm.SelectedDefaultProjectCode = ProjectCodeInfo.Unassigned;

        vm.QuickToggleRecording();
        time.Advance(TimeSpan.FromMinutes(30));
        vm.QuickToggleRecording();

        var recorded = vm.ScheduleItems.Single(i => i.Kind == ScheduleItemKind.Recorded);
        await Assert.That(recorded.ProjectCodeId).IsNull();
    }

    [Test]
    public async Task 未設定の予定から始めた記録に既定コードを付けない()
    {
        var (vm, time) = CreateViewModel();
        var item = new ScheduleItem
        {
            Kind = ScheduleItemKind.Planned,
            Title = "未設定の予定",
            StartTime = Now,
            EndTime = Now.AddHours(1),
            ProjectCodeId = null,
        };
        vm.ScheduleItems.Add(item);

        vm.StartRecordingFromItemCommand.Execute(item);
        time.Advance(TimeSpan.FromMinutes(30));
        vm.QuickToggleRecording();

        await Assert.That(item.Kind).IsEqualTo(ScheduleItemKind.Recorded);
        await Assert.That(item.ProjectCodeId).IsNull();
    }

    [Test]
    public async Task 出勤と退勤は注入した時刻で同じ勤務記録を更新する()
    {
        var (vm, time) = CreateViewModel();

        var clockedIn = vm.ClockIn();
        time.Advance(TimeSpan.FromHours(8));
        var clockedOut = vm.ClockOut();

        await Assert.That(clockedIn).IsTrue();
        await Assert.That(clockedOut).IsTrue();
        await Assert.That(vm.IsWorking).IsFalse();
        await Assert.That(vm.WorkDayMarkers.Count).IsEqualTo(2);
        await Assert.That(vm.WorkDayMarkers[0].Time).IsEqualTo(Now);
        await Assert.That(vm.WorkDayMarkers[1].Time).IsEqualTo(Now.AddHours(8));
    }

    [Test]
    public async Task 日をまたぐ記録は停止まで勤務を締めず実績の終了時刻で締める()
    {
        var (vm, time) = CreateViewModel();
        var todo = new TodoItem { Title = "深夜作業" };
        vm.Todos.Add(todo);
        vm.ClockIn();
        vm.StartRecordingFromTodoCommand.Execute(todo);

        time.Advance(TimeSpan.FromHours(16)); // 翌日1:00
        vm.UpdateWorkDayTick(Now.AddHours(16));

        await Assert.That(vm.IsWorking).IsTrue();
        await Assert.That(vm.IsRecording).IsTrue();

        vm.QuickToggleRecording();
        vm.UpdateWorkDayTick(Now.AddHours(16));

        await Assert.That(vm.IsRecording).IsFalse();
        await Assert.That(vm.IsWorking).IsFalse();
        await Assert.That(vm.WorkDayMarkers[^1].Time).IsEqualTo(Now.AddHours(16));
    }

    private static (MainViewModel ViewModel, ManualTimeProvider Time) CreateViewModel()
    {
        var time = new ManualTimeProvider(Now);
        var vm = new MainViewModel(new TestDialogService(), time, startRuntime: false);
        return (vm, time);
    }
}
