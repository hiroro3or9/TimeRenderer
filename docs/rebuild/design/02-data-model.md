# 02. データモデル

すべて `TimeRenderer.Models` 名前空間。JSON は `System.Text.Json`（`WriteIndented = true`、既定の命名＝プロパティ名そのまま、enum は**数値**、DateTime は ISO 8601、TimeSpan は `"hh:mm:ss"`）。
`[JsonIgnore]` と書いた項目は保存しない。**プロパティ名と enum の数値は既存データとの互換のため変更禁止。**

## 1. 共通基盤

```csharp
public abstract class ObservableObject : INotifyPropertyChanged
{
    public event PropertyChangedEventHandler? PropertyChanged;
    protected void OnPropertyChanged([CallerMemberName] string? name = null) => PropertyChanged?.Invoke(this, new(name));
    protected bool SetProperty<T>(ref T field, T value, [CallerMemberName] string? name = null)
    { if (EqualityComparer<T>.Default.Equals(field, value)) return false; field = value; OnPropertyChanged(name); return true; }
}
```

期間の表示書式（多くの箇所で共通）：`WorkDayLog.FormatDuration(TimeSpan)` = 1時間以上「{時間}時間{分}分」、未満「{分}分」。
「H:MM」形式が必要な箇所は `$"{(int)d.TotalHours}:{d.Minutes:D2}"`（24時間超でも桁落ちしない）。

## 2. ScheduleItem（予定・実績）

`ObservableObject` 派生。JSON のプロパティ順は宣言順。

| プロパティ | 型 | 既定 | 保存 | 説明 |
| --- | --- | --- | --- | --- |
| `Id` | string | `Guid.NewGuid().ToString("N")` | ○ | 永続ID |
| `Kind` | `ScheduleItemKind` | Legacy(0) | ○ | 変更時 `IsPlanned`,`IsRecorded`,`KindLabel`,`ToolTipText` も通知 |
| `SourcePlanId` | string? | null | ○ | 予定から開始した実績の元予定ID |
| `Title` | string | "" | ○ | 変更時 `ToolTipText` 通知 |
| `StartTime` / `EndTime` | DateTime | — | ○ | 変更時 `DurationHours`,`ToolTipText` 通知 |
| `IsAllDay` | bool | false | ○ | 終日は 0:00〜翌0:00 で保存 |
| `Content` | string | "" | ○ | メモ |
| `BackgroundColor` | Brush | `Brushes.LightBlue` | × | 表示色 |
| `CategoryId` | string? | null | ○ | null は旧データ（色でフォールバック解決） |
| `ProjectCodeId` | string? | null | ○ | null = 未設定 |
| `RoutineId` | string? | null | ○ | 生成元の定期予定 |
| `TodoId` | string? | null | ○ | 元になった ToDo |
| `IsVirtual` | bool | false | × | 定期予定の表示用仮想アイテム（保存しない） |
| `RemindAtStart` | bool | false | ○ | 単発予定：開始時に通知 |
| `AutoStartRecording` | bool | false | ○ | 単発予定：開始時に自動記録 |
| `ForceStartRecording` | bool | false | ○ | 自動開始時、記録中でも切り替える |
| `ColorCode` | string | — | ○ | get=`BackgroundColor.ToString()`（`#AARRGGBB`）、set=`BrushConverter` で解釈（失敗は無視） |
| `IsSelected` | bool | false | × | 選択状態（全ビュー共通） |
| `ColumnIndex` | int | 0 | × | 終日行の縦積み位置 |
| `DurationHours` | double | — | × | `(End-Start).TotalHours` |
| `IsPlanned` / `IsRecorded` | bool | — | × | Kind 判定 |
| `KindLabel` | string | — | × | 「予定」/「実績」/「未分類」 |
| `ToolTipText` | string | — | × | 下記 |

```csharp
public enum ScheduleItemKind { Legacy = 0, Planned = 1, Recorded = 2 }
```

`ToolTipText`（`\n` 区切り）：
1. タイトル（空白なら「(無題)」）
2. `KindLabel`
3. 終日：開始日と終了日が同日か終了日の前日なら `MM/dd (ddd) 終日`、それ以外 `MM/dd 〜 MM/dd 終日`（終了は -1日）
   同日：`MM/dd (ddd) HH:mm 〜 HH:mm`／日跨ぎ：`MM/dd (ddd) HH:mm 〜 MM/dd (ddd) HH:mm`
4. 終日以外：1時間以上 `所要 {0.#} 時間`、未満 `所要 {0} 分`
5. メモがあれば空行＋メモ（改行を空白に、60文字超は60文字＋「…」）

### 旧データの移行（読込時）
`Id` が空 → 新規採番。`Kind == Legacy` → `IsAllDay || RoutineId != null || RemindAtStart || AutoStartRecording || EndTime > 今` なら Planned、それ以外 Recorded。どちらかがあれば初期化完了後に1回保存。

## 3. ScheduleSegment（日/週描画用、保存しない）

`ScheduleSegment(ScheduleItem item, DateTime startTime, DateTime endTime)`：`Item`, `StartTime`, `EndTime`, `Title`/`Content`/`BackgroundColor`/`IsAllDay`（Item から）, `DurationHours`, `ColumnIndex`（set可）, `MaxColumnIndex`（set可）。変更通知なし（毎回作り直す）。

## 4. ItemSnapshot（取り消し用の写し）

`Title, Content, Kind, SourcePlanId, StartTime, EndTime, IsAllDay, BackgroundColor, CategoryId, ProjectCodeId, RoutineId, TodoId, RemindAtStart, AutoStartRecording, ForceStartRecording`（init のみ）
- `static Capture(ScheduleItem)` / `ApplyTo(ScheduleItem)`（同じ順に書き戻す）
- `IsSameAs(other)`：全項目比較（Brush は `ToString()` 比較）

## 5. CategoryInfo（カテゴリ）

| プロパティ | 型 | 既定 | 保存 |
| --- | --- | --- | --- |
| `Id` | string | Guid "N" | ○ |
| `Name` | string | "" | ○ |
| `ColorCode` | string | `Brushes.LightBlue.ToString()` | ○（変更時 `Brush` 通知） |
| `Brush` | Brush | `CreateBrush(ColorCode)` | × |
| `IsFilterEnabled` | bool | true | ×（表示フィルタ・セッション内のみ） |

`static Brush CreateBrush(string code)`：`BrushConverter` で変換し Freeze。失敗・空は `Brushes.LightBlue`。

既定カテゴリ（設定が空のとき）：

| Name | ColorCode |
| --- | --- |
| 作業 | #FFADD8E6 (LightBlue) |
| 会議 | #FF90EE90 (LightGreen) |
| 休憩 | #FFFFB6C1 (LightPink) |
| 学習 | #FFFFFFE0 (LightYellow) |
| 雑務 | #FFD3D3D3 (LightGray) |
| 重要 | #FFF08080 (LightCoral) |
| 予定 | #FFE6E6FA (Lavender) |
| その他 | #FFE0FFFF (LightCyan) |
| 記録 | #FFFF8C00 (DarkOrange) |

カテゴリ色パレット（`MainViewModel.PaletteColors`、`record PaletteColor(Name, Code){ Brush }`）：
ライトブルー LightBlue / ライトグリーン LightGreen / ライトピンク LightPink / ライトイエロー LightYellow / ライトグレー LightGray / ライトコーラル LightCoral / ラベンダー Lavender / ライトシアン LightCyan / ダークオレンジ DarkOrange / ライトサーモン LightSalmon / カーキ Khaki / プラム Plum / パウダーブルー PowderBlue / ミントクリーム **Aquamarine** / ウィート Wheat / シルバー Silver（Code は `Brushes.X.ToString()`）

## 6. ProjectCodeInfo（プロジェクトコード）

| プロパティ | 型 | 既定 | 保存 |
| --- | --- | --- | --- |
| `Id` | string | Guid "N" | ○ |
| `Code` | string | ""（set 時 Trim、変更で `DisplayName` 通知） | ○ |
| `Name` | string | ""（同上） | ○ |
| `IsActive` | bool | true（変更で `DisplayName` 通知） | ○ |
| `IsFilterEnabled` | bool | true | × |
| `DisplayName` | string | 下記 | × |

- `DisplayName`：Code と Name 両方→「{Code} - {Name}」、片方→その値、両方空→「（コード未入力）」。無効なら末尾に「（無効）」
- `const UnassignedId = ""`, `const UnassignedLabel = "（未設定）"`
- `static Unassigned { get; } = new(){ Id = "", Name = "（未設定）" }`（コンボ先頭に置く疑似項目。マスターには入れない）
- `static string? ToStoredId(ProjectCodeInfo?)` / `static ProjectCodeInfo? Normalize(ProjectCodeInfo?)`：Id が空なら null
- 既定：`[ { Code="GENERAL", Name="共通" } ]`

## 7. RoutineScheduleItem（定期予定テンプレート）

変更通知なしのプレーンクラス。挙動（`OccursOn` など）は `design/11`。

| プロパティ | 型 | 既定 | 備考 |
| --- | --- | --- | --- |
| `Id` | string | Guid "N" | |
| `Title` | string | "" | |
| `Recurrence` | `RecurrenceType` | Weekly | `Weekly=0, MonthlyByDate=1, MonthlyByWeekday=2` |
| `Interval` | int | 1 | get は 1 未満を 1 |
| `DaysOfWeek` | List&lt;DayOfWeek&gt; | [] | |
| `DayOfMonth` | int | 1 | get は 1〜31 に Clamp。31=末日 |
| `WeeksOfMonth` | List&lt;int&gt; | [] | 1〜5、`LastWeekOfMonth = -1` |
| `SkipEvery` | int | 0 | get：2未満→0、上限12 |
| `SkipIndex` | int | 1 | get：SkipEvery&lt;2→1、それ以外 1〜SkipEvery |
| `StartDate` | DateTime | default | default は旧データ（起動時に今日へ移行） |
| `StartTime` / `EndTime` | TimeSpan | — | 時刻部分 |
| `CategoryId` | string? | null | |
| `ProjectCodeId` | string? | null | |
| `ColorCode` | string | Lavender | カテゴリ未解決時の色 |
| `IsAutoStart` | bool | false | |
| `IsForceStart` | bool | false | |
| `IsEnabled` | bool | true | |
| `ExcludedDates` | List&lt;DateTime&gt; | [] | この日は生成しない |
| `IsValidRecurrence` [JsonIgnore] | bool | | `EndTime>StartTime && (MonthlyByDate || DaysOfWeek.Count>0) && (!MonthlyByWeekday || WeeksOfMonth.Count>0)` |
| `RecurrenceDisplay` / `TimeRangeDisplay` / `StartDateDisplay` [JsonIgnore] | string | | `design/11` |

## 8. SprintInfo

| プロパティ | 型 | 既定 |
| --- | --- | --- |
| `Id` | string | `Guid.NewGuid().ToString()`（**ハイフンあり**） |
| `Name` | string | "" |
| `StartDate` / `EndDate` | DateTime | 日付のみ |
| `IsManual` | bool | false |
| `UnrecordedTimeProjectCodeId` | string? | 旧形式の移行用（書き込まない） |

## 9. TodoItem（ToDo）

`ObservableObject` 派生。挙動（相対通知・繰り返し・派生表示）の詳細は `design/09`。

| プロパティ | 型 | 既定 | 保存 |
| --- | --- | --- | --- |
| `Id` | string | Guid "N" | ○ |
| `Title` | string | "" | ○ |
| `Content` | string | "" | ○ |
| `DueDate` | DateTime? | null（set で `.Date`） | ○ |
| `RemindAt` | DateTime? | null | ○ |
| `RemindOffsetDays` | int? | null（set で 0〜365 に Clamp） | ○ |
| `PlannedOn` | DateTime? | null（set で `.Date`） | ○ |
| `Priority` | `TodoPriority` | Normal | ○ `Low=0, Normal=1, High=2` |
| `IsCompleted` | bool | false | ○ |
| `CompletedAt` | DateTime? | null | ○ |
| `CategoryId` | string? | null | ○ |
| `ColorCode` | string | LightBlue（空文字の set は無視） | ○ |
| `RecordedTicks` | long | 0（負は0） | ○ |
| `EstimatedMinutes` | int | 0（0〜10080 に Clamp） | ○ |
| `SortOrder` | int | 0 | ○ |
| `Recurrence` | `TodoRecurrenceUnit` | None | ○ `None=0, Day=1, Week=2, Month=3` |
| `RecurrenceInterval` | int | 1（set で 1〜99、get は 1 未満を 1） | ○ |
| `RecurrenceDaysOfWeek` | List&lt;DayOfWeek&gt; | [] | ○ |
| `RecurrenceFromCompletion` | bool | false | ○ |
| `Subtasks` | List&lt;TodoSubtask&gt; | [] | ○ |
| `CreatedAt` | DateTime | `DateTime.Now` | ○ |
| `IsExpanded` / `NewSubtaskTitle` | bool / string | false / "" | × |
| `static DefaultRemindHour` | int | 9 | ×（設定から配る） |

## 10. TodoSubtask / TodoSnapshot

`TodoSubtask`（ObservableObject）：`Id`(Guid "N"), `Title`, `IsCompleted`。`Clone()`（Id含め複製）, `IsSameAs(other)`。

`TodoSnapshot`：`Title, Content, DueDate, RemindAt, RemindOffsetDays, PlannedOn, Priority, IsCompleted, CompletedAt, CategoryId, ColorCode, EstimatedMinutes, Recurrence, RecurrenceInterval, RecurrenceDaysOfWeek(コピー), RecurrenceFromCompletion, Subtasks(Clone のリスト)`。**`RecordedTicks` は含めない**（記録の取り消しで別途戻すため二重に戻さない）。
`ApplyTo` の順序：Title, Content, **RemindOffsetDays=null → DueDate → RemindAt → RemindOffsetDays**, PlannedOn, Priority, **IsCompleted → CompletedAt**, CategoryId, ColorCode, EstimatedMinutes, Recurrence, RecurrenceInterval, RecurrenceDaysOfWeek, RecurrenceFromCompletion, Subtasks（Clone）。
`IsSameAs`：CompletedAt 以外の全項目比較（曜日は SequenceEqual、サブタスクは件数＋順に IsSameAs）。

## 11. WorkDayLog（勤務記録）

変更通知なし。

| プロパティ | 型 | 既定 | 保存 |
| --- | --- | --- | --- |
| `Date` | DateTime | 出勤日の日付 | ○ |
| `StartTime` | DateTime | | ○ |
| `EndTime` | DateTime? | null=未退勤 | ○ |
| `EndSource` | `WorkEndSource` | Manual | ○ `Manual=0, AwayDetected=1, AutoClosed=2` |
| `Note` | string | "" | ○（その日のふりかえり） |
| `HasNote` / `NoteSingleLine` / `IsFinished` / `Duration` / `DurationText` | | | × |

- `NoteSingleLine`：CRLF/LF/CR を空白にして Trim
- `Duration`：`(EndTime ?? DateTime.Now) - StartTime`（負は0）
- `WorkDayEditResult(bool IsDeleted, DateTime Date, DateTime StartTime, DateTime? EndTime, string Note)`（record）

## 12. AwayPeriod（離席、保存しない）

```csharp
public enum AwayReason { Idle, Sleep, Locked }
public sealed record AwayPeriod(DateTime Start, DateTime End, AwayReason Reason)
```
- `Duration`（負は0）、`ReasonText`：Sleep「スリープ」/Locked「ロック」/Idle「無操作」
- `RangeText`：`HH:mm 〜 HH:mm`
- `DurationText`：1時間以上「{h}時間{m}分」、未満「{m}分」
- `DisplayText`：`{RangeText}  （{DurationText}・{ReasonText}）`（RangeText の後ろに半角空白2つ）
- `ClipTo(start, end)`：重なり部分の `with` コピー、重ならなければ null

## 13. AppUsageInterval（使用アプリ期間）

`Start`, `End`, `ProcessName`（例 devenv）, `AppName`（FileDescription、無ければプロセス名）, `WindowTitle`, `IsWindowTitleSpecific`（タイトル変更で区切った新形式なら true）, `Duration` [計算], `ClipTo(start,end)`（コピーを返す／重ならなければ null）。

## 14. UnrecordedGap（未記録の帯、保存しない）

`sealed record UnrecordedGap(DateTime StartTime, DateTime EndTime)`：`Duration`, `DurationHours`, `ColumnIndex=>0`, `MaxColumnIndex=>0`, `IsAllDay=>false`（コンバーター共用のため名前を ScheduleSegment に揃える）,
`RangeText`=`H:mm-H:mm`, `DurationText`=`WorkDayLog.FormatDuration`, `Label`=「未記録 {DurationText}」, `ToolTipText`=「{RangeText}（{DurationText}）\nこの時間の記録がありません」

## 15. UnrecordedTimeProjectAssignment

ObservableObject。`StartDate`（既定 `DateTime.Today`、set で `.Date`）, `ProjectCodeId`（string?）, `RangeText`（[JsonIgnore]、VM が入れる表示文字列）。

## 16. GitRepositoryInfo / GitCommit

`GitRepositoryInfo`（ObservableObject）：`Id`(Guid "N"), `Path`（Trim、変更で DisplayName/FolderName 通知）, `Name`（Trim）, `IsEnabled`(true), `ProjectCodeId`(null),
`FolderName`[JsonIgnore]（末尾区切りを除いた最後のフォルダ名、例外時は Path）, `DisplayName`[JsonIgnore]（Name が空なら FolderName）。

`sealed record GitCommit(string RepositoryId, string RepositoryName, DateTime CommittedAt, string ShortHash, string Subject)`：`TimeText`=`H:mm`, `OriginText`=「{RepositoryName} · {ShortHash}」。保存しない。

## 17. AppSettings（appsettings.json）

プレーンクラス。**46項目**。「導入」は `plan/phases.md` のフェーズ番号（そのフェーズで AppSettings と binding に追加する）。

| # | プロパティ | 型 | 既定 | 補正（AppSettingsNormalizer） | 導入 |
| --- | --- | --- | --- | --- | --- |
| 1 | `IsSettingsPanelVisible` | bool | false | | 4 |
| 2 | `IsManagementPanelVisible` | bool | false | 設定パネルが開いていれば false | 4 |
| 3 | `IsTodoPanelVisible` | bool | false | 設定または管理が開いていれば false | 13 |
| 4 | `ShowCompletedTodos` | bool | false | | 13 |
| 5 | `TodoSortMode` | int | 0 | 未定義値→0 | 13 |
| 6 | `IsTodoDigestEnabled` | bool | true | | 14 |
| 7 | `TodoDigestHour` | int | 9 | 0〜23 | 14 |
| 8 | `LastTodoDigestDate` | string? | null | （`yyyy-MM-dd`、壊れていれば未通知扱い） | 14 |
| 9 | `TodoArchiveRetentionDays` | int | 90 | 0以下→90、7〜3650 | 14 |
| 10 | `IsTodoQuickSyntaxEnabled` | bool | true | | 13 |
| 11 | `TodoDefaultRemindHour` | int | 9 | 0〜23 | 14 |
| 12 | `IsTodoReminderSoundEnabled` | bool | true | | 14 |
| 13 | `TodoSnoozeMinutes` | int | 10 | {5,10,15,30,60,120} 以外→10 | 14 |
| 14 | `ViewMode` | int | 7(Today) | 未定義値→7 | 4 |
| 15 | `DisplayStartHour` | int | 0 | 0〜23 | 5 |
| 16 | `DisplayEndHour` | int | 24 | Start+1〜24 | 5 |
| 17 | `IsDarkMode` | bool | false | | 4 |
| 18 | `TimelinePixelsPerDay` | double | 120.0 | 非有限/0以下→120、8〜960 | 15 |
| 19 | `TimelineGroupMode` | int | 0 | 未定義値→0 | 15 |
| 20 | `TimelineSprintCount` | int | 5 | 0以下→5、1〜25 | 15 |
| 21 | `IsAwayDetectionEnabled` | bool | true | | 9 |
| 22 | `AwayThresholdMinutes` | int | 10 | 0以下→10、1〜240 | 9 |
| 23 | `AwayHandlingMode` | int | 0 | 未定義値→0 | 9 |
| 24 | `IsWorkEndDetectionEnabled` | bool | true | | 10 |
| 25 | `WorkEndThresholdMinutes` | int | 30 | 0以下→30、5〜480 | 10 |
| 26 | `WorkEndEarliestHour` | int | 17 | 0〜23 | 10 |
| 27 | `IsWorkEndReviewEnabled` | bool | true | | 16 |
| 28 | `IsAppUsageTrackingEnabled` | bool | true | | 17 |
| 29 | `IsMiniRecordingBarEnabled` | bool | true | | 8 |
| 30 | `MiniRecordingBarLeft` | double? | null | | 8 |
| 31 | `MiniRecordingBarTop` | double? | null | | 8 |
| 32 | `IsGitCommitLookupEnabled` | bool | true | | 17 |
| 33 | `GitRepositories` | List&lt;GitRepositoryInfo&gt; | [] | null→[] | 17 |
| 34 | `SnapMinutes` | int | 15 | 0以下→15、1〜60 | 6 |
| 35 | `IsMagnetSnapEnabled` | bool | true | | 6 |
| 36 | `ManualSprints` | List&lt;SprintInfo&gt; | [] | null→[] | 11 |
| 37 | `Categories` | List&lt;CategoryInfo&gt; | [] | null→[]（空なら既定カテゴリ） | 7 |
| 38 | `RecordingCategoryId` | string? | null | | 7 |
| 39 | `ProjectCodes` | List&lt;ProjectCodeInfo&gt; | [] | null→[]（空なら既定） | 7 |
| 40 | `DefaultProjectCodeId` | string? | null | ""=明示的に未設定、null=先頭の有効コード | 7 |
| 41 | `IsUnrecordedTimeProjectAggregationEnabled` | bool | false | | 17 |
| 42 | `UnrecordedTimeProjectCodeId` | string? | null | | 17 |
| 43 | `UnrecordedTimeProjectAssignments` | List&lt;…&gt;? | null | null=旧形式から移行、[]=割り当て無し | 17 |
| 44 | `PinnedTitles` | List&lt;string&gt;? | null | null=既定「打ち合わせ」「休憩」、[]=全削除 | 7 |
| 45 | `RoutineSchedules` | List&lt;RoutineScheduleItem&gt; | [] | null→[] | 12 |
| 46 | `EnabledDaysOfWeek` | List&lt;DayOfWeek&gt; | 月〜日の7つ | null/空→月〜日 | 5 |

> Normalizer はフェーズ3で全項目ぶん作ってよい（AppSettings 自体を最初から46項目で作り、binding だけ各フェーズで足す、でもよい。その場合は契約テストをフェーズ18まで Skip にする）。

## 18. 設定の保存と復元（SettingsMapping）

```csharp
private interface IAppSettingsBinding { string PropertyName { get; } void Capture(MainViewModel vm, AppSettings s); void Apply(MainViewModel vm, AppSettings s); }
private sealed class AppSettingsBinding<T>(string name, Func<MainViewModel,T> capture, Action<AppSettings,T> write,
    Func<AppSettings,T> read, Action<MainViewModel,T> apply) : IAppSettingsBinding { ... }
private static IAppSettingsBinding Bind<T>(...) => new AppSettingsBinding<T>(...);
private static readonly IReadOnlyList<IAppSettingsBinding> AppSettingsBindings = [ Bind(nameof(AppSettings.X), vm => vm.X, (s,v)=>s.X=v, s=>s.X, (vm,v)=>vm.ApplySetting(ref vm._x, v, nameof(X))), ... ];
private static readonly bool AppSettingsMappingsAreComplete = ValidateAppSettingsBindings(); // 不足・重複・未知があれば InvalidOperationException
internal static IReadOnlyList<string> MappedAppSettingsPropertyNames => [.. AppSettingsBindings.Select(b => b.PropertyName)];
private void ApplySetting<T>(ref T field, T value, params string[] notify) { field = value; foreach (var n in notify) OnPropertyChanged(n); }
```

- `SaveSettings()`：`_isInitialized` なら `SettingsService.SaveSettings(BuildSettings())`（同期保存。デバウンスしない）
- `LoadSettings()`：読めたら `Normalize` → 全 binding の Apply → `FinalizeAppSettingsApplication()`
- Apply は**プロパティの setter を通さず**バッキングフィールドへ直接入れる（setter の SaveSettings を走らせない）
- コレクションの Capture はコピー（`[.. vm.List]`）

binding の並び順（依存があるもの）：`Categories` → `RecordingCategoryId`、`ProjectCodes` → `DefaultProjectCodeId`。

Apply 時の追加処理：
| 項目 | Apply |
| --- | --- |
| TodoSortMode | 通知 `CurrentTodoSortMode`, `SelectedTodoSortOption` |
| LastTodoDigestDate | `ParseTodoDigestDate` |
| TodoSnoozeMinutes | 通知 `TodoSnoozeMinutes`, `TodoSnoozeLabel` |
| ViewMode | フィールド設定＋`NotifyViewModeDependents()` |
| IsDarkMode | フィールド設定＋`App.ApplyTheme(value)` |
| TimelinePixelsPerDay | 通知 `TimelineZoomText` も |
| TimelineGroupMode | 通知 `SelectedTimelineGroupModeOption`, `IsTimelineCategoryMode`, `TimelineLabelColumnWidth` |
| TimelineSprintCount | 通知 `SelectedTimelineSpanOption` |
| AwayHandlingMode | 通知 `SelectedAwayHandlingOption` |
| WorkEndEarliestHour | 通知 `SelectedWorkEndEarliestOption` |
| GitRepositories | `LoadGitRepositories` |
| ManualSprints | `_manualSprints = value`（通知は Finalize） |
| Categories / RecordingCategoryId | `LoadCategories` / `LoadRecordingCategoryId` |
| ProjectCodes / DefaultProjectCodeId | `LoadProjectCodes` / `LoadDefaultProjectCodeId` |
| IsUnrecordedTimeProjectAggregationEnabled, UnrecordedTimeProjectCodeId | フィールドのみ |
| UnrecordedTimeProjectAssignments | 一時フィールド `_loadedUnrecordedTimeAssignments` へ |
| PinnedTitles | `LoadPinnedTitles` |
| RoutineSchedules | `_routines = value` ＋通知 `Routines` |
| EnabledDaysOfWeek | `_enabledDaysOfWeek = value` |

`FinalizeAppSettingsApplication()`（全 Apply 後に1回）：
1. `ApplyDefaultRemindHour()`（`TodoItem.DefaultRemindHour` と `TodoQuickParser.DefaultRemindHour` に配る）
2. 通知 `ScheduleGridHeight`、`InitializeTimeLabels()`
3. `ApplyAwaySettings()`
4. `LoadUnrecordedTimeProjectAggregation(enabled, codeId)` → `LoadUnrecordedTimeAssignments(loaded, _manualSprints)` → 通知 `ManualSprints`
5. `NotifyShowDaysProperties()`、通知 `EnabledDaysCount`, `EnabledDayHeaders`、`UpdateVisibleDays()`
6. `MigrateRoutineStartDates()` → `MigrateGeneratedRoutineItems()` → `EnsureRoutineOccurrences(CurrentDate)`

### 契約テスト
`AppSettings` の public インスタンスプロパティ名一覧と `MappedAppSettingsPropertyNames` が、件数・重複なし・名前すべて一致すること。

## 19. ViewModel 側の enum（設定に数値で保存）

```csharp
public enum ViewMode { Day, Week, Month, Sprint, SprintTimeline, Stats, Notes, Today }  // 0..7。末尾に足す
public enum TodoSortMode { DueDate = 0, Priority = 1, Created = 2, Manual = 3 }
public enum TimelineGroupMode { Packed, Category, Flat }
public enum AwayHandlingMode { Ask, AlwaysExclude, AlwaysKeep }
```
保存しない enum：`StatsPeriodMode { Week, Month, Sprint }`、`RoutineScope { ThisDay, WholeSeries }`（Services）、`LoadStatus`、`TodoReminderState`。

選択肢 record（`ToString()` は Label を返す）：`ViewModeOption(Mode, Label)`, `AwayHandlingOption`, `TimelineGroupModeOption`, `TimelineZoomPreset(Label, PixelsPerDay)`, `TimelineSpanOption(Count, Label)`, `WorkEndEarliestOption(Hour, Label)`, `TodoSortOption(Mode, Label)`（ToString なし、DisplayMemberPath="Label"）, `TimerOption(Name, Minutes)`。
