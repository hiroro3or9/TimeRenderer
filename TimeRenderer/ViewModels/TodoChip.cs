using System;

using TimeRenderer.Models;

namespace TimeRenderer.ViewModels;

/// <summary>
/// 日/週ビューの終日行に並べる ToDo 1件分の配置情報。
///
/// 期限日を持つ未完了の ToDo だけがここに現れる。
/// 横位置は終日イベントと同じコンバーター（DateToPagePositionConverter）で計算するため
/// <see cref="Date"/> を日付として渡し、縦位置は終日イベントの下に続く段として
/// <see cref="RowIndex"/> を割り当てる。
/// </summary>
/// <param name="Todo">元の ToDo</param>
/// <param name="Date">期限日（0:00）</param>
/// <param name="RowIndex">終日行の中での縦積み位置（終日イベントの段数の続きから始まる）</param>
public sealed record TodoChip(TodoItem Todo, DateTime Date, int RowIndex);
