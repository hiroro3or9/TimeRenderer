using System;
using System.Collections.Generic;
using System.Linq;
using System.Windows.Input;

using TimeRenderer.Helpers;
using TimeRenderer.Models;

namespace TimeRenderer.ViewModels;

/// <summary>
/// ふりかえり一覧ビュー：<see cref="WorkDayLog.Note"/> を全期間まとめて読み返す。
///
/// 統計にも同じ内容を出しているが、あちらは週／月／スプリントの区切りに縛られる。
/// 「いつ書いたか覚えていないものを探す」には期間を切り替えながら遡ることになり、
/// 読み返す場所としては使いものにならなかった。
///
/// ここは日付で絞らず、全部を新しい順に並べる。件数が増えても月の見出しで
/// 位置が掴めるので、スクロールだけで目的の時期まで辿れる。
/// 特定の言葉で探すときは、ツールバーの検索がふりかえりも対象にしている。
/// </summary>
public partial class MainViewModel
{
    /// <summary>
    /// ふりかえり1件分の表示用データ。
    /// 一覧ビューと統計の両方で使う（同じものを2つの形で持つ意味が無いため）。
    /// </summary>
    /// <param name="Date">勤務日。クリックで編集を開くときの対象</param>
    /// <param name="DateText">日付の表示</param>
    /// <param name="WorkText">その日の勤務時間帯。何をしていた日か思い出す手がかりにする</param>
    /// <param name="Note">ふりかえり本文（タグは取り除かず、書いたまま）</param>
    /// <param name="Tags">本文に含まれる <c>#タグ</c></param>
    public sealed record WorkDayNote(
        DateTime Date,
        string DateText,
        string WorkText,
        string Note,
        IReadOnlyList<string> Tags)
    {
        public bool HasTags => Tags.Count > 0;
    }

    /// <summary>絞り込み用のタグ1件（一覧の上に並べるチップ）</summary>
    /// <param name="Tag">タグ名（<c>#</c> は含まない）</param>
    /// <param name="Count">そのタグが付いたふりかえりの件数</param>
    /// <param name="IsSelected">いま絞り込みに使われているか</param>
    public sealed record NoteTagChip(string Tag, int Count, bool IsSelected)
    {
        public string Display => $"#{Tag}";
        public string CountText => Count.ToString();
    }

    /// <summary>ふりかえり一覧の月ごとの区切り</summary>
    /// <param name="Header">見出し（例: "2026年8月"）</param>
    /// <param name="Entries">その月のふりかえり（新しい順）</param>
    public sealed record NoteMonthGroup(string Header, IReadOnlyList<WorkDayNote> Entries)
    {
        public string CountText => $"{Entries.Count} 件";
    }

    private IReadOnlyList<NoteMonthGroup> _noteGroups = [];
    /// <summary>全期間のふりかえりを月ごとにまとめたもの（新しい月が上）</summary>
    public IReadOnlyList<NoteMonthGroup> NoteGroups
    {
        get => _noteGroups;
        private set
        {
            if (SetProperty(ref _noteGroups, value))
            {
                OnPropertyChanged(nameof(HasNotes));
                OnPropertyChanged(nameof(NotesSummaryText));
                OnPropertyChanged(nameof(DateDisplay)); // 見出しに件数を出している
            }
        }
    }

    /// <summary>ふりかえりが1件でもあるか（空のときの案内と出し分ける）</summary>
    public bool HasNotes => NoteGroups.Count > 0;

    private IReadOnlyList<NoteTagChip> _noteTagChips = [];
    /// <summary>絞り込みに使えるタグ（件数の多い順）</summary>
    public IReadOnlyList<NoteTagChip> NoteTagChips
    {
        get => _noteTagChips;
        private set
        {
            if (SetProperty(ref _noteTagChips, value)) OnPropertyChanged(nameof(HasNoteTags));
        }
    }

    /// <summary>タグが1つでも書かれているか（チップの行ごと出し分ける）</summary>
    public bool HasNoteTags => NoteTagChips.Count > 0;

    private string? _selectedNoteTag;
    /// <summary>絞り込み中のタグ（null なら絞り込みなし）</summary>
    public string? SelectedNoteTag
    {
        get => _selectedNoteTag;
        private set
        {
            if (SetProperty(ref _selectedNoteTag, value)) RebuildNoteGroups();
        }
    }

    /// <summary>タグのチップを押したときの絞り込み。同じタグをもう一度押すと解除する</summary>
    public ICommand SelectNoteTagCommand => _selectNoteTagCommand ??= new RelayCommand(param =>
    {
        var tag = param as string;
        SelectedNoteTag = string.IsNullOrEmpty(tag) || string.Equals(tag, SelectedNoteTag, StringComparison.OrdinalIgnoreCase)
            ? null
            : tag;
    });
    private RelayCommand? _selectNoteTagCommand;

    /// <summary>ふりかえりの件数（絞り込み中はその旨も出す）</summary>
    public string NotesSummaryText
    {
        get
        {
            var count = NoteGroups.Sum(g => g.Entries.Count);
            if (SelectedNoteTag is { } tag) return $"#{tag} {count} 件";
            return count == 0 ? "ふりかえり" : $"ふりかえり {count} 件";
        }
    }

    /// <summary>
    /// 勤務記録から一覧を組み直す。
    /// 新しい順に並べてから月でまとめているため、月・その中の日付ともに新しいものが上に来る。
    ///
    /// タグのチップは<b>絞り込み前の全件</b>から作る。絞り込むたびに他のタグが
    /// 消えてしまうと、そこから別のタグへ移れなくなるため。
    /// </summary>
    private void RebuildNoteGroups()
    {
        var all = _workDayLogs
            .Where(l => l.HasNote)
            .OrderByDescending(l => l.StartTime)
            .Select(ToWorkDayNote)
            .ToList();

        RebuildNoteTagChips(all);

        var visible = SelectedNoteTag is { } tag
            ? all.Where(n => n.Tags.Any(t => string.Equals(t, tag, StringComparison.OrdinalIgnoreCase)))
            : all;

        NoteGroups =
        [
            .. visible
                .GroupBy(n => new DateTime(n.Date.Year, n.Date.Month, 1))
                .Select(g => new NoteMonthGroup(g.Key.ToString("yyyy年M月"), [.. g]))
        ];
    }

    /// <summary>タグの一覧を件数の多い順に作り直す。同数なら名前順で並びを安定させる</summary>
    private void RebuildNoteTagChips(IReadOnlyList<WorkDayNote> notes)
    {
        var counts = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
        foreach (var note in notes)
        {
            foreach (var tag in note.Tags)
            {
                counts[tag] = counts.GetValueOrDefault(tag) + 1;
            }
        }

        NoteTagChips =
        [
            .. counts
                .OrderByDescending(kv => kv.Value)
                .ThenBy(kv => kv.Key, StringComparer.CurrentCulture)
                .Select(kv => new NoteTagChip(
                    kv.Key,
                    kv.Value,
                    string.Equals(kv.Key, SelectedNoteTag, StringComparison.OrdinalIgnoreCase)))
        ];

        // 絞り込み中のタグが消えた（最後の1件を書き換えた等）場合は解除する。
        // 残したままだと、1件も出ない一覧を見せることになる
        if (SelectedNoteTag is { } selected && !counts.ContainsKey(selected))
        {
            _selectedNoteTag = null;
            OnPropertyChanged(nameof(SelectedNoteTag));
        }
    }

    /// <summary>勤務記録1件を表示用に整える</summary>
    private static WorkDayNote ToWorkDayNote(WorkDayLog log)
    {
        var note = log.Note.Trim();

        return new WorkDayNote(
            log.StartTime.Date,
            log.StartTime.ToString("M/d(ddd)"),
            log.EndTime is { } end
                ? $"{log.StartTime:H:mm} - {end:H:mm} ・ {log.DurationText}"
                : $"{log.StartTime:H:mm} - 勤務中",
            note,
            NoteTagParser.Extract(note));
    }

    /// <summary>
    /// ふりかえりの内容が変わったときに、読み返し先の表示をまとめて作り直す。
    /// 一覧と統計の2箇所に出しているので、片方だけ古いままにならないよう1つにまとめている。
    /// </summary>
    private void NotifyWorkDayNotesChanged()
    {
        RebuildNoteGroups();
        UpdateStats();
    }
}
