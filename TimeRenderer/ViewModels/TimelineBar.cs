using System;
using System.ComponentModel;
using System.Runtime.CompilerServices;
using System.Windows.Media;
using Brush = System.Windows.Media.Brush;

using TimeRenderer.Models;

namespace TimeRenderer.ViewModels;

/// <summary>
/// タイムラインビューに描画する予定バー1本ぶんの、計算済みレイアウト情報。
///
/// 座標・幅はスケールやアイテムが変わるたびにリストごと作り直すため init のみ。
/// ただし選択状態と検索の減光だけは、レイアウトを組み直さずに切り替えたいので
/// 変更通知つきの可変プロパティにしている。
/// </summary>
public sealed class TimelineBar : INotifyPropertyChanged
{
    /// <summary>バーの高さ</summary>
    public const double BarHeight = 22.0;

    /// <summary>レーン1本ぶんの高さ（バー + 上下の余白）</summary>
    public const double LaneHeight = 30.0;

    /// <summary>実幅がこれ未満でも、この幅だけは必ず描画する（存在に気づけるように）</summary>
    public const double MinDrawWidth = 3.0;

    /// <summary>クリック・ホバーの判定幅の下限（描画幅とは別に確保する）</summary>
    public const double MinHitWidth = 14.0;

    /// <summary>バー内側のテキスト余白（左右合計）</summary>
    private const double LabelPadding = 14.0;

    public required ScheduleItem Item { get; init; }

    /// <summary>バー左端のX座標</summary>
    public double X { get; init; }

    /// <summary>バー上端のY座標</summary>
    public double Y { get; init; }

    /// <summary>実際の時間量に対応する幅（丸めなし。0に近い値もありうる）</summary>
    public double ActualWidth { get; init; }

    /// <summary>描画に使う幅（実幅を下限でクランプしたもの）</summary>
    public double DrawWidth => Math.Max(ActualWidth, MinDrawWidth);

    /// <summary>マウス判定に使う幅（描画幅より広くとり、細いバーでも掴めるようにする）</summary>
    public double HitWidth => Math.Max(ActualWidth, MinHitWidth);

    // XAML がバインドするためインスタンスプロパティ（CA1822 回避で自動プロパティ化）
    public double Height { get; } = BarHeight;

    /// <summary>割り当てられたレーン番号（0始まり）</summary>
    public int Lane { get; init; }

    public string Title => Item.Title;

    public Brush Background => Item.BackgroundColor;

    /// <summary>タイトルがバー内に収まるか。収まらない場合はバーの右隣に描画する</summary>
    public bool IsLabelInside { get; init; }

    /// <summary>バー右隣にラベルを出すときの左マージン</summary>
    public System.Windows.Thickness OutsideLabelMargin => new(DrawWidth + 4, 0, 0, 0);

    /// <summary>ホバー時に表示する詳細（細いバーでは唯一の情報源になるため必ず設定する）</summary>
    public required string ToolTipText { get; init; }

    private bool _isDimmed;
    /// <summary>検索中に、検索語へヒットしなかったため減光表示にするか</summary>
    public bool IsDimmed
    {
        get => _isDimmed;
        set => SetProperty(ref _isDimmed, value);
    }

    public event PropertyChangedEventHandler? PropertyChanged;

    private void SetProperty<T>(ref T field, T value, [CallerMemberName] string? propertyName = null)
    {
        if (System.Collections.Generic.EqualityComparer<T>.Default.Equals(field, value)) return;
        field = value;
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
    }

    /// <summary>
    /// タイトルの描画幅をおおまかに見積もる。
    /// FormattedText による正確な計測はアイテム数ぶんのコストがかかるため、
    /// 全角/半角の判定による近似で足りる（判定を誤っても「バー内か右隣か」が変わるだけ）。
    /// </summary>
    public static double EstimateLabelWidth(string text, double fontSize = 12.0)
    {
        if (string.IsNullOrEmpty(text)) return 0;

        double units = 0;
        foreach (var c in text)
        {
            units += IsFullWidth(c) ? 1.0 : 0.55;
        }
        return units * fontSize + LabelPadding;
    }

    /// <summary>
    /// 全角として扱う文字か。CJK・かな・全角記号を対象にする。
    /// </summary>
    private static bool IsFullWidth(char c) =>
        (c >= 0x1100 && c <= 0x115F) ||   // ハングル字母
        (c >= 0x2E80 && c <= 0xA4CF) ||   // CJK部首補助〜イ文字（かな・漢字を含む）
        (c >= 0xAC00 && c <= 0xD7A3) ||   // ハングル音節
        (c >= 0xF900 && c <= 0xFAFF) ||   // CJK互換漢字
        (c >= 0xFE30 && c <= 0xFE6F) ||   // CJK互換形
        (c >= 0xFF00 && c <= 0xFF60) ||   // 全角英数・記号
        (c >= 0xFFE0 && c <= 0xFFE6);     // 全角通貨記号
}
