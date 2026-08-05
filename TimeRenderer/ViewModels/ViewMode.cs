namespace TimeRenderer.ViewModels;

/// <summary>
/// メイン画面の表示モード。
/// 設定ファイルに数値で保存されるため、既存の値の並びは変更せず末尾に足すこと。
/// </summary>
public enum ViewMode
{
    Day,
    Week,
    Month,
    Sprint,
    SprintTimeline,
    Stats,
    /// <summary>ふりかえりの一覧（全期間）</summary>
    Notes,
    /// <summary>今日の予定・ToDo・実績を1画面にまとめたホーム</summary>
    Today
}

/// <summary>ツールバーの表示切替ドロップダウン用の選択肢</summary>
public sealed record ViewModeOption(ViewMode Mode, string Label)
{
    public override string ToString() => Label;
}
