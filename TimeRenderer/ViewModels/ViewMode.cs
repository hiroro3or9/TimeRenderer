namespace TimeRenderer.ViewModels;

/// <summary>メイン画面の表示モード</summary>
public enum ViewMode
{
    Day,
    Week,
    Month,
    Sprint,
    SprintTimeline,
    Stats
}

/// <summary>ツールバーの表示切替ドロップダウン用の選択肢</summary>
public sealed record ViewModeOption(ViewMode Mode, string Label)
{
    public override string ToString() => Label;
}
