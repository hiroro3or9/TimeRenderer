using System;
using System.Collections.Generic;
using System.Linq;

using TimeRenderer.Helpers;
using TimeRenderer.Models;

namespace TimeRenderer.ViewModels;

/// <summary>
/// ビューをまたいで共通の「選択」と、それに対するキーボード操作。
///
/// 選択状態は <see cref="ScheduleItem.IsSelected"/> がアイテム自身に持つ。
/// ビューごとに別々の選択を持つと、表示を切り替えたときに選択が消えて混乱するため。
///
/// 選択を動かす対象は表示中のビューによって変える:
/// - タイムライン: 表示範囲内のバー（検索で減光中のものは飛ばす）
/// - 日/週ビュー: 表示中の日に含まれるアイテム
/// </summary>
public partial class MainViewModel
{
    /// <summary>選択を前後のアイテムへ動かす（パラメータ: -1 で前、+1 で次）</summary>
    public RelayCommand MoveSelectionCommand => _moveSelectionCommand ??=
        new RelayCommand(param =>
        {
            int direction = param switch
            {
                int d => d,
                string s when s == "-1" => -1,
                _ => 1
            };

            var candidates = GetSelectionCandidates();
            if (candidates.Count == 0) return;

            int current = candidates.FindIndex(x => ReferenceEquals(x, _selectedItem));

            int next = current < 0
                ? (direction > 0 ? 0 : candidates.Count - 1)
                : Math.Clamp(current + direction, 0, candidates.Count - 1);

            // キーボードでの移動先は画面外のこともあるため、表示を寄せる
            SelectAndReveal(candidates[next]);
        });
    private RelayCommand? _moveSelectionCommand;

    /// <summary>現在のビューで選択を移動できるアイテムを、開始時刻順に得る</summary>
    private List<ScheduleItem> GetSelectionCandidates()
    {
        if (CurrentViewMode == ViewMode.SprintTimeline)
        {
            // 画面外のアイテムへも選択を進められるよう、仮想化前の全件を辿る。
            // 検索で減光中（非ヒット）のものは飛ばす
            return [.. AllTimelineBars.Where(b => !b.IsDimmed).Select(b => b.Item)];
        }

        // 日/週ビュー：表示中の日に含まれるアイテム
        var days = VisibleDays;
        if (days.Count == 0) return [];

        var seen = new HashSet<ScheduleItem>();
        var result = new List<ScheduleItem>();

        foreach (var day in days)
        {
            if (!DailyScheduleItems.TryGetValue(day.Date, out var items)) continue;

            foreach (var item in items)
            {
                // 日をまたぐアイテムは複数日に現れるため、重複を除く
                if (seen.Add(item)) result.Add(item);
            }
        }

        return [.. result.OrderBy(x => x.StartTime)];
    }

    /// <summary>選択中のアイテムを編集する</summary>
    public RelayCommand EditSelectedCommand => _editSelectedCommand ??=
        new RelayCommand(_ =>
        {
            if (_selectedItem == null) return;
            if (EditCommand.CanExecute(_selectedItem)) EditCommand.Execute(_selectedItem);
        });
    private RelayCommand? _editSelectedCommand;

    /// <summary>選択中のアイテムを削除する</summary>
    public RelayCommand DeleteSelectedCommand => _deleteSelectedCommand ??=
        new RelayCommand(_ =>
        {
            var item = _selectedItem;
            if (item == null) return;

            if (DeleteCommand.CanExecute(item))
            {
                DeleteCommand.Execute(item);
                // 削除が実際に行われた場合のみ選択を外す（確認ダイアログでキャンセルされることがある）
                if (!ScheduleItems.Contains(item)) SelectedItem = null;
            }
        });
    private RelayCommand? _deleteSelectedCommand;

    /// <summary>選択を解除する</summary>
    public RelayCommand ClearSelectionCommand => _clearSelectionCommand ??=
        new RelayCommand(_ => SelectedItem = null);
    private RelayCommand? _clearSelectionCommand;
}
