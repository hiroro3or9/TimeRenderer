using System;

namespace TimeRenderer.ViewModels;

/// <summary>
/// 日/週ビューに引く出勤・退勤の横ライン。
///
/// 予定バーと同じ「箱」で描くと作業記録と見分けが付かなくなるため、
/// 時刻の位置に線とラベルだけを置く。位置計算は予定と同じコンバーターを使う
/// （<c>Time</c> を StartTime として渡す）。
/// </summary>
/// <param name="Time">線を引く時刻</param>
/// <param name="IsStart">出勤なら true、退勤なら false</param>
/// <param name="Label">ラベル文言（例: "出勤 9:12"、"退勤 18:30 ・ 8時間12分"）</param>
/// <param name="IsAuto">自動で入った退勤か（ラベルに印を付ける）</param>
public sealed record WorkDayMarker(DateTime Time, bool IsStart, string Label, bool IsAuto);
