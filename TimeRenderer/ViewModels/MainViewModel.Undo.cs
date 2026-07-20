using System;

using TimeRenderer.Helpers;
using TimeRenderer.Models;

namespace TimeRenderer.ViewModels;

/// <summary>
/// 取り消し・やり直し。
///
/// 対象は予定アイテムの追加・削除・内容変更（編集ダイアログ、ドラッグでの移動・伸縮）と、
/// 記録停止による自動追加。設定・カテゴリ・スプリント・メモ・定期予定の自動生成は対象外。
///
/// アイテムの復元は必ず「元のインスタンスへ書き戻す」形にしている。
/// 複製で置き換えると、選択状態や他の履歴エントリが指す参照が食い違うため。
/// </summary>
public partial class MainViewModel
{
    private readonly UndoManager _undo = new();

    /// <summary>取り消し・やり直しの適用中か（この間は履歴に積まない・保存もまとめる）</summary>
    public bool IsApplyingUndo => _undo.IsApplying;

    public bool CanUndo => _undo.CanUndo;
    public bool CanRedo => _undo.CanRedo;

    public string UndoToolTip => _undo.CanUndo
        ? $"元に戻す: {_undo.UndoDescription} (Ctrl+Z)"
        : "元に戻す (Ctrl+Z)";

    public string RedoToolTip => _undo.CanRedo
        ? $"やり直す: {_undo.RedoDescription} (Ctrl+Y)"
        : "やり直す (Ctrl+Y)";

    /// <summary>コンストラクタから呼ぶ：履歴の変化をUIへ伝える配線</summary>
    private void InitializeUndo()
    {
        _undo.Changed += (_, _) =>
        {
            OnPropertyChanged(nameof(CanUndo));
            OnPropertyChanged(nameof(CanRedo));
            OnPropertyChanged(nameof(UndoToolTip));
            OnPropertyChanged(nameof(RedoToolTip));
        };
    }

    public RelayCommand UndoCommand => _undoCommand ??=
        new RelayCommand(_ => PerformUndo(), _ => CanUndo);
    private RelayCommand? _undoCommand;

    public RelayCommand RedoCommand => _redoCommand ??=
        new RelayCommand(_ => PerformRedo(), _ => CanRedo);
    private RelayCommand? _redoCommand;

    private void PerformUndo()
    {
        if (!_undo.Undo(ScheduleItems)) return;
        AfterUndoRedo();
    }

    private void PerformRedo()
    {
        if (!_undo.Redo(ScheduleItems)) return;
        AfterUndoRedo();
    }

    /// <summary>
    /// 取り消し・やり直しの後始末。
    ///
    /// 適用中はコレクション変更・プロパティ変更のハンドラが保存と再計算を抑止しているため、
    /// ここで1回だけまとめて実行する。
    /// </summary>
    private void AfterUndoRedo()
    {
        RecalculateLayout();
        SaveData();
    }

    // ===== 記録のためのヘルパー =====

    /// <summary>アイテムの追加を履歴に積む</summary>
    private void RecordAdd(ScheduleItem item) => _undo.Push(new AddItemEdit(item));

    /// <summary>アイテムの削除を履歴に積む（削除する直前に呼ぶこと）</summary>
    private void RecordRemove(ScheduleItem item)
    {
        int index = ScheduleItems.IndexOf(item);
        if (index < 0) return;
        _undo.Push(new RemoveItemEdit(item, index));
    }

    /// <summary>内容変更を履歴に積む。変化がなければ何もしない</summary>
    private void RecordModify(ScheduleItem item, ItemSnapshot before, string label)
    {
        var after = ItemSnapshot.Capture(item);
        if (before.IsSameAs(after)) return;
        _undo.Push(new ModifyItemEdit(item, before, after, label));
    }

    // ===== ドラッグの前後状態 =====

    private ScheduleItem? _dragUndoItem;
    private ItemSnapshot? _dragUndoBefore;

    /// <summary>
    /// ドラッグ開始時にビューから呼ぶ。開始前の状態を控えておく。
    /// ドラッグ中は UpdateItemTimesPreview が何度も走るが、履歴に積むのは確定時の1回だけ。
    /// </summary>
    public void BeginItemDragUndo(ScheduleItem item)
    {
        _dragUndoItem = item;
        _dragUndoBefore = ItemSnapshot.Capture(item);
    }

    /// <summary>ドラッグを取り消したときにビューから呼ぶ（履歴には積まない）</summary>
    public void ClearItemDragUndo()
    {
        _dragUndoItem = null;
        _dragUndoBefore = null;
    }

    /// <summary>ドラッグ確定時に、開始前との差分を履歴へ積む</summary>
    private void CommitItemDragUndo()
    {
        if (_dragUndoItem == null || _dragUndoBefore == null) return;

        // 移動と伸縮のどちらでも通るので、両方に当てはまる言い方にする
        RecordModify(_dragUndoItem, _dragUndoBefore, "時間の変更");
        ClearItemDragUndo();
    }

    /// <summary>
    /// 記録停止で生じた変更をまとめて1件の履歴として積む。
    /// 離席を除いて分割した場合、1回の停止操作が複数のアイテム変更になるため。
    /// </summary>
    private void PushRecordingEdits(string title, int segmentCount, List<IUndoableEdit> edits)
    {
        if (edits.Count == 0) return;

        if (edits.Count == 1)
        {
            _undo.Push(edits[0]);
            return;
        }

        _undo.Push(new CompositeEdit(edits, $"「{title}」の記録（{segmentCount}区間）"));
    }

    /// <summary>データ再読込などで履歴の参照先が無効になるときに呼ぶ</summary>
    private void ClearUndoHistory()
    {
        _undo.Clear();
        ClearItemDragUndo();
    }
}
