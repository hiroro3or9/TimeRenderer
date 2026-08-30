using System;

using TimeRenderer.Helpers;
using TimeRenderer.Models;

using TUnit.Assertions;
using TUnit.Assertions.Extensions;
using TUnit.Core;

namespace TimeRenderer.Tests;

/// <summary>完了済みToDoをアーカイブへ移す日付境界を固定する。</summary>
public class TodoArchiveHelperTests
{
    private static readonly DateTime Today = new(2026, 8, 30, 18, 0, 0);

    [Test]
    public async Task 保持期間より前に完了したToDoだけを対象にする()
    {
        var old = CompletedAt(new DateTime(2026, 5, 31));
        var atCutoff = CompletedAt(new DateTime(2026, 6, 1));
        var recent = CompletedAt(new DateTime(2026, 8, 29));

        var targets = TodoArchiveHelper.GetArchiveTargets(
            [old, atCutoff, recent], Today, retentionDays: 90);

        await Assert.That(targets.Count).IsEqualTo(1);
        await Assert.That(targets[0]).IsSameReferenceAs(old);
    }

    [Test]
    public async Task 古くても未完了または完了日時なしなら対象にしない()
    {
        var incomplete = new TodoItem
        {
            IsCompleted = false,
            CompletedAt = new DateTime(2020, 1, 1),
        };
        var missingDate = new TodoItem { IsCompleted = true, CompletedAt = null };

        var targets = TodoArchiveHelper.GetArchiveTargets(
            [incomplete, missingDate], Today, retentionDays: 90);

        await Assert.That(targets).IsEmpty();
    }

    [Test]
    public async Task 基準日時に時刻があっても日付単位で判定する()
    {
        var atCutoffLate = CompletedAt(new DateTime(2026, 6, 1, 23, 59, 59));

        var targets = TodoArchiveHelper.GetArchiveTargets(
            [atCutoffLate], Today, retentionDays: 90);

        await Assert.That(targets).IsEmpty();
    }

    private static TodoItem CompletedAt(DateTime completedAt) => new()
    {
        IsCompleted = true,
        CompletedAt = completedAt,
    };
}
