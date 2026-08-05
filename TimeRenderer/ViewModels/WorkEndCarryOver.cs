using TimeRenderer.Models;

namespace TimeRenderer.ViewModels;

/// <summary>
/// 退勤時のふりかえりに並べる「明日へ送る候補」1件。
///
/// なぜ候補に挙がったのか（<see cref="ReasonText"/>）と、
/// 今日それにどれだけ触ったのか（<see cref="DetailText"/>）を添える。
/// 一覧に並ぶだけでは「これは何だったか」を思い出すのに時間がかかり、
/// 退勤時に開くダイアログとしては重すぎるため。
///
/// 変更通知は持たない。チェックの増減はダイアログ側が
/// <c>Checked</c>／<c>Unchecked</c> で数え直すので、値の書き戻しだけで足りる。
/// </summary>
public sealed class WorkEndCarryOver
{
    public required TodoItem Todo { get; init; }

    /// <summary>候補に挙がった理由（例: "今日やる" / "期限 2日超過"）</summary>
    public required string ReasonText { get; init; }

    /// <summary>今日の状況（例: "今日 1:20 記録" / "手つかず"）</summary>
    public required string DetailText { get; init; }

    /// <summary>明日へ送るか。既定は送る（そのまま確定できるほうが多いため）</summary>
    public bool IsSelected { get; set; } = true;

    public string Title => Todo.Title;

    /// <summary>優先度が高いものだけ印を出す（一覧の情報量を増やしすぎない）</summary>
    public bool IsHighPriority => Todo.IsHighPriority;
}
