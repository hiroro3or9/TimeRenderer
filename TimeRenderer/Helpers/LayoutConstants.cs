namespace TimeRenderer.Helpers;

/// <summary>
/// 日/週ビューの座標系定数。
///
/// 「1時間 = 60px」を前提にした計算が VM・コードビハインド・コンバーターに
/// 散らばると、スケール変更時に漏れが出るためここに集約する。
/// ※ MainWindow.xaml 内の時刻罫線（Border Height="60"）も同じ値を前提にしている。
/// </summary>
public static class LayoutConstants
{
    /// <summary>日/週ビューの縦方向スケール（1時間あたりのピクセル数）</summary>
    public const double PixelsPerHour = 60.0;
}
