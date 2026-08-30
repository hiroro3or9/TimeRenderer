using System;

using TimeRenderer.Helpers;

using TUnit.Assertions;
using TUnit.Assertions.Extensions;
using TUnit.Core;

namespace TimeRenderer.Tests;

/// <summary>朝のToDoまとめ通知の日付・時刻境界と本文を固定する。</summary>
public class TodoDigestHelperTests
{
    private static readonly DateTime TodayAtNine = new(2026, 8, 30, 9, 0, 0);

    [Test]
    public async Task 有効で当日未実行かつ設定時刻なら実行する()
    {
        var shouldRun = TodoDigestHelper.ShouldRun(
            true, TodayAtNine.AddDays(-1), TodayAtNine, digestHour: 9);

        await Assert.That(shouldRun).IsTrue();
    }

    [Test]
    public async Task 無効同日実行済み設定時刻前は実行しない()
    {
        await Assert.That(TodoDigestHelper.ShouldRun(
            false, default, TodayAtNine, 9)).IsFalse();
        await Assert.That(TodoDigestHelper.ShouldRun(
            true, TodayAtNine.Date, TodayAtNine.AddHours(5), 9)).IsFalse();
        await Assert.That(TodoDigestHelper.ShouldRun(
            true, TodayAtNine.AddDays(-1), TodayAtNine.AddTicks(-1), 9)).IsFalse();
    }

    [Test]
    public async Task 設定時刻を過ぎて起動しても当日分を実行する()
    {
        var shouldRun = TodoDigestHelper.ShouldRun(
            true, default, TodayAtNine.AddHours(8), digestHour: 9);

        await Assert.That(shouldRun).IsTrue();
    }

    [Test]
    public async Task 今日と超過の件数を組み合わせて本文を作る()
    {
        await Assert.That(TodoDigestHelper.BuildNotice(2, 3)).IsEqualTo(
            "今日が期限の ToDo が 2 件、期限を過ぎた ToDo が 3 件 あります。");
        await Assert.That(TodoDigestHelper.BuildNotice(2, 0)).IsEqualTo(
            "今日が期限の ToDo が 2 件 あります。");
        await Assert.That(TodoDigestHelper.BuildNotice(0, 3)).IsEqualTo(
            "期限を過ぎた ToDo が 3 件 あります。");
        await Assert.That(TodoDigestHelper.BuildNotice(0, 0)).IsNull();
    }

    [Test]
    public async Task 最終通知日はカルチャに依存しない形式で往復する()
    {
        var date = new DateTime(2026, 8, 30, 18, 45, 0);
        var serialized = TodoDigestHelper.FormatLastRunDate(date);
        var restored = TodoDigestHelper.ParseLastRunDate(serialized);

        await Assert.That(serialized).IsEqualTo("2026-08-30");
        await Assert.That(restored).IsEqualTo(date.Date);
        await Assert.That(TodoDigestHelper.FormatLastRunDate(default)).IsNull();
        await Assert.That(TodoDigestHelper.ParseLastRunDate("壊れた値")).IsEqualTo(default(DateTime));
    }
}
