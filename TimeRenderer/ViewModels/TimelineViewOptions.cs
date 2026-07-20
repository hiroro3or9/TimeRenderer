namespace TimeRenderer.ViewModels;

/// <summary>タイムラインの行のまとめ方</summary>
public enum TimelineGroupMode
{
    /// <summary>重ならないアイテムを同じ行に詰める（最も密度が高い）</summary>
    Packed,
    /// <summary>カテゴリごとに行を分ける（何にどれだけ使ったかが見える）</summary>
    Category,
    /// <summary>1アイテム1行（件数が少ないときの見やすさ用）</summary>
    Flat
}

public sealed record TimelineGroupModeOption(TimelineGroupMode Mode, string Label)
{
    public override string ToString() => Label;
}

/// <summary>ズームのプリセット（ラベル, px/日）</summary>
public sealed record TimelineZoomPreset(string Label, double PixelsPerDay)
{
    public override string ToString() => Label;
}

/// <summary>表示するスプリント数の選択肢</summary>
public sealed record TimelineSpanOption(int Count, string Label)
{
    public override string ToString() => Label;
}
