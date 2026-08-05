using System.Collections.Generic;

using TimeRenderer.Models;

namespace TimeRenderer.ViewModels;

/// <summary>
/// 退勤時のふりかえりダイアログの結果。
///
/// ふりかえりの一言は「そのまま閉じる」でも書き残せるようにしたいので、
/// 繰り越しの有無とは別に必ず返す（閉じ方で入力が消えると、
/// 書いたのに残らないという最も避けたい失敗が起きる）。
/// </summary>
/// <param name="CarriedOver">明日へ送ることになった ToDo（送らない場合は空）</param>
/// <param name="Note">その日のふりかえり（書いていなければ空文字）</param>
public sealed record WorkEndReviewResult(IReadOnlyList<TodoItem> CarriedOver, string Note);
