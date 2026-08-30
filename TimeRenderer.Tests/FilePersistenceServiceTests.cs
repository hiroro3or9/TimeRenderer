using System;
using System.Collections.Generic;
using System.IO;

using TimeRenderer.Models;
using TimeRenderer.Services;

using TUnit.Assertions;
using TUnit.Assertions.Extensions;
using TUnit.Core;

namespace TimeRenderer.Tests;

/// <summary>データ種別ごとの保存往復、旧形式、破損時の扱いを一時フォルダで固定する。</summary>
public class FilePersistenceServiceTests
{
    [Test]
    public async Task 予定の主要な永続項目を保存して読み戻す()
    {
        await InTemporaryDirectory(async directory =>
        {
            var item = new ScheduleItem
            {
                Id = "schedule-1",
                Kind = ScheduleItemKind.Recorded,
                Title = "実装",
                Content = "詳細",
                StartTime = new DateTime(2026, 8, 30, 9, 0, 0),
                EndTime = new DateTime(2026, 8, 30, 10, 30, 0),
                CategoryId = "category-1",
                ProjectCodeId = "project-1",
                TodoId = "todo-1",
                RoutineId = "routine-1",
                SourcePlanId = "plan-1",
                ColorCode = "#FF123456",
                IsVirtual = true,
            };

            FilePersistenceService.SaveDataToDirectory(directory, [item]);
            var result = FilePersistenceService.LoadDataFromDirectory(directory);

            await Assert.That(result.Status).IsEqualTo(LoadStatus.Loaded);
            await Assert.That(result.Items.Count).IsEqualTo(1);
            var restored = result.Items[0];
            await Assert.That(restored.Id).IsEqualTo("schedule-1");
            await Assert.That(restored.Kind).IsEqualTo(ScheduleItemKind.Recorded);
            await Assert.That(restored.Title).IsEqualTo("実装");
            await Assert.That(restored.StartTime).IsEqualTo(item.StartTime);
            await Assert.That(restored.EndTime).IsEqualTo(item.EndTime);
            await Assert.That(restored.CategoryId).IsEqualTo("category-1");
            await Assert.That(restored.ProjectCodeId).IsEqualTo("project-1");
            await Assert.That(restored.TodoId).IsEqualTo("todo-1");
            await Assert.That(restored.RoutineId).IsEqualTo("routine-1");
            await Assert.That(restored.SourcePlanId).IsEqualTo("plan-1");
            await Assert.That(restored.IsVirtual).IsFalse();
        });
    }

    [Test]
    public async Task 種類を持たない旧予定はLegacyとして読み込む()
    {
        await InTemporaryDirectory(async directory =>
        {
            File.WriteAllText(Path.Combine(directory, "schedules.json"),
                """
                [
                  {
                    "Id": "legacy-schedule",
                    "Title": "旧予定",
                    "StartTime": "2026-08-30T09:00:00",
                    "EndTime": "2026-08-30T10:00:00",
                    "ColorCode": "#FFADD8E6"
                  }
                ]
                """);

            var result = FilePersistenceService.LoadDataFromDirectory(directory);

            await Assert.That(result.Status).IsEqualTo(LoadStatus.Loaded);
            await Assert.That(result.Items.Count).IsEqualTo(1);
            await Assert.That(result.Items[0].Kind).IsEqualTo(ScheduleItemKind.Legacy);
            await Assert.That(result.Items[0].Title).IsEqualTo("旧予定");
        });
    }

    [Test]
    public async Task 壊れた予定ファイルをサンプルデータへ置き換えない()
    {
        await InTemporaryDirectory(async directory =>
        {
            File.WriteAllText(Path.Combine(directory, "schedules.json"), "{ broken json");

            var result = FilePersistenceService.LoadDataFromDirectory(directory);

            await Assert.That(result.Status).IsEqualTo(LoadStatus.Failed);
            await Assert.That(result.Items).IsEmpty();
            await Assert.That(result.Message).Contains("いずれも読み込めませんでした");
        });
    }

    [Test]
    public async Task ToDoの通知繰り返しサブタスク実績を保存して空タイトルを除く()
    {
        await InTemporaryDirectory(async directory =>
        {
            var todo = new TodoItem
            {
                Id = "todo-1",
                Title = "月次確認",
                Content = "手順",
                DueDate = new DateTime(2026, 8, 31),
                RemindAt = new DateTime(2026, 8, 30, 9, 30, 0),
                RemindOffsetDays = 1,
                Priority = TodoPriority.High,
                CategoryId = "category-1",
                RecordedTicks = TimeSpan.FromMinutes(75).Ticks,
                EstimatedMinutes = 90,
                SortOrder = 4,
                Recurrence = TodoRecurrenceUnit.Week,
                RecurrenceInterval = 2,
                RecurrenceDaysOfWeek = [DayOfWeek.Monday, DayOfWeek.Thursday],
                RecurrenceFromCompletion = true,
                Subtasks =
                [
                    new TodoSubtask { Title = "集計", IsCompleted = true },
                    new TodoSubtask { Title = "共有" },
                ],
                CreatedAt = new DateTime(2026, 8, 1, 12, 0, 0),
            };

            FilePersistenceService.SaveTodosToDirectory(
                directory, [todo, new TodoItem { Title = "   " }]);
            var result = FilePersistenceService.LoadTodosFromDirectory(directory);
            var restored = result.Items;

            await Assert.That(result.Status).IsEqualTo(LoadStatus.Loaded);
            await Assert.That(restored.Count).IsEqualTo(1);
            var item = restored[0];
            await Assert.That(item.Id).IsEqualTo("todo-1");
            await Assert.That(item.DueDate).IsEqualTo(new DateTime(2026, 8, 31));
            await Assert.That(item.RemindAt).IsEqualTo(new DateTime(2026, 8, 30, 9, 30, 0));
            await Assert.That(item.RemindOffsetDays).IsEqualTo(1);
            await Assert.That(item.Priority).IsEqualTo(TodoPriority.High);
            await Assert.That(item.RecordedDuration).IsEqualTo(TimeSpan.FromMinutes(75));
            await Assert.That(item.EstimatedMinutes).IsEqualTo(90);
            await Assert.That(item.Recurrence).IsEqualTo(TodoRecurrenceUnit.Week);
            await Assert.That(item.RecurrenceInterval).IsEqualTo(2);
            await Assert.That(item.RecurrenceDaysOfWeek.Count).IsEqualTo(2);
            await Assert.That(item.Subtasks.Count).IsEqualTo(2);
            await Assert.That(item.Subtasks[0].IsCompleted).IsTrue();
            await Assert.That(item.Subtasks[1].IsCompleted).IsFalse();
        });
    }

    [Test]
    public async Task 旧ToDoは追加項目の既定値と不正値の補正を適用して読める()
    {
        await InTemporaryDirectory(async directory =>
        {
            File.WriteAllText(Path.Combine(directory, "todos.json"),
                """
                [
                  {
                    "Id": "legacy-todo",
                    "Title": "旧ToDo",
                    "DueDate": "2026-08-30T18:45:00",
                    "RecordedTicks": -1,
                    "EstimatedMinutes": 999999,
                    "RemindOffsetDays": 9999
                  }
                ]
                """);

            var result = FilePersistenceService.LoadTodosFromDirectory(directory);
            var restored = result.Items;

            await Assert.That(result.Status).IsEqualTo(LoadStatus.Loaded);
            await Assert.That(restored.Count).IsEqualTo(1);
            var item = restored[0];
            await Assert.That(item.DueDate).IsEqualTo(new DateTime(2026, 8, 30));
            await Assert.That(item.Priority).IsEqualTo(TodoPriority.Normal);
            await Assert.That(item.RecordedTicks).IsEqualTo(0);
            await Assert.That(item.EstimatedMinutes).IsEqualTo(60 * 24 * 7);
            await Assert.That(item.RemindOffsetDays).IsEqualTo(365);
            await Assert.That(item.Recurrence).IsEqualTo(TodoRecurrenceUnit.None);
            await Assert.That(item.RecurrenceInterval).IsEqualTo(1);
            await Assert.That(item.Subtasks).IsEmpty();
        });
    }

    [Test]
    public async Task ToDo本体とバックアップが壊れていれば失敗を空一覧と区別する()
    {
        await InTemporaryDirectory(async directory =>
        {
            File.WriteAllText(Path.Combine(directory, "todos.json"), "{ broken target");
            File.WriteAllText(Path.Combine(directory, "todos.json.bak"), "{ broken backup");

            var result = FilePersistenceService.LoadTodosFromDirectory(directory);

            await Assert.That(result.Status).IsEqualTo(LoadStatus.Failed);
            await Assert.That(result.Items).IsEmpty();
            await Assert.That(result.Message).Contains("いずれも読み込めませんでした");
        });
    }

    [Test]
    public async Task 勤務記録は壊れた行を除いて開始時刻順に戻す()
    {
        await InTemporaryDirectory(async directory =>
        {
            var later = new WorkDayLog
            {
                Date = new DateTime(2026, 8, 30),
                StartTime = new DateTime(2026, 8, 30, 10, 0, 0),
                EndTime = new DateTime(2026, 8, 30, 18, 0, 0),
                EndSource = WorkEndSource.AwayDetected,
                Note = "完了",
            };
            var earlier = new WorkDayLog
            {
                Date = new DateTime(2026, 8, 29),
                StartTime = new DateTime(2026, 8, 29, 9, 0, 0),
            };

            FilePersistenceService.SaveWorkDaysToDirectory(
                directory, [later, new WorkDayLog(), earlier]);
            var restored = FilePersistenceService.LoadWorkDaysFromDirectory(directory);

            await Assert.That(restored.Count).IsEqualTo(2);
            await Assert.That(restored[0].StartTime).IsEqualTo(earlier.StartTime);
            await Assert.That(restored[1].StartTime).IsEqualTo(later.StartTime);
            await Assert.That(restored[1].EndSource).IsEqualTo(WorkEndSource.AwayDetected);
            await Assert.That(restored[1].Note).IsEqualTo("完了");
        });
    }

    [Test]
    public async Task アプリ使用記録は保持境界を含め不正区間と古い区間を除く()
    {
        await InTemporaryDirectory(async directory =>
        {
            var today = new DateTime(2026, 8, 30);
            var cutoff = today.AddDays(-60);
            var keptAtBoundary = Usage(cutoff.AddHours(-1), cutoff, "boundary");
            var recent = Usage(today.AddHours(10), today.AddHours(11), "recent");
            var old = Usage(cutoff.AddDays(-1), cutoff.AddTicks(-1), "old");
            var invalid = Usage(today.AddHours(12), today.AddHours(12), "invalid");
            var missingProcess = Usage(today.AddHours(13), today.AddHours(14), string.Empty);

            FilePersistenceService.SaveAppUsageToDirectory(
                directory, [recent, invalid, old, missingProcess, keptAtBoundary]);
            var restored = FilePersistenceService.LoadAppUsageFromDirectory(directory, today);

            await Assert.That(restored.Count).IsEqualTo(2);
            await Assert.That(restored[0].ProcessName).IsEqualTo("boundary");
            await Assert.That(restored[1].ProcessName).IsEqualTo("recent");
        });
    }

    [Test]
    public async Task ToDoアーカイブも空タイトルを除いて保存往復する()
    {
        await InTemporaryDirectory(async directory =>
        {
            var completed = new TodoItem { Title = "完了済み", IsCompleted = true };

            FilePersistenceService.SaveTodoArchiveToDirectory(
                directory, [new TodoItem(), completed]);
            var restored = FilePersistenceService.LoadTodoArchiveFromDirectory(directory);

            await Assert.That(restored.Count).IsEqualTo(1);
            await Assert.That(restored[0].Title).IsEqualTo("完了済み");
            await Assert.That(restored[0].IsCompleted).IsTrue();
            await Assert.That(restored[0].CompletedAt).IsNotNull();
        });
    }

    private static AppUsageInterval Usage(
        DateTime start,
        DateTime end,
        string processName) => new()
    {
        Start = start,
        End = end,
        ProcessName = processName,
        AppName = processName,
    };

    private static async Task InTemporaryDirectory(Func<string, Task> action)
    {
        var directory = Path.Combine(
            Path.GetTempPath(), "TimeRenderer.Tests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(directory);
        try
        {
            await action(directory);
        }
        finally
        {
            DeleteTemporaryDirectory(directory);
        }
    }

    private static void DeleteTemporaryDirectory(string directory)
    {
        var root = Path.GetFullPath(Path.Combine(Path.GetTempPath(), "TimeRenderer.Tests"))
            .TrimEnd(Path.DirectorySeparatorChar) + Path.DirectorySeparatorChar;
        var target = Path.GetFullPath(directory);

        if (target.StartsWith(root, StringComparison.OrdinalIgnoreCase) && Directory.Exists(target))
        {
            Directory.Delete(target, recursive: true);
        }
    }
}
