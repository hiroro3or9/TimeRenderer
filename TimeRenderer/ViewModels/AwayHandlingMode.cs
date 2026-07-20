namespace TimeRenderer.ViewModels;

/// <summary>離席を検知したときの扱い</summary>
public enum AwayHandlingMode
{
    /// <summary>記録停止時に確認画面を出す</summary>
    Ask,
    /// <summary>確認せず、常に離席時間を除外する</summary>
    AlwaysExclude,
    /// <summary>確認せず、常にそのまま記録する</summary>
    AlwaysKeep
}

public sealed record AwayHandlingOption(AwayHandlingMode Mode, string Label)
{
    public override string ToString() => Label;
}
