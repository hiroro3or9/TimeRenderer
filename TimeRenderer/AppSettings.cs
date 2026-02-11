using System;

namespace TimeRenderer
{
    public class AppSettings
    {
        public bool IsMemoPanelVisible { get; set; } = true;
        public bool IsMemoEditMode { get; set; } = true;
        public string MemoText { get; set; } = "";
    }
}
