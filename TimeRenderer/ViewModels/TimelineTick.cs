using System;
using System.Windows.Media;
using Brush = System.Windows.Media.Brush;
using Brushes = System.Windows.Media.Brushes;

namespace TimeRenderer.ViewModels;

/// <summary>
/// ルーラー下段の目盛り1つぶん。ズーム倍率に応じて粒度（時刻/日/週/月）が切り替わる。
/// </summary>
public sealed class TimelineTick
{
    public required string Label { get; init; }
    public double X { get; init; }
    public double Width { get; init; }

    /// <summary>区切りとして強調するか（日境界・月曜など）</summary>
    public bool IsEmphasized { get; init; }

    /// <summary>今日を含む目盛りか</summary>
    public bool IsToday { get; init; }
}

/// <summary>
/// 背景の日単位の列。罫線・土日/無効曜日のシェード・今日の強調に使う。
/// </summary>
public sealed class TimelineDayColumn
{
    public DateTime Date { get; init; }
    public double X { get; init; }
    public double Width { get; init; }

    /// <summary>
    /// 列の種別。XAML の DataTrigger で背景色を出し分けるための文字列。
    /// （Normal / Weekend / Disabled / Today）
    /// </summary>
    public required string Kind { get; init; }

    /// <summary>週の開始日（月曜）か。罫線を濃くする</summary>
    public bool IsWeekStart { get; init; }

    /// <summary>左端に日境界の罫線を引くか（ズームアウト時は間引く）</summary>
    public bool ShowDayLine { get; init; }
}

/// <summary>
/// 最下部の密度ヒートバー1本ぶん（その日の総記録時間）。
/// ズームアウトして個々のバーが読めないときに「どこが忙しかったか」を示す。
/// </summary>
public sealed class TimelineDensityBar
{
    public double X { get; init; }
    public double Width { get; init; }

    /// <summary>棒の高さ（最大値を基準に正規化済み）</summary>
    public double BarHeight { get; init; }

    public required string ToolTipText { get; init; }

    public Brush Brush { get; init; } = Brushes.Transparent;
}
