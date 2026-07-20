using System;
using System.Collections.Generic;
using System.Linq;
using System.Windows.Media;
using Brush = System.Windows.Media.Brush;
using Brushes = System.Windows.Media.Brushes;

using TimeRenderer.Helpers;
using TimeRenderer.Models;

namespace TimeRenderer.ViewModels;

/// <summary>
/// タイムラインビューの「時間軸の文脈」を描くための計算。
/// ルーラーの目盛り、背景の罫線とシェード、現在時刻ライン、範囲外インジケータ、
/// 最下部の密度ヒートバーを扱う。
/// </summary>
public partial class MainViewModel
{
    // ===== ルーラー下段の目盛り =====

    private IReadOnlyList<TimelineTick> _timelineTicks = [];
    public IReadOnlyList<TimelineTick> TimelineTicks
    {
        get => _timelineTicks;
        private set => SetProperty(ref _timelineTicks, value);
    }

    /// <summary>
    /// 目盛りの粒度を決める。1目盛りが狭くなりすぎるとラベルが潰れるので、
    /// ズーム倍率に応じて 時刻 → 日 → 週 → 月 と粗くしていく。
    /// </summary>
    private enum TickUnit { Hour3, Day, Week, Month }

    private static TickUnit GetTickUnit(double pixelsPerDay) => pixelsPerDay switch
    {
        >= 300 => TickUnit.Hour3,   // 3時間 = 37px以上
        >= 70 => TickUnit.Day,      // 1日 = 70px以上
        >= 22 => TickUnit.Week,     // 1週 = 154px以上
        _ => TickUnit.Month
    };

    /// <summary>
    /// 目盛りの生成上限。Canvas は仮想化しないため、目盛りが多すぎると
    /// 画面外のぶんまで実体化して描画が重くなる。超える場合は1段階粗い粒度へ落とす。
    ///
    /// 既定の表示範囲は5スプリント（約105日）で、最大ズーム時の3時間刻みは約840個になる。
    /// これを弾いてしまうと時刻目盛りが一度も出せなくなるため、上限はそれより上に置く。
    /// </summary>
    private const int MaxTickCount = 1200;

    private static TickUnit LimitTickUnit(TickUnit unit, TimelineScale scale)
    {
        double totalDays = (scale.End - scale.Origin).TotalDays;

        while (unit != TickUnit.Month)
        {
            double estimated = unit switch
            {
                TickUnit.Hour3 => totalDays * 8,
                TickUnit.Day => totalDays,
                _ => totalDays / 7
            };

            if (estimated <= MaxTickCount) break;
            unit++;
        }

        return unit;
    }

    private void BuildTicks(TimelineScale scale)
    {
        var unit = LimitTickUnit(GetTickUnit(scale.PixelsPerDay), scale);
        var today = DateTime.Today;
        var ticks = new List<TimelineTick>();

        switch (unit)
        {
            case TickUnit.Hour3:
                for (var d = scale.Origin; d < scale.End; d = d.AddHours(3))
                {
                    double x = scale.ToX(d);
                    ticks.Add(new TimelineTick
                    {
                        // 0時だけは日付を出して、日の切れ目が分かるようにする
                        Label = d.Hour == 0 ? $"{d:M/d}" : $"{d.Hour}",
                        X = x,
                        Width = scale.ToX(d.AddHours(3)) - x,
                        IsEmphasized = d.Hour == 0,
                        IsToday = d.Date == today
                    });
                }
                break;

            case TickUnit.Day:
                for (var d = scale.Origin; d < scale.End; d = d.AddDays(1))
                {
                    double x = scale.ToX(d);
                    ticks.Add(new TimelineTick
                    {
                        Label = $"{d.Day}({Converters.DateTimeHelper.GetShortDayOfWeek(d)})",
                        X = x,
                        Width = scale.ToX(d.AddDays(1)) - x,
                        IsEmphasized = d.DayOfWeek == DayOfWeek.Monday,
                        IsToday = d.Date == today
                    });
                }
                break;

            case TickUnit.Week:
                // 週の区切り（月曜）に揃える
                var weekStart = Converters.DateTimeHelper.GetStartOfWeek(scale.Origin);
                for (var d = weekStart; d < scale.End; d = d.AddDays(7))
                {
                    double x = scale.ToX(d);
                    ticks.Add(new TimelineTick
                    {
                        Label = $"{d:M/d}週",
                        X = x,
                        Width = scale.ToX(d.AddDays(7)) - x,
                        IsEmphasized = true,
                        IsToday = today >= d && today < d.AddDays(7)
                    });
                }
                break;

            default: // Month
                var monthStart = new DateTime(scale.Origin.Year, scale.Origin.Month, 1);
                for (var d = monthStart; d < scale.End; d = d.AddMonths(1))
                {
                    double x = scale.ToX(d);
                    ticks.Add(new TimelineTick
                    {
                        Label = $"{d:yyyy/MM}",
                        X = x,
                        Width = scale.ToX(d.AddMonths(1)) - x,
                        IsEmphasized = true,
                        IsToday = today.Year == d.Year && today.Month == d.Month
                    });
                }
                break;
        }

        _allTicks = ticks;
    }

    // ===== 背景の日単位の列 =====

    private IReadOnlyList<TimelineDayColumn> _timelineDayColumns = [];
    public IReadOnlyList<TimelineDayColumn> TimelineDayColumns
    {
        get => _timelineDayColumns;
        private set => SetProperty(ref _timelineDayColumns, value);
    }

    /// <summary>これ未満のズームでは日境界の罫線を間引く（線だらけになるのを防ぐ）</summary>
    private const double DayLineMinPixelsPerDay = 18.0;

    private void BuildDayColumns(TimelineScale scale)
    {
        var today = DateTime.Today;
        bool showDayLines = scale.PixelsPerDay >= DayLineMinPixelsPerDay;
        var columns = new List<TimelineDayColumn>();

        for (var d = scale.Origin; d < scale.End; d = d.AddDays(1))
        {
            bool isWeekend = d.DayOfWeek is DayOfWeek.Saturday or DayOfWeek.Sunday;
            bool isDisabled = !EnabledDaysOfWeek.Contains(d.DayOfWeek);
            bool isToday = d.Date == today;

            // 優先順位: 今日 > 無効曜日 > 土日 > 通常
            string kind = isToday ? "Today"
                        : isDisabled ? "Disabled"
                        : isWeekend ? "Weekend"
                        : "Normal";

            double x = scale.ToX(d);
            columns.Add(new TimelineDayColumn
            {
                Date = d,
                X = x,
                Width = scale.ToX(d.AddDays(1)) - x,
                Kind = kind,
                IsWeekStart = d.DayOfWeek == DayOfWeek.Monday,
                ShowDayLine = showDayLines || d.DayOfWeek == DayOfWeek.Monday
            });
        }

        _allDayColumns = columns;
    }

    // ===== 現在時刻ライン =====

    private double _timelineNowX;
    public double TimelineNowX
    {
        get => _timelineNowX;
        private set
        {
            if (SetProperty(ref _timelineNowX, value))
            {
                OnPropertyChanged(nameof(TimelineNowMargin));
            }
        }
    }

    /// <summary>現在時刻ラインの配置用マージン（HorizontalAlignment=Left の要素に使う）</summary>
    public System.Windows.Thickness TimelineNowMargin => new(_timelineNowX, 0, 0, 0);

    private bool _isTimelineNowVisible;
    /// <summary>現在時刻が表示範囲に入っているか</summary>
    public bool IsTimelineNowVisible
    {
        get => _isTimelineNowVisible;
        private set => SetProperty(ref _isTimelineNowVisible, value);
    }

    private string _timelineNowText = string.Empty;
    public string TimelineNowText
    {
        get => _timelineNowText;
        private set => SetProperty(ref _timelineNowText, value);
    }

    /// <summary>現在時刻ラインを最後に更新した分（1分に1回だけ動かすための間引き用）</summary>
    private DateTime _lastNowLineUpdate = DateTime.MinValue;

    /// <summary>
    /// 時計タイマーから毎回呼ばれる。位置の更新は1分に1回で十分なので間引く。
    /// </summary>
    public void UpdateTimelineNowLine(DateTime now)
    {
        if (CurrentViewMode != ViewMode.SprintTimeline)
        {
            if (IsTimelineNowVisible) IsTimelineNowVisible = false;
            return;
        }

        if (now - _lastNowLineUpdate < TimeSpan.FromSeconds(30)) return;
        _lastNowLineUpdate = now;

        RefreshNowLine(now);
    }

    private void RefreshNowLine(DateTime now)
    {
        var scale = _timelineScale;
        if (scale == null || now < scale.Origin || now >= scale.End)
        {
            IsTimelineNowVisible = false;
            return;
        }

        TimelineNowX = scale.ToX(now);
        TimelineNowText = now.ToString("HH:mm");
        IsTimelineNowVisible = true;
    }

    // ===== 範囲外インジケータ =====

    private int _timelineOverflowBefore;
    /// <summary>表示範囲より前にあるアイテム数</summary>
    public int TimelineOverflowBefore
    {
        get => _timelineOverflowBefore;
        private set
        {
            if (SetProperty(ref _timelineOverflowBefore, value))
            {
                OnPropertyChanged(nameof(HasTimelineOverflowBefore));
                OnPropertyChanged(nameof(TimelineOverflowBeforeText));
            }
        }
    }

    public bool HasTimelineOverflowBefore => _timelineOverflowBefore > 0;
    public string TimelineOverflowBeforeText => $"◀ {_timelineOverflowBefore}件";

    private int _timelineOverflowAfter;
    /// <summary>表示範囲より後にあるアイテム数</summary>
    public int TimelineOverflowAfter
    {
        get => _timelineOverflowAfter;
        private set
        {
            if (SetProperty(ref _timelineOverflowAfter, value))
            {
                OnPropertyChanged(nameof(HasTimelineOverflowAfter));
                OnPropertyChanged(nameof(TimelineOverflowAfterText));
            }
        }
    }

    public bool HasTimelineOverflowAfter => _timelineOverflowAfter > 0;
    public string TimelineOverflowAfterText => $"{_timelineOverflowAfter}件 ▶";

    /// <summary>範囲外の直近アイテムへ移動する（パラメータ "before" / "after"）</summary>
    public RelayCommand TimelineJumpOverflowCommand => _timelineJumpOverflowCommand ??=
        new RelayCommand(param =>
        {
            var scale = _timelineScale;
            if (scale == null) return;

            bool before = (param as string) == "before";

            var target = before
                ? ScheduleItems.Where(IsItemVisible).Where(x => x.StartTime < scale.Origin)
                               .OrderByDescending(x => x.StartTime).FirstOrDefault()
                : ScheduleItems.Where(IsItemVisible).Where(x => x.StartTime >= scale.End)
                               .OrderBy(x => x.StartTime).FirstOrDefault();

            if (target == null) return;

            TransitionDirection = before
                ? Controls.TransitionDirection.Backward
                : Controls.TransitionDirection.Forward;
            CurrentDate = target.StartTime.Date;
            SelectAndReveal(target);
        });
    private RelayCommand? _timelineJumpOverflowCommand;

    private void UpdateOverflowCounts(TimelineScale scale)
    {
        int before = 0, after = 0;
        foreach (var item in ScheduleItems)
        {
            if (!IsItemVisible(item)) continue;
            if (item.EndTime < scale.Origin) before++;
            else if (item.StartTime >= scale.End) after++;
        }
        TimelineOverflowBefore = before;
        TimelineOverflowAfter = after;
    }

    // ===== 密度ヒートバー =====

    private IReadOnlyList<TimelineDensityBar> _timelineDensityBars = [];
    public IReadOnlyList<TimelineDensityBar> TimelineDensityBars
    {
        get => _timelineDensityBars;
        private set => SetProperty(ref _timelineDensityBars, value);
    }

    /// <summary>ヒートバー帯の高さ（XAML がバインドするためインスタンスプロパティ）</summary>
    public double TimelineDensityHeight { get; } = 34.0;

    private void BuildDensityBars(TimelineScale scale, IReadOnlyList<ScheduleItem> items)
    {
        const double maxBarHeight = 26.0;

        // 日ごとの記録時間を集計する（日をまたぐアイテムは日単位に按分する）
        var perDay = new Dictionary<DateTime, double>();
        foreach (var item in items)
        {
            if (item.IsAllDay) continue;

            var start = item.StartTime < scale.Origin ? scale.Origin : item.StartTime;
            var stop = item.EndTime > scale.End ? scale.End : item.EndTime;
            if (stop <= start) continue;

            for (var d = start.Date; d < stop; d = d.AddDays(1))
            {
                var segStart = d < start ? start : d;
                var segEnd = stop < d.AddDays(1) ? stop : d.AddDays(1);
                if (segEnd <= segStart) continue;

                perDay.TryGetValue(d, out double hours);
                perDay[d] = hours + (segEnd - segStart).TotalHours;
            }
        }

        if (perDay.Count == 0)
        {
            _allDensityBars = [];
            return;
        }

        double max = perDay.Values.Max();
        if (max <= 0) max = 1;

        var bars = new List<TimelineDensityBar>(perDay.Count);
        foreach (var kv in perDay.OrderBy(k => k.Key))
        {
            double x = scale.ToX(kv.Key);
            double width = scale.ToX(kv.Key.AddDays(1)) - x;

            bars.Add(new TimelineDensityBar
            {
                X = x,
                // 隙間を作って棒として見えるようにする（幅が狭いときは詰める）
                Width = Math.Max(1, width - Math.Min(2, width * 0.2)),
                BarHeight = Math.Max(1, kv.Value / max * maxBarHeight),
                ToolTipText = $"{kv.Key:MM/dd (ddd)}  {kv.Value:0.#} 時間",
                Brush = Brushes.Transparent
            });
        }

        _allDensityBars = bars;
    }

    // ===== スプリントごとのサマリー =====

    /// <summary>スプリント期間に含まれるアイテムから、合計時間と上位カテゴリの表示文字列を作る</summary>
    private (string Summary, string TopCategory) BuildSprintSummary(SprintInfo sprint, IReadOnlyList<ScheduleItem> items)
    {
        var rangeStart = sprint.StartDate.Date;
        var rangeEnd = sprint.EndDate.Date.AddDays(1);

        double total = 0;
        int count = 0;
        var byCategory = new Dictionary<string, double>();

        foreach (var item in items)
        {
            if (item.IsAllDay) continue;

            var start = item.StartTime < rangeStart ? rangeStart : item.StartTime;
            var stop = item.EndTime > rangeEnd ? rangeEnd : item.EndTime;
            if (stop <= start) continue;

            double hours = (stop - start).TotalHours;
            total += hours;
            count++;

            var name = ResolveCategory(item)?.Name ?? "未分類";
            byCategory.TryGetValue(name, out double h);
            byCategory[name] = h + hours;
        }

        if (count == 0) return ("記録なし", string.Empty);

        string summary = $"{total:0.#}h ・ {count}件";

        var top = byCategory.OrderByDescending(kv => kv.Value).First();
        string topText = total > 0 ? $"{top.Key} {top.Value / total * 100:0}%" : string.Empty;

        return (summary, topText);
    }

    // ===== 装飾のまとめて更新 =====

    /// <summary>UpdateTimelineItems の末尾から呼ぶ。時間軸の文脈まわりを一括で組み直す</summary>
    private void UpdateTimelineDecorations(TimelineScale scale, IReadOnlyList<ScheduleItem> items)
    {
        BuildTicks(scale);
        BuildDayColumns(scale);
        BuildDensityBars(scale, items);
        UpdateOverflowCounts(scale);
        RefreshNowLine(DateTime.Now);
    }

    private void ClearTimelineDecorations()
    {
        _allTicks = [];
        _allDayColumns = [];
        _allDensityBars = [];
        if (TimelineTicks.Count > 0) TimelineTicks = [];
        if (TimelineDayColumns.Count > 0) TimelineDayColumns = [];
        if (TimelineDensityBars.Count > 0) TimelineDensityBars = [];
        TimelineOverflowBefore = 0;
        TimelineOverflowAfter = 0;
        IsTimelineNowVisible = false;
    }
}
