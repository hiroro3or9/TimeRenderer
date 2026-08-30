using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;

using TimeRenderer.Helpers;
using TimeRenderer.Models;

using TUnit.Assertions;
using TUnit.Assertions.Extensions;
using TUnit.Core;

namespace TimeRenderer.Tests;

/// <summary>Undo/Redo 履歴の分岐、上限、適用中フラグを固定する。</summary>
public class UndoManagerTests
{
    [Test]
    public async Task 空の履歴ではUndoとRedoを実行しない()
    {
        var manager = new UndoManager();
        var context = CreateContext();

        var undone = manager.Undo(context);
        var redone = manager.Redo(context);

        await Assert.That(undone).IsFalse();
        await Assert.That(redone).IsFalse();
        await Assert.That(manager.CanUndo).IsFalse();
        await Assert.That(manager.CanRedo).IsFalse();
        await Assert.That(manager.UndoDescription).IsEmpty();
        await Assert.That(manager.RedoDescription).IsEmpty();
    }

    [Test]
    public async Task UndoとRedoの適用中だけIsApplyingが有効になる()
    {
        var manager = new UndoManager();
        var context = CreateContext();
        bool applyingDuringUndo = false;
        bool applyingDuringRedo = false;
        var edit = new ProbeEdit(
            "確認",
            _ => applyingDuringUndo = manager.IsApplying,
            _ => applyingDuringRedo = manager.IsApplying);

        manager.Push(edit);
        var undone = manager.Undo(context);
        var redone = manager.Redo(context);

        await Assert.That(undone).IsTrue();
        await Assert.That(redone).IsTrue();
        await Assert.That(applyingDuringUndo).IsTrue();
        await Assert.That(applyingDuringRedo).IsTrue();
        await Assert.That(manager.IsApplying).IsFalse();
        await Assert.That(manager.UndoDescription).IsEqualTo("確認");
    }

    [Test]
    public async Task Undo後に新しい編集を積むとRedoの分岐を破棄する()
    {
        var manager = new UndoManager();
        var context = CreateContext();
        manager.Push(new ProbeEdit("最初"));
        manager.Undo(context);

        manager.Push(new ProbeEdit("別の操作"));

        await Assert.That(manager.CanRedo).IsFalse();
        await Assert.That(manager.RedoDescription).IsEmpty();
        await Assert.That(manager.UndoDescription).IsEqualTo("別の操作");
    }

    [Test]
    public async Task 履歴は直近100件だけを保持する()
    {
        var manager = new UndoManager();
        var context = CreateContext();
        var undoneIds = new List<int>();

        for (int id = 0; id <= 100; id++)
        {
            int captured = id;
            manager.Push(new ProbeEdit($"操作{captured}", _ => undoneIds.Add(captured)));
        }

        while (manager.Undo(context)) { }

        await Assert.That(undoneIds.Count).IsEqualTo(100);
        await Assert.That(undoneIds[0]).IsEqualTo(100);
        await Assert.That(undoneIds[^1]).IsEqualTo(1);
        await Assert.That(undoneIds.Contains(0)).IsFalse();
    }

    [Test]
    public async Task 適用が例外になってもIsApplyingを解除する()
    {
        var manager = new UndoManager();
        manager.Push(new ProbeEdit("失敗", _ => throw new InvalidOperationException("test")));

        bool threw = false;
        try
        {
            manager.Undo(CreateContext());
        }
        catch (InvalidOperationException)
        {
            threw = true;
        }

        await Assert.That(threw).IsTrue();
        await Assert.That(manager.IsApplying).IsFalse();
        await Assert.That(manager.CanUndo).IsTrue();
        await Assert.That(manager.UndoDescription).IsEqualTo("失敗");
        await Assert.That(manager.CanRedo).IsFalse();
    }

    [Test]
    public async Task Redoが例外になってもRedo履歴を残す()
    {
        var manager = new UndoManager();
        manager.Push(new ProbeEdit(
            "やり直し失敗",
            redo: _ => throw new InvalidOperationException("test")));
        manager.Undo(CreateContext());

        bool threw = false;
        try
        {
            manager.Redo(CreateContext());
        }
        catch (InvalidOperationException)
        {
            threw = true;
        }

        await Assert.That(threw).IsTrue();
        await Assert.That(manager.IsApplying).IsFalse();
        await Assert.That(manager.CanRedo).IsTrue();
        await Assert.That(manager.RedoDescription).IsEqualTo("やり直し失敗");
        await Assert.That(manager.CanUndo).IsFalse();
    }

    [Test]
    public async Task Clearは履歴があるときだけ変更通知を一度発火する()
    {
        var manager = new UndoManager();
        int changed = 0;
        manager.Changed += (_, _) => changed++;

        manager.Clear();
        manager.Push(new ProbeEdit("操作"));
        manager.Clear();
        manager.Clear();

        await Assert.That(changed).IsEqualTo(2);
        await Assert.That(manager.CanUndo).IsFalse();
        await Assert.That(manager.CanRedo).IsFalse();
    }

    private static UndoContext CreateContext() => new(
        new ObservableCollection<ScheduleItem>(),
        new ObservableCollection<TodoItem>());

    private sealed class ProbeEdit(
        string description,
        Action<UndoContext>? undo = null,
        Action<UndoContext>? redo = null) : IUndoableEdit
    {
        public string Description { get; } = description;

        public void Undo(UndoContext context) => undo?.Invoke(context);

        public void Redo(UndoContext context) => redo?.Invoke(context);
    }
}
