using System;
using System.Collections.ObjectModel;

using TimeRenderer.Models;

namespace TimeRenderer.Helpers;

/// <summary>
/// 取り消し・やり直しの適用先。
///
/// 履歴を予定と ToDo で分けると Ctrl+Z が2系統になり、
/// 「今どちらが戻るのか」が操作者に分からなくなる。
/// 1本の履歴のまま両方を扱えるよう、適用先をまとめて渡す。
/// 対象を増やすときはここへ足す。
/// </summary>
/// <param name="Items">予定アイテム</param>
/// <param name="Todos">ToDo</param>
public sealed record UndoContext(
    ObservableCollection<ScheduleItem> Items,
    ObservableCollection<TodoItem> Todos);

/// <summary>
/// 取り消し・やり直しが可能な編集1件。
///
/// 対象は予定アイテム（<see cref="ScheduleItem"/>）と ToDo（<see cref="TodoItem"/>）の
/// 追加・削除・内容変更に限る。設定・カテゴリ・スプリント・メモは対象外。
/// これらまで含めると「何が戻るのか」が予測しづらくなり、
/// かえって誤操作の救済という目的から外れるため。
/// </summary>
public interface IUndoableEdit
{
    /// <summary>「元に戻す: 〜」のツールチップに出す説明</summary>
    string Description { get; }

    void Undo(UndoContext context);
    void Redo(UndoContext context);
}

// ===== 予定アイテム =====

/// <summary>アイテムの追加（Undo で取り除き、Redo で戻す）</summary>
public sealed class AddItemEdit(ScheduleItem item) : IUndoableEdit
{
    private readonly ScheduleItem _item = item;

    public string Description => $"「{Describe(_item)}」の追加";

    public void Undo(UndoContext context) => context.Items.Remove(_item);

    public void Redo(UndoContext context)
    {
        if (!context.Items.Contains(_item)) context.Items.Add(_item);
    }

    internal static string Describe(ScheduleItem item) =>
        string.IsNullOrWhiteSpace(item.Title) ? "(無題)" : item.Title;
}

/// <summary>
/// アイテムの削除。元の位置（インデックス）も覚えておき、
/// Undo したときに並び順が変わらないようにする。
/// </summary>
public sealed class RemoveItemEdit(ScheduleItem item, int index) : IUndoableEdit
{
    private readonly ScheduleItem _item = item;
    private readonly int _index = index;

    public string Description => $"「{AddItemEdit.Describe(_item)}」の削除";

    public void Undo(UndoContext context)
    {
        if (context.Items.Contains(_item)) return;

        // 保存時のインデックスが現在の件数を超えている場合は末尾へ
        int at = Math.Clamp(_index, 0, context.Items.Count);
        context.Items.Insert(at, _item);
    }

    public void Redo(UndoContext context) => context.Items.Remove(_item);
}

/// <summary>
/// アイテムの内容変更（編集ダイアログ・ドラッグでの移動や伸縮）。
/// 変更前後の状態を持ち、同じインスタンスへ書き戻す。
/// </summary>
public sealed class ModifyItemEdit(ScheduleItem item, ItemSnapshot before, ItemSnapshot after, string label)
    : IUndoableEdit
{
    private readonly ScheduleItem _item = item;
    private readonly ItemSnapshot _before = before;
    private readonly ItemSnapshot _after = after;

    public string Description => $"「{AddItemEdit.Describe(_item)}」の{label}";

    public void Undo(UndoContext context) => _before.ApplyTo(_item);

    public void Redo(UndoContext context) => _after.ApplyTo(_item);
}

// ===== ToDo =====

/// <summary>ToDo の追加</summary>
public sealed class AddTodoEdit(TodoItem todo) : IUndoableEdit
{
    private readonly TodoItem _todo = todo;

    public string Description => $"ToDo「{Describe(_todo)}」の追加";

    public void Undo(UndoContext context) => context.Todos.Remove(_todo);

    public void Redo(UndoContext context)
    {
        if (!context.Todos.Contains(_todo)) context.Todos.Add(_todo);
    }

    internal static string Describe(TodoItem todo) =>
        string.IsNullOrWhiteSpace(todo.Title) ? "(無題)" : todo.Title;
}

/// <summary>ToDo の削除。元の位置も覚えて、Undo で同じ場所へ戻す</summary>
public sealed class RemoveTodoEdit(TodoItem todo, int index) : IUndoableEdit
{
    private readonly TodoItem _todo = todo;
    private readonly int _index = index;

    public string Description => $"ToDo「{AddTodoEdit.Describe(_todo)}」の削除";

    public void Undo(UndoContext context)
    {
        if (context.Todos.Contains(_todo)) return;

        int at = Math.Clamp(_index, 0, context.Todos.Count);
        context.Todos.Insert(at, _todo);
    }

    public void Redo(UndoContext context) => context.Todos.Remove(_todo);
}

/// <summary>ToDo の内容変更（完了の切り替え・編集ダイアログ・期限の付け替え）</summary>
public sealed class ModifyTodoEdit(TodoItem todo, TodoSnapshot before, TodoSnapshot after, string label)
    : IUndoableEdit
{
    private readonly TodoItem _todo = todo;
    private readonly TodoSnapshot _before = before;
    private readonly TodoSnapshot _after = after;

    public string Description => $"ToDo「{AddTodoEdit.Describe(_todo)}」の{label}";

    public void Undo(UndoContext context) => _before.ApplyTo(_todo);

    public void Redo(UndoContext context) => _after.ApplyTo(_todo);
}

/// <summary>
/// ToDo の手動並べ替え。
/// 1回のドラッグで多くの ToDo の並び順が動くため、まとめて1件として扱う。
/// </summary>
/// <param name="before">変更前の (ToDo, 並び順) の一覧</param>
/// <param name="after">変更後の (ToDo, 並び順) の一覧</param>
public sealed class ReorderTodosEdit(
    IReadOnlyList<(TodoItem Todo, int Order)> before,
    IReadOnlyList<(TodoItem Todo, int Order)> after) : IUndoableEdit
{
    public string Description => "ToDo の並べ替え";

    public void Undo(UndoContext context) => Apply(before);

    public void Redo(UndoContext context) => Apply(after);

    private static void Apply(IReadOnlyList<(TodoItem Todo, int Order)> orders)
    {
        foreach (var (todo, order) in orders)
        {
            todo.SortOrder = order;
        }
    }
}

// ===== まとめ =====

/// <summary>
/// 複数の編集をひとまとまりとして扱う。
///
/// 「離席を除いて記録を分割する」「繰り返す ToDo を完了して次回分を作る」のように、
/// 1回のユーザー操作が複数の変更を生む場合に使う。
/// 個別に積むと、1回の操作を戻すのに Ctrl+Z を何度も押すことになる。
/// </summary>
public sealed class CompositeEdit(IReadOnlyList<IUndoableEdit> edits, string description) : IUndoableEdit
{
    private readonly IReadOnlyList<IUndoableEdit> _edits = edits;

    public string Description { get; } = description;

    /// <summary>取り消しは逆順に適用する（後の変更から巻き戻す）</summary>
    public void Undo(UndoContext context)
    {
        for (int i = _edits.Count - 1; i >= 0; i--)
        {
            _edits[i].Undo(context);
        }
    }

    public void Redo(UndoContext context)
    {
        foreach (var edit in _edits) edit.Redo(context);
    }
}
