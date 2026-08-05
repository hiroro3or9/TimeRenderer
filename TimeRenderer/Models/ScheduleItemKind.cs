namespace TimeRenderer.Models;

/// <summary>
/// カレンダー上の時間帯が、これから行う予定なのか、実際に行った記録なのかを表す。
/// 数値で JSON に保存されるため、既存値の割り当ては変更しないこと。
/// </summary>
public enum ScheduleItemKind
{
    /// <summary>
    /// 種類を保存していなかった旧データ。読み込み時に Planned / Recorded へ移行する。
    /// </summary>
    Legacy = 0,

    /// <summary>これから行う、または時間を確保した予定。</summary>
    Planned = 1,

    /// <summary>タイマーや記録漏れの補完で確定した作業実績。</summary>
    Recorded = 2,
}
