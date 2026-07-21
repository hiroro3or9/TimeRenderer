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
    /// <summary>タイムラインのズーム倍率（1日あたりのピクセル数）</summary>
    public double TimelinePixelsPerDay { get; set; } = 120.0;
    /// <summary>タイムラインの行のまとめ方（0: 詰める, 1: カテゴリ別, 2: 1件1行）</summary>
    public int TimelineGroupMode { get; set; } = 0;
    /// <summary>タイムラインに表示するスプリント数</summary>
    public int TimelineSprintCount { get; set; } = 5;
    /// <summary>離席・中断の検知を行うか</summary>
    public bool IsAwayDetectionEnabled { get; set; } = true;
    /// <summary>離席とみなすまでの無操作時間（分）</summary>
    public int AwayThresholdMinutes { get; set; } = 10;
    /// <summary>離席を検知したときの扱い（0: 毎回確認, 1: 常に除外, 2: 常にそのまま）</summary>
    public int AwayHandlingMode { get; set; } = 0;
    /// <summary>離席・スリープから復帰したときに勤務終了を確認するか</summary>
    public bool IsWorkEndDetectionEnabled { get; set; } = true;
    /// <summary>この時間だけ離席・スリープが続いたら勤務終了とみなして確認する（分）</summary>
    public int WorkEndThresholdMinutes { get; set; } = 30;
    /// <summary>ドラッグ操作で時刻を丸める単位（分）</summary>
    public int SnapMinutes { get; set; } = 15;
    public System.Collections.Generic.List<SprintInfo> ManualSprints { get; set; } = [];
    /// <summary>作業カテゴリ一覧（空の場合は既定値を使用）</summary>
    public System.Collections.Generic.List<CategoryInfo> Categories { get; set; } = [];
    /// <summary>タイトル入力欄に常に表示する定型タイトル（null は未設定＝既定値を使用）</summary>
    public System.Collections.Generic.List<string>? PinnedTitles { get; set; }
    /// <summary>定期予定（ルーティン）のテンプレート一覧</summary>
    public System.Collections.Generic.List<RoutineScheduleItem> RoutineSchedules { get; set; } = [];
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

