using System;

namespace TimeRenderer.Models;

public class AppSettings
{
    public bool IsSettingsPanelVisible { get; set; } = false;
    /// <summary>ToDo パネルを開いているか</summary>
    public bool IsTodoPanelVisible { get; set; } = false;
    /// <summary>ToDo 一覧に完了済みも表示するか</summary>
    public bool ShowCompletedTodos { get; set; } = false;
    /// <summary>ToDo 一覧の並べ替え（0: 期限順, 1: 優先度順, 2: 追加順, 3: 手動）</summary>
    public int TodoSortMode { get; set; } = 0;
    /// <summary>1日1回、ToDo の件数をまとめて通知するか</summary>
    public bool IsTodoDigestEnabled { get; set; } = true;
    /// <summary>まとめ通知を出す時刻（時）</summary>
    public int TodoDigestHour { get; set; } = 9;
    /// <summary>
    /// まとめ通知を最後に出した日（yyyy-MM-dd）。
    /// 起動のたびに出さないよう、セッションをまたいで覚えておく。
    /// </summary>
    public string? LastTodoDigestDate { get; set; }
    /// <summary>完了済みの ToDo を一覧に残す日数（過ぎたものはアーカイブへ移す）</summary>
    public int TodoArchiveRetentionDays { get; set; } = 90;
    /// <summary>クイック追加欄で @ ! # ~ * の記法を解釈するか</summary>
    public bool IsTodoQuickSyntaxEnabled { get; set; } = true;
    /// <summary>通知時刻を省略したときに使う時（クイック追加の記法と編集ダイアログの既定値）</summary>
    public int TodoDefaultRemindHour { get; set; } = 9;
    /// <summary>ToDo の通知を出すときに音を鳴らすか</summary>
    public bool IsTodoReminderSoundEnabled { get; set; } = true;
    /// <summary>通知バナーの「あとで」で先送りする分数</summary>
    public int TodoSnoozeMinutes { get; set; } = 10;
    public int ViewMode { get; set; } = 7; // Today（既存の列挙値を保つため末尾の値）
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
    /// <summary>この時刻より前に始まった離席は退勤確認の対象にしない（0 は制限なし）</summary>
    public int WorkEndEarliestHour { get; set; } = 17;
    /// <summary>退勤したときに、今日のふりかえりと ToDo の繰り越しを確認するか</summary>
    public bool IsWorkEndReviewEnabled { get; set; } = true;
    /// <summary>記録中に前面アプリを自動記録するか</summary>
    public bool IsAppUsageTrackingEnabled { get; set; } = true;
    /// <summary>ドラッグ操作で時刻を丸める単位（分）</summary>
    public int SnapMinutes { get; set; } = 15;
    public System.Collections.Generic.List<SprintInfo> ManualSprints { get; set; } = [];
    /// <summary>作業カテゴリ一覧（空の場合は既定値を使用）</summary>
    public System.Collections.Generic.List<CategoryInfo> Categories { get; set; } = [];
    /// <summary>記録開始時に使う既定カテゴリのID（null・未知のIDなら「記録」または先頭カテゴリ）</summary>
    public string? RecordingCategoryId { get; set; }
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

