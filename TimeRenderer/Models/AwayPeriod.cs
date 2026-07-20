using System;

namespace TimeRenderer.Models;

/// <summary>離席と判定した理由</summary>
public enum AwayReason
{
    /// <summary>キーボード・マウスの操作が一定時間なかった</summary>
    Idle,
    /// <summary>PC がスリープ・休止していた</summary>
    Sleep,
    /// <summary>画面がロックされていた（離席・ユーザー切替）</summary>
    Locked
}

/// <summary>
/// 席を外していたと判定した期間。
/// 記録中に発生したものだけを集め、記録停止時に「除外するか」を確認する。
/// </summary>
public sealed record AwayPeriod(DateTime Start, DateTime End, AwayReason Reason)
{
    public TimeSpan Duration => End > Start ? End - Start : TimeSpan.Zero;

    public string ReasonText => Reason switch
    {
        AwayReason.Sleep => "スリープ",
        AwayReason.Locked => "ロック",
        _ => "無操作"
    };

    public string RangeText => $"{Start:HH:mm} 〜 {End:HH:mm}";

    public string DurationText => Duration.TotalHours >= 1
        ? $"{(int)Duration.TotalHours}時間{Duration.Minutes}分"
        : $"{(int)Duration.TotalMinutes}分";

    public string DisplayText => $"{RangeText}  （{DurationText}・{ReasonText}）";

    /// <summary>指定範囲と重なる部分に切り詰めたものを返す。重ならない場合は null</summary>
    public AwayPeriod? ClipTo(DateTime rangeStart, DateTime rangeEnd)
    {
        var start = Start < rangeStart ? rangeStart : Start;
        var end = End > rangeEnd ? rangeEnd : End;
        return end > start ? this with { Start = start, End = end } : null;
    }
}
