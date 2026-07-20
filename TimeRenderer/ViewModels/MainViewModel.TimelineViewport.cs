using System;
using System.Collections.Generic;
using System.Linq;

namespace TimeRenderer.ViewModels;

/// <summary>
/// タイムラインの描画量を、実際に見えている範囲へ絞り込む（仮想化）。
///
/// バー・目盛り・日単位の背景列・密度バーはいずれも <see cref="System.Windows.Controls.Canvas"/> 上に
/// 置いており、Canvas は仮想化を行わない。そのため何もしないと、
/// 表示範囲全体（最大25スプリント＝約525日、最大ズームでは目盛りだけで数千個）を
/// すべて実体化してしまう。
///
/// ここでは「実体化済みの窓」を持ち、ビューポートがその窓の内側にある限り
/// 再構築しない（ヒステリシス）。窓はビューポートの前後に余裕を取ってあるので、
/// 通常のスクロールでは数回に1度しか作り直しが起きない。
/// </summary>
public partial class MainViewModel
{
    /// <summary>ビューポートの前後に確保する余白（ビューポート幅に対する倍率）</summary>
    private const double ViewportOverscan = 1.0;

    /// <summary>
    /// 実体化済みの窓（コンテンツ座標）。Start > End のときは「未設定＝全件表示」を意味する。
    /// </summary>
    private double _realizedStart;
    private double _realizedEnd = -1;

    /// <summary>ビューポートが一度も通知されていない間は絞り込みを行わない</summary>
    private bool _hasViewport;

    private double _viewportOffset;
    private double _viewportWidth;

    // 絞り込み前の全件。表示用のプロパティにはここから切り出したものを入れる
    private IReadOnlyList<TimelineBar> _allBars = [];
    private IReadOnlyList<TimelineTick> _allTicks = [];
    private IReadOnlyList<TimelineDayColumn> _allDayColumns = [];
    private IReadOnlyList<TimelineDensityBar> _allDensityBars = [];

    /// <summary>
    /// 選択移動や状態更新は、画面に出ていないものも含めた全件を対象にする必要がある。
    /// </summary>
    private IReadOnlyList<TimelineBar> AllTimelineBars => _allBars;

    /// <summary>
    /// ビューの横スクロール位置が変わったときにコードビハインドから呼ぶ。
    /// 実体化済みの窓を外れたときだけ切り出しをやり直す。
    /// </summary>
    public void SetTimelineViewport(double horizontalOffset, double viewportWidth)
    {
        if (viewportWidth <= 0) return;

        _viewportOffset = horizontalOffset;
        _viewportWidth = viewportWidth;
        _hasViewport = true;

        if (IsViewportInsideRealizedWindow()) return;

        RealizeViewport();
    }

    private bool IsViewportInsideRealizedWindow()
    {
        if (_realizedEnd < _realizedStart) return false; // 未設定

        // ビューポートが窓の内側に完全に収まっているか
        return _viewportOffset >= _realizedStart && _viewportOffset + _viewportWidth <= _realizedEnd;
    }

    /// <summary>現在のビューポートに合わせて窓を張り直し、各コレクションを切り出す</summary>
    private void RealizeViewport()
    {
        double overscan = _viewportWidth * ViewportOverscan;
        _realizedStart = Math.Max(0, _viewportOffset - overscan);
        _realizedEnd = _viewportOffset + _viewportWidth + overscan;

        ApplyViewportFilter();
    }

    /// <summary>
    /// レイアウトを組み直したあとに呼ぶ。
    /// 窓の位置はそのままに、新しい全件リストから切り出しをやり直す。
    /// </summary>
    private void ApplyViewportFilter()
    {
        if (!_hasViewport)
        {
            // ビューポート未通知（初回レイアウト時など）は絞り込まずに全件出す。
            // 何も描かれないより、多く描くほうが安全側に倒れる。
            TimelineBars = _allBars;
            TimelineTicks = _allTicks;
            TimelineDayColumns = _allDayColumns;
            TimelineDensityBars = _allDensityBars;
            return;
        }

        // ズーム変更などで窓が無効化されていれば、現在のビューポートから張り直す
        EnsureRealizedWindow();

        double start = _realizedStart;
        double end = _realizedEnd;

        // バーはラベルが右へはみ出すため、左側に少し余裕を持たせて拾う
        TimelineBars = [.. _allBars.Where(b => b.X <= end && b.X + b.HitWidth + 200 >= start)];
        TimelineTicks = [.. _allTicks.Where(t => t.X <= end && t.X + t.Width >= start)];
        TimelineDayColumns = [.. _allDayColumns.Where(c => c.X <= end && c.X + c.Width >= start)];
        TimelineDensityBars = [.. _allDensityBars.Where(d => d.X <= end && d.X + d.Width >= start)];
    }

    /// <summary>全件リストを差し替えて、現在の窓で切り出す</summary>
    private void SetTimelineSources(
        IReadOnlyList<TimelineBar> bars,
        IReadOnlyList<TimelineTick> ticks,
        IReadOnlyList<TimelineDayColumn> dayColumns,
        IReadOnlyList<TimelineDensityBar> densityBars)
    {
        _allBars = bars;
        _allTicks = ticks;
        _allDayColumns = dayColumns;
        _allDensityBars = densityBars;

        ApplyViewportFilter();
    }

    /// <summary>
    /// バーだけを差し替える（ドラッグ中用）。
    ///
    /// ドラッグ中は時間軸が動かないので目盛り・背景・密度バーは変化しない。
    /// それらまで作り直すと、新しいリストのたびに ItemsControl が
    /// 数百〜千個の要素を再生成してしまい、マウス移動が引っかかる。
    /// </summary>
    private void SetTimelineBarsOnly(IReadOnlyList<TimelineBar> bars)
    {
        _allBars = bars;

        // ドラッグ中は絞り込まない。
        // 掴んだバーが窓の外へ出た瞬間に消えてしまい、どこを掴んでいるのか分からなくなる。
        // ドラッグ中はスクロールが起きず窓も張り直されないため、絞り込む利点も小さい。
        if (!_hasViewport || _isTimelineDragging)
        {
            TimelineBars = _allBars;
            return;
        }

        EnsureRealizedWindow();
        TimelineBars = [.. _allBars.Where(b => b.X <= _realizedEnd && b.X + b.HitWidth + 200 >= _realizedStart)];
    }

    /// <summary>窓が無効化されていれば、現在のビューポートから張り直す</summary>
    private void EnsureRealizedWindow()
    {
        if (_realizedEnd >= _realizedStart) return;

        double span = _viewportWidth * ViewportOverscan;
        _realizedStart = Math.Max(0, _viewportOffset - span);
        _realizedEnd = _viewportOffset + _viewportWidth + span;
    }

    /// <summary>
    /// ズームやスプリント数の変更でコンテンツ幅が大きく変わったときに呼ぶ。
    /// 窓を無効化して、次のスクロール通知で張り直させる。
    /// </summary>
    private void InvalidateRealizedWindow()
    {
        _realizedEnd = -1;
        _realizedStart = 0;
    }
}
