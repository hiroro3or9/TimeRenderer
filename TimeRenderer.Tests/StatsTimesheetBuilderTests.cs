using System;
using System.Collections.Generic;

using TimeRenderer.Helpers;
using TimeRenderer.Models;
using TimeRenderer.ViewModels;

using TUnit.Assertions;
using TUnit.Assertions.Extensions;
using TUnit.Core;

namespace TimeRenderer.Tests;

/// <summary>月次タイムシート行列の列、行、丸め、合計を固定する。</summary>
public class StatsTimesheetBuilderTests
{
    [Test]
    public async Task 日別案件時間を15分単位へ丸めて列合計と総合計を作る()
    {
        var start = new DateTime(2026, 8, 1);
        var end = start.AddDays(2);
        var projectA = new ProjectCodeInfo { Id = "a", Code = "PRJ-A", Name = "案件A" };
        var projectB = new ProjectCodeInfo { Id = "b", Code = "PRJ-B", Name = "案件B" };
        Dictionary<DateTime, Dictionary<string, double>> daily = new()
        {
            [start] = new() { ["a"] = 1.12, ["b"] = 0.38 },
            [start.AddDays(1)] = new() { ["a"] = 0.13 },
        };

        var result = StatsTimesheetBuilder.Build(start, end, ["a", "b"], daily, [projectA, projectB]);

        await Assert.That(result.Columns.Count).IsEqualTo(2);
        await Assert.That(result.Columns[0].Date).IsEqualTo(start);
        await Assert.That(result.Columns[1].Date).IsEqualTo(start.AddDays(1));
        await Assert.That(result.Rows[0].Cells[0].ActualHours).IsEqualTo(1.12);
        await Assert.That(result.Rows[0].Cells[0].RoundedHours).IsEqualTo(1.0);
        await Assert.That(result.Rows[0].Cells[1].RoundedHours).IsEqualTo(0.25);
        await Assert.That(result.Rows[1].Cells[0].RoundedHours).IsEqualTo(0.5);
        await Assert.That(result.Rows[1].Cells[1].HasValue).IsFalse();
        await Assert.That(result.TotalCells[0].RoundedHours).IsEqualTo(1.5);
        await Assert.That(result.TotalCells[1].RoundedHours).IsEqualTo(0.25);
        await Assert.That(result.GrandTotalText).IsEqualTo("1.75h");
    }

    [Test]
    public async Task 無効コードとコード未入力と未設定と不明を行から落とさない()
    {
        var start = new DateTime(2026, 8, 1);
        var inactive = new ProjectCodeInfo
        {
            Id = "inactive",
            Code = "OLD",
            Name = "旧案件",
            IsActive = false,
        };
        var noCode = new ProjectCodeInfo { Id = "no-code", Name = "名称のみ" };
        var keys = new[]
        {
            inactive.Id,
            noCode.Id,
            StatsAggregationHelper.UnassignedProjectKey,
            "missing",
        };
        Dictionary<DateTime, Dictionary<string, double>> daily = new()
        {
            [start] = new()
            {
                [inactive.Id] = 1,
                [noCode.Id] = 1,
                [StatsAggregationHelper.UnassignedProjectKey] = 1,
                ["missing"] = 1,
            },
        };

        var result = StatsTimesheetBuilder.Build(start, start.AddDays(1), keys, daily, [inactive, noCode]);

        await Assert.That(result.Rows.Count).IsEqualTo(4);
        await Assert.That(result.Rows[0].HeaderText).IsEqualTo("OLD");
        await Assert.That(result.Rows[0].DetailText).IsEqualTo("旧案件（無効）");
        await Assert.That(result.Rows[0].IsInactive).IsTrue();
        await Assert.That(result.Rows[1].HeaderText).IsEqualTo("名称のみ");
        await Assert.That(result.Rows[1].IsWarning).IsTrue();
        await Assert.That(result.Rows[2].HeaderText).IsEqualTo("未設定");
        await Assert.That(result.Rows[3].HeaderText).IsEqualTo("不明");
    }
}
