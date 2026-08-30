using System;

using TimeRenderer.Helpers;

using TUnit.Assertions;
using TUnit.Assertions.Extensions;
using TUnit.Core;

namespace TimeRenderer.Tests;

/// <summary>予定の端を近隣時刻へ吸着する選択規則を固定する。</summary>
public class MagnetSnapHelperTests
{
    private static readonly DateTime BaseTime = new(2026, 8, 30, 10, 0, 0);

    [Test]
    public async Task 許容範囲内で最も近い時刻へ吸着する()
    {
        var result = MagnetSnapHelper.SnapEdge(
            BaseTime,
            [BaseTime.AddMinutes(-4), BaseTime.AddMinutes(2), BaseTime.AddMinutes(8)],
            TimeSpan.FromMinutes(5));

        await Assert.That(result).IsNotNull();
        await Assert.That(result!.GuideTime).IsEqualTo(BaseTime.AddMinutes(2));
        await Assert.That(result.Offset).IsEqualTo(TimeSpan.FromMinutes(2));
    }

    [Test]
    public async Task 許容範囲ちょうどは含み超えた候補は除外する()
    {
        var included = MagnetSnapHelper.SnapEdge(
            BaseTime, [BaseTime.AddMinutes(5)], TimeSpan.FromMinutes(5));
        var excluded = MagnetSnapHelper.SnapEdge(
            BaseTime, [BaseTime.AddMinutes(5).AddTicks(1)], TimeSpan.FromMinutes(5));

        await Assert.That(included).IsNotNull();
        await Assert.That(included!.Offset).IsEqualTo(TimeSpan.FromMinutes(5));
        await Assert.That(excluded).IsNull();
    }

    [Test]
    public async Task 同じ距離の候補では先に渡された時刻を選ぶ()
    {
        var result = MagnetSnapHelper.SnapEdge(
            BaseTime,
            [BaseTime.AddMinutes(3), BaseTime.AddMinutes(-3)],
            TimeSpan.FromMinutes(5));

        await Assert.That(result).IsNotNull();
        await Assert.That(result!.GuideTime).IsEqualTo(BaseTime.AddMinutes(3));
    }

    [Test]
    public async Task 移動範囲では開始端と終了端のうち小さい移動量を選ぶ()
    {
        var result = MagnetSnapHelper.SnapRange(
            BaseTime,
            BaseTime.AddHours(1),
            [BaseTime.AddMinutes(-4), BaseTime.AddHours(1).AddMinutes(1)],
            TimeSpan.FromMinutes(5));

        await Assert.That(result).IsNotNull();
        await Assert.That(result!.GuideTime).IsEqualTo(BaseTime.AddHours(1).AddMinutes(1));
        await Assert.That(result.Offset).IsEqualTo(TimeSpan.FromMinutes(1));
    }

    [Test]
    public async Task 対象が無い場合は吸着しない()
    {
        var edge = MagnetSnapHelper.SnapEdge(BaseTime, [], TimeSpan.FromMinutes(5));
        var range = MagnetSnapHelper.SnapRange(
            BaseTime, BaseTime.AddHours(1), [], TimeSpan.FromMinutes(5));

        await Assert.That(edge).IsNull();
        await Assert.That(range).IsNull();
    }
}
