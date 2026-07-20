using System.Windows.Media;
using Brush = System.Windows.Media.Brush;
using Brushes = System.Windows.Media.Brushes;

namespace TimeRenderer.ViewModels;

/// <summary>
/// タイムラインのレーン群（カテゴリ別表示のときの1カテゴリぶん）。
/// 左端の固定ラベル列と、背景の帯の描画に使う。
/// </summary>
public sealed class TimelineLaneGroup
{
    public required string Name { get; init; }

    /// <summary>この群の上端Y座標</summary>
    public double Y { get; init; }

    /// <summary>この群の高さ（レーン数 × LaneHeight）</summary>
    public double Height { get; init; }

    /// <summary>カテゴリ色（ラベル列の色チップに使う）</summary>
    public Brush Brush { get; init; } = Brushes.Transparent;

    /// <summary>この群に含まれるアイテムの合計時間の表示文字列（例: "12.5h"）</summary>
    public required string TotalText { get; init; }

    /// <summary>件数</summary>
    public int Count { get; init; }

    /// <summary>交互の背景色付け用（偶数群だけ薄く塗る）</summary>
    public bool IsAlternate { get; init; }
}

/// <summary>
/// タイムライン上のスプリント1つぶんのヘッダー／背景区画。
/// 幅はスプリントの実日数から求めるため、長さの違うスプリントが正しく異なる幅で描かれる。
/// </summary>
public sealed class TimelineSprintBand
{
    public required string Name { get; init; }
    public required string RangeText { get; init; }
    public double X { get; init; }
    public double Width { get; init; }
    public bool IsCurrent { get; init; }

    /// <summary>このスプリントの総記録時間（例: "38.5h ・ 24件"）</summary>
    public required string SummaryText { get; init; }

    /// <summary>最も時間を使ったカテゴリ（例: "実装 62%"）。該当なしのときは空</summary>
    public required string TopCategoryText { get; init; }

    /// <summary>サマリーを描く余地があるか（幅が狭いときは隠す）</summary>
    public bool ShowSummary => Width >= 120;
}
