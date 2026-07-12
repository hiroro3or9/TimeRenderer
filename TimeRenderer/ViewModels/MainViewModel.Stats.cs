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
/// 統計ビュー：期間内の作業時間をカテゴリ別・日別に集計する。
/// </summary>
public partial class MainViewModel
{
    public enum StatsPeriodMode
    {
        Week,
        Month,
        Sprint
    }

    /// <summary>日別チャートのバー描画領域の高さ(px)</summary>
    private const double DailyChartHeight = 180;

    private StatsPeriodMode _statsPeriod = StatsPeriodMode.Week;
    public StatsPeriodMode StatsPeriod
    {
        get => _statsPeriod;
        set
        {
            if (SetProperty(ref _statsPeriod, value))
            {
                OnPropertyChanged(nameof(IsStatsWeekPeriod));
                OnPropertyChanged(nameof(IsStatsMonthPeriod));
                OnPropertyChanged(nameof(IsStatsSprintPeriod));
                OnPropertyChanged(nameof(DateDisplay));
                UpdateStats();
            }
        }
    }

    public bool IsStatsWeekPeriod => StatsPeriod == StatsPeriodMode.Week;
    public bool IsStatsMonthPeriod => StatsPeriod == StatsPeriodMode.Month;
    public bool IsStatsSprintPeriod => StatsPeriod == StatsPeriodMode.Sprint;

    public ICommand ChangeStatsPeriodCommand { get; private set; } = null!;

    private void InitializeStatsCommands()
    {
        ChangeStatsPeriodCommand = new RelayCommand(param =>
        {
            if (param is string s && Enum.TryParse<StatsPeriodMode>(s, out var mode))
            {
                StatsPeriod = mode;
            }
        });
    }

    /// <summary>カテゴリ別集計の1行分</summary>
    public record CategoryStat(string Name, Brush Brush, double Hours, double MaxHours)
    {
        public string HoursText => FormatHours(Hours);
        public string PercentText { get; init; } = "";
    }

    /// <summary>日別チャートの1セグメント（1カテゴリ分の積み上げ要素）</summary>
    public record DailyStatSegment(Brush Brush, double HeightPx, string ToolTipText);

    /// <summary>日別チャートの1日分</summary>
    public record DailyStat(DateTime Date, string Label, bool IsToday, double TotalHours, IReadOnlyList<DailyStatSegment> Segments)
    {
        public string TotalText => TotalHours > 0 ? FormatHours(TotalHours) : "";
    }

    private IReadOnlyList<CategoryStat> _statsCategoryItems = [];
    public IReadOnlyList<CategoryStat> StatsCategoryItems
    {
        get => _statsCategoryItems;
        private set => SetProperty(ref _statsCategoryItems, value);
    }

    private IReadOnlyList<DailyStat> _statsDailyItems = [];
    public IReadOnlyList<DailyStat> StatsDailyItems
    {
        get => _statsDailyItems;
        private set => SetProperty(ref _statsDailyItems, value);
    }

    private string _statsSummaryText = "";
    public string StatsSummaryText
    {
        get => _statsSummaryText;
        private set => SetProperty(ref _statsSummaryText, value);
    }

    private bool _hasStatsData;
    public bool HasStatsData
    {
        get => _hasStatsData;
        private set => SetProperty(ref _hasStatsData, value);
    }

    internal static string FormatHours(double hours)
    {
        var span = TimeSpan.FromHours(hours);
        return $"{(int)span.TotalHours}:{span.Minutes:D2}";
    }

    /// <summary>統計対象期間 [start, end) を取得する</summary>
    private (DateTime Start, DateTime End) GetStatsRange()
    {
        switch (StatsPeriod)
        {
            case StatsPeriodMode.Month:
                var first = new DateTime(CurrentDate.Year, CurrentDate.Month, 1);
                return (first, first.AddMonths(1));
            case StatsPeriodMode.Sprint:
                var sprint = SprintHelper.GetSprintForDate(ManualSprints, CurrentDate);
                return (sprint.StartDate.Date, sprint.EndDate.Date.AddDays(1));
            default: // Week
                var weekStart = Converters.DateTimeHelper.GetStartOfWeek(CurrentDate);
                return (weekStart, weekStart.AddDays(7));
        }
    }

    /// <summary>統計ビュー用の期間表示文字列</summary>
    private string GetStatsRangeDisplay()
    {
        var (start, end) = GetStatsRange();
        var last = end.AddDays(-1);
        var label = StatsPeriod switch
        {
            StatsPeriodMode.Month => "月",
            StatsPeriodMode.Sprint => SprintHelper.GetSprintForDate(ManualSprints, CurrentDate).Name,
            _ => "週"
        };
        return $"統計 [{label}] {start:yyyy/MM/dd} - {last:MM/dd}";
    }

    /// <summary>
    /// 期間内の作業時間を集計して統計ビュー用のコレクションを更新する。
    /// 終日イベントは時間の集計対象から除外する。
    /// </summary>
    private void UpdateStats()
    {
        if (CurrentViewMode != ViewMode.Stats) return;

        var (rangeStart, rangeEnd) = GetStatsRange();

        // colorCode -> 合計時間 / 日付 -> (colorCode -> 時間)
        var categoryTotals = new Dictionary<string, double>();
        var dailyTotals = new Dictionary<DateTime, Dictionary<string, double>>();
        int itemCount = 0;

        foreach (var item in ScheduleItems)
        {
            if (item.IsAllDay) continue;

            // 期間でクリップ
            var start = item.StartTime < rangeStart ? rangeStart : item.StartTime;
            var end = item.EndTime > rangeEnd ? rangeEnd : item.EndTime;
            if (end <= start) continue;

            itemCount++;
            var code = item.ColorCode;

            // 日単位に分割して集計（日またぎ対応）
            for (var d = start.Date; d < end; d = d.AddDays(1))
            {
                var segStart = d > start ? d : start;
                var segEnd = end < d.AddDays(1) ? end : d.AddDays(1);
                if (segEnd <= segStart) continue;

                var hours = (segEnd - segStart).TotalHours;
                categoryTotals[code] = categoryTotals.GetValueOrDefault(code) + hours;

                if (!dailyTotals.TryGetValue(d, out var perDay))
                {
                    perDay = [];
                    dailyTotals[d] = perDay;
                }
                perDay[code] = perDay.GetValueOrDefault(code) + hours;
            }
        }

        // カテゴリ表示順：登録順 → 未登録色（時間の多い順）
        var orderedCodes = Categories.Select(c => c.ColorCode)
            .Where(categoryTotals.ContainsKey)
            .Concat(categoryTotals.Keys
                .Where(code => Categories.All(c => c.ColorCode != code))
                .OrderByDescending(code => categoryTotals[code]))
            .Distinct()
            .ToList();

        var grandTotal = categoryTotals.Values.Sum();
        var maxCategoryHours = categoryTotals.Count > 0 ? categoryTotals.Values.Max() : 0;

        StatsCategoryItems = [.. orderedCodes.Select(code =>
        {
            var hours = categoryTotals[code];
            var percent = grandTotal > 0 ? hours / grandTotal * 100 : 0;
            return new CategoryStat(GetCategoryName(code), CategoryInfo.CreateBrush(code), hours, Math.Max(maxCategoryHours, 0.001))
            {
                PercentText = $"{percent:0.#}%"
            };
        })];

        // 日別積み上げチャート
        var maxDayHours = dailyTotals.Count > 0 ? dailyTotals.Values.Max(d => d.Values.Sum()) : 0;
        var dailyStats = new List<DailyStat>();
        var totalDays = (rangeEnd - rangeStart).Days;
        var compact = totalDays > 14; // 月表示などは日付ラベルを短縮

        for (var d = rangeStart; d < rangeEnd; d = d.AddDays(1))
        {
            dailyTotals.TryGetValue(d, out var perDay);
            var dayTotal = perDay?.Values.Sum() ?? 0;

            var segments = new List<DailyStatSegment>();
            if (perDay != null && maxDayHours > 0)
            {
                // StackPanel(上→下)で下端揃えのため、表示順の逆順で積む（先頭カテゴリが一番下）
                foreach (var code in orderedCodes.AsEnumerable().Reverse())
                {
                    if (!perDay.TryGetValue(code, out var hours) || hours <= 0) continue;
                    var height = hours / maxDayHours * DailyChartHeight;
                    segments.Add(new DailyStatSegment(
                        CategoryInfo.CreateBrush(code),
                        Math.Max(height, 2),
                        $"{GetCategoryName(code)}: {FormatHours(hours)}"));
                }
            }

            var label = compact ? d.Day.ToString() : d.ToString("M/d(ddd)");
            dailyStats.Add(new DailyStat(d, label, d.Date == DateTime.Today, dayTotal, segments));
        }
        StatsDailyItems = dailyStats;

        HasStatsData = grandTotal > 0;
        StatsSummaryText = $"合計 {FormatHours(grandTotal)} ／ {itemCount} 件";
    }
}
