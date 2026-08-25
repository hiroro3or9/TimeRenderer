using System;

using TimeRenderer.Helpers;
using TimeRenderer.Models;

namespace TimeRenderer.ViewModels;

/// <summary>
/// 予定・実績の複製とコピー／貼り付け。
///
/// 作業記録は「昨日と同じ作業」が繰り返し現れるため、
/// 同じ内容を作り直す手間を減らす経路を用意する。
///
/// 写しは <see cref="ItemSnapshot"/> で持つ。アイテムの参照を持つと、
/// コピー元が削除・編集されたときに貼り付け結果が変わってしまう。
///
/// Windows のクリップボードは使わない。他アプリとやり取りする内容ではなく、
/// 外部からの書き換えで貼り付け結果が変わるのを避けるため。
/// </summary>
public partial class MainViewModel
{
    private ItemSnapshot? _clipboardItem;

    /// <summary>コピー済みの内容があるか（貼り付けメニューの有効・無効に使う）</summary>
    public bool HasClipboardItem => _clipboardItem != null;

    /// <summary>コピー済みの内容のタイトル（メニューやツールチップの表示用）</summary>
    public string ClipboardItemTitle => _clipboardItem == null
        ? string.Empty
        : (string.IsNullOrWhiteSpace(_clipboardItem.Title) ? "(無題)" : _clipboardItem.Title);

    /// <summary>選択中のアイテム、または引数で渡されたアイテムを対象にする</summary>
    private ScheduleItem? ResolveTargetItem(object? param) =>
        param as ScheduleItem ?? SelectedItem;

    public RelayCommand CopyItemCommand => _copyItemCommand ??= new RelayCommand(
        param =>
        {
            if (ResolveTargetItem(param) is not { } item) return;

            _clipboardItem = ItemSnapshot.Capture(item);
            OnPropertyChanged(nameof(HasClipboardItem));
            OnPropertyChanged(nameof(ClipboardItemTitle));
        },
        param => ResolveTargetItem(param) != null);
    private RelayCommand? _copyItemCommand;

    /// <summary>選択中のアイテムを、その直後の時間帯へ複製する</summary>
    public RelayCommand DuplicateItemCommand => _duplicateItemCommand ??= new RelayCommand(
        param =>
        {
            if (ResolveTargetItem(param) is not { } item) return;

            var snapshot = ItemSnapshot.Capture(item);

            // 終日は時間帯を持たないため同じ日へ重ねる。
            // 時間帯を持つものは直後に置き、続けて別の作業をした記録を作りやすくする
            var start = item.IsAllDay ? item.StartTime : item.EndTime;

            AddCopy(snapshot, start);
        },
        param => ResolveTargetItem(param) != null);
    private RelayCommand? _duplicateItemCommand;

    /// <summary>
    /// コピー済みの内容を貼り付ける。
    /// 引数に <see cref="DateTime"/> を渡すとその時刻へ、渡さなければ
    /// 表示中の日の同じ時間帯へ置く（前日の記録を今日へ写す使い方を想定している）。
    /// </summary>
    public RelayCommand PasteItemCommand => _pasteItemCommand ??= new RelayCommand(
        param =>
        {
            if (_clipboardItem is not { } snapshot) return;

            var start = param is DateTime at
                ? at
                : CurrentDate.Date.Add(snapshot.StartTime.TimeOfDay);

            AddCopy(snapshot, start);
        },
        _ => _clipboardItem != null);
    private RelayCommand? _pasteItemCommand;

    /// <summary>写しから新しいアイテムを作って一覧へ加え、履歴に積む</summary>
    private void AddCopy(ItemSnapshot snapshot, DateTime start)
    {
        var copy = CreateCopy(snapshot, start);

        ScheduleItems.Add(copy);
        RecordAdd(copy);
        SelectedItem = copy;
        SaveData();
    }

    /// <summary>
    /// 写しから新しいアイテムを作る。長さは元のまま、開始時刻だけ差し替える。
    ///
    /// 引き継がないもの:
    /// - Id: 別のアイテムなので採番し直す
    /// - RoutineId / SourcePlanId: 定期予定・元の予定との対応は写しには無い
    /// - TodoId: 引き継ぐと同じ ToDo へ実績が二重に積まれる
    /// </summary>
    private static ScheduleItem CreateCopy(ItemSnapshot snapshot, DateTime start)
    {
        var duration = snapshot.EndTime - snapshot.StartTime;

        return new ScheduleItem
        {
            Kind = snapshot.Kind,
            Title = snapshot.Title,
            Content = snapshot.Content,
            StartTime = start,
            EndTime = start + duration,
            IsAllDay = snapshot.IsAllDay,
            BackgroundColor = snapshot.BackgroundColor,
            CategoryId = snapshot.CategoryId,
            ProjectCodeId = snapshot.ProjectCodeId,
            RemindAtStart = snapshot.RemindAtStart,
            AutoStartRecording = snapshot.AutoStartRecording,
            ForceStartRecording = snapshot.ForceStartRecording
        };
    }

    // ===== Alt を押しながらのドラッグ（元を残して複製） =====

    /// <summary>Alt ドラッグで元の位置に残す写し。ドラッグ確定まで履歴には積まない</summary>
    private ScheduleItem? _dragCopyClone;

    /// <summary>
    /// Alt を押しながらの移動ドラッグを開始する。
    ///
    /// 元の位置に写しを置き、掴んだアイテムの方を動かす。
    /// 見た目は「元が残って複製が動く」のと変わらず、
    /// 掴んだアイテムの Id と紐づけ（ToDo・予定との対応）がそのまま動くぶん筋がよい。
    /// </summary>
    /// <returns>複製ドラッグを始められたら true</returns>
    public bool BeginItemCopyDrag(ScheduleItem item)
    {
        // 定期予定の仮想アイテムは実体化の確認が絡むため対象外にする
        if (item.IsVirtual) return false;

        var clone = CreateCopy(ItemSnapshot.Capture(item), item.StartTime);
        ScheduleItems.Add(clone);
        _dragCopyClone = clone;

        BeginItemDragUndo(item);
        return true;
    }

    /// <summary>
    /// ドラッグを取り消したときにビューから呼ぶ。
    /// 時刻を戻し、Alt ドラッグで置いた写しがあれば取り除く。
    /// </summary>
    public void CancelItemDrag(ScheduleItem item, DateTime originalStart, DateTime originalEnd)
    {
        UpdateItemTimesPreview(item, originalStart, originalEnd);

        if (_dragCopyClone is { } clone)
        {
            ScheduleItems.Remove(clone);
        }

        ClearItemDragUndo();
    }
}
