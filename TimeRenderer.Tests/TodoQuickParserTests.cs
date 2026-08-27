using System;
using System.Collections.Generic;

using TimeRenderer.Helpers;
using TimeRenderer.Models;

using TUnit.Assertions;
using TUnit.Assertions.Extensions;
using TUnit.Core;

namespace TimeRenderer.Tests;

/// <summary>
/// <see cref="TodoQuickParser"/> の特性テスト。
///
/// 「思いついた瞬間に置ける」ことを壊さないのがこのクラスの設計方針なので、
/// 記法が効く場合の解釈だけでなく、
/// <b>効かない場合に入力がそのまま本文へ残ること</b>を同じ重みで固定する。
///
/// 基準時刻は 2026-08-27(木) 10:30 に固定する。
/// DateTime.Now を使うと「今日と同じ曜日は来週」「時刻を過ぎたら明日」の分岐が
/// 実行日によって変わり、テストが日替わりで落ちるため。
/// </summary>
public class TodoQuickParserTests
{
    /// <summary>基準時刻。2026-08-27 は木曜日</summary>
    private static readonly DateTime Now = new(2026, 8, 27, 10, 30, 0);

    private static DateTime Today => Now.Date;

    private static List<CategoryInfo> Categories() =>
    [
        new() { Name = "開発" },
        new() { Name = "会議" },
    ];

    // ============================================================
    // 本文の保護
    //
    // TodoQuickParser の中心方針。解釈できなかった記号は本文に残す。
    // ここが崩れると「#1 の対応」のような普通の文章が黙って削られる。
    // ============================================================

    [Test]
    public async Task 記法が無い入力はそのまま本文になる()
    {
        var result = TodoQuickParser.Parse("ただのメモ", Categories(), Now);

        await Assert.That(result.Title).IsEqualTo("ただのメモ");
        await Assert.That(result.HasAttributes).IsFalse();
        await Assert.That(result.Summary.Count).IsEqualTo(0);
    }

    [Test]
    public async Task 一致するカテゴリが無い井桁は本文に残る()
    {
        var result = TodoQuickParser.Parse("#1 の対応", Categories(), Now);

        await Assert.That(result.Title).IsEqualTo("#1 の対応");
        await Assert.That(result.Category).IsNull();
    }

    [Test]
    public async Task 記号の前に空白が無ければ見積もりとして解釈しない()
    {
        // "9時〜10時" の 〜 が見積もりに化けると、時間帯の記述が壊れる
        var result = TodoQuickParser.Parse("9時〜10時 打ち合わせ", Categories(), Now);

        await Assert.That(result.Title).IsEqualTo("9時〜10時 打ち合わせ");
        await Assert.That(result.EstimatedMinutes.HasValue).IsFalse();
    }

    [Test]
    public async Task 記号の前に空白が無ければ通知として解釈しない()
    {
        // "腕立て*30回" の * が通知に化けると、回数の記述が壊れる
        var result = TodoQuickParser.Parse("腕立て*30回", Categories(), Now);

        await Assert.That(result.Title).IsEqualTo("腕立て*30回");
        await Assert.That(result.RemindAt.HasValue).IsFalse();
    }

    [Test]
    public async Task 日付として読めないアットマークは本文に残る()
    {
        var result = TodoQuickParser.Parse("@会議室で相談", Categories(), Now);

        await Assert.That(result.Title).IsEqualTo("@会議室で相談");
        await Assert.That(result.DueDate.HasValue).IsFalse();
    }

    [Test]
    public async Task 単位の無い数字に区切りが続かなければ見積もりにしない()
    {
        // "~50%" を 50分 と読まない
        var result = TodoQuickParser.Parse("~50% の進捗", Categories(), Now);

        await Assert.That(result.Title).IsEqualTo("~50% の進捗");
        await Assert.That(result.EstimatedMinutes.HasValue).IsFalse();
    }

    [Test]
    public async Task 存在しない日付は本文に残る()
    {
        var result = TodoQuickParser.Parse("@2/30 X", Categories(), Now);

        await Assert.That(result.Title).IsEqualTo("@2/30 X");
        await Assert.That(result.DueDate.HasValue).IsFalse();
    }

    [Test]
    public async Task 二つ目の期限指定は本文に残る()
    {
        var result = TodoQuickParser.Parse("@明日 @来週 X", Categories(), Now);

        await Assert.That(result.DueDate).IsEqualTo(Today.AddDays(1));
        await Assert.That(result.Title).IsEqualTo("@来週 X");
    }

    [Test]
    public async Task 優先度として読めない記号は本文に残る()
    {
        var result = TodoQuickParser.Parse("!x X", Categories(), Now);

        await Assert.That(result.Title).IsEqualTo("!x X");
        await Assert.That(result.Priority.HasValue).IsFalse();
    }

    // ============================================================
    // 期限（@）
    // ============================================================

    [Test]
    public async Task 記号の直後に続けて書いても本文が壊れない()
    {
        // 日本語は単語を空白で区切らないので、この書き方を許すのが方針
        var result = TodoQuickParser.Parse("@明日資料をまとめる", Categories(), Now);

        await Assert.That(result.DueDate).IsEqualTo(Today.AddDays(1));
        await Assert.That(result.Title).IsEqualTo("資料をまとめる");
    }

    [Test]
    [Arguments("@今日 X", 0)]
    [Arguments("@きょう X", 0)]
    [Arguments("@本日 X", 0)]
    [Arguments("@today X", 0)]
    [Arguments("@明日 X", 1)]
    [Arguments("@あした X", 1)]
    [Arguments("@あす X", 1)]
    [Arguments("@tomorrow X", 1)]
    [Arguments("@明後日 X", 2)]
    [Arguments("@あさって X", 2)]
    public async Task 相対語の期限を今日からの日数で解釈する(string input, int expectedOffsetDays)
    {
        var result = TodoQuickParser.Parse(input, Categories(), Now);

        await Assert.That(result.DueDate).IsEqualTo(Today.AddDays(expectedOffsetDays));
        await Assert.That(result.Title).IsEqualTo("X");
    }

    [Test]
    [Arguments("@+3 X", 3)]     // 単位なしは日
    [Arguments("@+3d X", 3)]
    [Arguments("@+3日 X", 3)]
    [Arguments("@+2w X", 14)]   // 週は7倍
    [Arguments("@+2週 X", 14)]
    public async Task プラス記法の期限を日数で解釈する(string input, int expectedOffsetDays)
    {
        var result = TodoQuickParser.Parse(input, Categories(), Now);

        await Assert.That(result.DueDate).IsEqualTo(Today.AddDays(expectedOffsetDays));
    }

    [Test]
    public async Task 来週と再来週は月曜始まりでそろえる()
    {
        // 基準は 2026-08-27(木)。その週の月曜は 08-24
        var nextWeek = TodoQuickParser.Parse("@来週 X", Categories(), Now);
        var weekAfter = TodoQuickParser.Parse("@再来週 X", Categories(), Now);

        await Assert.That(nextWeek.DueDate).IsEqualTo(new DateTime(2026, 8, 31));
        await Assert.That(weekAfter.DueDate).IsEqualTo(new DateTime(2026, 9, 7));
    }

    [Test]
    public async Task 週末は今週の土曜を指す()
    {
        var result = TodoQuickParser.Parse("@今週末 X", Categories(), Now);

        await Assert.That(result.DueDate).IsEqualTo(new DateTime(2026, 8, 29));
    }

    [Test]
    public async Task 来月は翌月の一日を指す()
    {
        var result = TodoQuickParser.Parse("@来月 X", Categories(), Now);

        await Assert.That(result.DueDate).IsEqualTo(new DateTime(2026, 9, 1));
    }

    [Test]
    public async Task 曜日指定は次に来るその曜日を指す()
    {
        var monday = TodoQuickParser.Parse("@月 X", Categories(), Now);

        await Assert.That(monday.DueDate).IsEqualTo(new DateTime(2026, 8, 31));
    }

    [Test]
    public async Task 今日と同じ曜日を指定したら来週になる()
    {
        // 基準は木曜。"@木" は今日ではなく来週の木曜
        var result = TodoQuickParser.Parse("@木 X", Categories(), Now);

        await Assert.That(result.DueDate).IsEqualTo(new DateTime(2026, 9, 3));
    }

    [Test]
    public async Task 年を含む日付をそのまま読む()
    {
        var result = TodoQuickParser.Parse("@2026/9/1 X", Categories(), Now);

        await Assert.That(result.DueDate).IsEqualTo(new DateTime(2026, 9, 1));
    }

    [Test]
    public async Task 月日だけの指定は今年として読む()
    {
        var result = TodoQuickParser.Parse("@9/1 X", Categories(), Now);

        await Assert.That(result.DueDate).IsEqualTo(new DateTime(2026, 9, 1));
    }

    [Test]
    public async Task 半年以上前になる月日は翌年として読む()
    {
        // 基準は 8/27。"1/5" を今年と読むと 234 日前になるので翌年に送る
        var result = TodoQuickParser.Parse("@1/5 X", Categories(), Now);

        await Assert.That(result.DueDate).IsEqualTo(new DateTime(2027, 1, 5));
    }

    // ============================================================
    // 優先度（!）
    // ============================================================

    [Test]
    [Arguments("!高 X", TodoPriority.High)]
    [Arguments("!low X", TodoPriority.Low)]
    [Arguments("!低 X", TodoPriority.Low)]
    [Arguments("!中 X", TodoPriority.Normal)]
    [Arguments("!標準 X", TodoPriority.Normal)]
    [Arguments("!high X", TodoPriority.High)]
    [Arguments("!normal X", TodoPriority.Normal)]
    [Arguments("!h X", TodoPriority.High)]
    [Arguments("!n X", TodoPriority.Normal)]
    [Arguments("!l X", TodoPriority.Low)]
    public async Task 優先度を解釈する(string input, TodoPriority expected)
    {
        var result = TodoQuickParser.Parse(input, Categories(), Now);

        await Assert.That(result.Priority).IsEqualTo(expected);
        await Assert.That(result.Title).IsEqualTo("X");
    }

    [Test]
    public async Task 全角の記号も受け付ける()
    {
        // 日本語入力のまま打てないと、そもそも使われない
        var result = TodoQuickParser.Parse("＠明日 ！高 X", Categories(), Now);

        await Assert.That(result.DueDate).IsEqualTo(Today.AddDays(1));
        await Assert.That(result.Priority).IsEqualTo(TodoPriority.High);
        await Assert.That(result.Title).IsEqualTo("X");
    }

    // ============================================================
    // 見積もり（~）
    // ============================================================

    [Test]
    [Arguments("~30m X", 30)]
    [Arguments("~30分 X", 30)]
    [Arguments("~45 X", 45)]          // 単位省略。後ろに区切りが要る
    [Arguments("~1時間 X", 60)]
    [Arguments("~1h X", 60)]
    [Arguments("~1時間30分 X", 90)]
    [Arguments("~2h30 X", 150)]
    [Arguments("~1.5h X", 90)]        // 小数の時間は分へ丸める
    public async Task 見積もりを分として解釈する(string input, int expectedMinutes)
    {
        var result = TodoQuickParser.Parse(input, Categories(), Now);

        await Assert.That(result.EstimatedMinutes).IsEqualTo(expectedMinutes);
        await Assert.That(result.Title).IsEqualTo("X");
    }

    // ============================================================
    // カテゴリ（#）
    // ============================================================

    [Test]
    public async Task 名前が完全に一致するカテゴリを取り込む()
    {
        var result = TodoQuickParser.Parse("#開発 X", Categories(), Now);

        await Assert.That(result.Category).IsNotNull();
        await Assert.That(result.Category!.Name).IsEqualTo("開発");
        await Assert.That(result.Title).IsEqualTo("X");
    }

    [Test]
    public async Task 一致した分だけ取り込み残りは本文へ戻す()
    {
        // "#開発資料" は カテゴリ=開発 ／ 本文="資料"
        var result = TodoQuickParser.Parse("#開発資料 X", Categories(), Now);

        await Assert.That(result.Category).IsNotNull();
        await Assert.That(result.Category!.Name).IsEqualTo("開発");
        await Assert.That(result.Title).IsEqualTo("資料 X");
    }

    [Test]
    public async Task 前方一致が一つだけなら採用する()
    {
        var result = TodoQuickParser.Parse("#開 X", Categories(), Now);

        await Assert.That(result.Category).IsNotNull();
        await Assert.That(result.Category!.Name).IsEqualTo("開発");
        await Assert.That(result.Title).IsEqualTo("X");
    }

    [Test]
    public async Task 前方一致が複数あるときは本文に残す()
    {
        List<CategoryInfo> ambiguous =
        [
            new() { Name = "開発A" },
            new() { Name = "開発B" },
        ];

        var result = TodoQuickParser.Parse("#開発 X", ambiguous, Now);

        await Assert.That(result.Category).IsNull();
        await Assert.That(result.Title).IsEqualTo("#開発 X");
    }

    [Test]
    public async Task カテゴリ候補がnullなら井桁は本文に残る()
    {
        var result = TodoQuickParser.Parse("#開発 X", null, Now);

        await Assert.That(result.Category).IsNull();
        await Assert.That(result.Title).IsEqualTo("#開発 X");
    }

    // ============================================================
    // 通知（*）
    //
    // TodoQuickParser.DefaultRemindHour は static な可変状態のため、
    // 並列実行すると値の書き換えが他のテストへ漏れる。
    // TUnit は既定で並列実行するので [NotInParallel] を付ける。
    // ============================================================

    [Test]
    [NotInParallel]
    public async Task 時刻だけの通知は期限が無ければ今日を使う()
    {
        TodoQuickParser.DefaultRemindHour = 9;

        // 基準は 10:30。18:00 はまだ来ていないので今日のまま
        var result = TodoQuickParser.Parse("*18:00 X", Categories(), Now);

        await Assert.That(result.RemindAt).IsEqualTo(new DateTime(2026, 8, 27, 18, 0, 0));
        await Assert.That(result.RemindOffsetDays.HasValue).IsFalse();
        await Assert.That(result.Title).IsEqualTo("X");
    }

    [Test]
    [NotInParallel]
    public async Task 指定時刻を過ぎていれば翌日にずらす()
    {
        TodoQuickParser.DefaultRemindHour = 9;

        // 基準は 10:30。09:00 は過ぎているので明日
        var result = TodoQuickParser.Parse("*9:00 X", Categories(), Now);

        await Assert.That(result.RemindAt).IsEqualTo(new DateTime(2026, 8, 28, 9, 0, 0));
    }

    [Test]
    [NotInParallel]
    public async Task 期限があれば通知は期限の当日になる()
    {
        TodoQuickParser.DefaultRemindHour = 9;

        var result = TodoQuickParser.Parse("@明日 *14:30 X", Categories(), Now);

        await Assert.That(result.RemindAt).IsEqualTo(new DateTime(2026, 8, 28, 14, 30, 0));
        // 日付を書かなかったので「期限からの相対」として覚える
        await Assert.That(result.RemindOffsetDays).IsEqualTo(0);
    }

    [Test]
    [NotInParallel]
    public async Task 前日指定は期限の一日前になる()
    {
        TodoQuickParser.DefaultRemindHour = 9;

        var result = TodoQuickParser.Parse("@明日 *前日 X", Categories(), Now);

        await Assert.That(result.RemindAt).IsEqualTo(new DateTime(2026, 8, 27, 9, 0, 0));
        await Assert.That(result.RemindOffsetDays).IsEqualTo(1);
    }

    [Test]
    [NotInParallel]
    public async Task N日前指定は期限からその日数だけ遡る()
    {
        TodoQuickParser.DefaultRemindHour = 9;

        var result = TodoQuickParser.Parse("@9/10 *3日前 X", Categories(), Now);

        await Assert.That(result.RemindAt).IsEqualTo(new DateTime(2026, 9, 7, 9, 0, 0));
        await Assert.That(result.RemindOffsetDays).IsEqualTo(3);
    }

    [Test]
    [NotInParallel]
    public async Task 時刻を省略したら既定の通知時刻を使う()
    {
        // static な可変状態なので、アサーションが落ちても必ず戻す
        TodoQuickParser.DefaultRemindHour = 20;
        try
        {
            var result = TodoQuickParser.Parse("@明日 *当日 X", Categories(), Now);

            await Assert.That(result.RemindAt).IsEqualTo(new DateTime(2026, 8, 28, 20, 0, 0));
        }
        finally
        {
            TodoQuickParser.DefaultRemindHour = 9;
        }
    }

    [Test]
    [NotInParallel]
    public async Task 日付も時刻も無い裸のアスタリスクは本文に残る()
    {
        TodoQuickParser.DefaultRemindHour = 9;

        var result = TodoQuickParser.Parse("* X", Categories(), Now);

        await Assert.That(result.RemindAt.HasValue).IsFalse();
        await Assert.That(result.Title).IsEqualTo("* X");
    }

    // ============================================================
    // 確認用の説明（Summary）
    //
    // 期限・通知の表示は書式指定子 "M/d" と "HH:mm" を使っており、
    // 区切り文字が現在のカルチャに依存する。ここでは文字列全体を固定せず、
    // カルチャに依存しない項目だけを突き合わせる。
    // ============================================================

    [Test]
    public async Task 解釈した項目だけが説明に並ぶ()
    {
        var result = TodoQuickParser.Parse("@明日 !高 ~30m X", Categories(), Now);

        await Assert.That(result.HasAttributes).IsTrue();
        await Assert.That(result.Summary.Count).IsEqualTo(3);
        await Assert.That(result.Summary[1]).IsEqualTo("優先度 高");
        await Assert.That(result.Summary[2]).IsEqualTo("見積もり 30分");
    }

    [Test]
    [Arguments("~30m X", "見積もり 30分")]
    [Arguments("~1時間 X", "見積もり 1時間")]
    [Arguments("~1時間30分 X", "見積もり 1時間30分")]
    public async Task 見積もりの表示を時間と分で組み立てる(string input, string expected)
    {
        var result = TodoQuickParser.Parse(input, Categories(), Now);

        await Assert.That(result.Summary[0]).IsEqualTo(expected);
    }

    // ============================================================
    // 入力そのもの
    // ============================================================

    [Test]
    public async Task Nullや空文字でも落ちない()
    {
        var fromNull = TodoQuickParser.Parse(null, Categories(), Now);
        var fromEmpty = TodoQuickParser.Parse(string.Empty, Categories(), Now);

        await Assert.That(fromNull.Title).IsEqualTo(string.Empty);
        await Assert.That(fromNull.HasAttributes).IsFalse();
        await Assert.That(fromEmpty.Title).IsEqualTo(string.Empty);
    }

    [Test]
    public async Task 記法だけを書いたら本文は空になる()
    {
        var result = TodoQuickParser.Parse("@明日 !高", Categories(), Now);

        await Assert.That(result.Title).IsEqualTo(string.Empty);
        await Assert.That(result.DueDate).IsEqualTo(Today.AddDays(1));
        await Assert.That(result.Priority).IsEqualTo(TodoPriority.High);
    }
}
