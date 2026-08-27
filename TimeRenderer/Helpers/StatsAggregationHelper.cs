using System;
using System.Collections.Generic;

using TimeRenderer.Models;

namespace TimeRenderer.Helpers;

/// <summary>カテゴリ集計の表示に必要な、UI型を含まない情報。</summary>
public sealed record StatsCategoryDisplayInfo(string Name, string ColorCode);

/// <summary>統計対象の実績をカテゴリ・プロジェクトコード・日へ配分した結果。</summary>
public sealed class StatsAggregationResult
{
    public Dictionary<string, double> CategoryTotals { get; } = [];
    public Dictionary<string, double> ProjectCodeTotals { get; } = [];
    public Dictionary<DateTime, Dictionary<string, double>> DailyCategoryTotals { get; } = [];
    public Dictionary<DateTime, Dictionary<string, double>> DailyProjectCodeTotals { get; } = [];
    public Dictionary<string, StatsCategoryDisplayInfo> CategoryDisplayInfo { get; } = [];
    public Dictionary<string, string> ProjectCodeDisplayNames { get; } = [];
    public int ItemCount { get; internal set; }
}

/// <summary>
/// 統計の基礎集計を行う純粋ロジック。
/// 画面状態や現在時刻を参照せず、指定期間を終端排他の [start, end) として扱う。
/// </summary>
public static class StatsAggregationHelper
{
    public const string UnassignedProjectKey = "__unassigned_project__";

    public static StatsAggregationResult Aggregate(
        IEnumerable<ScheduleItem> items,
        IReadOnlyList<CategoryInfo> categories,
        IReadOnlyList<ProjectCodeInfo> projectCodes,
        DateTime rangeStart,
        DateTime rangeEnd)
    {
        ArgumentNullException.ThrowIfNull(items);
        ArgumentNullException.ThrowIfNull(categories);
        ArgumentNullException.ThrowIfNull(projectCodes);

        var result = new StatsAggregationResult();
        if (rangeEnd <= rangeStart) return result;

        var categoriesById = new Dictionary<string, CategoryInfo>(categories.Count);
        var categoriesByColor = new Dictionary<string, CategoryInfo>(categories.Count);
        foreach (var category in categories)
        {
            if (!string.IsNullOrEmpty(category.Id)) categoriesById[category.Id] = category;
            if (!string.IsNullOrEmpty(category.ColorCode))
                categoriesByColor.TryAdd(category.ColorCode, category);
        }

        var projectCodesById = new Dictionary<string, ProjectCodeInfo>(projectCodes.Count);
        foreach (var projectCode in projectCodes)
        {
            if (!string.IsNullOrEmpty(projectCode.Id))
                projectCodesById.TryAdd(projectCode.Id, projectCode);
        }

        foreach (var item in items)
        {
            if (!item.IsRecorded || item.IsAllDay) continue;

            var start = item.StartTime < rangeStart ? rangeStart : item.StartTime;
            var end = item.EndTime > rangeEnd ? rangeEnd : item.EndTime;
            if (end <= start) continue;

            result.ItemCount++;

            var projectKey = item.ProjectCodeId ?? UnassignedProjectKey;
            result.ProjectCodeTotals[projectKey] =
                result.ProjectCodeTotals.GetValueOrDefault(projectKey) + (end - start).TotalHours;
            result.ProjectCodeDisplayNames[projectKey] =
                ResolveProjectDisplayName(item.ProjectCodeId, projectCodesById);

            var category = ResolveCategory(item, categoriesById, categoriesByColor);
            var categoryKey = category?.Id ?? $"color:{item.ColorCode}";
            if (!result.CategoryDisplayInfo.ContainsKey(categoryKey))
            {
                result.CategoryDisplayInfo[categoryKey] = category != null
                    ? new StatsCategoryDisplayInfo(category.Name, category.ColorCode)
                    : new StatsCategoryDisplayInfo("未分類", item.ColorCode);
            }

            AddDailySegments(result, start, end, categoryKey, projectKey);
        }

        return result;
    }

    private static CategoryInfo? ResolveCategory(
        ScheduleItem item,
        IReadOnlyDictionary<string, CategoryInfo> categoriesById,
        IReadOnlyDictionary<string, CategoryInfo> categoriesByColor)
    {
        if (!string.IsNullOrEmpty(item.CategoryId)
            && categoriesById.TryGetValue(item.CategoryId, out var byId))
        {
            return byId;
        }

        return categoriesByColor.TryGetValue(item.ColorCode, out var byColor) ? byColor : null;
    }

    private static string ResolveProjectDisplayName(
        string? projectCodeId,
        IReadOnlyDictionary<string, ProjectCodeInfo> projectCodesById)
    {
        if (projectCodeId != null && projectCodesById.TryGetValue(projectCodeId, out var projectCode))
            return projectCode.DisplayName;

        return projectCodeId == null ? "（未設定）" : "（不明なプロジェクトコード）";
    }

    private static void AddDailySegments(
        StatsAggregationResult result,
        DateTime start,
        DateTime end,
        string categoryKey,
        string projectKey)
    {
        for (var date = start.Date; date < end; date = date.AddDays(1))
        {
            var segmentStart = date > start ? date : start;
            var segmentEnd = end < date.AddDays(1) ? end : date.AddDays(1);
            if (segmentEnd <= segmentStart) continue;

            var hours = (segmentEnd - segmentStart).TotalHours;
            result.CategoryTotals[categoryKey] =
                result.CategoryTotals.GetValueOrDefault(categoryKey) + hours;

            if (!result.DailyCategoryTotals.TryGetValue(date, out var categoriesPerDay))
            {
                categoriesPerDay = [];
                result.DailyCategoryTotals[date] = categoriesPerDay;
            }
            categoriesPerDay[categoryKey] = categoriesPerDay.GetValueOrDefault(categoryKey) + hours;

            if (!result.DailyProjectCodeTotals.TryGetValue(date, out var projectCodesPerDay))
            {
                projectCodesPerDay = [];
                result.DailyProjectCodeTotals[date] = projectCodesPerDay;
            }
            projectCodesPerDay[projectKey] = projectCodesPerDay.GetValueOrDefault(projectKey) + hours;
        }
    }
}
