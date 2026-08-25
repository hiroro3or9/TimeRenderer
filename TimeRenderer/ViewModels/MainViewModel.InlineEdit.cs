using System;
using System.Linq;

using TimeRenderer.Models;

using Brushes = System.Windows.Media.Brushes;

namespace TimeRenderer.ViewModels;

/// <summary>
/// バー上でタイトルを直接入力する新規作成・名称変更。
///
/// 従来は作成も編集も必ず編集ダイアログを開いていたため、
/// 「枠を引く」と「名前を付ける」が別の操作に分かれていた。
/// タイトルだけ決めれば済む場面が大半なので、その場で打てる経路を用意する。
/// カテゴリやメモまで指定したいときは Ctrl+Enter でダイアログへ移る。
///
/// 新規作成は「まず追加してから名前を打つ」形になるため、
/// 取り消し履歴へ積むのは確定した時点にしている。
/// 入力の途中で取りやめた場合は履歴に何も残さない。
/// </summary>
public partial class MainViewModel
{
    /// <summary>
    /// インライン入力用に空のアイテムを作って一覧へ加える。まだ履歴には積まない。
    /// </summary>
    public ScheduleItem BeginInlineCreate(DateTime start, DateTime end)
    {
        if (end <= start) end = start.AddMinutes(SnapMinutes);

        var category = Categories.FirstOrDefault();

        var item = new ScheduleItem
        {
            // 終わった時間帯に引いた枠は実績、これから先の時間帯は予定として扱う。
            // 作業記録を後から埋める操作が多く、その都度種別を選ばせるのは手数になる
            Kind = end <= DateTime.Now ? ScheduleItemKind.Recorded : ScheduleItemKind.Planned,
            StartTime = start,
            EndTime = end,
            Title = string.Empty,
            ColorCode = category?.ColorCode ?? Brushes.LightBlue.ToString(),
            CategoryId = category?.Id,
            ProjectCodeId = DefaultProjectCode?.Id
        };

        ScheduleItems.Add(item);
        SelectedItem = item;
        return item;
    }

    /// <summary>
    /// インライン入力を確定する。タイトルが空なら作成そのものを取りやめる。
    /// 名前の無いブロックは後から見て意味を持たないため、
    /// 枠を引いたまま別の場所をクリックしたときに空のアイテムが残らないようにする。
    /// </summary>
    /// <returns>アイテムが残ったら true（取りやめたら false）</returns>
    public bool CommitInlineCreate(ScheduleItem item, string title)
    {
        var trimmed = title.Trim();
        if (trimmed.Length == 0)
        {
            CancelInlineCreate(item);
            return false;
        }

        item.Title = trimmed;
        RecordAdd(item);
        SaveData();
        return true;
    }

    /// <summary>インライン入力を取りやめる（作りかけのアイテムを取り除く）</summary>
    public void CancelInlineCreate(ScheduleItem item)
    {
        ScheduleItems.Remove(item);
        if (ReferenceEquals(SelectedItem, item)) SelectedItem = null;
        SaveData();
    }

    /// <summary>名称変更の開始。取り消し用に変更前の状態を控える</summary>
    public ItemSnapshot BeginInlineRename(ScheduleItem item) => ItemSnapshot.Capture(item);

    /// <summary>
    /// 名称変更を確定する。空文字と変化なしは、履歴を汚さないよう何もしない。
    /// </summary>
    public void CommitInlineRename(ScheduleItem item, ItemSnapshot before, string title)
    {
        var trimmed = title.Trim();
        if (trimmed.Length == 0 || trimmed == item.Title) return;

        item.Title = trimmed;
        RecordModify(item, before, "タイトルの変更");
        SaveData();
    }
}
