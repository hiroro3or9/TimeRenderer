using System;

namespace TimeRenderer
{
    public class AppSettings
    {
        public bool IsMemoPanelVisible { get; set; } = true;
        public bool IsMemoEditMode { get; set; } = true;
        public string MemoText { get; set; } = "";
        public int ViewMode { get; set; } = 0; // 0: Day, 1: Week
    }
}
