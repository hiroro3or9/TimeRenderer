using System;
using System.Collections.Generic;
using System.Globalization;
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
                if (param is TimesheetMatrixColumn { CopyValue.Length: > 0 } column)
                    CopyTimesheetValue(column.CopyValue);
            },
            param => param is TimesheetMatrixColumn { CopyValue.Length: > 0 });
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
        public string HoursText => FormatHours(Hours);
        public string PercentText { get; init; } = "";
    }

    /// <summary>プロジェクトコード別集計の1行分</summary>
    public record ProjectCodeStat(string DisplayName, double Hours, double MaxHours)
    {
        public string HoursText => FormatHours(Hours);
        public string PercentText { get; init; } = "";
    }

    /// <summary>タイムシート用マトリクスのプロジェクトコード列。</summary>
    public record TimesheetMatrixColumn(
        string ProjectKey,
        string HeaderText,
        string DetailText,
        string CopyValue,
        bool IsInactive,
        bool IsWarning);

    /// <summary>タイムシート用マトリクスの時間セル。</summary>
    public record TimesheetMatrixCell(bool HasValue, double ActualHours, double RoundedHours)
    {
        public string ActualHoursText => FormatHours(ActualHours);
        public string CopyValue => FormatDecimalHoursValue(RoundedHours);
        public string RoundedHoursText => HasValue ? $"{CopyValue}h" : "";
        public string ToolTipText => HasValue
            ? $"実績 {ActualHoursText} → 15分単位 {RoundedHoursText}"
            : "記録なし";
    }

    /// <summary>タイムシート用マトリクスの1日分。</summary>
    public record TimesheetMatrixRow(DateTime Date, IReadOnlyList<TimesheetMatrixCell> Cells)
    {
        public string DateText => Date.ToString("M/d(ddd)");
        public bool IsToday => Date.Date == DateTime.Today;
        public string RoundedTotalText => FormatDecimalHours(Cells.Sum(cell => cell.RoundedHours));
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

    private IReadOnlyList<ProjectCodeStat> _statsProjectCodeItems = [];
    public IReadOnlyList<ProjectCodeStat> StatsProjectCodeItems
    {
        get => _statsProjectCodeItems;
        private set => SetProperty(ref _statsProjectCodeItems, value);
    }

    private IReadOnlyList<TimesheetMatrixColumn> _statsTimesheetMatrixColumns = [];
    public IReadOnlyList<TimesheetMatrixColumn> StatsTimesheetMatrixColumns
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

    internal static string FormatHours(double hours)
    {
        var span = TimeSpan.FromHours(hours);
        return $"{(int)span.TotalHours}:{span.Minutes:D2}";
    }

    /// <summary>時間を最寄りの15分単位へ丸める。</summary>
    internal static double RoundHoursToQuarter(double hours)
        => Math.Round(hours * 4, MidpointRounding.AwayFromZero) / 4;

    /// <summary>タイムシートへ転記しやすい10進時間（例: 7.25h）に整形する。</summary>
    internal static string FormatDecimalHours(double hours)
        => $"{FormatDecimalHoursValue(hours)}h";

    /// <summary>クリップボードへコピーする10進時間の数字部分を整形する。</summary>
    internal static string FormatDecimalHoursValue(double hours)
        => hours.ToString("0.##", CultureInfo.InvariantCulture);

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

        // 集計キー：カテゴリID（未分類は "color:<コード>"）
        // キー -> 合計時間 / 日付 -> (キー -> 時間) / キー -> 表示情報
        var categoryTotals = new Dictionary<string, double>();
        var projectCodeTotals = new Dictionary<string, double>();
        var dailyTotals = new Dictionary<DateTime, Dictionary<string, double>>();
        var dailyProjectCodeTotals = new Dictionary<DateTime, Dictionary<string, double>>();
        var displayInfo = new Dictionary<string, (string Name, Brush Brush)>();
        var projectCodeDisplayNames = new Dictionary<string, string>();
        const string unassignedProjectKey = "__unassigned_project__";
        int itemCount = 0;

        foreach (var item in ScheduleItems)
        {
            if (!item.IsRecorded || item.IsAllDay) continue;

            // 期間でクリップ
            var start = item.StartTime < rangeStart ? rangeStart : item.StartTime;
            var end = item.EndTime > rangeEnd ? rangeEnd : item.EndTime;
            if (end <= start) continue;

            itemCount++;

            var projectCode = ResolveProjectCode(item.ProjectCodeId);
            var projectKey = item.ProjectCodeId ?? unassignedProjectKey;
            projectCodeTotals[projectKey] = projectCodeTotals.GetValueOrDefault(projectKey) + (end - start).TotalHours;
            projectCodeDisplayNames[projectKey] = projectCode?.DisplayName
                ?? (item.ProjectCodeId == null ? "（未設定）" : "（不明なプロジェクトコード）");

            var category = ResolveCategory(item);
            var key = category?.Id ?? $"color:{item.ColorCode}";
            if (!displayInfo.ContainsKey(key))
            {
                displayInfo[key] = category != null
                    ? (category.Name, category.Brush)
                    : ("未分類", CategoryInfo.CreateBrush(item.ColorCode));
            }

            // 日単位に分割して集計（日またぎ対応）
            for (var d = start.Date; d < end; d = d.AddDays(1))
            {
                var segStart = d > start ? d : start;
                var segEnd = end < d.AddDays(1) ? end : d.AddDays(1);
                if (segEnd <= segStart) continue;

                var hours = (segEnd - segStart).TotalHours;
                categoryTotals[key] = categoryTotals.GetValueOrDefault(key) + hours;

                if (!dailyTotals.TryGetValue(d, out var perDay))
                {
                    perDay = [];
                    dailyTotals[d] = perDay;
                }
                perDay[key] = perDay.GetValueOrDefault(key) + hours;

                if (!dailyProjectCodeTotals.TryGetValue(d, out var projectCodesPerDay))
                {
                    projectCodesPerDay = [];
                    dailyProjectCodeTotals[d] = projectCodesPerDay;
                }
                projectCodesPerDay[projectKey] = projectCodesPerDay.GetValueOrDefault(projectKey) + hours;
            }
        }

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

        StatsProjectCodeItems = [.. orderedProjectKeys.Select(key =>
        {
            var hours = projectCodeTotals[key];
            var percent = grandTotal > 0 ? hours / grandTotal * 100 : 0;
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
            var matrixColumns = orderedProjectKeys.Select(key =>
            {
                var projectCode = ResolveProjectCode(key);
                if (projectCode != null)
                {
                    var header = projectCode.Code.Length > 0
                        ? projectCode.Code
                        : projectCode.Name.Length > 0 ? projectCode.Name : "コード未入力";
                    var detail = projectCode.Name;
                    if (!projectCode.IsActive)
                        detail = detail.Length > 0 ? $"{detail}（無効）" : "無効";
                    return new TimesheetMatrixColumn(
                        key, header, detail, projectCode.Code,
                        !projectCode.IsActive, projectCode.Code.Length == 0);
                }

                return key == unassignedProjectKey
                    ? new TimesheetMatrixColumn(key, "未設定", "コードなし", "", false, true)
                    : new TimesheetMatrixColumn(key, "不明", "マスターに存在しません", "", false, true);
            }).ToList();

            var matrixRows = new List<TimesheetMatrixRow>();
            foreach (var (date, projectCodesPerDay) in dailyProjectCodeTotals.OrderBy(pair => pair.Key))
            {
                var cells = matrixColumns.Select(column =>
                {
                    if (!projectCodesPerDay.TryGetValue(column.ProjectKey, out var hours))
                        return new TimesheetMatrixCell(false, 0, 0);
                    return new TimesheetMatrixCell(true, hours, RoundHoursToQuarter(hours));
                }).ToList();
                matrixRows.Add(new TimesheetMatrixRow(date, cells));
            }

            var totalCells = Enumerable.Range(0, matrixColumns.Count)
                .Select(index =>
                {
                    var cells = matrixRows.Select(row => row.Cells[index]).ToList();
                    return new TimesheetMatrixCell(
                        cells.Any(cell => cell.HasValue),
                        cells.Sum(cell => cell.ActualHours),
                        cells.Sum(cell => cell.RoundedHours));
                })
                .ToList();

            StatsTimesheetMatrixColumns = matrixColumns;
            StatsTimesheetMatrixRows = matrixRows;
            StatsTimesheetMatrixTotalCells = totalCells;
            StatsTimesheetMatrixGrandTotalText = FormatDecimalHours(
                matrixRows.Sum(row => row.Cells.Sum(cell => cell.RoundedHours)));
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
                        $"{name}: {FormatHours(hours)}"));
                }
            }

            var label = compact ? d.Day.ToString() : d.ToString("M/d(ddd)");
            dailyStats.Add(new DailyStat(d, label, d.Date == DateTime.Today, dayTotal, segments));
        }
        StatsDailyItems = dailyStats;
        StatsNoteItems = BuildStatsNoteItems(rangeStart, rangeEnd);

        HasStatsData = grandTotal > 0;
        StatsSummaryText = $"合計 {FormatHours(grandTotal)} ／ {itemCount} 件";
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
