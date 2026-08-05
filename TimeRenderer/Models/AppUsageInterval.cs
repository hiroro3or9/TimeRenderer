using System;

namespace TimeRenderer.Models;

/// <summary>
/// 前面（フォアグラウンド）にあったアプリの使用期間。
/// 勤務中に収集し、「この時間、実際に何を使っていたか」の裏付けに使う。
/// ウィンドウタイトルは記録中の期間だけ保存する。
/// JSON で保存するため、プロパティは get/set のプレーンなクラスにしている。
/// </summary>
public class AppUsageInterval
{
    public DateTime Start { get; set; }
    public DateTime End { get; set; }

    /// <summary>プロセス名（例: devenv, chrome）</summary>
    public string ProcessName { get; set; } = string.Empty;

    /// <summary>表示用のアプリ名（FileDescription。取れない場合はプロセス名）</summary>
    public string AppName { get; set; } = string.Empty;

    /// <summary>この期間に見えていたウィンドウタイトル</summary>
    public string WindowTitle { get; set; } = string.Empty;

    /// <summary>
    /// タイトル変更時に区間を分けて記録したデータなら true。
    /// 旧形式のデータは最後に見えたタイトルしか持たないため、
    /// タイトル別の正確な使用時間には含めない。
    /// </summary>
    public bool IsWindowTitleSpecific { get; set; }

    public TimeSpan Duration => End > Start ? End - Start : TimeSpan.Zero;

    /// <summary>指定範囲と重なる部分に切り詰めたものを返す。重ならない場合は null</summary>
    public AppUsageInterval? ClipTo(DateTime rangeStart, DateTime rangeEnd)
    {
        var start = Start < rangeStart ? rangeStart : Start;
        var end = End > rangeEnd ? rangeEnd : End;
        if (end <= start) return null;

        return new AppUsageInterval
        {
            Start = start,
            End = end,
            ProcessName = ProcessName,
            AppName = AppName,
            WindowTitle = WindowTitle,
            IsWindowTitleSpecific = IsWindowTitleSpecific
        };
    }
}
