using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;

using TimeRenderer.Helpers;
using TimeRenderer.Models;

namespace TimeRenderer.ViewModels;

/// <summary>統計画面で共通して使う時間表示と15分丸め。</summary>
public static class StatsFormatting
{
    public static string FormatHours(double hours)
    {
        var span = TimeSpan.FromHours(hours);
        return $"{(int)span.TotalHours}:{span.Minutes:D2}";
    }

    public static double RoundHoursToQuarter(double hours)
        => Math.Round(hours * 4, MidpointRounding.AwayFromZero) / 4;

    public static string FormatDecimalHours(double hours)
        => $"{FormatDecimalHoursValue(hours)}h";

    public static string FormatDecimalHoursValue(double hours)
        => hours.ToString("0.##", CultureInfo.InvariantCulture);
}

/// <summary>タイムシート用マトリクスの日付列。</summary>
public record TimesheetMatrixDateColumn(DateTime Date)
{
    public string DateText => Date.Day.ToString(CultureInfo.InvariantCulture);
    public string DayText => Date.ToString("ddd");
    public bool IsToday => Date.Date == DateTime.Today;
    public bool IsWeekend => Date.DayOfWeek is DayOfWeek.Saturday or DayOfWeek.Sunday;
}

/// <summary>タイムシート用マトリクスの時間セル。</summary>
public record TimesheetMatrixCell(bool HasValue, double ActualHours, double RoundedHours)
{
    public string ActualHoursText => StatsFormatting.FormatHours(ActualHours);
    public string CopyValue => StatsFormatting.FormatDecimalHoursValue(RoundedHours);
    public string RoundedHoursText => HasValue ? $"{CopyValue}h" : "";
    public string ToolTipText => HasValue
        ? $"実績 {ActualHoursText} → 15分単位 {RoundedHoursText}"
        : "記録なし";
}

/// <summary>タイムシート用マトリクスのプロジェクトコード1行分。</summary>
public record TimesheetMatrixRow(
    string ProjectKey,
    string HeaderText,
    string DetailText,
    string CopyValue,
    bool IsInactive,
    bool IsWarning,
    IReadOnlyList<TimesheetMatrixCell> Cells)
{
    public string RoundedTotalText =>
        StatsFormatting.FormatDecimalHours(Cells.Sum(cell => cell.RoundedHours));
}

/// <summary>月次タイムシート行列の構築結果。</summary>
public sealed record TimesheetMatrixResult(
    IReadOnlyList<TimesheetMatrixDateColumn> Columns,
    IReadOnlyList<TimesheetMatrixRow> Rows,
    IReadOnlyList<TimesheetMatrixCell> TotalCells,
    string GrandTotalText);

/// <summary>日・プロジェクト別の実績から、転記用の月次行列を構築する。</summary>
public static class StatsTimesheetBuilder
{
    public static TimesheetMatrixResult Build(
        DateTime rangeStart,
        DateTime rangeEnd,
        IReadOnlyList<string> orderedProjectKeys,
        IReadOnlyDictionary<DateTime, Dictionary<string, double>> dailyProjectCodeTotals,
        IReadOnlyList<ProjectCodeInfo> projectCodes)
    {
        ArgumentNullException.ThrowIfNull(orderedProjectKeys);
        ArgumentNullException.ThrowIfNull(dailyProjectCodeTotals);
        ArgumentNullException.ThrowIfNull(projectCodes);

        var columnCount = Math.Max(0, (rangeEnd - rangeStart).Days);
        var columns = Enumerable.Range(0, columnCount)
            .Select(offset => new TimesheetMatrixDateColumn(rangeStart.AddDays(offset)))
            .ToList();

        var projectCodesById = new Dictionary<string, ProjectCodeInfo>(projectCodes.Count);
        foreach (var projectCode in projectCodes)
        {
            if (!string.IsNullOrEmpty(projectCode.Id))
                projectCodesById.TryAdd(projectCode.Id, projectCode);
        }

        var rows = orderedProjectKeys.Select(key =>
        {
            var descriptor = BuildProjectDescriptor(key, projectCodesById);
            var cells = columns.Select(column =>
            {
                if (!dailyProjectCodeTotals.TryGetValue(column.Date, out var projectsPerDay)
                    || !projectsPerDay.TryGetValue(key, out var hours))
                {
                    return new TimesheetMatrixCell(false, 0, 0);
                }

                return new TimesheetMatrixCell(
                    true,
                    hours,
                    StatsFormatting.RoundHoursToQuarter(hours));
            }).ToList();

            return new TimesheetMatrixRow(
                key,
                descriptor.Header,
                descriptor.Detail,
                descriptor.CopyValue,
                descriptor.IsInactive,
                descriptor.IsWarning,
                cells);
        }).ToList();

        var totalCells = Enumerable.Range(0, columns.Count)
            .Select(index =>
            {
                var cells = rows.Select(row => row.Cells[index]).ToList();
                return new TimesheetMatrixCell(
                    cells.Any(cell => cell.HasValue),
                    cells.Sum(cell => cell.ActualHours),
                    cells.Sum(cell => cell.RoundedHours));
            })
            .ToList();

        var grandTotal = rows.Sum(row => row.Cells.Sum(cell => cell.RoundedHours));
        return new TimesheetMatrixResult(
            columns,
            rows,
            totalCells,
            StatsFormatting.FormatDecimalHours(grandTotal));
    }

    private static ProjectDescriptor BuildProjectDescriptor(
        string key,
        IReadOnlyDictionary<string, ProjectCodeInfo> projectCodesById)
    {
        if (projectCodesById.TryGetValue(key, out var projectCode))
        {
            var header = projectCode.Code.Length > 0
                ? projectCode.Code
                : projectCode.Name.Length > 0 ? projectCode.Name : "コード未入力";
            var detail = projectCode.Name;
            if (!projectCode.IsActive)
                detail = detail.Length > 0 ? $"{detail}（無効）" : "無効";

            return new ProjectDescriptor(
                header,
                detail,
                projectCode.Code,
                !projectCode.IsActive,
                projectCode.Code.Length == 0);
        }

        return key == StatsAggregationHelper.UnassignedProjectKey
            ? new ProjectDescriptor("未設定", "コードなし", "", false, true)
            : new ProjectDescriptor("不明", "マスターに存在しません", "", false, true);
    }

    private sealed record ProjectDescriptor(
        string Header,
        string Detail,
        string CopyValue,
        bool IsInactive,
        bool IsWarning);
}
