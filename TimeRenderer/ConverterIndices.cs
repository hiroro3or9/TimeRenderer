using System;

namespace TimeRenderer
{
    /// <summary>
    /// XAMLのMultiBindingで各コンバーターに渡される配列(values)のインデックス定義
    /// </summary>
    public static class ConverterIndices
    {
        public static class DateToPageVisibility
        {
            public const int RequiredCount = 3;

            public const int StartTime = 0;
            public const int CurrentDate = 1;
            public const int ViewMode = 2;
        }

        public static class DateToPagePosition
        {
            public const int RequiredCount = 6;

            public const int StartTime = 0;
            public const int ColumnIndex = 1;
            public const int MaxColumnIndex = 2;
            public const int CurrentDate = 3;
            public const int ViewMode = 4;
            public const int CanvasActualWidth = 5;
            public const int IsAllDay = 6;
        }

        public static class DateToVisibleDays
        {
            public const int RequiredCount = 2;

            public const int CurrentDate = 0;
            public const int ViewMode = 1;
        }

        public static class TimeToPosition
        {
            public const int RequiredCount = 1;

            public const int Time = 0;
            public const int DisplayStartHour = 1;
        }

        public static class LShapeGeometry
        {
            public const int RequiredCount = 2;

            public const int Width = 0;
            public const int Height = 1;
        }
    }
}
