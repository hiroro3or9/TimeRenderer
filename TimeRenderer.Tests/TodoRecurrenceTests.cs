using System;

using TimeRenderer.Models;

using TUnit.Assertions;
using TUnit.Assertions.Extensions;
using TUnit.Core;

namespace TimeRenderer.Tests;

/// <summary>完了した繰り返しToDoから次回分を作る日付規則を固定する。</summary>
public class TodoRecurrenceTests
{
    [Test]
    public async Task 繰り返し無しなら次回分を作らない()
    {
        var todo = new TodoItem { Title = "単発" };

        var next = todo.CreateNextOccurrence(new DateTime(2026, 8, 30));

        await Assert.That(next).IsNull();
    }

    [Test]
    public async Task 期限基準の日次ToDoは遅れて完了しても未来まで進める()
    {
        var todo = new TodoItem
        {
            Title = "日報",
            DueDate = new DateTime(2026, 8, 1),
            Recurrence = TodoRecurrenceUnit.Day,
            RecurrenceInterval = 1,
        };

        var next = todo.CreateNextOccurrence(new DateTime(2026, 8, 30));

        await Assert.That(next).IsNotNull();
        await Assert.That(next!.DueDate).IsEqualTo(new DateTime(2026, 8, 31));
    }

    [Test]
    public async Task 五百回を超えて遅れた日次ToDoも未来まで直接進める()
    {
        var todo = new TodoItem
        {
            Title = "長期休止していた日報",
            DueDate = new DateTime(2024, 1, 1),
            Recurrence = TodoRecurrenceUnit.Day,
            RecurrenceInterval = 1,
        };

        var next = todo.CreateNextOccurrence(new DateTime(2026, 8, 30));

        await Assert.That(next).IsNotNull();
        await Assert.That(next!.DueDate).IsEqualTo(new DateTime(2026, 8, 31));
    }

    [Test]
    public async Task 完了日起点では指定間隔を完了日へ足す()
    {
        var todo = new TodoItem
        {
            Title = "棚卸し",
            DueDate = new DateTime(2026, 1, 1),
            Recurrence = TodoRecurrenceUnit.Day,
            RecurrenceInterval = 2,
            RecurrenceFromCompletion = true,
        };

        var next = todo.CreateNextOccurrence(new DateTime(2026, 8, 30, 20, 0, 0));

        await Assert.That(next).IsNotNull();
        await Assert.That(next!.DueDate).IsEqualTo(new DateTime(2026, 9, 1));
    }

    [Test]
    public async Task 月末の月次ToDoは存在する月末日へ丸める()
    {
        var todo = new TodoItem
        {
            Title = "月末処理",
            DueDate = new DateTime(2026, 1, 31),
            Recurrence = TodoRecurrenceUnit.Month,
            RecurrenceInterval = 1,
        };

        var next = todo.CreateNextOccurrence(new DateTime(2026, 1, 31));

        await Assert.That(next).IsNotNull();
        await Assert.That(next!.DueDate).IsEqualTo(new DateTime(2026, 2, 28));
    }

    [Test]
    public async Task 遅れて完了した月末ToDoも元の日付をアンカーにする()
    {
        var todo = new TodoItem
        {
            Title = "月末処理",
            DueDate = new DateTime(2026, 1, 31),
            Recurrence = TodoRecurrenceUnit.Month,
            RecurrenceInterval = 1,
        };

        var next = todo.CreateNextOccurrence(new DateTime(2026, 4, 1));

        await Assert.That(next).IsNotNull();
        await Assert.That(next!.DueDate).IsEqualTo(new DateTime(2026, 4, 30));
    }

    [Test]
    public async Task 曜日指定は完了日の次に来る対象曜日を選ぶ()
    {
        var todo = new TodoItem
        {
            Title = "週次確認",
            DueDate = new DateTime(2026, 8, 24), // 月曜
            Recurrence = TodoRecurrenceUnit.Week,
            RecurrenceInterval = 1,
            RecurrenceDaysOfWeek = [DayOfWeek.Monday, DayOfWeek.Wednesday],
        };

        var next = todo.CreateNextOccurrence(new DateTime(2026, 8, 25)); // 火曜

        await Assert.That(next).IsNotNull();
        await Assert.That(next!.DueDate).IsEqualTo(new DateTime(2026, 8, 26));
    }

    [Test]
    public async Task 二週間隔の曜日指定は対象外の週を飛ばす()
    {
        var todo = new TodoItem
        {
            Title = "隔週確認",
            DueDate = new DateTime(2026, 8, 24), // 月曜・起点週
            Recurrence = TodoRecurrenceUnit.Week,
            RecurrenceInterval = 2,
            RecurrenceDaysOfWeek = [DayOfWeek.Monday, DayOfWeek.Wednesday],
        };

        var next = todo.CreateNextOccurrence(new DateTime(2026, 8, 26)); // 起点週の水曜に完了

        await Assert.That(next).IsNotNull();
        await Assert.That(next!.DueDate).IsEqualTo(new DateTime(2026, 9, 7));
    }

    [Test]
    public async Task 通知は期限との相対差を次回期限へ引き継ぐ()
    {
        var todo = new TodoItem
        {
            Title = "準備",
            DueDate = new DateTime(2026, 8, 30),
            RemindAt = new DateTime(2026, 8, 28, 9, 30, 0),
            RemindOffsetDays = 2,
            Recurrence = TodoRecurrenceUnit.Day,
            RecurrenceInterval = 1,
        };

        var next = todo.CreateNextOccurrence(new DateTime(2026, 8, 30));

        await Assert.That(next).IsNotNull();
        await Assert.That(next!.DueDate).IsEqualTo(new DateTime(2026, 8, 31));
        await Assert.That(next.RemindAt).IsEqualTo(new DateTime(2026, 8, 29, 9, 30, 0));
        await Assert.That(next.RemindOffsetDays).IsEqualTo(2);
    }

    [Test]
    public async Task 次回分は内容を引き継ぎサブタスクの完了だけを外す()
    {
        var todo = new TodoItem
        {
            Title = "定例",
            Content = "手順あり",
            DueDate = new DateTime(2026, 8, 30),
            Priority = TodoPriority.High,
            EstimatedMinutes = 45,
            CategoryId = "category-1",
            SortOrder = 7,
            Recurrence = TodoRecurrenceUnit.Day,
            Subtasks =
            [
                new TodoSubtask { Title = "準備", IsCompleted = true },
                new TodoSubtask { Title = "確認", IsCompleted = false },
            ],
        };

        var next = todo.CreateNextOccurrence(new DateTime(2026, 8, 30));

        await Assert.That(next).IsNotNull();
        await Assert.That(next!.Title).IsEqualTo("定例");
        await Assert.That(next.Content).IsEqualTo("手順あり");
        await Assert.That(next.Priority).IsEqualTo(TodoPriority.High);
        await Assert.That(next.EstimatedMinutes).IsEqualTo(45);
        await Assert.That(next.CategoryId).IsEqualTo("category-1");
        await Assert.That(next.SortOrder).IsEqualTo(7);
        await Assert.That(next.Subtasks.Count).IsEqualTo(2);
        await Assert.That(next.Subtasks[0].Title).IsEqualTo("準備");
        await Assert.That(next.Subtasks[0].IsCompleted).IsFalse();
        await Assert.That(next.Subtasks[1].IsCompleted).IsFalse();
    }
}
