using System;

namespace TimeRenderer.Helpers;

/// <summary>
/// タイムラインビューの時間軸スケール。
///
/// 従来は「表示範囲全体をウィンドウ幅に押し込む」方式だったため、
/// (1) 1日あたりの幅が極端に狭くなる
/// (2) スプリントごとに幅を均等配分するため時間軸が非線形になる
/// (3) 日付単位に丸めるため時刻情報が失われる
/// という3つの問題があった。
///
/// このクラスは「1日 = PixelsPerDay ピクセル」の連続した一次元スケールとして
/// 時刻→X座標を線形変換する。丸めを行わないため、30分の記録は30分ぶんの幅で描画され、
/// スプリントの長さの違いもそのまま幅に反映される。
/// </summary>
public sealed class TimelineScale(DateTime origin, DateTime end, double pixelsPerDay)
{
    /// <summary>1日あたりのピクセル数の下限（これ以上ズームアウトさせない）</summary>
    public const double MinPixelsPerDay = 8.0;

    /// <summary>1日あたりのピクセル数の上限（これ以上ズームインさせない）</summary>
    public const double MaxPixelsPerDay = 960.0;

    /// <summary>既定のズーム倍率（1日が読める幅）</summary>
    public const double DefaultPixelsPerDay = 120.0;

    /// <summary>X=0 に対応する時刻（表示範囲の開始。日付境界に揃える）</summary>
    public DateTime Origin { get; } = origin;

    /// <summary>X=TotalWidth に対応する時刻（表示範囲の終端）</summary>
    public DateTime End { get; } = end < origin ? origin : end;

    /// <summary>1日あたりのピクセル数</summary>
    public double PixelsPerDay { get; } = Math.Clamp(pixelsPerDay, MinPixelsPerDay, MaxPixelsPerDay);

    /// <summary>1時間あたりのピクセル数</summary>
    public double PixelsPerHour => PixelsPerDay / 24.0;

    /// <summary>表示範囲全体の幅（ピクセル）</summary>
    public double TotalWidth => ToX(End);

    /// <summary>
    /// 時刻をX座標へ変換する。TotalDays が小数を返すため、
    /// 時・分・秒がそのまま位置に反映される（日付への丸めを行わない）。
    /// </summary>
    public double ToX(DateTime time) => (time - Origin).TotalDays * PixelsPerDay;

    /// <summary>X座標を時刻へ変換する（ToX の逆変換。ドラッグ操作やクリック位置の解決に使う）</summary>
    public DateTime ToTime(double x) => Origin.AddDays(x / PixelsPerDay);

    /// <summary>指定した時間幅のピクセル幅を返す</summary>
    public double ToWidth(TimeSpan duration) => duration.TotalDays * PixelsPerDay;

    /// <summary>同じ範囲のまま倍率だけ変えた新しいスケールを返す</summary>
    public TimelineScale WithPixelsPerDay(double pixelsPerDay)
        => new(Origin, End, pixelsPerDay);

    /// <summary>
    /// 現在の倍率で、指定ピクセル数に相当する時間幅を返す。
    /// 「ラベルが重ならない最小の間隔」をピクセルで指定してレーン詰めに渡すために使う。
    /// </summary>
    public TimeSpan PixelsToDuration(double pixels)
        => TimeSpan.FromDays(pixels / PixelsPerDay);
}
