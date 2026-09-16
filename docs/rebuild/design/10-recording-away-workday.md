# 10. 記録・離席検知・出退勤・退勤時ふりかえり

## 1. 記録セッション（MainViewModel.Recording.cs）

「記録」＝タイマーで作業時間を測り、停止時に実績（`Kind=Recorded` の ScheduleItem）を作ること。**出勤/退勤とは別の軸**。

### 1.1 セッション中だけ持つ情報
`_recordingColorCode`, `_recordingCategoryId`, `_recordingProjectCodeId`, `_recordingSourceItem`（実績へ変換する元予定）, `_recordingTodo`（積算先 ToDo）。

### 1.2 開始経路
| 経路 | 処理 |
| --- | --- |
| ツールバーの記録ボタン `ToggleRecordingCommand` | 記録中なら `StopRecording()`、それ以外 `StartRecordingViaDialog()` |
| `StartRecordingViaDialog()` | メタデータクリア → 既定タイトル「作業ログ {HH:mm}」→ `ShowRecordingStartDialog(defaultTitle, TimerOptions, SelectedTimerOption, GetTitleSuggestions(), ActiveProjectCodes, DefaultProjectCode)`。キャンセルなら何もしない。タイトル（空なら既定）・タイマー・プロジェクトコードを反映して `BeginRecording(useSelectedTimer: true)` |
| トレイ・ホットキー `QuickToggleRecording()` | 記録中なら停止。それ以外メタデータクリア、プロジェクト=既定、タイトル「作業ログ {HH:mm}」で `BeginRecording(false)`（ダイアログなし） |
| 予定から `StartRecordingFromItem(item)` | 記録中なら `StopRecording(() => StartRecordingFromItem(item))`。予定かつ仮想なら先に `MaterializeOccurrence(item)`。タイトル・色・カテゴリ（`CategoryId ?? ResolveCategory(item)?.Id`）・**プロジェクトは予定のもの（未設定は未設定のまま）**・source=予定なら item／実績なら null・todo=`FindTodoById(item.TodoId)` → `BeginRecording(false)` |
| ToDo から | `design/09` §9 |

`BeginRecording(useSelectedTimer)`：`ClearAwayState()` → IsRecording=true → RecordingStartTime=今 → Duration=0 → `IsCountdownMode = useSelectedTimer && SelectedTimerOption.Minutes > 0` → `CountdownRemaining = カウントダウンなら分数 : null`。

### 1.3 停止 `StopRecording(Action? startNextRecording = null)`
```
if (!IsRecording) return
start = RecordingStartTime; end = 今; title = 空白なら「作業ログ {start:HH:mm}」
source = _recordingSourceItem; todo = _recordingTodo; metadata = CaptureRecordingMetadata(source, todo)
try { periods = start あり ? TakeAwayPeriodsForRecording(start, end) : [] }   // リセット前に取り出す
finally { ResetRecordingSession() }        // IsRecording=false, 時間/タイトル/メタデータ/カウントダウン/離席状態をクリア
startNextRecording?.Invoke()               // 確認ダイアログの待ち時間を次の計測に含めない
if (start なし) return
segments = ResolveRecordingSegments(title, start, end, periods)
if (segments.Count == 0) ShowAutoStartNotice("「{title}」は全体が離席だったため、記録しませんでした")
else SaveRecordingSegments(title, source, segments, metadata, todo)
```
カウントダウンが0になったら時計から `ToggleRecording()` で同じ停止処理（`design/01` §4）。

## 2. 停止後の保存（MainViewModel.RecordingStop.cs）

### 2.1 ResolveRecordingSegments
- 離席検知が無効、または離席0件 → `BuildSegments(start, end, periods, excludeAway:false)`（=1区間）
- `AwayHandlingMode.AlwaysExclude`：除外する＋`ShowAutoStartNotice(BuildAutoExcludeNotice(...))`
- `AlwaysKeep`：除外しない
- `Ask`：`ShowAwayReviewDialog(title, start, end, periods)` の結果

### 2.2 SaveRecordingSegments
```
edits = []
useSource = source != null && source.IsPlanned && ScheduleItems.Contains(source)
for i, (s, e) in segments:
    if (i == 0 && useSource) { ConvertSourcePlanToRecordedItem(source, title, s, e, edits); continue }
    item = RecordingItemHelper.CreateRecordedItem(title, s, e, metadata); ScheduleItems.Add(item); edits.Add(new AddItemEdit(item))
AddTodoRecordedTimeEdit(todo, segments, edits)   // todo が Todos に含まれるときだけ積算し ChangeTodoRecordedTimeEdit("実績時間の追加")
PushRecordingEdits(title, segments.Count, edits)
RecalculateLayout(); SaveData()
```
- `ConvertSourcePlanToRecordedItem`：仮想なら実体化 → before → `_isBatchUpdatingItem` の間に `RecordingItemHelper.ApplyRecordedSegment(source, title, s, e)` → 変化があれば `ModifyItemEdit(…, "記録")`
- `CaptureRecordingMetadata(source, todo)` = `RecordingItemMetadata(ColorCode: _recordingColorCode ?? RecordingCategory?.ColorCode ?? DarkOrange, CategoryId: _recordingCategoryId ?? RecordingCategory?.Id, ProjectCodeId: _recordingProjectCodeId（既定を足さない）, TodoId: todo?.Id ?? source?.TodoId, RoutineId: source?.RoutineId, SourcePlanId: source?.Id)`

### 2.3 RecordingItemHelper（純粋）
- `ApplyRecordedSegment(item, title, s, e)`：Kind=Recorded, Title, Start, End。**Content が空白のときだけ** `FormatDuration(e-s)`
- `CreateRecordedItem(title, s, e, metadata)`：Recorded, Title, Content=`FormatDuration`, 時間, ColorCode, CategoryId, ProjectCodeId, TodoId, RoutineId, SourcePlanId, ColumnIndex=0
- `FormatDuration(d)` =「記録時間: {(int)TotalHours}:{Minutes:D2}」

### 2.4 RecordingStopHelper（純粋）
- `ClipAndSortAwayPeriods(start, end, periods)`：end≤start なら空。各 `ClipTo` の非 null を Start 昇順
- `BuildSegments(start, end, periods, excludeAway)`：end≤start 空。除外しないなら `[(start,end)]`。除外：クリップ＆ソートした離席を**重なり・接触（`p.Start <= merged.End`）で統合**し、cursor=start から「離席開始&gt;cursor なら (cursor, 離席開始) を追加、離席終了&gt;cursor なら cursor=終了」、最後に cursor&lt;end なら (cursor, end)
- `GetExcludedDuration` = (end-start) − Σ除外後区間（重なりを二重計上しない）
- `BuildAutoExcludeNotice` =「離席 {件数} 件・合計 {h時間m分|m分} を記録から除きました（Ctrl+Z でこの記録ごと取り消せます）」

## 3. 記録開始ダイアログ（RecordingStartDialog）

`Title="記録開始" Height=345 Width=360 CenterOwner NoResize WindowStyle=ToolWindow 地 Background`。DataContext=自身。
プロパティ：`InputText`, `TimerOptions`, `SelectedTimerOption`, `TitleSuggestions`, `ProjectCodes`（先頭「（未設定）」）, `SelectedProjectCode`（既定 ?? 未設定）。
Grid Margin 20：「作業タイトル:」FieldLabel Margin 0,0,0,6 ／ `ComboBox InputCombo` 編集可 TextSearch無効 `{TitleSuggestions}` Text⇔`{InputText, PropertyChanged}` MaxDropDown 240 14px Padding 6,6 Margin 0,0,0,16 ／「プロジェクトコード:」／ ComboBox `{ProjectCodes}` `{SelectedProjectCode}` DisplayMemberPath DisplayName Height 32 Margin 0,0,0,16 ／「タイマー設定:」／ ComboBox `{TimerOptions}` `{SelectedTimerOption}` DisplayMemberPath Name Height 32 Margin 0,0,0,20 ／ 右寄せ［「開始」IsDefault Width 90 Margin 0,0,10,0 Primary ｜「キャンセル」IsCancel Width 90 Base］。
Loaded で InputCombo にフォーカスし、テンプレート内 `PART_EditableTextBox` を SelectAll。
DialogService の戻り値：`(InputText, SelectedTimerOption ?? default, ProjectCodeInfo.ToStoredId(SelectedProjectCode))`。

## 4. 離席検知（Services/AwayDetector.cs）

3経路：
| 経路 | 仕組み | 理由 |
| --- | --- | --- |
| 無操作 | `GetLastInputInfo`（5秒ポーリング） | 他アプリ操作中もシステム全体の入力を拾える |
| スリープ | `SystemEvents.PowerModeChanged` | `GetTickCount` はスリープ中に進まない |
| ロック | `SystemEvents.SessionSwitch` | 理由を「ロック」と出し分ける |

```csharp
[StructLayout(Sequential)] struct LASTINPUTINFO { public uint cbSize; public uint dwTime; }
[LibraryImport("user32.dll")] [return: MarshalAs(Bool)] static partial bool GetLastInputInfo(ref LASTINPUTINFO p);
[LibraryImport("kernel32.dll")] static partial uint GetTickCount();
public static TimeSpan GetIdleTime() { cbSize=SizeOf; if(!Get) return 0; return FromMilliseconds(unchecked(GetTickCount() - info.dwTime)); }  // 49.7日の一周対策
```
- プロパティ：`IdleThreshold`（既定10分）, `IsEnabled`, `IsAway`, `AwaySince`。イベント：`AwayDetected(AwayPeriod)`, `AwayStarted(DateTime since)`, `AwayEnded`
- `CheckIdle()`（5秒 DispatcherTimer）：無効なら離席中の状態を畳む。スリープ/ロック中は判定しない。idle ≥ 閾値で未離席なら `IsAway=true, AwaySince = 今 - idle`（閾値ぶん遡るのではなく実際の無操作時間）→ `AwayStarted`。閾値未満に戻ったら `Reset` → `Raise(since, 今 - idle, Idle)`
- `FlushPendingAway()`（記録停止直前に呼ぶ。ポーリング取りこぼし対策）：無効なら何もしない。スリープ中/ロック中なら今までを Raise して解除。離席中なら `returnedAt = max(since, 今 - idle)` で Raise
- `DiscardPendingAway()`（記録開始時）：スリープ/ロック/離席状態を通知せず破棄
- `ResetAwayState()`：IsAway=false, AwaySince=null, `AwayEnded`
- PowerMode：Suspend→`_suspendedAt=今`。Resume→あれば Raise(Sleep) してクリア、**`ResetAwayState()`**（復帰直後の大きな無操作を二重に数えない）
- SessionSwitch：Lock/ConsoleDisconnect/RemoteDisconnect→`_lockedAt ??= 今`。Unlock/ConsoleConnect/RemoteConnect→あれば Raise(Locked)、Reset
- `Raise(start, end, reason)`：**長さが閾値未満なら通知しない**
- `Dispose()`：タイマー停止、SystemEvents 購読解除

## 5. 離席の受け取り（MainViewModel.Away.cs）

- 設定：`IsAwayDetectionEnabled`（変更で `ApplyAwaySettings()`、false ならクリア、保存）、`AwayThresholdMinutes`（1〜240、選択肢 `[3,5,10,15,30,60]`）、`CurrentAwayHandlingMode`＋`AwayHandlingOptions`（「毎回確認する」「常に除外する」「常にそのまま記録する」）＋`SelectedAwayHandlingOption`
- 表示：`IsAwayNow`, `AwayBannerText`, `ShowAwayBanner = IsAwayNow && IsRecording`
- `InitializeAwayDetection()`（実行時のみ）：`new AwayDetector{ IsEnabled = ShouldRunAwayDetector, IdleThreshold = EffectiveIdleThreshold }` にイベント購読
- `ShouldRunAwayDetector = 離席検知有効 || 勤務終了検知有効`
- `EffectiveIdleThreshold`：両方有効→短い方、片方→その値（検知器は閾値未満を通知しないため短い方に合わせる）
- `ApplyAwaySettings()`：上2つを検知器へ反映
- `OnAwayStarted(since)`：離席検知有効かつ記録中なら IsAwayNow=true、`AwayBannerText = "{since:HH:mm} から操作がありません。記録は続いています（停止時に除外できます）"`
- `OnAwayEnded`：IsAwayNow=false、テキスト空
- `OnAwayDetected(period)`：まず `HandleAwayForWorkDay(period)`（§7.7）。離席検知有効かつ記録中なら `period.ClipTo(RecordingStartTime, 今)` を `_awayDuringRecording` に追加
- `ClearAwayState()`：リストクリア、`DiscardPendingAway()`、表示リセット
- `TakeAwayPeriodsForRecording(start, end)`：`FlushPendingAway()` → `ClipAndSortAwayPeriods` → リストクリアして返す
- `DisposeAwayDetection()`：購読解除・Dispose

## 6. 離席確認ダイアログ（AwayReviewDialog）

`Title="離席時間の確認" Height=420 Width=460 CenterOwner NoResize`。`ShouldExclude`。Grid Margin 20、5行：
- HeadlineText 15 Bold 折返し「「{title}」の記録中に離席を検知しました」Margin 0,0,0,6
- SummaryText 12 TextSecondary Margin 0,0,0,12「記録全体 {HH:mm} 〜 {HH:mm}（{全体}）のうち、{件数} 件・合計 {離席合計} が離席です。」
- Border 枠 Border 1 CornerRadius 4 地 Surface Padding 4 ＞ ScrollViewer ＞ ItemsControl（各 Border Padding 8,6 下線 ＞ `{DisplayText}` 12）
- EffectText 11 TextSecondary Margin 0,12,0,0「「離席時間を除く」を選ぶと、実作業 {実作業} 分のみが記録されます。」＋（件数&gt;1 または記録の途中に離席がある場合）「\n離席をまたぐため、記録は複数のアイテムに分割されます。」
- 右寄せ Margin 0,16,0,0：「そのまま記録」Padding 14,6 Margin 0,0,8,0（既定スタイル）→ ShouldExclude=false ／「離席時間を除く」Padding 14,6 IsDefault Primary → true
- 期間表示は「{h}時間{m}分」/「{m}分」（実作業は `max(0, 全体-Σ離席)`）
- DialogService：オーナーが非表示なら `Show()` してから ShowDialog。`DialogResult==true && ShouldExclude`

## 7. 出退勤（MainViewModel.WorkDay.cs）

### 7.1 方針
- 未退勤の記録は**同時に1件だけ**
- 自動で入れた退勤には `EndSource` で印を付け、マーカーで区別
- 勝手に確定するのは**日付が変わった場合だけ**。当日中の離席・スリープは**確認してから**確定

### 7.2 設定
- `IsWorkEndDetectionEnabled`（変更で ApplyAwaySettings・保存）
- `WorkEndThresholdMinutes`（5〜480、選択肢 `[15,30,45,60,90,120]`）
- `WorkEndEarliestHour`（0〜23）＋`WorkEndEarliestOptions`：0「制限しない（常に確認）」/12「12時以降」/15/16/17/18/19/20（「{h}時以降」）＋`SelectedWorkEndEarliestOption`

### 7.3 状態
- `_workDayLogs`, `_activeWorkLog`（未退勤）, `_isAskingWorkEnd`, `_workDaysLoaded`
- `IsWorking = _activeWorkLog != null`、`WorkDayButtonText`：「退勤」/「出勤」
- `WorkStatusText`：勤務中「出勤 {H:mm} ・ {DurationText}」／今日退勤済み「{H:mm} - {H:mm} ・ {DurationText}」／他「未出勤」
- `WorkDayMarkers`
- `NotifyWorkDayChanged()`：IsWorking, WorkDayButtonText, WorkStatusText 通知 → `RebuildWorkDayMarkers()` → `RebuildUnrecordedGaps()` → `ApplyAppUsageTrackingState()` → `RebuildTodayWorkload()`

### 7.4 コマンド
- `ClockInCommand`(!IsWorking) / `ClockOutCommand`(IsWorking) / `ToggleWorkDayCommand`
- `EditWorkDayCommand(param)`：param が WorkDayMarker→`marker.Date`、DateTime→その日、null→CurrentDate → `EditWorkDay(date)`
- `DeleteWorkDayCommand(param)`：その日の記録があれば確認「{M月d日}の勤務記録を削除しますか？」「削除確認」→ `DeleteWorkDay`

### 7.5 読込・自動締め
- `LoadWorkDays()`：読込 → 未退勤が複数あれば最新以外を `CloseWithLastActivity` → `_activeWorkLog` = 最新の未退勤 → `_workDaysLoaded=true` → `CloseStaleWorkLog(今)` → `NotifyWorkDayChanged()` → `RebuildNoteGroups()`
- `CloseStaleWorkLog(now)`：`WorkDayPolicy.ShouldAutoClose(active, now, IsRecording)` = **記録中でなく、未退勤の出勤日が今日より前**。締めて保存・通知、`ShowAutoStartNotice("{M月d日}の退勤が登録されていなかったため、{H:mm}（最終の記録）で締めました。必要なら設定→勤務記録から直せます")`
- `CloseWithLastActivity(log)`：`EndTime = WorkDayPolicy.FindLastActivityEnd(log, ScheduleItems)`, `EndSource = AutoClosed`
- `WorkDayPolicy.FindLastActivityEnd`：実績・非終日・`Start < 出勤日+1日`・`End > 出勤時刻`・End&gt;Start のうち最大の End。無ければ出勤時刻（勤務時間を水増ししない）
- `UpdateWorkDayTick(now)`（10秒）：CloseStaleWorkLog → 勤務中なら WorkStatusText 通知・RebuildTodayWorkload

### 7.6 出勤・退勤・編集
- `ClockIn(at = null)`：勤務中なら false。同じ日に記録があれば EndTime=null・EndSource=Manual にして再開（1日に何本もマーカーを並べない）、無ければ新規 `WorkDayLog{ Date=now.Date, StartTime=now }`。保存・通知・true
- `ClockOut(at = null, source = Manual)`：未勤務なら false。`End = WorkDayPolicy.ClampEnd(log, at ?? 今)`（出勤より前にしない）、EndSource、active=null、保存・通知 → `ShowWorkEndReview(log)`（§9）→ true
- `EditWorkDay(date)`：`ShowWorkDayEditDialog(date, existing?.Start, existing?.End, canDelete: existing!=null, note)` → 削除なら `DeleteWorkDay(existing?.Date ?? result.Date)`、それ以外 `ApplyWorkDayEdit`
- `ApplyWorkDayEdit(original, result)`：original と、移動先日付の既存（衝突）を除去 → 日付・開始・終了が元と同じなら EndSource を引き継ぎ、変わっていれば Manual → 追加 → StartTime 順ソート → `RefreshActiveWorkLog()`（未退勤の最新）→ 保存 → 通知 → `NotifyWorkDayNotesChanged()`
- `DeleteWorkDay(date)`：除去 → Refresh → 保存 → 通知 → NotesChanged
- `FindLogByDate(date)`：`StartTime.Date == date.Date`

### 7.7 離席からの退勤確認 `HandleAwayForWorkDay(period)`
- 勤務終了検知無効・確認中・未勤務なら何もしない
- `WorkDayPolicy.ShouldPromptForAwayEnd(log, period, threshold, earliest)`：`period.Start <= 出勤` なら false、長さ &lt; 閾値なら false、`earliest<=0 || period.Start.Hour >= earliest || 日付をまたいでいる` なら true
- `_isAskingWorkEnd=true`、Dispatcher.BeginInvoke（ApplicationIdle。SystemEvents の中で待たせない）で：まだ同じ勤務中なら確認
  「{ReasonText}が {DurationText} 続きました（{RangeText}）。\n\n{period.Start:H:mm} を退勤時刻として記録しますか？\n「いいえ」を選ぶと勤務は続いたままになります。」タイトル「勤務の終了」→ Yes なら `ClockOut(period.Start, AwayDetected)` ＋ `ShowAutoStartNotice("{H:mm} で退勤を記録しました")`。finally で false

### 7.8 マーカー
`RebuildWorkDayMarkers()`：VisibleDays が空なら空。範囲（先頭日〜末尾日）に出勤日が入る記録ごとに、note=`HasNote ? NoteSingleLine : ""`：
- 出勤 `WorkDayMarker(StartTime, true, "出勤 {H:mm}", false, date, note)`
- 退勤があれば `WorkDayMarker(EndTime, false, "退勤 {H:mm}{auto ? "（自動）" : ""} ・ {DurationText}", auto = EndSource != Manual, date, note)`
- `WorkDayMarker.ToolTipText`：note なし「{M月d日}の勤務\nクリックで編集・右クリックでメニュー」、あり「{M月d日}の勤務\n\n{note}\n\nクリックで編集・右クリックでメニュー」

## 8. 勤務時間の編集ダイアログ（WorkDayEditDialog）

`Title="勤務時間の編集"`（新規は「勤務時間の追加」）`Height=420 Width=360 CenterOwner NoResize ToolWindow`。DataContext=自身（`SelectedDate`, `StartText`, `EndText`, `NoteText`）。`Result`。
Grid Margin 20：
- 「勤務日:」＋ DatePicker `{SelectedDate}` 14px Margin 0,0,0,16
- Grid[*|16|*]：「出勤:」＋ TextBox `{StartText}` 14 Padding 6,6 ｜「退勤:」＋ TextBox `{EndText}`
- 11px TextSecondary Margin 0,0,0,16「時刻は 9:05 のように入力します。退勤を空にすると「勤務中」として扱います（当日のみ）。退勤が出勤より前の場合は翌日として記録します。」
- 「ふりかえり:」＋ TextBox `{NoteText}` Height 66 Padding 6,6 13 AcceptsReturn 折返し Top
- ErrorText 12 Danger 折返し Margin 0,10,0,0（Collapsed）
- 下段 Grid：左「削除」Width 80 Base 文字/枠 Danger（canDelete のときだけ）／右［「保存」IsDefault Width 90 Margin 0,0,10,0 Primary ｜「キャンセル」IsCancel Width 90 Base］

初期値：開始 `H:mm`（無ければ「9:00」）、終了 `H:mm` か空。Loaded で StartTextBox にフォーカス＋全選択。
保存：日付なし「勤務日を選んでください。」／開始が解釈不可「出勤時刻を 9:05 の形式で入力してください。」／終了あり：解釈不可「退勤時刻を 18:30 の形式で入力してください。」、`candidate > start ? candidate : candidate+1日`、24時間超「勤務時間が24時間を超えています。時刻を確認してください。」／終了なしで今日以外「過去の日は退勤時刻が必要です。」。
時刻の解釈：Trim、全角「：」→「:」、`["H:m","H:mm","HH:mm","Hmm","HHmm"]` の TryParseExact。
削除：`WorkDayEditResult(IsDeleted:true, date, date, null, "")`。

## 9. 退勤時のふりかえり（MainViewModel.WorkEndReview.cs）

### 9.1 VM
- `IsWorkEndReviewEnabled`（保存）
- `ShowWorkEndReview(log)`：無効・表示中・退勤なしなら何もしない。`recorded = SumRecordedOn(日付)`（実績・非終日・その日開始の合計）、`completed = CompletedAt がその日の件数`、`candidates = BuildCarryOverCandidates(日付)`。**3つとも0なら出さない**。`commits = GetCommitsBetween(日付0時, 退勤)`（出すと決めてから読む）→ ダイアログ → `SetWorkDayNote(log, result.Note)` → 繰り越しがあれば `CarryOverTodos`
- 自動締め（CloseWithLastActivity）は ClockOut を通らないので出ない
- `SetWorkDayNote(log, note)`：Trim して同じなら何もしない。保存・マーカー再構築・NotesChanged
- `BuildCarryOverCandidates(date)`：未完了の ToDo のうち「PlannedOn==date」または「期限≤date」。
  - Reason：今日やる「今日やる」／超過「期限 {n}日超過」／当日「期限が今日」
  - Detail：その日のその ToDo（TodoId 一致の実績）の合計が &gt;0 なら「{今日 or M/d} {H:MM} 記録」、無ければ「手つかず」
  - `IsSelected = PlannedOn が null または ≤ date`（既に明日以降に予定済みなら外す）
  - 並び：今日やる → 超過日数降順（期限なしは最小）→ 優先度降順 → CreatedAt
- `CarryOverTodos(list)`：target=明日。`_isUpdatingTodo` の間に、含まれていて PlannedOn≠明日 のものを `PlannedOn=明日` にして Modify「明日へ繰り越し」→ 0件なら終了 → Composite「ToDo {n} 件を明日へ繰り越し」→ OnTodoChanged → `ShowAutoStartNotice("{n} 件の ToDo を明日やることにしました")`。**期限は変えない**

`WorkEndCarryOver`（class）：`Todo`, `ReasonText`, `DetailText`, `IsSelected`(set可, 通知なし), `Title`, `IsHighPriority`。
`WorkEndReviewResult(IReadOnlyList<TodoItem> CarriedOver, string Note)`。

### 9.2 ダイアログ（WorkEndReviewDialog）
`Title="今日のふりかえり" Height=620 Width=480 MinHeight=420 CenterOwner NoResize`。`MaxHeight = max(MinHeight, WorkArea.Height-24)`、コミットがあれば `Height = min(Height+150, MaxHeight)`。
Grid Margin 20、6行：
1. HeadlineText 15 Bold「{M月d日 (ddd)} おつかれさまでした」Margin 0,0,0,10
2. StackPanel Margin 0,0,0,16：
   - Border CornerRadius 4 Padding 12,10 Margin 0,0,0,12 地 MutedBackground ＞ Grid 3等分［「勤務」11 TextSecondary ／ WorkText 15 SemiBold（h時間m分）／ WorkRangeText 10 TextMuted「H:mm - H:mm」｜「記録した時間」／ RecordedText（0なら「なし」）／ RecordedRatioText「勤務の {n}%」｜「完了した ToDo」／ CompletedText「{n} 件」］
   - CommitPanel（コミットがあるとき）Margin 0,0,0,12：Grid［CommitHeadText 12「今日のコミット {n} 件」｜「ひとことへ貼る」Ghost Padding 8,3 11px］＋ Border 枠1 CornerRadius 4 地 Surface Padding 4 MaxHeight 120 ＞ ScrollViewer ＞ ItemsControl（Grid Margin 8,4［TimeText 11 MinWidth 38 TextMuted ｜ Subject 12 省略 ｜ RepositoryName 10 MaxWidth 110 TextMuted］）
   - 「今日のひとこと（任意）」12 TextSecondary Margin 0,0,0,4 ／ NoteTextBox Height 58 Padding 8,6 13 AcceptsReturn 折返し Top 地 Surface 枠 Border 1
3. Grid：CarryOverHeadText 12 TextSecondary「片付かなかった ToDo が {n} 件あります」｜ ToggleAllButton Ghost 11「すべて外す」/「すべて選ぶ」
4. ListPanel Border 枠1 CornerRadius 4 地 Surface Padding 4 ＞ ScrollViewer ＞ ItemsControl：Border Padding 8,6 下線 ＞ Grid［CheckBox `{IsSelected}` Checked/Unchecked で UpdateButtons ｜ StackPanel［横並び `&#xE8C9;` 9px TodoHighPriority（IsHighPriority）＋ Title 13 省略 ／ DetailText 10 TextMuted］｜ Border CornerRadius 3 Padding 6,2 地 MutedBackground ＞ ReasonText 10 TextSecondary］
   EmptyPanel（候補0件）：中央に `&#xE73E;` 22px TextMuted ＋「今日やると決めたものは、すべて片付いています。」12。見出しは「明日へ送るもの」、ToggleAll は隠す
5. 11px TextSecondary「送るのは「今日やる」の印だけです。期限は変わりません。Ctrl+Z で元に戻せます。」
6. 右寄せ：「そのまま閉じる」IsCancel Padding 14,6 Base → CarriedOver=[]・false ／ CarryOverButton IsDefault Primary：候補0件「閉じる」、選択あり「{n} 件を明日へ」、選択0「明日へ送る」（無効）→ 選択分・true

- Loaded で CarryOverButton にフォーカス（一言を書かない日は Enter だけで抜けられる）
- 「ひとことへ貼る」：コミットを古い順に「- {Subject}」の行にし、既存テキストの末尾（TrimEnd＋改行）へ追記、末尾へキャレット
- `OnClosing` で `Note = NoteTextBox.Text.Trim()`（どの閉じ方でも一言を拾う）
- すべて選ぶ/外す：IsSelected を一括設定し ItemsSource を張り直す（通知が無いため）
- DialogService は `new WorkEndReviewResult(confirmed ? CarriedOver : [], Note)` を返す

## 10. テスト観点

- RecordingStopHelper：末尾の離席で1区間に縮む、途中で分割、重複する離席を統合、全体離席で0区間、除外時間を二重計上しない
- RecordingItemHelper：Content が既にあれば残す、24時間超の表記
- WorkDayPolicy：自動締め条件（記録中は締めない）、最終活動の求め方、ClampEnd、退勤確認の時刻下限と日付跨ぎ
- VM：予定から記録→停止で予定が実績化（Undo で戻る）、ToDo 記録で積算が同じ Undo 単位、停止中に次の記録を開始しても前の記録が保存される
