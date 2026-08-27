using System.Collections.Generic;

using TimeRenderer.Infrastructure;
using TimeRenderer.Models;

using TUnit.Assertions;
using TUnit.Assertions.Extensions;
using TUnit.Core;

namespace TimeRenderer.Tests;

/// <summary>共通のプロパティ変更通知と、移行したモデルの依存通知を固定する。</summary>
public class ObservableObjectTests
{
    private sealed class ObservableProbe : ObservableObject
    {
        private string _name = "";
        public string Name
        {
            get => _name;
            set => SetProperty(ref _name, value);
        }
    }

    [Test]
    public async Task 値が変わった時だけ呼び出し元のプロパティ名を通知する()
    {
        var probe = new ObservableProbe();
        var notifications = new List<string?>();
        probe.PropertyChanged += (_, e) => notifications.Add(e.PropertyName);

        probe.Name = "開発";
        probe.Name = "開発";
        probe.Name = "レビュー";

        await Assert.That(notifications.Count).IsEqualTo(2);
        await Assert.That(notifications[0]).IsEqualTo(nameof(ObservableProbe.Name));
        await Assert.That(notifications[1]).IsEqualTo(nameof(ObservableProbe.Name));
    }

    [Test]
    public async Task ScheduleItemのタイトル変更は表示用ツールチップも通知する()
    {
        var item = new ScheduleItem();
        var notifications = new List<string?>();
        item.PropertyChanged += (_, e) => notifications.Add(e.PropertyName);

        item.Title = "設計レビュー";

        await Assert.That(notifications.Count).IsEqualTo(2);
        await Assert.That(notifications[0]).IsEqualTo(nameof(ScheduleItem.Title));
        await Assert.That(notifications[1]).IsEqualTo(nameof(ScheduleItem.ToolTipText));
    }

    [Test]
    public async Task ProjectCodeの変更は整形して表示名も通知する()
    {
        var project = new ProjectCodeInfo();
        var notifications = new List<string?>();
        project.PropertyChanged += (_, e) => notifications.Add(e.PropertyName);

        project.Code = "  PRJ-1  ";

        await Assert.That(project.Code).IsEqualTo("PRJ-1");
        await Assert.That(notifications.Count).IsEqualTo(2);
        await Assert.That(notifications[0]).IsEqualTo(nameof(ProjectCodeInfo.Code));
        await Assert.That(notifications[1]).IsEqualTo(nameof(ProjectCodeInfo.DisplayName));
    }

    [Test]
    public async Task 移行対象モデルは共通通知基底クラスを使う()
    {
        object[] models =
        [
            new ScheduleItem(),
            new TodoItem(),
            new CategoryInfo(),
            new GitRepositoryInfo(),
            new ProjectCodeInfo(),
            new TodoSubtask(),
        ];

        foreach (var model in models)
        {
            await Assert.That(model is ObservableObject).IsTrue();
        }
    }
}
