using System;
using System.Collections.Generic;

using TimeRenderer.Helpers;
using TimeRenderer.Models;

using TUnit.Assertions;
using TUnit.Assertions.Extensions;
using TUnit.Core;

namespace TimeRenderer.Tests;

/// <summary>
/// <see cref="SprintHelper"/> の特性テスト。
///
/// スプリントは「手動で登録した期間はそのまま使い、その前後は2週間で自動補間する」
/// という規則で並ぶ。日付の境界（自動の切れ目、手動の直前でのカット、過去方向の生成）は
/// 画面上すぐには誤りに気づけないため、期待値を固定しておく。
///
/// <see cref="SprintHelper.DefaultReferenceDate"/> は 2026-01-05（月曜）。
/// 手動スプリントが1件も無いときの基準になる。
/// </summary>
public class SprintHelperTests
{
    private static readonly List<SprintInfo> NoManualSprints = [];

    private static SprintInfo Manual(
        string name, DateTime start, DateTime end, string? id = null, string? projectCodeId = null) =>
        new()
        {
            Id = id ?? Guid.NewGuid().ToString(),
            Name = name,
            StartDate = start,
            EndDate = end,
            IsManual = true,
            UnrecordedTimeProjectCodeId = projectCodeId,
        };

    // ============================================================
    // 自動補間（手動スプリントが無い場合）
    // ============================================================

    [Test]
    public async Task 既定の基準日は2026年最初の月曜日()
    {
        await Assert.That(SprintHelper.DefaultReferenceDate).IsEqualTo(new DateTime(2026, 1, 5));
        await Assert.That(SprintHelper.DefaultReferenceDate.DayOfWeek).IsEqualTo(DayOfWeek.Monday);
    }

    [Test]
    public async Task 手動スプリントが無ければ基準日から2週間ごとに生成する()
    {
        var sprints = SprintHelper.GetSprintsForRange(
            NoManualSprints, new DateTime(2026, 1, 5), new DateTime(2026, 1, 20));

        await Assert.That(sprints.Count).IsEqualTo(2);

        await Assert.That(sprints[0].StartDate).IsEqualTo(new DateTime(2026, 1, 5));
        await Assert.That(sprints[0].EndDate).IsEqualTo(new DateTime(2026, 1, 18));
        await Assert.That(sprints[0].IsManual).IsFalse();

        await Assert.That(sprints[1].StartDate).IsEqualTo(new DateTime(2026, 1, 19));
        await Assert.That(sprints[1].EndDate).IsEqualTo(new DateTime(2026, 2, 1));
    }

    [Test]
    public async Task 自動生成したスプリントは14日間になる()
    {
        var sprints = SprintHelper.GetSprintsForRange(
            NoManualSprints, new DateTime(2026, 1, 5), new DateTime(2026, 3, 1));

        foreach (var sprint in sprints)
        {
            var days = (sprint.EndDate - sprint.StartDate).Days + 1;
            await Assert.That(days).IsEqualTo(14);
        }
    }

    [Test]
    public async Task 自動生成したスプリントは隙間なく連続する()
    {
        var sprints = SprintHelper.GetSprintsForRange(
            NoManualSprints, new DateTime(2026, 1, 5), new DateTime(2026, 4, 1));

        for (var i = 1; i < sprints.Count; i++)
        {
            await Assert.That(sprints[i].StartDate).IsEqualTo(sprints[i - 1].EndDate.AddDays(1));
        }
    }

    [Test]
    public async Task 基準日より前を要求したら過去方向へも生成する()
    {
        var sprints = SprintHelper.GetSprintsForRange(
            NoManualSprints, new DateTime(2025, 12, 25), new DateTime(2026, 1, 10));

        await Assert.That(sprints.Count).IsEqualTo(2);

        // 基準日の直前は 12/22〜1/4
        await Assert.That(sprints[0].StartDate).IsEqualTo(new DateTime(2025, 12, 22));
        await Assert.That(sprints[0].EndDate).IsEqualTo(new DateTime(2026, 1, 4));
        await Assert.That(sprints[0].IsManual).IsFalse();

        await Assert.That(sprints[1].StartDate).IsEqualTo(SprintHelper.DefaultReferenceDate);
    }

    [Test]
    public async Task 結果は常に開始日の昇順で返る()
    {
        var sprints = SprintHelper.GetSprintsForRange(
            NoManualSprints, new DateTime(2025, 11, 1), new DateTime(2026, 3, 1));

        for (var i = 1; i < sprints.Count; i++)
        {
            await Assert.That(sprints[i].StartDate > sprints[i - 1].StartDate).IsTrue();
        }
    }

    // ============================================================
    // 手動スプリントの採用
    // ============================================================

    [Test]
    public async Task 手動スプリントの期間はそのまま採用する()
    {
        List<SprintInfo> manuals =
        [
            Manual("Sprint A", new DateTime(2026, 3, 2), new DateTime(2026, 3, 15), id: "m1"),
        ];

        var sprints = SprintHelper.GetSprintsForRange(
            manuals, new DateTime(2026, 3, 2), new DateTime(2026, 3, 20));

        await Assert.That(sprints.Count).IsEqualTo(2);

        await Assert.That(sprints[0].Id).IsEqualTo("m1");
        await Assert.That(sprints[0].Name).IsEqualTo("Sprint A");
        await Assert.That(sprints[0].IsManual).IsTrue();
        await Assert.That(sprints[0].StartDate).IsEqualTo(new DateTime(2026, 3, 2));
        await Assert.That(sprints[0].EndDate).IsEqualTo(new DateTime(2026, 3, 15));

        // 手動スプリントの後ろは自動生成が続く
        await Assert.That(sprints[1].IsManual).IsFalse();
        await Assert.That(sprints[1].StartDate).IsEqualTo(new DateTime(2026, 3, 16));
        await Assert.That(sprints[1].EndDate).IsEqualTo(new DateTime(2026, 3, 29));
    }

    [Test]
    public async Task 手動スプリントは長さが14日でなくても尊重される()
    {
        // 3日だけのスプリント
        List<SprintInfo> manuals =
        [
            Manual("短期", new DateTime(2026, 3, 2), new DateTime(2026, 3, 4)),
        ];

        // 手動の直後まで見えるよう、範囲は手動スプリントより後ろまで取る
        var sprints = SprintHelper.GetSprintsForRange(
            manuals, new DateTime(2026, 3, 2), new DateTime(2026, 3, 10));

        await Assert.That(sprints.Count).IsEqualTo(2);
        await Assert.That(sprints[0].IsManual).IsTrue();
        await Assert.That(sprints[0].EndDate).IsEqualTo(new DateTime(2026, 3, 4));
        // 直後は翌日から自動生成が再開する
        await Assert.That(sprints[1].StartDate).IsEqualTo(new DateTime(2026, 3, 5));
    }

    [Test]
    public async Task 手動スプリントの間の隙間を自動生成で埋める()
    {
        List<SprintInfo> manuals =
        [
            Manual("M1", new DateTime(2026, 2, 2), new DateTime(2026, 2, 15)),
            Manual("M2", new DateTime(2026, 3, 10), new DateTime(2026, 3, 23)),
        ];

        var sprints = SprintHelper.GetSprintsForRange(
            manuals, new DateTime(2026, 2, 2), new DateTime(2026, 3, 25));

        await Assert.That(sprints.Count).IsEqualTo(5);

        await Assert.That(sprints[0].Name).IsEqualTo("M1");
        await Assert.That(sprints[0].IsManual).IsTrue();

        // 隙間の1本目は通常どおり14日
        await Assert.That(sprints[1].IsManual).IsFalse();
        await Assert.That(sprints[1].StartDate).IsEqualTo(new DateTime(2026, 2, 16));
        await Assert.That(sprints[1].EndDate).IsEqualTo(new DateTime(2026, 3, 1));

        await Assert.That(sprints[3].Name).IsEqualTo("M2");
        await Assert.That(sprints[3].IsManual).IsTrue();
    }

    [Test]
    public async Task 隙間の最後は次の手動スプリントの直前でカットする()
    {
        List<SprintInfo> manuals =
        [
            Manual("M1", new DateTime(2026, 2, 2), new DateTime(2026, 2, 15)),
            Manual("M2", new DateTime(2026, 3, 10), new DateTime(2026, 3, 23)),
        ];

        var sprints = SprintHelper.GetSprintsForRange(
            manuals, new DateTime(2026, 2, 2), new DateTime(2026, 3, 25));

        // 3/2 から14日なら 3/15 だが、次の手動が 3/10 なので 3/9 で切る
        await Assert.That(sprints[2].IsManual).IsFalse();
        await Assert.That(sprints[2].StartDate).IsEqualTo(new DateTime(2026, 3, 2));
        await Assert.That(sprints[2].EndDate).IsEqualTo(new DateTime(2026, 3, 9));

        // カットしても手動スプリントとは重ならない
        await Assert.That(sprints[2].EndDate < sprints[3].StartDate).IsTrue();
    }

    [Test]
    public async Task 手動スプリントを渡す順序は結果に影響しない()
    {
        List<SprintInfo> ordered =
        [
            Manual("M1", new DateTime(2026, 2, 2), new DateTime(2026, 2, 15)),
            Manual("M2", new DateTime(2026, 3, 10), new DateTime(2026, 3, 23)),
        ];
        List<SprintInfo> reversed =
        [
            Manual("M2", new DateTime(2026, 3, 10), new DateTime(2026, 3, 23)),
            Manual("M1", new DateTime(2026, 2, 2), new DateTime(2026, 2, 15)),
        ];

        var from = new DateTime(2026, 2, 2);
        var to = new DateTime(2026, 3, 25);

        var a = SprintHelper.GetSprintsForRange(ordered, from, to);
        var b = SprintHelper.GetSprintsForRange(reversed, from, to);

        await Assert.That(b.Count).IsEqualTo(a.Count);
        for (var i = 0; i < a.Count; i++)
        {
            await Assert.That(b[i].StartDate).IsEqualTo(a[i].StartDate);
            await Assert.That(b[i].EndDate).IsEqualTo(a[i].EndDate);
            await Assert.That(b[i].IsManual).IsEqualTo(a[i].IsManual);
        }
    }

    [Test]
    public async Task 自動生成として渡されたスプリントは手動として扱わない()
    {
        // IsManual = false のものは基準日の決定にも採用にも使われない
        List<SprintInfo> notManual =
        [
            new()
            {
                Name = "自動のつもり",
                StartDate = new DateTime(2026, 3, 2),
                EndDate = new DateTime(2026, 3, 15),
                IsManual = false,
            },
        ];

        var sprints = SprintHelper.GetSprintsForRange(
            notManual, new DateTime(2026, 1, 5), new DateTime(2026, 1, 20));

        // 既定の基準日から自動生成された結果と同じになる
        await Assert.That(sprints.Count).IsEqualTo(2);
        await Assert.That(sprints[0].StartDate).IsEqualTo(SprintHelper.DefaultReferenceDate);
    }

    // ============================================================
    // 日付からスプリントを引く
    //
    // GetSprintForDate は static なキャッシュ（_cachedSprints / _cacheSource）を持つ。
    // TUnit は既定で並列実行するため、キャッシュを共有するテストは [NotInParallel] にする。
    // ============================================================

    [Test]
    [NotInParallel]
    public async Task 日付を含む自動スプリントを返す()
    {
        var sprint = SprintHelper.GetSprintForDate(NoManualSprints, new DateTime(2026, 1, 10));

        await Assert.That(sprint.StartDate).IsEqualTo(new DateTime(2026, 1, 5));
        await Assert.That(sprint.EndDate).IsEqualTo(new DateTime(2026, 1, 18));
        await Assert.That(sprint.IsManual).IsFalse();
    }

    [Test]
    [NotInParallel]
    public async Task 日付を含む手動スプリントを返す()
    {
        List<SprintInfo> manuals =
        [
            Manual("Sprint A", new DateTime(2026, 3, 2), new DateTime(2026, 3, 15), id: "m1"),
        ];

        var sprint = SprintHelper.GetSprintForDate(manuals, new DateTime(2026, 3, 5));

        await Assert.That(sprint.Name).IsEqualTo("Sprint A");
        await Assert.That(sprint.IsManual).IsTrue();
        await Assert.That(sprint.Id).IsEqualTo("m1");
    }

    [Test]
    [NotInParallel]
    public async Task 手動スプリントを差し替えたら結果も切り替わる()
    {
        // キャッシュは手動スプリント一覧の参照が変わったときに作り直される。
        // ここが壊れると、設定を変えても古いスプリントを返し続ける。
        var target = new DateTime(2026, 3, 5);

        List<SprintInfo> before = [Manual("旧", new DateTime(2026, 3, 2), new DateTime(2026, 3, 15))];
        var first = SprintHelper.GetSprintForDate(before, target);
        await Assert.That(first.Name).IsEqualTo("旧");

        List<SprintInfo> after = [Manual("新", new DateTime(2026, 3, 2), new DateTime(2026, 3, 15))];
        var second = SprintHelper.GetSprintForDate(after, target);
        await Assert.That(second.Name).IsEqualTo("新");
    }

    [Test]
    [NotInParallel]
    public async Task 同じ日付を続けて引いても同じ期間を返す()
    {
        var target = new DateTime(2026, 5, 20);

        var first = SprintHelper.GetSprintForDate(NoManualSprints, target);
        var second = SprintHelper.GetSprintForDate(NoManualSprints, target);

        await Assert.That(second.StartDate).IsEqualTo(first.StartDate);
        await Assert.That(second.EndDate).IsEqualTo(first.EndDate);
    }

    [Test]
    [NotInParallel]
    public async Task 返したスプリントは必ずその日付を含む()
    {
        List<SprintInfo> manuals =
        [
            Manual("M1", new DateTime(2026, 2, 2), new DateTime(2026, 2, 15)),
        ];

        foreach (var offset in new[] { -40, -14, -1, 0, 1, 14, 40, 100 })
        {
            var target = new DateTime(2026, 2, 10).AddDays(offset);
            var sprint = SprintHelper.GetSprintForDate(manuals, target);

            await Assert.That(target >= sprint.StartDate && target <= sprint.EndDate).IsTrue();
        }
    }

    // ============================================================
    // 未記録時間の加算先
    // ============================================================

    [Test]
    public async Task 加算先を持つ手動スプリントだけを開始日順に取り出す()
    {
        List<SprintInfo> manuals =
        [
            Manual("後", new DateTime(2026, 3, 2), new DateTime(2026, 3, 15), projectCodeId: "P2"),
            Manual("先", new DateTime(2026, 2, 2), new DateTime(2026, 2, 15), projectCodeId: "P1"),
            Manual("加算先なし", new DateTime(2026, 1, 5), new DateTime(2026, 1, 18)),
        ];

        var sources = SprintHelper.GetUnrecordedTimeProjectCodeSources(manuals);

        await Assert.That(sources.Count).IsEqualTo(2);
        await Assert.That(sources[0].Name).IsEqualTo("先");
        await Assert.That(sources[1].Name).IsEqualTo("後");
    }

    [Test]
    public async Task 加算先が空文字のスプリントは取り出さない()
    {
        List<SprintInfo> manuals =
        [
            Manual("空文字", new DateTime(2026, 2, 2), new DateTime(2026, 2, 15), projectCodeId: string.Empty),
        ];

        var sources = SprintHelper.GetUnrecordedTimeProjectCodeSources(manuals);

        await Assert.That(sources.Count).IsEqualTo(0);
    }

    [Test]
    public async Task 自動生成のスプリントは加算先を持っていても取り出さない()
    {
        // 自動生成分は保存されないため、引き継ぎ元になれない
        List<SprintInfo> sprints =
        [
            new()
            {
                Name = "自動",
                StartDate = new DateTime(2026, 2, 2),
                EndDate = new DateTime(2026, 2, 15),
                IsManual = false,
                UnrecordedTimeProjectCodeId = "P1",
            },
        ];

        var sources = SprintHelper.GetUnrecordedTimeProjectCodeSources(sprints);

        await Assert.That(sources.Count).IsEqualTo(0);
    }

    [Test]
    public async Task 手動スプリントが空でも落ちない()
    {
        var sources = SprintHelper.GetUnrecordedTimeProjectCodeSources(NoManualSprints);

        await Assert.That(sources.Count).IsEqualTo(0);
    }
}
