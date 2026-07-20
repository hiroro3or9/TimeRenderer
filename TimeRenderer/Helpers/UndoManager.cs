using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;

using TimeRenderer.Models;

namespace TimeRenderer.Helpers;

/// <summary>
/// 編集履歴のスタック。
///
/// 適用中（Undo/Redo の実行中）はコレクション変更やプロパティ変更が発火するが、
/// それを新しい編集として積み直すと履歴が壊れる。
/// <see cref="IsApplying"/> を見て、呼び出し側が記録を抑止すること。
/// </summary>
public sealed class UndoManager
{
    /// <summary>保持する履歴の上限。古いものから捨てる</summary>
    private const int MaxDepth = 100;

    private readonly LinkedList<IUndoableEdit> _undo = new();
    private readonly Stack<IUndoableEdit> _redo = new();

    /// <summary>Undo/Redo の適用中か（この間の変更は履歴に積まない）</summary>
    public bool IsApplying { get; private set; }

    public bool CanUndo => _undo.Count > 0;
    public bool CanRedo => _redo.Count > 0;

    public string UndoDescription => CanUndo ? _undo.Last!.Value.Description : string.Empty;
    public string RedoDescription => CanRedo ? _redo.Peek().Description : string.Empty;

    /// <summary>履歴の状態が変わったときに発火する（ボタンの活性やツールチップの更新用）</summary>
    public event EventHandler? Changed;

    public void Push(IUndoableEdit edit)
    {
        if (IsApplying) return;

        _undo.AddLast(edit);
        while (_undo.Count > MaxDepth)
        {
            _undo.RemoveFirst();
        }

        // 新しい操作をしたらやり直しの分岐は捨てる（一般的な undo スタックの挙動）
        _redo.Clear();

        Changed?.Invoke(this, EventArgs.Empty);
    }

    /// <summary>直前の編集を取り消す。実行したら true</summary>
    public bool Undo(ObservableCollection<ScheduleItem> items)
    {
        if (!CanUndo) return false;

        var edit = _undo.Last!.Value;
        _undo.RemoveLast();

        IsApplying = true;
        try
        {
            edit.Undo(items);
        }
        finally
        {
            IsApplying = false;
        }

        _redo.Push(edit);
        Changed?.Invoke(this, EventArgs.Empty);
        return true;
    }

    /// <summary>取り消した編集をやり直す。実行したら true</summary>
    public bool Redo(ObservableCollection<ScheduleItem> items)
    {
        if (!CanRedo) return false;

        var edit = _redo.Pop();

        IsApplying = true;
        try
        {
            edit.Redo(items);
        }
        finally
        {
            IsApplying = false;
        }

        _undo.AddLast(edit);
        Changed?.Invoke(this, EventArgs.Empty);
        return true;
    }

    /// <summary>
    /// 履歴を捨てる。データを読み直したときなど、
    /// 履歴が指しているインスタンスが無効になる場面で呼ぶ。
    /// </summary>
    public void Clear()
    {
        if (_undo.Count == 0 && _redo.Count == 0) return;

        _undo.Clear();
        _redo.Clear();
        Changed?.Invoke(this, EventArgs.Empty);
    }
}
