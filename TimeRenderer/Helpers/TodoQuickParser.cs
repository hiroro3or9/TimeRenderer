using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text.RegularExpressions;

using TimeRenderer.Models;

namespace TimeRenderer.Helpers;

/// <summary>
/// クイック追加の解析結果。値が null の項目は「入力で指定されなかった」を意味する
/// （呼び出し側の既定値をそのまま使ってよい）。
/// </summary>
public sealed class TodoQuickParseResult
{
    /// <summary>記法を取り除いた後の本文</summary>
    public required string Title { get; init; }

    public DateTime? DueDate { get; init; }
    public DateTime? RemindAt { get; init; }

    /// <summary>通知を期限からの相対で指定していた場合の日数（0 は当日）</summary>
    public int? RemindOffsetDays { get; init; }

    public TodoPriority? Priority { get; init; }
    public CategoryInfo? Category { get; init; }
    public int? EstimatedMinutes { get; init; }

    /// <summary>入力欄の下に出す確認用の説明（例: "期限 8/6(木)"）</summary>
    public required IReadOnlyList<string> Summary { get; init; }

    /// <summary>記法が1つでも解釈されたか</summary>
    public bool HasAttributes => Summary.Count > 0;
}

/// <summary>
/// ToDo のクイック追加欄に書かれた記法を解析する。
///
/// 「思いついた瞬間に置ける」ことを保ったまま、期限や優先度まで一度に指定できるようにするための入口。
/// 記法は先頭1文字の記号で区別する（@ 期限／! 優先度／# カテゴリ／~ 見積もり／* 通知）。
///
/// 解釈できなかった記号はそのまま本文に残す。
/// これが方針の中心で、「#1 の対応」「@会議室で相談」のような普通の文章を書いても
/// 勝手に削られない。逆に言うと、記法が効かないときは本文にそのまま見えるので気づける。
///
/// 記号の前には空白が要る（行頭は除く）。後ろは続けて書けるので、
/// "@明日資料をまとめる" は 期限=明日／本文="資料をまとめる" になる。
/// 前を縛らないと "9時〜10時 打ち合わせ" の 〜 が見積もりに化けるが、
/// 後ろまで縛ると単語を空白で区切らない日本語では書きにくくなる。
/// </summary>
public static partial class TodoQuickParser
{
    /// <summary>通知時刻を省略したときに使う時（設定から差し替える）</summary>
    public static int DefaultRemindHour { get; set; } = 9;

    // ===== 記法のパターン =====
    //
    // 全角の記号（＠！＃〜＊）も受ける。日本語入力のまま打てないと、そもそも使われない。
    //
    // どの記号も「行頭か空白の直後」でしか効かない（Head）。
    // これが無いと "9時〜10時 打ち合わせ" の 〜 が見積もりに、"腕立て*30回" の * が通知に化ける。
    // 記号の後ろは続けて書ける（"@明日資料をまとめる" は期限=明日／本文="資料をまとめる"）ので、
    // 日本語で単語を区切らない書き方は保てる。

    private const string Head = @"(?:^|(?<=[\s　]))";

    [GeneratedRegex(
        Head + @"[@＠](?:" +
        @"(?<ymd>\d{4}/\d{1,2}/\d{1,2})" +
        @"|(?<md>\d{1,2}/\d{1,2})" +
        @"|\+(?<plus>\d{1,3})(?<plusunit>[dwDW日週])?" +
        @"|(?<word>明後日|あさって|今週末|週末|再来週|来週|来月|今日|きょう|本日|明日|あした|あす|tomorrow|today)" +
        @"|(?<dow>[月火水木金土日])(?:曜日?)?" +
        @")",
        RegexOptions.IgnoreCase)]
    private static partial Regex DueRegex();

    [GeneratedRegex(
        Head + @"[!！](?:(?<jp>高|低|中|標準)|(?<en>high|normal|low|h|n|l)(?=$|[\s　#＃~〜@＠*＊]))",
        RegexOptions.IgnoreCase)]
    private static partial Regex PriorityRegex();

    [GeneratedRegex(Head + @"[#＃](?<v>[^\s　#＃!！~〜@＠*＊]{1,20})")]
    private static partial Regex CategoryRegex();

    // 単位を省いた数字（~30）だけは後ろの区切りも要る。"~50%" を 50分 と読まないため
    [GeneratedRegex(
        Head + @"[~〜](?:" +
        @"(?<h>\d{1,3}(?:\.\d{1,2})?)(?:時間|h)(?:(?<hm>\d{1,2})(?:分|m)?)?" +
        @"|(?<m>\d{1,4})(?:分|m)" +
        @"|(?<mbare>\d{1,4})(?=$|[\s　#＃!！@＠*＊])" +
        @")",
        RegexOptions.IgnoreCase)]
    private static partial Regex EstimateRegex();

    [GeneratedRegex(
        Head + @"[*＊]" +
        @"(?:(?<rel>当日|前日)|(?<nd>\d{1,2})日前" +
        @"|(?<ymd>\d{4}/\d{1,2}/\d{1,2})" +
        @"|(?<md>\d{1,2}/\d{1,2})" +
        @"|(?<word>明後日|あさって|今日|きょう|本日|明日|あした|あす)" +
        @"|(?<dow>[月火水木金土日])曜日?" +
        @")?" +
        // 裸の数字（*9）は後ろの区切りも要る。"*30回" を 30時 と読まないため
        @"(?:(?<th>\d{1,2}):(?<tm>\d{2})|(?<th2>\d{1,2})時(?:(?<tm2>\d{1,2})分?)?" +
        @"|(?<th3>\d{1,2})(?=$|[\s　#＃!！~〜@＠]))?")]
    private static partial Regex RemindRegex();

    [GeneratedRegex(@"[ \t　]{2,}")]
    private static partial Regex SpaceRegex();

    private static readonly string[] DayNames = ["日", "月", "火", "水", "木", "金", "土"];

    /// <summary>入力欄の下に常時出すヒント（記法を忘れても思い出せるようにする）</summary>
    public const string SyntaxHint = "@明日 ｜ !高 ｜ #カテゴリ ｜ ~30m ｜ *9:00";

    /// <summary>
    /// クイック追加欄の入力を解析する。
    /// </summary>
    /// <param name="input">入力された文字列</param>
    /// <param name="categories">カテゴリ候補（# の解決に使う。null なら # は本文に残る）</param>
    /// <param name="now">「今日」「明日」の基準（テストと日またぎのため引数で受ける）</param>
    public static TodoQuickParseResult Parse(
        string? input, IReadOnlyList<CategoryInfo>? categories, DateTime now)
    {
        var text = input ?? string.Empty;
        var summary = new List<string>();

        DateTime? due = null;
        TodoPriority? priority = null;
        CategoryInfo? category = null;
        int? estimate = null;
        Match? remindMatch = null;

        var today = now.Date;

        // 期限を先に確定させる。通知の「前日」「当日」がこれを基準にするため
        text = DueRegex().Replace(text, m =>
        {
            if (due.HasValue) return m.Value; // 2つ目以降は本文として扱う
            var resolved = ResolveDueDate(m, today);
            if (resolved == null) return m.Value;

            due = resolved;
            return " ";
        });

        text = PriorityRegex().Replace(text, m =>
        {
            if (priority.HasValue) return m.Value;
            var resolved = ResolvePriority(m);
            if (resolved == null) return m.Value;

            priority = resolved;
            return " ";
        });

        text = EstimateRegex().Replace(text, m =>
        {
            if (estimate.HasValue) return m.Value;
            var parsed = ResolveEstimate(m);
            if (parsed is not > 0) return m.Value;

            estimate = parsed;
            return " ";
        });

        text = RemindRegex().Replace(text, m =>
        {
            if (remindMatch != null) return m.Value;
            // 日付も時刻も無い裸の記号は、ただの「*」なので本文に残す
            if (!HasRemindContent(m)) return m.Value;

            remindMatch = m;
            return " ";
        });

        // カテゴリは「一致した分だけ取り込み、残りは本文へ戻す」ため最後に処理する
        if (categories is { Count: > 0 })
        {
            text = CategoryRegex().Replace(text, m =>
            {
                if (category != null) return m.Value;

                var value = m.Groups["v"].Value;
                var found = MatchCategory(categories, value, out var consumed);
                if (found == null) return m.Value;

                category = found;
                return " " + value[consumed..];
            });
        }

        DateTime? remindAt = null;
        int? remindOffset = null;
        if (remindMatch is { } remind) (remindAt, remindOffset) = ResolveRemind(remind, due, now);

        // ===== 確認用の説明 =====

        if (due is { } dueDate) summary.Add($"期限 {FormatDate(dueDate)}");
        if (priority is { } p)
        {
            summary.Add(p switch
            {
                TodoPriority.High => "優先度 高",
                TodoPriority.Low => "優先度 低",
                _ => "優先度 標準",
            });
        }
        if (category != null) summary.Add($"カテゴリ {category.Name}");
        if (estimate is { } minutes) summary.Add($"見積もり {FormatMinutes(minutes)}");
        if (remindAt is { } at)
        {
            summary.Add(remindOffset switch
            {
                0 => $"通知 期限の当日 {at:HH:mm}",
                1 => $"通知 期限の前日 {at:HH:mm}",
                > 1 => $"通知 期限の{remindOffset}日前 {at:HH:mm}",
                _ => $"通知 {FormatDate(at)} {at:HH:mm}",
            });
        }

        return new TodoQuickParseResult
        {
            Title = SpaceRegex().Replace(text, " ").Trim(),
            DueDate = due,
            RemindAt = remindAt,
            RemindOffsetDays = remindOffset,
            Priority = priority,
            Category = category,
            EstimatedMinutes = estimate,
            Summary = summary,
        };
    }

    // ===== 期限 =====

    private static DateTime? ResolveDueDate(Match m, DateTime today)
    {
        if (m.Groups["ymd"].Success) return ParseExactDate(m.Groups["ymd"].Value);
        if (m.Groups["md"].Success) return ParseMonthDay(m.Groups["md"].Value, today);

        if (m.Groups["plus"].Success)
        {
            if (!int.TryParse(m.Groups["plus"].Value, out var n)) return null;

            var unit = m.Groups["plusunit"].Value;
            var isWeek = unit.Equals("w", StringComparison.OrdinalIgnoreCase) || unit == "週";
            return today.AddDays(isWeek ? n * 7 : n);
        }

        if (m.Groups["word"].Success)
        {
            return m.Groups["word"].Value.ToLowerInvariant() switch
            {
                "今日" or "きょう" or "本日" or "today" => today,
                "明日" or "あした" or "あす" or "tomorrow" => today.AddDays(1),
                "明後日" or "あさって" => today.AddDays(2),
                // 週の切れ目は月曜始まりでそろえる（アプリ内の他の週計算と同じ）
                "来週" => StartOfWeek(today).AddDays(7),
                "再来週" => StartOfWeek(today).AddDays(14),
                "来月" => new DateTime(today.Year, today.Month, 1).AddMonths(1),
                "今週末" or "週末" => NextSaturday(today),
                _ => null,
            };
        }

        if (m.Groups["dow"].Success) return NextDayOfWeek(today, m.Groups["dow"].Value);

        return null;
    }

    private static DateTime? ParseExactDate(string value) =>
        DateTime.TryParseExact(value, "yyyy/M/d", CultureInfo.InvariantCulture, DateTimeStyles.None, out var parsed)
            ? parsed.Date
            : null;

    /// <summary>
    /// 「8/12」を今年の日付として読む。
    /// 半年以上前になる場合だけ翌年に送る（年末に「1/5」と書けば来年、昨日の日付はそのまま）。
    /// </summary>
    private static DateTime? ParseMonthDay(string value, DateTime today)
    {
        var parts = value.Split('/');
        if (parts.Length != 2) return null;
        if (!int.TryParse(parts[0], out var month) || !int.TryParse(parts[1], out var day)) return null;
        if (month is < 1 or > 12) return null;

        try
        {
            var date = new DateTime(today.Year, month, day);
            return (today - date).TotalDays > 180 ? date.AddYears(1) : date;
        }
        catch (ArgumentOutOfRangeException)
        {
            return null; // 2/30 のような存在しない日付
        }
    }

    /// <summary>次に来るその曜日（今日と同じ曜日なら来週）</summary>
    private static DateTime? NextDayOfWeek(DateTime today, string name)
    {
        var index = Array.IndexOf(DayNames, name);
        if (index < 0) return null;

        var diff = ((index - (int)today.DayOfWeek) + 7) % 7;
        return today.AddDays(diff == 0 ? 7 : diff);
    }

    private static DateTime NextSaturday(DateTime today)
    {
        var saturday = StartOfWeek(today).AddDays(5);
        return saturday < today ? saturday.AddDays(7) : saturday;
    }

    private static DateTime StartOfWeek(DateTime date) =>
        date.Date.AddDays(-(((int)date.DayOfWeek + 6) % 7));

    // ===== 優先度 =====

    private static TodoPriority? ResolvePriority(Match m)
    {
        if (m.Groups["jp"].Success)
        {
            return m.Groups["jp"].Value switch
            {
                "高" => TodoPriority.High,
                "低" => TodoPriority.Low,
                _ => TodoPriority.Normal,
            };
        }

        return m.Groups["en"].Value.ToLowerInvariant() switch
        {
            "high" or "h" => TodoPriority.High,
            "low" or "l" => TodoPriority.Low,
            "normal" or "n" => TodoPriority.Normal,
            _ => null,
        };
    }

    // ===== 見積もり =====

    private static int? ResolveEstimate(Match m)
    {
        if (m.Groups["h"].Success)
        {
            if (!double.TryParse(m.Groups["h"].Value, NumberStyles.Float, CultureInfo.InvariantCulture, out var hours))
            {
                return null;
            }

            var minutes = (int)Math.Round(hours * 60);
            if (m.Groups["hm"].Success && int.TryParse(m.Groups["hm"].Value, out var extra)) minutes += extra;
            return minutes;
        }

        var value = m.Groups["m"].Success ? m.Groups["m"].Value : m.Groups["mbare"].Value;
        return int.TryParse(value, out var raw) ? raw : null;
    }

    // ===== カテゴリ =====

    /// <summary>
    /// 「#開発資料」のように名前の後ろが続いていても拾えるよう、
    /// 長い方から前方一致を試して、一致した文字数（consumed）を返す。
    /// </summary>
    private static CategoryInfo? MatchCategory(
        IReadOnlyList<CategoryInfo> categories, string value, out int consumed)
    {
        for (var length = value.Length; length >= 1; length--)
        {
            var candidate = value[..length];

            var exact = categories.FirstOrDefault(
                c => string.Equals(c.Name, candidate, StringComparison.OrdinalIgnoreCase));
            if (exact != null)
            {
                consumed = length;
                return exact;
            }
        }

        // 完全一致が無ければ、入力を頭に持つカテゴリが1つだけのときに限って採用する
        var prefixed = categories
            .Where(c => c.Name.StartsWith(value, StringComparison.OrdinalIgnoreCase))
            .ToList();
        if (prefixed.Count == 1)
        {
            consumed = value.Length;
            return prefixed[0];
        }

        consumed = 0;
        return null;
    }

    // ===== 通知 =====

    private static bool HasRemindContent(Match m) =>
        m.Groups["rel"].Success || m.Groups["nd"].Success ||
        m.Groups["ymd"].Success || m.Groups["md"].Success ||
        m.Groups["word"].Success || m.Groups["dow"].Success ||
        m.Groups["th"].Success || m.Groups["th2"].Success || m.Groups["th3"].Success;

    /// <summary>
    /// 通知日時を求める。日付を書かなかった場合は期限日（期限が無ければ今日）を使い、
    /// その場合だけ「期限からの相対」として覚える（期限を動かすと通知も一緒に動く）。
    /// </summary>
    private static (DateTime? At, int? OffsetDays) ResolveRemind(Match m, DateTime? due, DateTime now)
    {
        var time = ResolveTime(m);
        var today = now.Date;

        // 期限からの相対（当日・前日・N日前）
        int? offset = null;
        if (m.Groups["rel"].Success) offset = m.Groups["rel"].Value == "前日" ? 1 : 0;
        else if (m.Groups["nd"].Success && int.TryParse(m.Groups["nd"].Value, out var days)) offset = days;

        if (offset.HasValue)
        {
            // 期限が無いと相対の基準が無いので、今日の指定として扱う
            if (due is not { } dueDate) return (today + time, null);
            return (dueDate.AddDays(-offset.Value) + time, offset);
        }

        DateTime? explicitDate =
            m.Groups["ymd"].Success ? ParseExactDate(m.Groups["ymd"].Value)
            : m.Groups["md"].Success ? ParseMonthDay(m.Groups["md"].Value, today)
            : m.Groups["dow"].Success ? NextDayOfWeek(today, m.Groups["dow"].Value)
            : m.Groups["word"].Success
                ? m.Groups["word"].Value switch
                {
                    "今日" or "きょう" or "本日" => today,
                    "明日" or "あした" or "あす" => today.AddDays(1),
                    "明後日" or "あさって" => today.AddDays(2),
                    _ => null,
                }
                : null;

        if (explicitDate is { } date) return (date + time, null);

        // 日付の指定なし：期限があればその当日、無ければ今日（時刻を過ぎていれば明日）
        if (due is { } d) return (d + time, 0);

        var at = today + time;
        return (at <= now ? at.AddDays(1) : at, null);
    }

    private static TimeSpan ResolveTime(Match m)
    {
        if (m.Groups["th"].Success &&
            int.TryParse(m.Groups["th"].Value, out var h) && int.TryParse(m.Groups["tm"].Value, out var min))
        {
            return NormalizeTime(h, min);
        }

        if (m.Groups["th2"].Success && int.TryParse(m.Groups["th2"].Value, out var h2))
        {
            var min2 = int.TryParse(m.Groups["tm2"].Value, out var parsed) ? parsed : 0;
            return NormalizeTime(h2, min2);
        }

        if (m.Groups["th3"].Success && int.TryParse(m.Groups["th3"].Value, out var h3))
        {
            return NormalizeTime(h3, 0);
        }

        return TimeSpan.FromHours(Math.Clamp(DefaultRemindHour, 0, 23));
    }

    private static TimeSpan NormalizeTime(int hour, int minute) =>
        new(Math.Clamp(hour, 0, 23), Math.Clamp(minute, 0, 59), 0);

    // ===== 表示 =====

    private static string FormatDate(DateTime date) => $"{date:M/d}({DayNames[(int)date.DayOfWeek]})";

    private static string FormatMinutes(int minutes) => minutes switch
    {
        < 60 => $"{minutes}分",
        _ when minutes % 60 == 0 => $"{minutes / 60}時間",
        _ => $"{minutes / 60}時間{minutes % 60}分",
    };
}
