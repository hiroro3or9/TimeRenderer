using System;
using System.Collections.Generic;

using TimeRenderer.Helpers;
using TimeRenderer.Models;

using TUnit.Assertions;
using TUnit.Assertions.Extensions;
using TUnit.Core;

namespace TimeRenderer.Tests;

/// <summary>
/// <see cref="UnrecordedTimeAssignmentHelper"/> の特性テスト。
///
/// 割り当ては開始日だけを持ち、期間の終わりは次の行から導出する。境界の1日ずれは
/// 月次表の数字にしか出ず画面では気づけないため、期待値を固定しておく。
/// </summary>
public class UnrecordedTimeAssignmentHelperTests
{
    private static readonly List<UnrecordedTimeProjectAssignment> NoAssignments = [];

    private static UnrecordedTimeProjectAssignment At(DateTime start, string? projectCodeId) =>
        new() { StartDate = start, ProjectCodeId = projectCodeId };

    // ============================================================
    // 並べ替え
    // ============================================================

    [Test]
    public async Task 開始日の昇順に並べ替える()
    {
        List<UnrecordedTimeProjectAssignment> assignments =
        [
            At(new DateTime(2026, 3, 2), "P3"),
            At(new DateTime(2026, 1, 5), "P1"),
            At(new DateTime(2026, 2, 2), "P2"),
        ];

        var ordered = UnrecordedTimeAssignmentHelper.Order(assignments);

        await Assert.That(ordered[0].ProjectCodeId).IsEqualTo("P1");
        await Assert.That(ordered[1].ProjectCodeId).IsEqualTo("P2");
        await Assert.That(ordered[2].ProjectCodeId).IsEqualTo("P3");
    }

    [Test]
    public async Task 空でも並べ替えで落ちない()
    {
        var ordered = UnrecordedTimeAssignmentHelper.Order(NoAssignments);

        await Assert.That(ordered.Count).IsEqualTo(0);
    }

    // ============================================================
    // 参照
    // ============================================================

    [Test]
    public async Task その日以前で最後の割り当てを採る()
    {
        List<UnrecordedTimeProjectAssignment> ordered =
        [
            At(new DateTime(2026, 1, 5), "P1"),
            At(new DateTime(2026, 2, 2), "P2"),
        ];

        var resolved = UnrecordedTimeAssignmentHelper.Resolve(ordered, new DateTime(2026, 2, 20));

        await Assert.That(resolved!.ProjectCodeId).IsEqualTo("P2");
    }

    [Test]
    public async Task 開始日当日はその割り当てを採る()
    {
        List<UnrecordedTimeProjectAssignment> ordered =
        [
            At(new DateTime(2026, 1, 5), "P1"),
            At(new DateTime(2026, 2, 2), "P2"),
        ];

        var resolved = UnrecordedTimeAssignmentHelper.Resolve(ordered, new DateTime(2026, 2, 2));

        await Assert.That(resolved!.ProjectCodeId).IsEqualTo("P2");
    }

    [Test]
    public async Task 開始日の前日はひとつ前の割り当てを採る()
    {
        List<UnrecordedTimeProjectAssignment> ordered =
        [
            At(new DateTime(2026, 1, 5), "P1"),
            At(new DateTime(2026, 2, 2), "P2"),
        ];

        var resolved = UnrecordedTimeAssignmentHelper.Resolve(ordered, new DateTime(2026, 2, 1));

        await Assert.That(resolved!.ProjectCodeId).IsEqualTo("P1");
    }

    [Test]
    public async Task 最初の割り当てより前の日は該当なし()
    {
        List<UnrecordedTimeProjectAssignment> ordered = [At(new DateTime(2026, 1, 5), "P1")];

        var resolved = UnrecordedTimeAssignmentHelper.Resolve(ordered, new DateTime(2026, 1, 4));

        await Assert.That(resolved).IsNull();
    }

    [Test]
    public async Task 割り当てが空なら該当なし()
    {
        var resolved = UnrecordedTimeAssignmentHelper.Resolve(NoAssignments, new DateTime(2026, 1, 5));

        await Assert.That(resolved).IsNull();
    }

    [Test]
    public async Task 時刻付きの日付でも日付部分で判定する()
    {
        List<UnrecordedTimeProjectAssignment> ordered = [At(new DateTime(2026, 2, 2), "P2")];

        var resolved = UnrecordedTimeAssignmentHelper.Resolve(
            ordered, new DateTime(2026, 2, 2, 23, 59, 0));

        await Assert.That(resolved!.ProjectCodeId).IsEqualTo("P2");
    }

    // ============================================================
    // 期間の表示
    // ============================================================

    [Test]
    public async Task 終了日は次の行の開始日の前日になる()
    {
        List<UnrecordedTimeProjectAssignment> ordered =
        [
            At(new DateTime(2026, 1, 5), "P1"),
            At(new DateTime(2026, 2, 2), "P2"),
        ];

        var text = UnrecordedTimeAssignmentHelper.BuildRangeText(ordered, 0);

        await Assert.That(text).IsEqualTo("2026/01/05 〜 2026/02/01");
    }

    [Test]
    public async Task 最後の行は以降ずっとになる()
    {
        List<UnrecordedTimeProjectAssignment> ordered =
        [
            At(new DateTime(2026, 1, 5), "P1"),
            At(new DateTime(2026, 2, 2), "P2"),
        ];

        var text = UnrecordedTimeAssignmentHelper.BuildRangeText(ordered, 1);

        await Assert.That(text).IsEqualTo("2026/02/02 〜 （以降ずっと）");
    }

    [Test]
    public async Task 開始日が同じ行が続くと上書きされる旨を出す()
    {
        List<UnrecordedTimeProjectAssignment> ordered =
        [
            At(new DateTime(2026, 2, 2), "P1"),
            At(new DateTime(2026, 2, 2), "P2"),
        ];

        var text = UnrecordedTimeAssignmentHelper.BuildRangeText(ordered, 0);

        await Assert.That(text).IsEqualTo("2026/02/02（次の行に上書きされます）");
    }

    [Test]
    public async Task 一日だけ受け持つ行も期間として出す()
    {
        List<UnrecordedTimeProjectAssignment> ordered =
        [
            At(new DateTime(2026, 2, 2), "P1"),
            At(new DateTime(2026, 2, 3), "P2"),
        ];

        var text = UnrecordedTimeAssignmentHelper.BuildRangeText(ordered, 0);

        await Assert.That(text).IsEqualTo("2026/02/02 〜 2026/02/02");
    }
}
