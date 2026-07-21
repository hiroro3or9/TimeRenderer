using System;

namespace TimeRenderer.Models;

/// <summary>
/// 勤務記録の編集ダイアログの結果。
/// 「削除」も同じ導線から行えるようにするため、削除かどうかをフラグで表す。
/// </summary>
/// <param name="IsDeleted">その日の勤務記録を削除する場合は true（他の項目は無視される）</param>
/// <param name="Date">勤務日</param>
/// <param name="StartTime">出勤時刻</param>
/// <param name="EndTime">退勤時刻。未退勤のままにする場合は null</param>
public sealed record WorkDayEditResult(bool IsDeleted, DateTime Date, DateTime StartTime, DateTime? EndTime);
