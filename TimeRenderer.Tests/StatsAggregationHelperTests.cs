using System;
using System.Collections.Generic;

using TimeRenderer.Helpers;
using TimeRenderer.Models;

using TUnit.Assertions;
using TUnit.Assertions.Extensions;
using TUnit.Core;

namespace TimeRenderer.Tests;

/// <summary>統計のカテゴリ別・プロジェクト別・日別の基礎集計を固定する。</summary>
public class StatsAggregationHelperTests
{
    private static ScheduleItem Recorded(
        DateTime start,
        DateTime end,
        string color,
        string? categoryId = null,
        string? projectCodeId = null,
        bool isAllDay = false) =>
        new()
        {
            Kind = ScheduleItemKind.Recorded,
            StartTime = start,
            EndTime = end,
            ColorCode = color,
            CategoryId = categoryId,
            ProjectCodeId = projectCodeId,
            IsAllDay = isAllDay,
        };

    [Test]
    public async Task 期間で切り詰め日またぎを分割してカテゴリと案件へ集計する()
    {
        var category = new CategoryInfo { Id = "category-a", Name = "開発", ColorCode = "#FF87CEFA" };
        var project = new ProjectCodeInfo { Id = "project-a", Code = "PRJ-A", Name = "案件A" };
        var rangeStart = new DateTime(2026, 8, 1);
        var rangeEnd = new DateTime(2026, 8, 3);
        List<ScheduleItem> items =
        [
            // 期間の開始前は切り捨て、8/1 の2時間だけを集計する。
            Recorded(rangeStart.AddHours(-1), rangeStart.AddHours(2), category.ColorCode, category.Id, project.Id),
            // 日またぎは 8/1 に1時間、8/2 に2時間へ分割する。
            Recorded(rangeStart.AddHours(23), rangeStart.AddDays(1).AddHours(2), "#FF00FF00"),
            // CategoryId が無い旧データは色で既存カテゴリへ解決する。
            Recorded(rangeStart.AddDays(1).AddHours(10), rangeStart.AddDays(1).AddHours(11), category.ColorCode),
            // マスターに無い案件IDも集計から落とさない。
            Recorded(rangeStart.AddDays(1).AddHours(12), rangeStart.AddDays(1).AddHours(13), "#FFFF0000", projectCodeId: "missing"),
        ];

        var result = StatsAggregationHelper.Aggregate(items, [category], [project], rangeStart, rangeEnd);
        var uncategorizedGreenKey = "color:#FF00FF00";

        await Assert.That(result.ItemCount).IsEqualTo(4);
        await Assert.That(result.CategoryTotals[category.Id]).IsEqualTo(3);
        await Assert.That(result.CategoryTotals[uncategorizedGreenKey]).IsEqualTo(3);
        await Assert.That(result.DailyCategoryTotals[rangeStart][uncategorizedGreenKey]).IsEqualTo(1);
        await Assert.That(result.DailyCategoryTotals[rangeStart.AddDays(1)][uncategorizedGreenKey]).IsEqualTo(2);
        await Assert.That(result.ProjectCodeTotals[project.Id]).IsEqualTo(2);
        await Assert.That(result.ProjectCodeTotals[StatsAggregationHelper.UnassignedProjectKey]).IsEqualTo(4);
        await Assert.That(result.ProjectCodeTotals["missing"]).IsEqualTo(1);
        await Assert.That(result.ProjectCodeDisplayNames[project.Id]).IsEqualTo("PRJ-A - 案件A");
        await Assert.That(result.ProjectCodeDisplayNames["missing"]).IsEqualTo("（不明なプロジェクトコード）");
    }

    [Test]
    public async Task 予定と終日実績と期間外実績は集計しない()
    {
        var rangeStart = new DateTime(2026, 8, 1);
        var rangeEnd = rangeStart.AddDays(1);
        var planned = Recorded(rangeStart.AddHours(9), rangeStart.AddHours(10), "#FF0000FF");
        planned.Kind = ScheduleItemKind.Planned;
        List<ScheduleItem> items =
        [
            planned,
            Recorded(rangeStart, rangeEnd, "#FF0000FF", isAllDay: true),
            Recorded(rangeEnd, rangeEnd.AddHours(1), "#FF0000FF"),
        ];

        var result = StatsAggregationHelper.Aggregate(items, [], [], rangeStart, rangeEnd);

        await Assert.That(result.ItemCount).IsEqualTo(0);
        await Assert.That(result.CategoryTotals).IsEmpty();
        await Assert.That(result.ProjectCodeTotals).IsEmpty();
        await Assert.That(result.DailyCategoryTotals).IsEmpty();
    }

    [Test]
    public async Task 終端日時は排他的に扱う()
    {
        var rangeStart = new DateTime(2026, 8, 1);
        var rangeEnd = rangeStart.AddDays(1);
        var item = Recorded(rangeEnd.AddHours(-1), rangeEnd.AddHours(1), "#FFFFA500");

        var result = StatsAggregationHelper.Aggregate([item], [], [], rangeStart, rangeEnd);

        await Assert.That(result.ItemCount).IsEqualTo(1);
        await Assert.That(result.CategoryTotals.Count).IsEqualTo(1);
        await Assert.That(result.CategoryTotals["color:#FFFFA500"]).IsEqualTo(1);
        await Assert.That(result.DailyCategoryTotals.ContainsKey(rangeEnd)).IsFalse();
    }
}
