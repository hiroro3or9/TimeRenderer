using System;
using System.Collections.Generic;
using System.Linq;
using System.Windows.Input;
using System.Windows.Media;
using Brush = System.Windows.Media.Brush;

using TimeRenderer.Models;
using TimeRenderer.Helpers;
using Clipboard = System.Windows.Clipboard;

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
    public ICommand CopyTimesheetHoursCommand { get; private set; } = null!;
    public ICommand CopyTimesheetProjectCodeCommand { get; private set; } = null!;

    private void InitializeStatsCommands()
    {
        ChangeStatsPeriodCommand = new RelayCommand(param =>
        {
            if (param is string s && Enum.TryParse<StatsPeriodMode>(s, out var mode))
            {
                StatsPeriod = mode;
            }
        });

        CopyTimesheetHoursCommand = new RelayCommand(
            param =>
            {
                if (param is TimesheetMatrixCell { HasValue: true } cell)
                    CopyTimesheetValue(cell.CopyValue);
            },
            param => param is TimesheetMatrixCell { HasValue: true });

        CopyTimesheetProjectCodeCommand = new RelayCommand(
            param =>
            {
                if (param is TimesheetMatrixRow { CopyValue.Length: > 0 } row)
                    CopyTimesheetValue(row.CopyValue);
            },
            param => param is TimesheetMatrixRow { CopyValue.Length: > 0 });
    }

    private void CopyTimesheetValue(string value)
    {
        try
        {
            Clipboard.SetText(value);
            TimesheetCopyStatusText = $"{value} をコピーしました";
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"Copy timesheet value failed: {ex.Message}");
            TimesheetCopyStatusText = "コピーできませんでした";
            _dialogService.ShowMessage(
                "クリップボードへコピーできませんでした。少し待ってからもう一度お試しください。",
                "コピーエラー");
        }
    }

    /// <summary>カテゴリ別集計の1行分</summary>
    public record CategoryStat(string Name, Brush Brush, double Hours, double MaxHours)
    {
        public string HoursText => StatsFormatting.FormatHours(Hours);
        public string PercentText { get; init; } = "";
    }

    /// <summary>プロジェクトコード別集計の1行分</summary>
    public record ProjectCodeStat(string DisplayName, double Hours, double MaxHours)
    {
        public string HoursText => StatsFormatting.FormatHours(Hours);
        public string PercentText { get; init; } = "";
    }

    /// <summary>日別チャートの1セグメント（1カテゴリ分の積み上げ要素）</summary>
    public record DailyStatSegment(Brush Brush, double HeightPx, string ToolTipText);

    /// <summary>日別チャートの1日分</summary>
    public record DailyStat(DateTime Date, string Label, bool IsToday, double TotalHours, IReadOnlyList<DailyStatSegment> Segments)
    {
        public string TotalText => TotalHours > 0 ? StatsFormatting.FormatHours(TotalHours) : "";
    }

    private IReadOnlyList<CategoryStat> _statsCategoryItems = [];
    public IReadOnlyList<CategoryStat> StatsCategoryItems
    {
        get => _statsCategoryItems;
        private set => SetProperty(ref _statsCategoryItems, value);
    }

    private IReadOnlyList<ProjectCodeStat> _statsProjectCodeItems = [];
    public IReadOnlyList<ProjectCodeStat> StatsProjectCodeItems
    {
        get => _statsProjectCodeItems;
        private set => SetProperty(ref _statsProjectCodeItems, value);
    }

    private IReadOnlyList<TimesheetMatrixDateColumn> _statsTimesheetMatrixColumns = [];
    public IReadOnlyList<TimesheetMatrixDateColumn> StatsTimesheetMatrixColumns
    {
        get => _statsTimesheetMatrixColumns;
        private set => SetProperty(ref _statsTimesheetMatrixColumns, value);
    }

    private IReadOnlyList<TimesheetMatrixRow> _statsTimesheetMatrixRows = [];
    public IReadOnlyList<TimesheetMatrixRow> StatsTimesheetMatrixRows
    {
        get => _statsTimesheetMatrixRows;
        private set => SetProperty(ref _statsTimesheetMatrixRows, value);
    }

    private IReadOnlyList<TimesheetMatrixCell> _statsTimesheetMatrixTotalCells = [];
    public IReadOnlyList<TimesheetMatrixCell> StatsTimesheetMatrixTotalCells
    {
        get => _statsTimesheetMatrixTotalCells;
        private set => SetProperty(ref _statsTimesheetMatrixTotalCells, value);
    }

    private string _statsTimesheetMatrixGrandTotalText = "";
    public string StatsTimesheetMatrixGrandTotalText
    {
        get => _statsTimesheetMatrixGrandTotalText;
        private set => SetProperty(ref _statsTimesheetMatrixGrandTotalText, value);
    }

    private string _timesheetCopyStatusText = "";
    public string TimesheetCopyStatusText
    {
        get => _timesheetCopyStatusText;
        private set => SetProperty(ref _timesheetCopyStatusText, value);
    }

    private IReadOnlyList<DailyStat> _statsDailyItems = [];
    public IReadOnlyList<DailyStat> StatsDailyItems
    {
        get => _statsDailyItems;
        private set => SetProperty(ref _statsDailyItems, value);
    }

    private IReadOnlyList<WorkDayNote> _statsNoteItems = [];
    /// <summary>
    /// 期間内のふりかえり（新しい日が上）。
    /// 全期間を読み返すのは「ふりかえり」ビュー（MainViewModel.Notes.cs）の役目で、
    /// ここはあくまで「この週／月はどうだったか」を数字の隣で振り返るためのもの。
    /// </summary>
    public IReadOnlyList<WorkDayNote> StatsNoteItems
    {
        get => _statsNoteItems;
        private set
        {
            if (SetProperty(ref _statsNoteItems, value)) OnPropertyChanged(nameof(HasStatsNotes));
        }
    }

    /// <summary>この期間にふりかえりが1件でもあるか（見出しごと出し分ける）</summary>
    public bool HasStatsNotes => StatsNoteItems.Count > 0;

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

    private bool _hasRecordedStatsData;
    /// <summary>カテゴリ別・日別へ表示する実績があるか。</summary>
    public bool HasRecordedStatsData
    {
        get => _hasRecordedStatsData;
        private set => SetProperty(ref _hasRecordedStatsData, value);
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

        TimesheetCopyStatusText = "";

        var (rangeStart, rangeEnd) = GetStatsRange();

        var aggregation = StatsAggregationHelper.Aggregate(
            ScheduleItems,
            Categories,
            ProjectCodes,
            rangeStart,
            rangeEnd);
        var categoryTotals = aggregation.CategoryTotals;
        var projectCodeTotals = aggregation.ProjectCodeTotals;
        var dailyTotals = aggregation.DailyCategoryTotals;
        var dailyProjectCodeTotals = aggregation.DailyProjectCodeTotals;
        var projectCodeDisplayNames = aggregation.ProjectCodeDisplayNames;
        var displayInfo = aggregation.CategoryDisplayInfo.ToDictionary(
            pair => pair.Key,
            pair => (pair.Value.Name, CategoryInfo.CreateBrush(pair.Value.ColorCode)));
        var itemCount = aggregation.ItemCount;

        AddUnrecordedTimeToProjectStats(
            rangeStart,
            rangeEnd,
            projectCodeTotals,
            dailyProjectCodeTotals,
            projectCodeDisplayNames);

        // カテゴリ表示順：登録順 → 未分類（時間の多い順）
        var orderedKeys = Categories.Select(c => c.Id)
            .Where(categoryTotals.ContainsKey)
            .Concat(categoryTotals.Keys
                .Where(key => Categories.All(c => c.Id != key))
                .OrderByDescending(key => categoryTotals[key]))
            .Distinct()
            .ToList();

        var grandTotal = categoryTotals.Values.Sum();
        var maxCategoryHours = categoryTotals.Count > 0 ? categoryTotals.Values.Max() : 0;

        // プロジェクトコード表示順：マスターの登録順 → 未設定・不明（時間の多い順）
        var orderedProjectKeys = ProjectCodes.Select(p => p.Id)
            .Where(projectCodeTotals.ContainsKey)
            .Concat(projectCodeTotals.Keys
                .Where(key => ProjectCodes.All(p => p.Id != key))
                .OrderByDescending(key => projectCodeTotals[key]))
            .Distinct()
            .ToList();
        var maxProjectHours = projectCodeTotals.Count > 0 ? projectCodeTotals.Values.Max() : 0;
        var projectGrandTotal = projectCodeTotals.Values.Sum();

        StatsProjectCodeItems = [.. orderedProjectKeys.Select(key =>
        {
            var hours = projectCodeTotals[key];
            var percent = projectGrandTotal > 0 ? hours / projectGrandTotal * 100 : 0;
            return new ProjectCodeStat(
                projectCodeDisplayNames[key], hours, Math.Max(maxProjectHours, 0.001))
            {
                PercentText = $"{percent:0.#}%"
            };
        })];

        // 月次タイムシート用：日・プロジェクトコード単位で合算してから15分単位へ丸める。
        // 個々の記録を先に丸めると、細切れの記録が多い日に誤差が積み上がるため合算後に行う。
        if (StatsPeriod == StatsPeriodMode.Month)
        {
            var matrix = StatsTimesheetBuilder.Build(
                rangeStart,
                rangeEnd,
                orderedProjectKeys,
                dailyProjectCodeTotals,
                ProjectCodes);
            StatsTimesheetMatrixColumns = matrix.Columns;
            StatsTimesheetMatrixRows = matrix.Rows;
            StatsTimesheetMatrixTotalCells = matrix.TotalCells;
            StatsTimesheetMatrixGrandTotalText = matrix.GrandTotalText;
        }
        else
        {
            StatsTimesheetMatrixColumns = [];
            StatsTimesheetMatrixRows = [];
            StatsTimesheetMatrixTotalCells = [];
            StatsTimesheetMatrixGrandTotalText = "";
        }

        StatsCategoryItems = [.. orderedKeys.Select(key =>
        {
            var hours = categoryTotals[key];
            var percent = grandTotal > 0 ? hours / grandTotal * 100 : 0;
            var (name, brush) = displayInfo[key];
            return new CategoryStat(name, brush, hours, Math.Max(maxCategoryHours, 0.001))
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
                foreach (var key in orderedKeys.AsEnumerable().Reverse())
                {
                    if (!perDay.TryGetValue(key, out var hours) || hours <= 0) continue;
                    var height = hours / maxDayHours * DailyChartHeight;
                    var (name, brush) = displayInfo[key];
                    segments.Add(new DailyStatSegment(
                        brush,
                        Math.Max(height, 2),
                        $"{name}: {StatsFormatting.FormatHours(hours)}"));
                }
            }

            var label = compact ? d.Day.ToString() : d.ToString("M/d(ddd)");
            dailyStats.Add(new DailyStat(d, label, d.Date == DateTime.Today, dayTotal, segments));
        }
        StatsDailyItems = dailyStats;
        StatsNoteItems = BuildStatsNoteItems(rangeStart, rangeEnd);

        HasRecordedStatsData = grandTotal > 0;
        HasStatsData = grandTotal > 0 || projectGrandTotal > 0;
        StatsSummaryText = projectGrandTotal > grandTotal + 0.000001
            ? $"記録 {StatsFormatting.FormatHours(grandTotal)} ／ {itemCount} 件（プロジェクト集計 {StatsFormatting.FormatHours(projectGrandTotal)}）"
            : $"合計 {StatsFormatting.FormatHours(grandTotal)} ／ {itemCount} 件";
    }

    /// <summary>
    /// 勤務開始から退勤までのうち、実績で覆われていない時間を加算先コードへ振り分ける。
    /// 実績や勤務記録そのものは変更せず、プロジェクトコード別統計だけに反映する。
    ///
    /// 加算先はスプリントごとに変わりうるので、集計期間が複数スプリントにまたがっても
    /// 正しく分かれるよう、未記録時間を日単位に割ってから日ごとに解決する。
    /// </summary>
    private void AddUnrecordedTimeToProjectStats(
        DateTime rangeStart,
        DateTime rangeEnd,
        Dictionary<string, double> projectCodeTotals,
        Dictionary<DateTime, Dictionary<string, double>> dailyProjectCodeTotals,
        Dictionary<string, string> projectCodeDisplayNames)
    {
        if (!IsUnrecordedTimeProjectAggregationEnabled) return;

        var sprintSources = SprintHelper.GetUnrecordedTimeProjectCodeSources(ManualSprints);

        var now = DateTime.Now;
        foreach (var log in _workDayLogs)
        {
            var workStart = log.StartTime < rangeStart ? rangeStart : log.StartTime;
            var rawWorkEnd = log.EndTime ?? now;
            if (rawWorkEnd > now) rawWorkEnd = now;
            var workEnd = rawWorkEnd > rangeEnd ? rangeEnd : rawWorkEnd;
            if (workEnd <= workStart) continue;

            var gaps = UnrecordedGapHelper.Detect(
                workStart,
                workEnd,
                CollectCoveredRanges(workStart, workEnd),
                TimeSpan.Zero);

            foreach (var gap in gaps)
            {
                foreach (var segment in UnrecordedGapHelper.SplitByDay(gap))
                {
                    var date = segment.StartTime.Date;
                    var projectCode = ResolveUnrecordedTimeProjectCode(sprintSources, date);
                    if (projectCode == null) continue;

                    var hours = segment.Duration.TotalHours;

                    projectCodeTotals[projectCode.Id] =
                        projectCodeTotals.GetValueOrDefault(projectCode.Id) + hours;

                    if (!dailyProjectCodeTotals.TryGetValue(date, out var projectCodesPerDay))
                    {
                        projectCodesPerDay = [];
                        dailyProjectCodeTotals[date] = projectCodesPerDay;
                    }

                    projectCodesPerDay[projectCode.Id] =
                        projectCodesPerDay.GetValueOrDefault(projectCode.Id) + hours;

                    projectCodeDisplayNames[projectCode.Id] = projectCode.DisplayName;
                }
            }
        }
    }

    /// <summary>
    /// 期間内のふりかえりを集める。
    ///
    /// 書いたものを読み返す場所がここしか無いので、記録が0時間の期間でも出す
    /// （<see cref="HasStatsData"/> の出し分けとは独立させている）。
    /// 新しい日を上にするのは、直近のふりかえりほど読み返す頻度が高いため。
    /// </summary>
    private IReadOnlyList<WorkDayNote> BuildStatsNoteItems(DateTime rangeStart, DateTime rangeEnd)
    {
        return
        [
            .. _workDayLogs
                .Where(l => l.HasNote && l.StartTime.Date >= rangeStart && l.StartTime.Date < rangeEnd)
                .OrderByDescending(l => l.StartTime)
                .Select(ToWorkDayNote)
        ];
    }
}
