using System;

namespace TimeRenderer;

public class AppSettings
{
    public bool IsMemoPanelVisible { get; set; } = true;
    public bool IsMemoEditMode { get; set; } = true;
    public string MemoText { get; set; } = "";
    public int ViewMode { get; set; } = 0; // 0: Day, 1: Week
    public int DisplayStartHour { get; set; } = 0;  // 表示開始時刻（0～23）
    public int DisplayEndHour { get; set; } = 24;    // 表示終了時刻（1～24）
}
