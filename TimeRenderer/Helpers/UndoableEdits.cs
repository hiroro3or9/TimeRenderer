using System;
using System.Collections.ObjectModel;

using TimeRenderer.Models;

namespace TimeRenderer.Helpers;

/// <summary>
/// 取り消し・やり直しが可能な編集1件。
///
/// 対象は予定アイテム（<see cref="ScheduleItem"/>）の追加・削除・内容変更に限る。
/// 設定・カテゴリ・スプリント・メモは対象外。
/// これらまで含めると「何が戻るのか」が予測しづらくなり、
/// かえって誤操作の救済という目的から外れるため。
/// </summary>
public interface IUndoableEdit
{
    /// <summary>「元に戻す: 〜」のツールチップに出す説明</summary>
    string Description { get; }

    void Undo(ObservableCollection<ScheduleItem> items);
    void Redo(ObservableCollection<ScheduleItem> items);
}

/// <summary>アイテムの追加（Undo で取り除き、Redo で戻す）</summary>
public sealed class AddItemEdit(ScheduleItem item) : IUndoableEdit
{
    private readonly ScheduleItem _item = item;

    public string Description => $"「{Describe(_item)}」の追加";

    public void Undo(ObservableCollection<ScheduleItem> items) => items.Remove(_item);

    public void Redo(ObservableCollection<ScheduleItem> items)
    {
        if (!items.Contains(_item)) items.Add(_item);
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

    public void Undo(ObservableCollection<ScheduleItem> items)
    {
        if (items.Contains(_item)) return;

        // 保存時のインデックスが現在の件数を超えている場合は末尾へ
        int at = Math.Clamp(_index, 0, items.Count);
        items.Insert(at, _item);
    }

    public void Redo(ObservableCollection<ScheduleItem> items) => items.Remove(_item);
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

    public void Undo(ObservableCollection<ScheduleItem> items) => _before.ApplyTo(_item);

    public void Redo(ObservableCollection<ScheduleItem> items) => _after.ApplyTo(_item);
}

/// <summary>
/// 複数の編集をひとまとまりとして扱う。
///
/// 「離席を除いて記録を分割する」のように、1回のユーザー操作が
/// 複数のアイテム変更を生む場合に使う。
/// 個別に積むと、1回の操作を戻すのに Ctrl+Z を何度も押すことになる。
/// </summary>
public sealed class CompositeEdit(IReadOnlyList<IUndoableEdit> edits, string description) : IUndoableEdit
{
    private readonly IReadOnlyList<IUndoableEdit> _edits = edits;

    public string Description { get; } = description;

    /// <summary>取り消しは逆順に適用する（後の変更から巻き戻す）</summary>
    public void Undo(ObservableCollection<ScheduleItem> items)
    {
        for (int i = _edits.Count - 1; i >= 0; i--)
        {
            _edits[i].Undo(items);
        }
    }

    public void Redo(ObservableCollection<ScheduleItem> items)
    {
        foreach (var edit in _edits) edit.Redo(items);
    }
}
