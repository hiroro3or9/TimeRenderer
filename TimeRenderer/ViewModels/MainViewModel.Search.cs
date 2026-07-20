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

    /// <summary>検索結果1件分の表示用ラッパー。</summary>
    public record SearchResultVm(ScheduleItem Item)
    {
        public string Title => string.IsNullOrWhiteSpace(Item.Title) ? "(無題)" : Item.Title;
        public string Content => Item.Content;
        public bool HasContent => !string.IsNullOrWhiteSpace(Item.Content);
        public Brush Brush => Item.BackgroundColor;
        public string DateText => Item.StartTime.ToString("yyyy/MM/dd (ddd)");
        public string TimeText => Item.IsAllDay
            ? "終日"
            : $"{Item.StartTime:HH:mm} - {Item.EndTime:HH:mm}";
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
            var item = param switch
            {
                SearchResultVm vm => vm.Item,
                ScheduleItem si => si,
                _ => null
            };
            if (item != null) JumpToItem(item);
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

        SearchResults =
        [
            .. ScheduleItems
                .Where(x =>
                    x.Title.Contains(query, StringComparison.OrdinalIgnoreCase) ||
                    x.Content.Contains(query, StringComparison.OrdinalIgnoreCase))
                .OrderByDescending(x => x.StartTime)
                .Take(MaxSearchResults)
                .Select(x => new SearchResultVm(x))
        ];
    }

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

    // ===== 色フィルタ =====

    /// <summary>いずれかのカテゴリが非表示になっているか（フィルタ適用中か）。</summary>
    public bool IsColorFilterActive => Categories.Any(c => !c.IsFilterEnabled);
}
