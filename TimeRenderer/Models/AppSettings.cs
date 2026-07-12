using System;

namespace TimeRenderer.Models;

public class AppSettings
{
    public bool IsMemoPanelVisible { get; set; } = true;
    public bool IsSettingsPanelVisible { get; set; } = false;
    public bool IsMemoEditMode { get; set; } = true;
    public int ViewMode { get; set; } = 0; // 0: Day, 1: Week
    public int DisplayStartHour { get; set; } = 0;  // 表示開始時刻（0～23）
    public int DisplayEndHour { get; set; } = 24;    // 表示終了時刻（1～24）
    public bool IsDarkMode { get; set; } = false;    // ダークモード
    public System.Collections.Generic.List<SprintInfo> ManualSprints { get; set; } = [];
    /// <summary>作業カテゴリ一覧（空の場合は既定値を使用）</summary>
    public System.Collections.Generic.List<CategoryInfo> Categories { get; set; } = [];
    public System.Collections.Generic.List<DayOfWeek> EnabledDaysOfWeek { get; set; } =
    [
        DayOfWeek.Monday,
        DayOfWeek.Tuesday,
        DayOfWeek.Wednesday,
        DayOfWeek.Thursday,
        DayOfWeek.Friday,
        DayOfWeek.Saturday,
        DayOfWeek.Sunday
    ];
}

