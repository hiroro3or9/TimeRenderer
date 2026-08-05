using System;
using System.Collections.Generic;
using System.Linq;
using System.Windows.Input;
using System.Windows.Media;
using Brush = System.Windows.Media.Brush;

using TimeRenderer.Models;
using TimeRenderer.Helpers;

namespace TimeRenderer.ViewModels;

/// <summary>
/// 検索（タイトル/内容の部分一致→該当日にジャンプ）と色フィルタ（カテゴリ絞り込み）。
/// </summary>
public partial class MainViewModel
{
    private const int MaxSearchResults = 100;

    /// <summary>
    /// 検索結果1件分の表示用ラッパー。
    /// 予定・ToDo・ふりかえりを1つの一覧に混ぜるため、すべてこの形に揃える
    /// （探しているものがどれか分からないまま打ち込めるようにするため）。
    /// 種別はアイコンと日付欄の書き方で示す。
    /// </summary>
    public sealed class SearchResultVm
    {
        /// <summary>ふりかえりの結果に付ける色（種別が一目で分かればよいので固定色）</summary>
        private static readonly Brush NoteBrush = CreateFrozenBrush("#94A3B8");

        /// <summary>予定アイテム（他の種別なら null）</summary>
        public ScheduleItem? Item { get; private init; }

        /// <summary>ToDo（他の種別なら null）</summary>
        public TodoItem? Todo { get; private init; }

        /// <summary>ふりかえりの勤務日（他の種別なら null）</summary>
        public DateTime? NoteDate { get; private init; }

        public required string Title { get; init; }
        public required string Content { get; init; }
        public required Brush Brush { get; init; }
        public required string DateText { get; init; }
        public required string TimeText { get; init; }

        /// <summary>行頭に出す記号（Segoe MDL2 Assets）</summary>
        public required string Glyph { get; init; }

        public bool HasContent => !string.IsNullOrWhiteSpace(Content);

        /// <summary>並べ替え用の基準日時（ToDo で期限が無い場合は最小値＝末尾へ）</summary>
        public DateTime SortKey { get; init; }

        public static SearchResultVm ForItem(ScheduleItem item) => new()
        {
            Item = item,
            Title = string.IsNullOrWhiteSpace(item.Title) ? "(無題)" : item.Title,
            Content = item.Content,
            Brush = item.BackgroundColor,
            DateText = item.StartTime.ToString("yyyy/MM/dd (ddd)"),
            TimeText = item.IsAllDay ? "終日" : $"{item.StartTime:HH:mm} - {item.EndTime:HH:mm}",
            Glyph = "\uE787", // カレンダー
            SortKey = item.StartTime,
        };

        public static SearchResultVm ForTodo(TodoItem todo) => new()
        {
            Todo = todo,
            Title = string.IsNullOrWhiteSpace(todo.Title) ? "(無題)" : todo.Title,
            Content = todo.Content,
            Brush = todo.Brush,
            DateText = todo.DueDate.HasValue ? $"期限 {todo.DueDate.Value:yyyy/MM/dd (ddd)}" : "期限なし",
            TimeText = todo.IsCompleted ? "完了" : "ToDo",
            Glyph = "\uE73A", // チェックボックス
            SortKey = todo.DueDate ?? DateTime.MinValue,
        };

        /// <summary>
        /// ふりかえり。タイトルに当たるものが無いので本文の冒頭を見出しに使い、
        /// 収まりきらない場合だけ全文を Content に置く（同じ文が2行続くのを避ける）。
        /// </summary>
        public static SearchResultVm ForWorkDayNote(WorkDayLog log)
        {
            var note = log.NoteSingleLine;
            var head = note.Length > 40 ? string.Concat(note.AsSpan(0, 40), "…") : note;

            return new()
            {
                NoteDate = log.StartTime.Date,
                Title = head,
                Content = note.Length > head.Length ? note : string.Empty,
                Brush = NoteBrush,
                DateText = log.StartTime.ToString("yyyy/MM/dd (ddd)"),
                TimeText = "ふりかえり",
                Glyph = "\uE70F", // ペン
                SortKey = log.StartTime,
            };
        }

        private static Brush CreateFrozenBrush(string colorCode)
        {
            var brush = CategoryInfo.CreateBrush(colorCode);
            if (brush.CanFreeze) brush.Freeze();
            return brush;
        }
    }

    /// <summary>該当アイテムの日付へジャンプ後、その時刻までスクロールさせるための通知。</summary>
    public event EventHandler<DateTime>? ScrollToTimeRequested;

    public ICommand ClearSearchCommand { get; private set; } = null!;
    public ICommand JumpToSearchResultCommand { get; private set; } = null!;
    public ICommand ResetColorFilterCommand { get; private set; } = null!;

    private void InitializeSearchCommands()
    {
        ClearSearchCommand = new RelayCommand(_ => SearchQuery = string.Empty);

        JumpToSearchResultCommand = new RelayCommand(param =>
        {
            switch (param)
            {
                case SearchResultVm { Item: { } resultItem }:
                    JumpToItem(resultItem);
                    break;

                case SearchResultVm { Todo: { } resultTodo }:
                    JumpToTodo(resultTodo);
                    break;

                case SearchResultVm { NoteDate: { } noteDate }:
                    JumpToWorkDayNote(noteDate);
                    break;

                case ScheduleItem item:
                    JumpToItem(item);
                    break;

                case TodoItem todo:
                    JumpToTodo(todo);
                    break;
            }
        });

        ResetColorFilterCommand = new RelayCommand(_ =>
        {
            foreach (var category in Categories)
            {
                category.IsFilterEnabled = true;
            }
        });
    }

    private string _searchQuery = string.Empty;
    public string SearchQuery
    {
        get => _searchQuery;
        set
        {
            if (SetProperty(ref _searchQuery, value))
            {
                UpdateSearchResults();
                // タイムラインではヒットしたバーだけを残して他を減光する
                OnSearchQueryChangedForTimeline();
            }
        }
    }

    private IReadOnlyList<SearchResultVm> _searchResults = [];
    public IReadOnlyList<SearchResultVm> SearchResults
    {
        get => _searchResults;
        private set
        {
            if (SetProperty(ref _searchResults, value))
            {
                OnPropertyChanged(nameof(HasSearchResults));
                OnPropertyChanged(nameof(SearchResultCountText));
            }
        }
    }

    public bool HasSearchResults => SearchResults.Count > 0;

    public string SearchResultCountText => HasSearchResults ? $"{SearchResults.Count} 件" : "該当なし";

    private void UpdateSearchResults()
    {
        var query = _searchQuery?.Trim() ?? string.Empty;
        if (query.Length == 0)
        {
            SearchResults = [];
            return;
        }

        var items = ScheduleItems
            .Where(x => Matches(x.Title, x.Content, query))
            .Select(SearchResultVm.ForItem);

        var todos = Todos
            .Where(t => Matches(t.Title, t.Content, query))
            .Select(SearchResultVm.ForTodo);

        // ふりかえりは見出しに当たるものが無いので、本文だけを対象にする。
        // 日付やラベルまで拾うと「ふりかえり」の一語で全件出てきてしまう
        var notes = _workDayLogs
            .Where(l => l.HasNote && l.Note.Contains(query, StringComparison.OrdinalIgnoreCase))
            .Select(SearchResultVm.ForWorkDayNote);

        // 種別は混ぜたまま、新しい（期限が近い）ものから並べる。
        // 期限なしの ToDo は SortKey が最小値なので末尾へ回る
        SearchResults =
        [
            .. items.Concat(todos).Concat(notes)
                .OrderByDescending(r => r.SortKey)
                .Take(MaxSearchResults)
        ];
    }

    private static bool Matches(string title, string content, string query) =>
        title.Contains(query, StringComparison.OrdinalIgnoreCase) ||
        content.Contains(query, StringComparison.OrdinalIgnoreCase);

    private void JumpToItem(ScheduleItem item)
    {
        var targetDate = item.StartTime.Date;
        if (targetDate < CurrentDate.Date)
            TransitionDirection = Controls.TransitionDirection.Backward;
        else if (targetDate > CurrentDate.Date)
            TransitionDirection = Controls.TransitionDirection.Forward;

        // 現在のビューモードは維持したまま、該当日へ移動する
        CurrentDate = targetDate;

        // 時刻スクロールは時間軸のある日/週ビューのときのみ意味を持つ
        if (!item.IsAllDay && (IsDayMode || IsWeekMode))
        {
            ScrollToTimeRequested?.Invoke(this, item.StartTime);
        }
    }

    /// <summary>
    /// 検索結果の ToDo を選ぶ：ToDo パネルを開いて選択し、期限があればその日へ移動する。
    /// パネルを開かずに日付だけ動かしても、どれがヒットしたのか分からないため。
    /// </summary>
    private void JumpToTodo(TodoItem todo)
    {
        IsTodoPanelVisible = true;
        SelectedTodo = todo;

        if (todo.DueDate is not { } due) return;

        var targetDate = due.Date;
        if (targetDate < CurrentDate.Date)
            TransitionDirection = Controls.TransitionDirection.Backward;
        else if (targetDate > CurrentDate.Date)
            TransitionDirection = Controls.TransitionDirection.Forward;

        CurrentDate = targetDate;
    }

    /// <summary>
    /// 検索結果のふりかえりを選ぶ：その勤務日へ移動する。
    ///
    /// ビューモードは変えない。日/週なら勤務ラインのツールチップ、統計ならその期間の
    /// ふりかえり一覧、ふりかえりビューなら元から全件出ているので、
    /// どのモードでも「その日に寄る」だけで目的のものに辿り着ける。
    /// </summary>
    private void JumpToWorkDayNote(DateTime date)
    {
        var targetDate = date.Date;
        if (targetDate < CurrentDate.Date)
            TransitionDirection = Controls.TransitionDirection.Backward;
        else if (targetDate > CurrentDate.Date)
            TransitionDirection = Controls.TransitionDirection.Forward;

        CurrentDate = targetDate;
    }

    // ===== 色フィルタ =====

    /// <summary>いずれかのカテゴリが非表示になっているか（フィルタ適用中か）。</summary>
    public bool IsColorFilterActive => Categories.Any(c => !c.IsFilterEnabled);
}
