using System;
using System.Collections.Generic;

using TimeRenderer.Models;

namespace TimeRenderer.ViewModels;

/// <summary>
/// 月・スプリントビューの1セル分。
///
/// 予定（<paramref name="dailyItems"/>）と、その日が期限の ToDo（<paramref name="dailyTodos"/>）を
/// 別々に持つ。セル内では予定を先に並べ、その下へ ToDo を続ける
/// （日/週ビューの終日行と同じ並びにして、ビューを切り替えても位置関係が変わらないようにする）。
/// </summary>
public class CalendarCellViewModel(
    DateTime date,
    bool isCurrentMonth,
    bool isToday,
    IReadOnlyList<ScheduleItem> dailyItems,
    IReadOnlyList<TodoItem> dailyTodos)
{
    public DateTime Date { get; } = date;
    public string DayText => Date.Day.ToString();
    public DayOfWeek DayOfWeek => Date.DayOfWeek;
    public bool IsCurrentMonth { get; } = isCurrentMonth;
    public bool IsToday { get; } = isToday;
    public IReadOnlyList<ScheduleItem> DailyItems { get; } = dailyItems;
    public IReadOnlyList<TodoItem> DailyTodos { get; } = dailyTodos;
}
