using System;

namespace TimeRenderer.ViewModels;

/// <summary>
/// 記録中に出す常時最前面ミニバー（<see cref="Views.MiniRecordingBar"/>）の設定。
///
/// 窓そのものの生成・表示は MainWindow のコードビハインドが持つ。
/// ここが持つのは「出すかどうか」と「どこに出すか」だけで、
/// 画面座標の扱い（画面外からの引き戻しなど）はビュー側の責務にしている。
///
/// 位置を設定へ持たせるのは、記録の開始・停止のたびに窓を作り直すため。
/// VM のフィールドに置くだけではアプリの再起動で毎回右下へ戻ってしまう。
/// </summary>
public partial class MainViewModel
{
    private bool _isMiniRecordingBarEnabled = true;

    /// <summary>記録中にミニバーを出すか</summary>
    public bool IsMiniRecordingBarEnabled
    {
        get => _isMiniRecordingBarEnabled;
        set
        {
            if (SetProperty(ref _isMiniRecordingBarEnabled, value))
            {
                SaveSettings();
            }
        }
    }

    private double? _miniRecordingBarLeft;
    private double? _miniRecordingBarTop;

    /// <summary>前回ミニバーを置いた位置（左）。null なら既定位置に出す</summary>
    public double? MiniRecordingBarLeft => _miniRecordingBarLeft;

    /// <summary>前回ミニバーを置いた位置（上）。null なら既定位置に出す</summary>
    public double? MiniRecordingBarTop => _miniRecordingBarTop;

    /// <summary>
    /// ドラッグで動かした位置を覚える。
    /// 座標が数値として壊れている場合（NaN・無限大）は覚えない。
    /// 次の起動で復元できない位置を保存すると、バーごと見失う。
    /// </summary>
    public void SaveMiniRecordingBarPosition(double left, double top)
    {
        if (double.IsNaN(left) || double.IsNaN(top)
            || double.IsInfinity(left) || double.IsInfinity(top))
        {
            return;
        }

        _miniRecordingBarLeft = left;
        _miniRecordingBarTop = top;
        SaveSettings();
    }
}
