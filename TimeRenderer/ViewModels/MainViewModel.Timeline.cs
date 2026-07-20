using System;
using System.Collections.Generic;
using System.Linq;
using System.Windows.Media;
using System.Windows.Threading;
using Brushes = System.Windows.Media.Brushes;

using TimeRenderer.Helpers;
using TimeRenderer.Models;

namespace TimeRenderer.ViewModels;

/// <summary>
/// スプリントタイムラインビューのレイアウト計算。
///
/// 設計の要点:
/// - 表示範囲を画面幅に押し込むのをやめ、「1日 = PixelsPerDay」の連続スケール
///   (<see cref="TimelineScale"/>) に統一した。横スクロールとズームで解像度を確保する。
/// - 時刻を日付に丸めないため、30分の記録は30分ぶんの幅で描かれる。
/// - 1アイテム1行をやめ、重ならないものを同じレーンに詰めて縦の密度を上げる。
/// </summary>
public partial class MainViewModel
{
    public IReadOnlyList<TimelineGroupModeOption> TimelineGroupModeOptions { get; } =
    [
        new(TimelineGroupMode.Packed, "詰める"),
        new(TimelineGroupMode.Category, "カテゴリ別"),
        new(TimelineGroupMode.Flat, "1件1行"),
    ];

    public IReadOnlyList<TimelineZoomPreset> TimelineZoomPresets { get; } =
    [
        new("時間", 480),
        new("日", 120),
        new("週", 40),
        new("スプリント", 12),
    ];

    // ===== 設定値 =====

    private double _timelinePixelsPerDay = TimelineScale.DefaultPixelsPerDay;
    /// <summary>タイムラインのズーム倍率（1日あたりのピクセル数）</summary>
    public double TimelinePixelsPerDay
    {
        get => _timelinePixelsPerDay;
        set
        {
            var clamped = Math.Clamp(value, TimelineScale.MinPixelsPerDay, TimelineScale.MaxPixelsPerDay);
            // 連続的なズーム操作でも無駄な再計算をしないよう、微小な差は無視する
            if (Math.Abs(clamped - _timelinePixelsPerDay) < 0.01) return;

            _timelinePixelsPerDay = clamped;
            OnPropertyChanged();
            OnPropertyChanged(nameof(TimelineZoomText));

            // 実体化済みの窓はコンテンツ座標で持っているため、倍率が変わると意味が変わる。
            // 無効化して、ビューからの次のビューポート通知で張り直す。
            InvalidateRealizedWindow();

            UpdateTimelineItems();
            RequestZoomSave();
        }
    }

    /// <summary>
    /// ズーム倍率の保存を遅延させるタイマー。
    /// Ctrl+ホイールは1回の操作で何度も発火するため、そのたびに設定ファイルへ書き込むと
    /// ディスクI/Oでズームが引っかかる。操作が落ち着いてから1回だけ保存する。
    /// </summary>
    private DispatcherTimer? _zoomSaveTimer;

    private void RequestZoomSave()
    {
        if (!_isInitialized) return;

        if (_zoomSaveTimer == null)
        {
            _zoomSaveTimer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(600) };
            _zoomSaveTimer.Tick += (_, _) =>
            {
                _zoomSaveTimer!.Stop();
                SaveSettings();
            };
        }

        // 操作が続く限りタイマーを先送りする
        _zoomSaveTimer.Stop();
        _zoomSaveTimer.Start();
    }

    /// <summary>ツールバー表示用のズーム倍率テキスト</summary>
    public string TimelineZoomText => $"{_timelinePixelsPerDay:0}px/日";

    public IReadOnlyList<TimelineSpanOption> TimelineSpanOptions { get; } =
    [
        new(3, "3"),
        new(5, "5"),
        new(9, "9"),
        new(15, "15"),
        new(25, "25"),
    ];

    /// <summary>表示範囲の上限。これを超えると日単位の背景列や集計が重くなる</summary>
    private const int MaxTimelineSprintCount = 25;

    private int _timelineSprintCount = 5;
    /// <summary>
    /// タイムラインに表示するスプリント数。
    /// 従来は5固定だったが、px/日スケールにしたことで範囲の広さと解像度が独立したため、
    /// 広い範囲を俯瞰する使い方ができるようにした。
    /// </summary>
    public int TimelineSprintCount
    {
        get => _timelineSprintCount;
        set
        {
            var clamped = Math.Clamp(value, 1, MaxTimelineSprintCount);
            if (SetProperty(ref _timelineSprintCount, clamped))
            {
                OnPropertyChanged(nameof(SelectedTimelineSpanOption));
                InvalidateRealizedWindow();
                UpdateVisibleDays();
                SaveSettings();
            }
        }
    }

    public TimelineSpanOption SelectedTimelineSpanOption
    {
        get => TimelineSpanOptions.FirstOrDefault(o => o.Count == _timelineSprintCount)
               ?? TimelineSpanOptions[1];
        set
        {
            if (value != null) TimelineSprintCount = value.Count;
        }
    }

    private TimelineGroupMode _timelineGroupMode = TimelineGroupMode.Packed;
    public TimelineGroupMode CurrentTimelineGroupMode
    {
        get => _timelineGroupMode;
        set
        {
            if (SetProperty(ref _timelineGroupMode, value))
            {
                OnPropertyChanged(nameof(IsTimelineCategoryMode));
                OnPropertyChanged(nameof(TimelineLabelColumnWidth));
                OnPropertyChanged(nameof(SelectedTimelineGroupModeOption));
                UpdateTimelineItems();
                SaveSettings();
            }
        }
    }

    public TimelineGroupModeOption SelectedTimelineGroupModeOption
    {
        get => TimelineGroupModeOptions.FirstOrDefault(o => o.Mode == _timelineGroupMode)
               ?? TimelineGroupModeOptions[0];
        set
        {
            if (value != null) CurrentTimelineGroupMode = value.Mode;
        }
    }

    /// <summary>カテゴリ別モードか（左端のラベル列の表示切替に使う）</summary>
    public bool IsTimelineCategoryMode => _timelineGroupMode == TimelineGroupMode.Category;

    /// <summary>左端の固定ラベル列の幅（カテゴリ別モード以外では0にして畳む）</summary>
    public double TimelineLabelColumnWidth => IsTimelineCategoryMode ? 150.0 : 0.0;

    // ===== 計算結果 =====

    private TimelineScale? _timelineScale;
    /// <summary>
    /// 現在の時間軸スケール。ズーム時のアンカー計算やクリック位置の時刻解決のため、
    /// コードビハインドからも参照する。
    /// </summary>
    public TimelineScale? CurrentTimelineScale => _timelineScale;

    private IReadOnlyList<TimelineBar> _timelineBars = [];
    /// <summary>描画するバーの一覧（座標・幅は計算済み）</summary>
    public IReadOnlyList<TimelineBar> TimelineBars
    {
        get => _timelineBars;
        private set => SetProperty(ref _timelineBars, value);
    }

    private IReadOnlyList<TimelineLaneGroup> _timelineLaneGroups = [];
    /// <summary>カテゴリ別モードでの行グループ（左端ラベル列・背景の帯）</summary>
    public IReadOnlyList<TimelineLaneGroup> TimelineLaneGroups
    {
        get => _timelineLaneGroups;
        private set => SetProperty(ref _timelineLaneGroups, value);
    }

    private IReadOnlyList<TimelineSprintBand> _timelineSprintBands = [];
    /// <summary>スプリントのヘッダー・区画（実日数に比例した幅を持つ）</summary>
    public IReadOnlyList<TimelineSprintBand> TimelineSprintBands
    {
        get => _timelineSprintBands;
        private set => SetProperty(ref _timelineSprintBands, value);
    }

    private double _timelineContentWidth;
    public double TimelineContentWidth
    {
        get => _timelineContentWidth;
        private set => SetProperty(ref _timelineContentWidth, value);
    }

    private double _timelineContentHeight = 200;
    public double TimelineContentHeight
    {
        get => _timelineContentHeight;
        private set => SetProperty(ref _timelineContentHeight, value);
    }

    private bool _isTimelineEmpty = true;
    /// <summary>表示範囲に1件もないか（空状態のメッセージ表示用）</summary>
    public bool IsTimelineEmpty
    {
        get => _isTimelineEmpty;
        private set => SetProperty(ref _isTimelineEmpty, value);
    }

    // ===== ズーム操作 =====

    /// <summary>1段階ズームイン（1.5倍）</summary>
    public RelayCommand TimelineZoomInCommand => _timelineZoomInCommand ??=
        new RelayCommand(_ => TimelinePixelsPerDay *= 1.5);
    private RelayCommand? _timelineZoomInCommand;

    /// <summary>1段階ズームアウト</summary>
    public RelayCommand TimelineZoomOutCommand => _timelineZoomOutCommand ??=
        new RelayCommand(_ => TimelinePixelsPerDay /= 1.5);
    private RelayCommand? _timelineZoomOutCommand;

    /// <summary>プリセット倍率を適用する</summary>
    public RelayCommand TimelineZoomPresetCommand => _timelineZoomPresetCommand ??=
        new RelayCommand(param =>
        {
            if (param is TimelineZoomPreset preset) TimelinePixelsPerDay = preset.PixelsPerDay;
            else if (param is double px) TimelinePixelsPerDay = px;
        });
    private RelayCommand? _timelineZoomPresetCommand;

    // ===== 選択 =====

    private ScheduleItem? _selectedItem;
    /// <summary>
    /// タイムラインで選択中のアイテム。キーボード操作の起点になる。
    /// バーの見た目はレイアウトを組み直さずに切り替えたいので、
    /// TimelineBar.IsSelected を直接更新する。
    /// </summary>
    public ScheduleItem? SelectedItem
    {
        get => _selectedItem;
        set
        {
            if (ReferenceEquals(_selectedItem, value)) return;

            // 選択はアイテム自身が持つ（日/週/月/タイムラインで共通にするため）
            if (_selectedItem != null) _selectedItem.IsSelected = false;
            _selectedItem = value;
            if (_selectedItem != null) _selectedItem.IsSelected = true;

            OnPropertyChanged();
            RefreshBarStates();
        }
    }

    /// <summary>
    /// 選択したうえで、その位置まで表示を寄せる。
    ///
    /// スクロールを設定側（setter）でやらないのは、
    /// クリックで選んだときにも表示が動いてしまい、
    /// 掴もうとした対象がカーソルの下から逃げるため。
    /// キーボード操作やジャンプなど、表示外へ選択が動きうる場面でだけ呼ぶ。
    /// </summary>
    public void SelectAndReveal(ScheduleItem? item)
    {
        SelectedItem = item;
        if (item == null) return;

        if (CurrentViewMode == ViewMode.SprintTimeline)
        {
            TimelineScrollToItemRequested?.Invoke(this, item);
        }
        else if (!item.IsAllDay && (IsDayMode || IsWeekMode))
        {
            ScrollToTimeRequested?.Invoke(this, item.StartTime);
        }
    }

    /// <summary>選択アイテムが画面外のとき、そこまでスクロールさせるための通知</summary>
    public event EventHandler<ScheduleItem>? TimelineScrollToItemRequested;

    /// <summary>選択アイテムに合わせてズームさせるための通知（F キー）</summary>
    public event EventHandler<ScheduleItem>? TimelineFitToItemRequested;

    /// <summary>
    /// 選択状態と検索の減光を、既存のバーに反映する。
    /// バーの座標は変わらないので、リストを作り直さずプロパティだけ更新する。
    /// </summary>
    private void RefreshBarStates()
    {
        bool searching = !string.IsNullOrWhiteSpace(SearchQuery);
        var query = SearchQuery?.Trim() ?? string.Empty;

        // 選択は ScheduleItem 側が持つため、ここでは検索の減光だけを反映する。
        // 仮想化で画面外のバーは TimelineBars から外れているため、全件を対象にする
        foreach (var bar in AllTimelineBars)
        {
            bar.IsDimmed = searching && !MatchesSearch(bar.Item, query);
        }
    }

    private static bool MatchesSearch(ScheduleItem item, string query) =>
        item.Title.Contains(query, StringComparison.OrdinalIgnoreCase) ||
        item.Content.Contains(query, StringComparison.OrdinalIgnoreCase);

    /// <summary>検索語が変わったときに呼ぶ（ヒットしたバーだけを残して他を減光する）</summary>
    private void OnSearchQueryChangedForTimeline()
    {
        if (CurrentViewMode != ViewMode.SprintTimeline) return;
        RefreshBarStates();
    }

    // ===== キーボード操作（タイムライン固有） =====

    /// <summary>選択中のアイテムが画面に収まるようズームする</summary>
    public RelayCommand TimelineFitToSelectionCommand => _timelineFitToSelectionCommand ??=
        new RelayCommand(_ =>
        {
            if (_selectedItem == null) return;
            TimelineFitToItemRequested?.Invoke(this, _selectedItem);
        });
    private RelayCommand? _timelineFitToSelectionCommand;

    // ===== レイアウト計算 =====

    /// <summary>
    /// タイムラインビューのバー配置を計算する。
    /// 表示モードが SprintTimeline でないときは空にして早期に抜ける。
    /// </summary>
    private void UpdateTimelineItems()
    {
        if (CurrentViewMode != ViewMode.SprintTimeline || TimelineSprints.Count == 0)
        {
            ClearTimeline();
            return;
        }

        var origin = TimelineSprints[0].StartDate.Date;
        var end = TimelineSprints[^1].EndDate.Date.AddDays(1);
        var scale = new TimelineScale(origin, end, _timelinePixelsPerDay);
        _timelineScale = scale;

        var items = ScheduleItems
            .Where(IsItemVisible)
            .Where(x => x.EndTime >= origin && x.StartTime < end)
            .OrderBy(x => x.StartTime)
            .ToList();

        // ドラッグ中は時間軸そのものは動かないので、目盛り・背景・スプリント帯は作り直さない。
        // （マウス移動のたびに数百〜数千要素を再生成するのを避ける）
        if (!_isTimelineDragging)
        {
            BuildSprintBands(scale, items);
            UpdateTimelineDecorations(scale, items);
        }

        IsTimelineEmpty = items.Count == 0;

        // 選択中のアイテムが表示範囲から外れたら選択を解除する
        if (_selectedItem != null && !items.Contains(_selectedItem))
        {
            _selectedItem = null;
            OnPropertyChanged(nameof(SelectedItem));
        }

        if (items.Count == 0)
        {
            SetTimelineSources([], _allTicks, _allDayColumns, _allDensityBars);
            TimelineLaneGroups = [];
            _lastLanes = null;
            TimelineContentWidth = Math.Max(scale.TotalWidth, 1);
            TimelineContentHeight = 200;
            return;
        }

        Dictionary<ScheduleItem, int> lanes;
        int laneCount;

        // ドラッグ中はレーンを組み替えない。
        // 掴んだバーが移動のたびに別の行へ飛ぶと狙った位置に置けないため、
        // 直前の割り当てをそのまま使い、確定時に組み直す。
        if (_isTimelineDragging && CanReuseLanes(items))
        {
            lanes = _lastLanes!;
            laneCount = _lastLaneCount;
        }
        else
        {
            lanes = new Dictionary<ScheduleItem, int>();

            switch (_timelineGroupMode)
            {
                case TimelineGroupMode.Flat:
                    for (int i = 0; i < items.Count; i++) lanes[items[i]] = i;
                    laneCount = items.Count;
                    TimelineLaneGroups = [];
                    break;

                case TimelineGroupMode.Category:
                    var groups = TimelineLaneHelper.AssignLanesByGroup(
                        items,
                        item => ResolveCategory(item)?.Name ?? "未分類",
                        scale,
                        lanes);
                    laneCount = groups.Count == 0 ? 0 : groups[^1].LaneOffset + groups[^1].LaneCount;
                    TimelineLaneGroups = BuildLaneGroups(groups);
                    break;

                default: // Packed
                    laneCount = TimelineLaneHelper.AssignLanes(items, scale, lanes);
                    TimelineLaneGroups = [];
                    break;
            }

            _lastLanes = lanes;
            _lastLaneCount = laneCount;
        }

        var bars = BuildBars(items, lanes, scale, origin, end);

        // ドラッグ中は時間軸が動かないので、バー以外は差し替えない
        if (_isTimelineDragging)
        {
            SetTimelineBarsOnly(bars);
        }
        else
        {
            SetTimelineSources(bars, _allTicks, _allDayColumns, _allDensityBars);
        }

        TimelineContentWidth = Math.Max(scale.TotalWidth, 1);
        TimelineContentHeight = Math.Max(200, (laneCount * TimelineBar.LaneHeight) + TimelineTopPadding + 12);

        // 新しく作ったバーへ選択・検索の状態を反映する
        RefreshBarStates();
    }

    /// <summary>直前のレーン割り当てが、今回のアイテム集合をすべて覆っているか</summary>
    private bool CanReuseLanes(List<ScheduleItem> items)
    {
        if (_lastLanes == null) return false;
        foreach (var item in items)
        {
            if (!_lastLanes.ContainsKey(item)) return false;
        }
        return true;
    }

    private Dictionary<ScheduleItem, int>? _lastLanes;
    private int _lastLaneCount;

    // ===== ドラッグ中のレイアウト抑制 =====

    private bool _isTimelineDragging;

    /// <summary>ドラッグ開始時にビューから呼ぶ。以降のレイアウトは軽量経路になる</summary>
    public void BeginTimelineDragLayout() => _isTimelineDragging = true;

    /// <summary>ドラッグ確定・取り消し時にビューから呼ぶ。レーンと装飾を組み直す</summary>
    public void EndTimelineDragLayout()
    {
        if (!_isTimelineDragging) return;
        _isTimelineDragging = false;
        UpdateTimelineItems();
    }

    /// <summary>バー領域の上端余白</summary>
    private const double TimelineTopPadding = 8.0;

    private void ClearTimeline()
    {
        _timelineScale = null;
        _allBars = [];
        _lastLanes = null;
        if (TimelineBars.Count > 0) TimelineBars = [];
        if (TimelineLaneGroups.Count > 0) TimelineLaneGroups = [];
        if (TimelineSprintBands.Count > 0) TimelineSprintBands = [];
        IsTimelineEmpty = true;
        ClearTimelineDecorations();
    }

    private void BuildSprintBands(TimelineScale scale, IReadOnlyList<ScheduleItem> items)
    {
        var today = DateTime.Today;
        var bands = new List<TimelineSprintBand>(TimelineSprints.Count);

        foreach (var sprint in TimelineSprints)
        {
            double x = scale.ToX(sprint.StartDate.Date);
            // 終了日を含めるため +1日。長さの違うスプリントが正しく異なる幅になる
            double width = scale.ToX(sprint.EndDate.Date.AddDays(1)) - x;

            var (summary, topCategory) = BuildSprintSummary(sprint, items);

            bands.Add(new TimelineSprintBand
            {
                Name = sprint.Name,
                RangeText = $"{sprint.StartDate:MM/dd} - {sprint.EndDate:MM/dd}",
                X = x,
                Width = Math.Max(width, 0),
                IsCurrent = today >= sprint.StartDate.Date && today <= sprint.EndDate.Date,
                SummaryText = summary,
                TopCategoryText = topCategory
            });
        }

        TimelineSprintBands = bands;
    }

    private static List<TimelineLaneGroup> BuildLaneGroups(
        List<(string Name, List<ScheduleItem> Items, int LaneOffset, int LaneCount)> groups)
    {
        var result = new List<TimelineLaneGroup>(groups.Count);

        for (int i = 0; i < groups.Count; i++)
        {
            var (name, groupItems, offset, count) = groups[i];

            double totalHours = groupItems.Sum(x => Math.Max(0, (x.EndTime - x.StartTime).TotalHours));
            var brush = groupItems.Count > 0 ? groupItems[0].BackgroundColor : Brushes.Transparent;

            result.Add(new TimelineLaneGroup
            {
                Name = name,
                Y = (offset * TimelineBar.LaneHeight) + TimelineTopPadding,
                Height = count * TimelineBar.LaneHeight,
                Brush = brush,
                TotalText = $"{totalHours:0.#}h",
                Count = groupItems.Count,
                IsAlternate = i % 2 == 1
            });
        }

        return result;
    }

    private static List<TimelineBar> BuildBars(
        List<ScheduleItem> items,
        Dictionary<ScheduleItem, int> lanes,
        TimelineScale scale,
        DateTime rangeStart,
        DateTime rangeEnd)
    {
        var bars = new List<TimelineBar>(items.Count);

        foreach (var item in items)
        {
            // 表示範囲からはみ出すアイテムは端で切り詰める（範囲外へ飛ばして隠す旧実装をやめる）
            var start = item.StartTime < rangeStart ? rangeStart : item.StartTime;
            var stop = item.EndTime > rangeEnd ? rangeEnd : item.EndTime;
            if (stop < start) stop = start;

            double x = scale.ToX(start);
            double width = scale.ToX(stop) - x;
            double drawWidth = Math.Max(width, TimelineBar.MinDrawWidth);

            double labelWidth = TimelineBar.EstimateLabelWidth(item.Title);

            bars.Add(new TimelineBar
            {
                Item = item,
                X = x,
                Y = (lanes[item] * TimelineBar.LaneHeight) + TimelineTopPadding,
                ActualWidth = width,
                Lane = lanes[item],
                IsLabelInside = labelWidth <= drawWidth,
                ToolTipText = item.ToolTipText
            });
        }

        return bars;
    }

}
