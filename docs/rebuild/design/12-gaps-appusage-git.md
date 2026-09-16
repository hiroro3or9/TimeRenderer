# 12. 記録漏れの帯・使用アプリ・Git コミット

3つとも「この時間、実際に何をしていたか」の手がかり。**記録そのものは勝手に変えない**（ユーザーが確定したときだけ作る）。

## 1. 記録漏れ（未記録の帯）

### 1.1 UnrecordedGap（Models/UnrecordedGap.cs、保存しない）
```csharp
public sealed record UnrecordedGap(DateTime StartTime, DateTime EndTime)
{
    public TimeSpan Duration => EndTime > StartTime ? EndTime - StartTime : TimeSpan.Zero;
    public double DurationHours => Duration.TotalHours;
    public int ColumnIndex => 0;          // 位置計算コンバーター用（常に 0。CA1822 は SuppressMessage）
    public int MaxColumnIndex => 0;
    public bool IsAllDay => false;
    public string RangeText => $"{StartTime:H:mm}-{EndTime:H:mm}";
    public string DurationText => WorkDayLog.FormatDuration(Duration);
    public string Label => $"未記録 {DurationText}";
    public string ToolTipText => $"{RangeText}（{DurationText}）\nこの時間の記録がありません";
}
```
プロパティ名を `ScheduleSegment` と揃え、日/週の位置コンバーターを共用する。

### 1.2 UnrecordedGapHelper（純粋ロジック）
```csharp
public static List<UnrecordedGap> Detect(DateTime workStart, DateTime workEnd, IEnumerable<(DateTime Start, DateTime End)> covered, TimeSpan minDuration)
{
    var gaps = new List<UnrecordedGap>(); if (workEnd <= workStart) return gaps;
    var ranges = covered.Select(c => (Start: max(c.Start, workStart), End: min(c.End, workEnd))).Where(c => c.End > c.Start).OrderBy(c => c.Start).ToList();
    var cursor = workStart;
    foreach (var (start, end) in ranges) {
        if (start > cursor) AddIfLongEnough(gaps, cursor, start, minDuration);   // end - start >= minDuration なら追加
        if (end > cursor) cursor = end;
        if (cursor >= workEnd) return gaps;
    }
    AddIfLongEnough(gaps, cursor, workEnd, minDuration);
    return gaps;
}
public static IEnumerable<UnrecordedGap> SplitByDay(UnrecordedGap gap)   // 0時で切って日ごとに yield
```

### 1.3 ViewModel（MainViewModel.Gaps.cs）
- `UnrecordedGapMinDuration = 15分`、`UnrecordedGapRefreshInterval = 5分`
- `UnrecordedGaps`（変更で `RebuildTodayOverview()`）
- `RebuildUnrecordedGaps()`（RecalculateLayoutCore と `NotifyWorkDayChanged` から）：
  1. `_lastUnrecordedGapRefresh = 今`
  2. 日/週モードでない、または勤務記録0件 → 空にして return。`GetLayoutRange()` が null でも空
  3. 各勤務記録：`workEnd = EndTime ?? 今`、今より後なら今（これからの時間は漏れにしない）。表示範囲でクリップ。空なら skip
  4. `Detect(workStart, workEnd, CollectCoveredRanges(workStart, workEnd), 15分)` を `SplitByDay` して追加
- `CollectCoveredRanges(start, end)`：実績・非終日・**非仮想**で範囲に重なるもの（表示フィルタで隠れていても含める）＋記録中なら `(RecordingStartTime, 今)`
- `UpdateUnrecordedGapTick(now)`（時計から）：`refreshStats = 統計モード && 未記録時間集計が有効`。日/週でも refreshStats でもなければ return。前回から 5分未満なら return。日/週なら RebuildUnrecordedGaps、それ以外 `_lastUnrecordedGapRefresh = now; UpdateStats()`

### 1.4 帯の描画（DayWeekView.xaml、予定バーの ItemsControl の**直前**＝背面）
```
ItemsControl ItemsSource={DataContext.UnrecordedGaps (Window)}  ItemsPanel: Canvas ClipToBounds=True
 ItemContainerStyle (ContentPresenter):
   Visibility  = MultiBinding DateToPageVisibilityConverter(StartTime, ItemsControl.DataContext(=ページの日付), CurrentViewMode, EnabledDaysOfWeek) FallbackValue Collapsed
   Canvas.Top  = MultiBinding TimeToPositionConverter(StartTime, DisplayStartHour)
   Canvas.Left = MultiBinding DateToPagePositionConverter(StartTime, {StaticResource Zero}, {StaticResource Zero}, ページ日付, CurrentViewMode, Canvas.ActualWidth, {StaticResource FalseValue}, EnabledDaysOfWeek)
   Width       = 同上 + ConverterParameter=WIDTH
 ItemTemplate:
   Grid Height={DurationHours→DurationToHeightConverter} Margin 1,0,1,0 ToolTip={ToolTipText}
     ContextMenu: 「自由入力で埋める…」/ Separator /「使用アプリから埋める…」/「ToDo から埋める…」
     Rectangle Fill={UnrecordedGapBrush} Stroke={UnrecordedGapBorderBrush} StrokeThickness 1 StrokeDashArray "3 3" RadiusX/Y 4
     TextBlock {Label} 10 Margin 6,2,4,0 上寄せ 省略 TextMuted
```
- 色トークン：Light `UnrecordedGapColor #1F94A3B8` / `UnrecordedGapBorderColor #8094A3B8`、Dark `#26CBD5E1` / `#8094A3B8`（`design/03` のトークン表に含める）
- **左クリックは受けない**（背景へ透過。空白のドラッグ＝予定作成がそのまま穴埋めになる）。右クリックだけ帯が受ける
- メニューのハンドラ（DayWeekView.xaml.cs）：`ContextMenu.PlacementTarget` の DataContext が `UnrecordedGap` なら `FillGapManually` / `FillGapFromAppUsage` / `FillGapFromTodoPicker`（`design/09` §9）

## 2. 帯を埋める（MainViewModel.GapFill.cs）

- `CategoryGuessMinOverlap = 30分`、`MaxAppTitleSuggestions = 8`

### 2.1 FillGapManually(gap)
`End <= Start` なら何もしない。`AddViaDialog(new ScheduleItem { Kind = Recorded, StartTime, EndTime, Title = "", ColorCode = RecordingCategory?.ColorCode ?? LightBlue, CategoryId = RecordingCategory?.Id, ProjectCodeId = DefaultProjectCode?.Id })`（通常の予定編集ダイアログ）。

### 2.2 FillGapFromAppUsage(gap)
```
stats = GetAppUsageStats(gap.Start, gap.End)
commits = GetCommitsBetween(gap.Start, gap.End)
if (stats.Count == 0 && commits.Count == 0) {
    ShowMessage("この時間帯の手がかりがありません。\n" +
                "使用アプリは出勤から退勤までの間だけ収集されます（設定でオン/オフできます）。\n" +
                "コミット履歴を使うには、設定でリポジトリを登録してください。\n\n" +
                "右クリックの「ToDo から埋める」か、空白のドラッグで記録を作れます。", "この時間の作業から埋める")
    return
}
suggestion = BuildGapFillSuggestion(stats, commits)
result = ShowGapFillDialog(gap.Start, gap.End, suggestion, [..Categories], ActiveProjectCodes)
if (result != null) CreateItemForGap(gap, result, commits)
```

### 2.3 下書きの組み立て
- `BuildGapFillSuggestion(stats, commits)`：
  - 一番長く使ったアプリ（stats[0]）について `LearnFromApp(processName)` → (titles, category)
  - `commitTitles = commits.Subject を Ordinal で Distinct、先頭 8`
  - 候補 = commitTitles の後に titles（重複除く）
  - 既定タイトル = titles[0] → commitTitles[0] → stats[0].AppName → ""
  - プロジェクト = `GuessProjectCodeFromCommits(commits) ?? DefaultProjectCode`
- `LearnFromApp(processName)`：使用履歴からそのプロセスの期間を開始日ごとにまとめる。実績・非終日・非仮想・タイトル空でないアイテムごとに、開始日と翌日の期間との**重なり ticks** を重みとしてタイトル・カテゴリ（ResolveCategory）に加算。タイトルは重み降順で 8件、カテゴリは `PickCategory`（最大の重みが 30分未満なら null）
- `CreateItemForGap(gap, result, commits)`：`Kind=Recorded, Title, Start/End = 帯, CategoryId, ProjectCodeId, Content = BuildGapFillContent(...)`、カテゴリがあれば色も → Add → `PushEdits([AddItemEdit], "「{Title}」で記録漏れを埋める")`
- `BuildGapFillContent`：使用アプリ上位3件があれば「使用アプリから復元: {AppName} {DurationText} / …」、コミットがあれば「コミット: {H:mm} {Subject} / …（先頭3件）」＋3件超なら「 ほか{n}件」。改行で連結

```csharp
public sealed record GapFillSuggestion(IReadOnlyList<AppUsageStat> Stats, IReadOnlyList<GitCommit> Commits, string Title,
    IReadOnlyList<string> TitleSuggestions, CategoryInfo? Category, ProjectCodeInfo? ProjectCode)
{ public bool HasStats => Stats.Count > 0; public bool HasCommits => Commits.Count > 0; }
public sealed record GapFillResult(string Title, CategoryInfo? Category, ProjectCodeInfo? ProjectCode);
```

### 2.4 GapFillDialog
`Title="この時間の作業から埋める" Height=660 Width=470 MinHeight=360 CenterOwner ResizeMode=CanResize 地 Background`。コンストラクタで `MaxHeight = max(MinHeight, WorkArea.Height - 24)`、`Height = min(Height, MaxHeight)`。

```
Grid Margin 20 [*|Auto]
 Row0 ScrollViewer 縦Auto 横Disabled Padding 0,0,8,0 > Grid 7行
   HeadlineText 15 Bold Wrap Margin 0,0,0,4   「{M月d日 (ddd)} {H:mm} - {H:mm}（{h時間m分|m分}）」
   SummaryText 12 TextSecondary Wrap Margin 0,0,0,10
     (アプリ有,コミット有)「この時間の記録がありません。収集できた {X} の内訳と、この時間に作ったコミットが手がかりです。」
     (アプリ有,無)        「この時間の記録がありません。収集できた {X} の内訳は次の通りです。」
     (無,コミット有)      「この時間の記録がありません。この時間に作ったコミットが手がかりです。」
   UsagePanel Border 枠1 CornerRadius 4 地 Surface Padding 4 MinHeight 120 MaxHeight 220（HasStats でなければ Collapsed）
     ScrollViewer > ItemsControl UsageList: Border Padding 8,7 下線1 > StackPanel
        Grid[*|Auto]: {AppName} 13 SemiBold 省略 ｜ 横並び [{DurationText} 12 Margin 8,0,8,0 ｜ {PercentText} 12 MinWidth 36 右揃え TextSecondary]
        ProgressBar Percent/100 Height 4 Margin 0,5,0,0 枠0 地 Border 前景 Primary
        (HasSampleTitle) {SampleTitle} 11 Margin 0,4,0,0 TextMuted 省略
   CommitPanel StackPanel Margin 0,10,0,0 Collapsed（HasCommits で Visible）
     CommitHeadText 12 TextSecondary Margin 0,0,0,4「この時間のコミット {n} 件」
     Border 枠1 CornerRadius 4 地 Surface Padding 4 MaxHeight 160 > ScrollViewer > ItemsControl CommitList:
        Grid[Auto|*] Margin 8,5: {TimeText} 11 MinWidth 38 上 Margin 0,1,8,0 TextMuted ｜ StackPanel [{Subject} 12 Wrap ／ {OriginText} 10 Margin 0,2,0,0 省略 TextMuted]
   StackPanel Margin 0,14,0,0:「タイトル」FieldLabelStyle ／ TitleCombo 編集可 14 Padding 6,6 TextSearch無効 MaxDropDown 200
                              ／ GuessNoteText 11 Margin 0,4,0,0 Wrap TextMuted
     (コミット有)「候補の先頭はこの時間のコミットです。その後ろに、過去に同じアプリを使っていた時間帯へ付けていたタイトルが並びます。」
     (無,候補有) 「候補は、過去に同じアプリを使っていた時間帯へ付けていたタイトルです。」
     (無,無)     「このアプリでの記録がまだ無いため、アプリ名を入れてあります。」
   StackPanel Margin 0,12,0,0:「カテゴリ」＋ CategoryCombo Width 220 左 14 Padding 6,6（項目 Border 18×18 CornerRadius 3 枠 Border 1 Margin 0,0,8,0 ＋ Name）
   StackPanel Margin 0,12,0,0:「プロジェクトコード」＋ ProjectCodeCombo Width 280 左 14 Padding 6,6 DisplayMemberPath DisplayName
 Row1 右寄せ Margin 0,18,0,0:「キャンセル」Padding 14,6 Margin 0,0,8,0 IsCancel Base ／ CreateButton「記録を作る」Padding 14,6 IsDefault Primary
```
- カテゴリ初期選択は推測の Id に一致するもの（無ければ未選択）
- プロジェクト選択肢は `[ProjectCodeInfo.Unassigned, ..projectCodes]`、初期は推測に一致するもの ?? 未設定
- Loaded で TitleCombo にフォーカス＋`PART_EditableTextBox` 全選択
- 「記録を作る」：タイトル Trim 空ならフォーカスを戻して何もしない。`Result = new GapFillResult(title, CategoryCombo.SelectedItem as CategoryInfo, ProjectCodeInfo.Normalize(選択))`、DialogResult=true

## 3. 使用アプリの収集（Services/ActiveWindowTracker.cs）

**UI スレッド専用**（フックのコールバックも DispatcherTimer も UI スレッドに届く。ロックなし）。

### 3.1 P/Invoke（すべて LibraryImport）
`GetForegroundWindow`, `GetAncestor(hWnd, gaFlags)`, `GetWindowTextW(hWnd, Span<char>, int)`, `GetClassNameW`, `GetWindowThreadProcessId(hWnd, out uint)`, `OpenProcess(uint, [MarshalAs(Bool)] bool, uint)`(SetLastError), `CloseHandle`, `QueryFullProcessImageNameW(h, flags, Span<char>, ref uint)`, `GetProcessTimes(h, out long ×4)`,
`SetWinEventHook(uint min, uint max, IntPtr hmod, delegate* unmanaged[Stdcall]<IntPtr, uint, IntPtr, int, int, uint, uint, void>, uint idProcess, uint idThread, uint flags)`, `UnhookWinEvent`, `EnumChildWindows(IntPtr, delegate* unmanaged[Stdcall]<IntPtr, IntPtr, int>, IntPtr)`。
定数：`EVENT_SYSTEM_FOREGROUND 0x0003`, `EVENT_OBJECT_NAMECHANGE 0x800C`, `WINEVENT_OUTOFCONTEXT 0`, `PROCESS_QUERY_LIMITED_INFORMATION 0x1000`, `GA_ROOT 2`, `OBJID_WINDOW 0`, `CHILDID_SELF 0`。

### 3.2 定数・状態
- `PollInterval = 5秒`、`MaxTailExtension = 7秒`、`MergeGap = 2秒`、`ProcessInfoCacheLimit = 256`
- `UwpHostProcessName = "ApplicationFrameHost"`、`UwpCoreWindowClass = "Windows.UI.Core.CoreWindow"`
- `_timer`（DispatcherTimer Normal、Tick で Sample）、`_processInfoCache: Dictionary<(uint Pid, long CreationTime), (ProcessName, AppName)>`、`_isCollecting`、`_captureWindowTitles`、`_captureModeChangedAt`、`_completed`、`_current`、フックハンドル2つ、`static _hookOwner`、`_disposed`

### 3.3 公開 API
- `SetCaptureWindowTitles(bool)`：同じなら return。変更し、収集中なら `CloseCurrent(今)`、`_captureModeChangedAt = 今`、`Sample()`
- `Start()`：収集中なら return（集めた分を捨てない）。completed/current クリア、収集開始、`HookWindowEvents()`、タイマー開始、`Sample()`
- `Stop()`：収集停止、タイマー停止、アンフック、`CloseCurrent(今)`、completed を返してクリア
- `Drain()`：completed を返してクリア（進行中は閉じない）
- `IsCollecting`、`Dispose()`（タイマー停止・アンフック・収集停止）

### 3.4 フック
- `HookWindowEvents()`：既に張っていれば return。`_hookOwner = this`。FOREGROUND と NAMECHANGE をそれぞれ `&OnWindowEventNative`、OUTOFCONTEXT で張る（**SKIPOWNTHREAD は付けない**＝自アプリへの切替も拾う）。両方失敗なら `_hookOwner = null`（ポーリングのみで継続）
- `[UnmanagedCallersOnly(CallConvs = [typeof(CallConvStdcall)])] static void OnWindowEventNative(...)`：全体を try/catch。FOREGROUND → `owner.Sample()`。NAMECHANGE はタイトル収集中・hwnd≠0・`idObject == OBJID_WINDOW && idChild == CHILDID_SELF` で、`hwnd == 前面` または `GetAncestor(hwnd, GA_ROOT) == 前面` のときだけ Sample

### 3.5 Sample()
```
if (!_isCollecting) return; now = 今
try:
  hwnd = GetForegroundWindow(); 0 なら CloseCurrent(now); return
  pid = GetWindowThreadProcessId; 0 なら CloseCurrent; return
  (processName, appName) = ResolveProcess(pid)
  processName が ApplicationFrameHost（大小無視）なら realPid = FindUwpApplicationPid(hwnd, pid)、解決できれば置き換え
  processName 空なら CloseCurrent; return
  title = _captureWindowTitles ? GetWindowTitle(hwnd).Trim() : ""
  if (_current != null && 同じプロセス && (!_captureWindowTitles || タイトル一致(Ordinal))) { now > End なら End = now; return }
  CloseCurrent(now)
  if (TryResumeLast(processName, now, title)) return
  _current = new AppUsageInterval { Start = now, End = now, ProcessName, AppName, WindowTitle = title, IsWindowTitleSpecific = _captureWindowTitles }
catch: Debug 出力のみ
```
- `TryResumeLast`：直前の確定期間が同じプロセスで `now - last.End <= 2秒`、タイトル収集中ならタイトル一致、`last.End > _captureModeChangedAt` のとき、それを completed から外して End=now（タイトルがあれば更新）し current に戻す
- `CloseCurrent(now)`：current が無ければ return。`End < now && now - End <= 7秒` なら End=now（スリープ明けなどの長い空白は足さない）。長さ &gt; 0 なら completed へ。current=null
- `GetWindowTitle`：stackalloc 512、`GetClassName`：256
- `FindUwpApplicationPid(host, hostPid)`：`UwpSearchContext { HostPid, FoundPid }` をスタックに置き、ポインタを lParam で `EnumChildWindows(host, &EnumUwpChildWindow, ...)`。コールバック（UnmanagedCallersOnly、try/catch）は子の PID が 0 やホストと同じなら続行(1)、クラス名が CoreWindow なら FoundPid を入れて 0（停止）。例外時 0
- `ResolveProcess(pid)`：`OpenProcess(LIMITED)` 失敗なら `ResolveProcessFallback`（`Process.GetProcessById(pid).ProcessName` を両方に、失敗なら空）。成功なら `GetProcessTimes` で生成時刻を取り、`(pid, creationTime)` でキャッシュ参照。無ければ `QueryFullProcessImageName`（stackalloc 520）→ `ProcessName = ファイル名（拡張子なし）`、`AppName = FileVersionInfo.FileDescription`（空白・例外なら ProcessName）。キャッシュが 256 以上なら全消去してから追加。finally で CloseHandle

### 3.6 AppUsageInterval（Models）
`Start`, `End`, `ProcessName`, `AppName`, `WindowTitle`, `IsWindowTitleSpecific`（get/set のプレーンクラス）、`Duration`、`ClipTo(rangeStart, rangeEnd)`（重ならなければ null、全プロパティ複製）。

## 4. 使用アプリの ViewModel（MainViewModel.AppUsage.cs）

- `AppUsageFlushInterval = 5分`、`_appUsageHistory`（読込済み。保持 60日、保存時に古い分を落とすのは `design/15` の FilePersistenceService）
- `IsAppUsageTrackingEnabled`（既定 true）：変更で `ApplyAppUsageTrackingState()` → SaveSettings
- `InitializeAppUsageTracking()`（ctor）：`new ActiveWindowTracker()`／`LoadAppUsage()`：`FilePersistenceService.LoadAppUsage()`
- `DisposeAppUsageTracking()`（終了時）：`CollectAndSaveAppUsage()` → Dispose → null
- `ApplyAppUsageTrackingState()`（出退勤・記録開始停止・設定変更の全経路がここを通る）：有効かつ勤務中なら `SetCaptureWindowTitles(IsRecording)`、未収集なら Start と `_lastAppUsageFlush = 今`。それ以外 `CollectAndSaveAppUsage()`
- `OnRecordingChangedForAppUsage(bool)`（IsRecording の変化時）：`ApplyAppUsageTrackingState()` → `DrainAndSaveAppUsage()`
- `UpdateAppUsageTick(now)`：収集中かつ 5分経過で Drain&Save
- `DrainAndSaveAppUsage()` / `CollectAndSaveAppUsage()`：Drain / Stop の結果が1件以上なら履歴に足して `FilePersistenceService.SaveAppUsage(履歴)`
- `ShowAppUsageCommand(ScheduleItem)`：`GetAppUsageStats(Start, End)` が空なら `ShowMessage("この時間帯のアプリ使用記録はありません。\n使用アプリは出勤から退勤までの間だけ収集されます（設定でオン/オフできます）。", "使用アプリ")`、あれば `ShowAppUsageDialog(item.Title, Start, End, stats)`
- `internal List<AppUsageStat> GetAppUsageStats(start, end)`：
  1. 履歴を ClipTo、空なら []。分母 = クリップ後の合計 ticks（**範囲の長さではない**）、0以下なら []
  2. ProcessName でグループ：`duration` 合計、`AppName` = 最長期間の AppName（空ならプロセス名）、`SampleTitle` = タイトルがある期間のうち最長のもの、`TitleStats` = `IsWindowTitleSpecific && タイトルあり` を Trim 後のタイトル（Ordinal）でグループ化し時間降順
  3. `Percent = duration.Ticks * 100.0 / total`。時間降順

```csharp
public sealed record AppUsageTitleStat(string Title, TimeSpan Duration)
{   // DurationText: ≥1時間「{h}時間{m}分」、≥1分「{m}分」、他「{max(1,s)}秒」
}
public sealed record AppUsageStat(string ProcessName, string AppName, TimeSpan Duration, double Percent, string SampleTitle)
{
    public IReadOnlyList<AppUsageTitleStat> TitleStats { get; init; } = [];
    public bool HasSampleTitle => !IsNullOrWhiteSpace(SampleTitle);
    public bool HasTitleStats => TitleStats.Count > 0;
    public bool HasLegacySampleTitle => HasSampleTitle && !HasTitleStats;
    public string TitleTrackedDurationText  // TitleStats 合計を「{h}時間{m}分」/「{m}分」/「{s}秒」
    public string DurationText              // 同形式（秒は max なし）
    public string PercentText => $"{Percent:0}%";
}
```

### 4.1 AppUsageDialog
`Title="使用アプリ" Height=600 Width=620 CenterOwner NoResize 地 Background`。Grid Margin 20、5行：
- HeadlineText 15 Bold Wrap Margin 0,0,0,6「「{itemTitle}」の使用アプリ」
- SummaryText 12 TextSecondary Wrap Margin 0,0,0,12「{M/d HH:mm} 〜 {HH:mm} のうち、収集できた {h時間m分|m分} の内訳（{n} アプリ）」
- Border 枠1 CornerRadius 4 地 Surface Padding 4 ＞ ScrollViewer ＞ ItemsControl UsageList：
  ```
  Border Padding 8,8 下線1 > StackPanel
    Grid[*|Auto]: {AppName} 13 Bold 省略 ｜ 横並び[{DurationText} 12 Margin 8,0,8,0 ｜ {PercentText} 12 MinWidth 36 右揃え TextSecondary]
    ProgressBar Percent/100 Height 5 Margin 0,5,0,4 枠0 地 Border 前景 Primary
    (HasTitleStats) StackPanel Margin 0,5,0,0
       Grid Margin 0,0,0,3: 「記録中のウィンドウタイトル」11 TextSecondary ｜ {TitleTrackedDurationText} 11 Margin 8,0,0,0 TextSecondary
       ItemsControl {TitleStats}: Grid[*|Auto] Margin 8,2,0,0: {Title} 11 省略 ToolTip={Title} ｜ {DurationText} 11 Margin 12,0,0,0 TextSecondary
    (HasLegacySampleTitle) TextBlock 11 Margin 0,4,0,0 TextSecondary 省略:「参考タイトル（旧形式）: 」+{SampleTitle}
  ```
- 11px TextSecondary Wrap Margin 0,12,0,0「使用アプリは出勤から退勤まで収集されます。タイトル別時間が残るのは記録中だけです。データはこの PC の中にのみ保存されます。」
- 右寄せ Margin 0,16,0,0「閉じる」Padding 14,6 IsDefault IsCancel Primary

## 5. Git コミット

### 5.1 モデル
```csharp
public sealed class GitRepositoryInfo : ObservableObject
{
    public string Id { get; set; } = Guid.NewGuid().ToString("N");
    public string Path { get; set; }          // Trim、変更で DisplayName/FolderName 通知
    public string Name { get; set; }          // Trim、変更で DisplayName 通知
    public bool IsEnabled { get; set; } = true;
    public string? ProjectCodeId { get; set; }
    [JsonIgnore] public string FolderName      // 末尾区切りを落として GetFileName（空ならそのまま、ArgumentException ならパス）
    [JsonIgnore] public string DisplayName => Name.Length > 0 ? Name : FolderName;
}
public sealed record GitCommit(string RepositoryId, string RepositoryName, DateTime CommittedAt, string ShortHash, string Subject)
{ public string TimeText => CommittedAt.ToString("H:mm"); public string OriginText => $"{RepositoryName} · {ShortHash}"; }
```

### 5.2 GitCommitReader（Services）
- `PerRepositoryTimeout = 3秒`、`TotalBudget = 8秒`、`MaxCommitsPerRepository = 100`、`QueryPadding = 1日`、区切り `''`
- `IsGitAvailable`：初回だけ `git --version` を実行（3秒）し、出力に "git" を含むか覚える
- `Read(repositories, from, to)`：空・`to <= from`・git 無しなら []。Stopwatch で合計 8秒を超えたら残りを諦める。パス空・存在しないものは skip。`ReadRepository` を集め、`CommittedAt` 降順
- `static LooksLikeRepository(path)`：ディレクトリが存在し、`.git` がディレクトリ**またはファイル**（worktree/submodule）なら true。例外（Argument/IO/UnauthorizedAccess）は false
- `ReadRepository`：引数 `-C {path} log --all --no-merges -n100 --since={from-1日} --until={to+1日} --pretty=tformat:%h%x1f%aI%x1f%s`（時刻は `yyyy-MM-ddTHH:mm:ss` Invariant）。`GetAuthorEmail(path)`（`git -C path config --get user.email`、パスごとにキャッシュ）が取れれば `--author={email}`。各行を `` で3分割、`%aI` を `DateTimeOffset.TryParse` → `ToLocalTime().DateTime` が `[from, to)` 外なら捨てる、Subject 空も捨てる
- `TryRunGit(args, timeout, out output)`：`ProcessStartInfo("git") { UseShellExecute=false, CreateNoWindow=true, Redirect 標準出力/エラー, UTF8(BOMなし) }`、`ArgumentList` に追加。stderr は `BeginErrorReadLine` で読み捨て、stdout を ReadToEnd、`WaitForExit(timeout)` できなければ `Kill(entireProcessTree: true)` して false。ExitCode==0 で true。例外は false
- **書き込み系の git コマンドは一切実行しない**。コミットは保存しない

### 5.3 ViewModel（MainViewModel.Git.cs）
- `GitRepositories`（ObservableCollection、CollectionChanged で `NotifyGitRepositoriesChanged()`）
- `ActiveGitRepositories` = 有効かつパスあり、`HasGitRepositories`
- `IsGitCommitLookupEnabled`（既定 true、保存）
- `GitStatusText`：0件「リポジトリはまだ登録されていません。」／git 無し「git コマンドが見つかりません。git を PATH の通った場所へ入れると使えるようになります。」／全件有効「{n} 件のリポジトリを見に行きます。」／一部「{全} 件中 {有効} 件を見に行きます。」
- `AddGitRepositoryCommand`：`ShowFolderPicker("コミット履歴を見に行くリポジトリのフォルダーを選びます")` → 空なら終了 → 同じパス（大小無視）があれば `ShowMessage("このリポジトリは既に登録されています。", "リポジトリの追加")` → `.git` が無ければ確認「選んだフォルダーに .git が見つかりません。\n{path}\n\nこのまま登録しますか？」「リポジトリの追加」→ `new GitRepositoryInfo { Path, ProjectCodeId = DefaultProjectCode?.Id }` を購読して追加 → SaveSettings
- `DeleteGitRepositoryCommand(repo)`：**確認なし**で購読解除・削除・SaveSettings
- `LoadGitRepositories(loaded)`：旧購読解除・クリア。パス空は捨て、Id 空なら新規 Id、購読して追加
- `OnGitRepositoryPropertyChanged`：DisplayName/FolderName は無視。IsEnabled なら Notify。読込中でなければ SaveSettings
- `GetCommitsBetween(from, to)`：無効・有効リポジトリ0件なら []。それ以外 `_gitCommitReader.Read(...)`（**ユーザー操作の直後にだけ呼ぶ**：帯の穴埋めと退勤ふりかえり）
- `GuessProjectCodeFromCommits(commits)`：0件 null。全コミットが同じリポジトリで、そのリポジトリの ProjectCodeId が有効なコードに解決できればそれ、他は null

## 6. 未記録時間の加算先（期間割り当て）

```csharp
public sealed class UnrecordedTimeProjectAssignment : ObservableObject
{
    public DateTime StartDate { get; set; } = DateTime.Today;   // setter で .Date
    public string? ProjectCodeId { get; set; }
    [JsonIgnore] public string RangeText { get; set; } = "";     // VM が並べ替え後に書き込む
}
public static class UnrecordedTimeAssignmentHelper
{
    public static List<UnrecordedTimeProjectAssignment> Order(IEnumerable<…> a) => [.. a.OrderBy(x => x.StartDate.Date)];
    public static UnrecordedTimeProjectAssignment? Resolve(IReadOnlyList<…> ordered, DateTime date)   // 後ろから見て StartDate ≤ date の最初
    public static string BuildRangeText(IReadOnlyList<…> ordered, int index)
    // 最後の行「yyyy/MM/dd 〜 （以降ずっと）」／次の行の開始 ≤ この行の開始「yyyy/MM/dd（次の行に上書きされます）」／他「yyyy/MM/dd 〜 {次の開始-1日:yyyy/MM/dd}」
}
```
VM 側（MainViewModel.ProjectCodes.cs。管理パネルの UI は `design/13`）：
- `UnrecordedTimeAssignments`（ObservableCollection）
- `AddUnrecordedTimeAssignmentCommand`：最後の行が無いか過去なら今日、未来なら最後の開始+1日。ProjectCodeId は最後の行の値（未設定含む）、無ければ `DefaultProjectCode?.Id` → 購読・追加 → `OnUnrecordedTimeAssignmentsChanged()`
- `DeleteUnrecordedTimeAssignmentCommand(a)`：購読解除・削除・Changed
- `OnUnrecordedTimeAssignmentPropertyChanged`：RangeText は無視、他は Changed
- `OnUnrecordedTimeAssignmentsChanged()`：読込中は return。`SortUnrecordedTimeAssignments()`（Move で並べ替え）→ `RefreshUnrecordedTimeAssignmentRanges()` → `AssignmentProjectCodeChoices` 通知 → SaveSettings → UpdateStats
- `LoadUnrecordedTimeAssignments(loaded, manualSprints)`：loaded が null（旧形式）なら手動スプリントの `UnrecordedTimeProjectCodeId` を開始日順に移行。Order して購読・追加、Range 更新、選択肢通知
- `ResolveUnrecordedTimeProjectCode(date)`：集計無効なら null。割り当てが解決できれば**そのコードを無効でも**返す（未設定なら null＝加算しない）。無ければ既定の加算先（有効なら）
- `ClearAssignmentProjectCodeReferences(id)`：プロジェクトコード削除時にその行を除去

## 7. テスト観点

- UnrecordedGapHelper：重なり・入れ子・勤務外にはみ出す記録、最小長、日跨ぎの分割
- ActiveWindowTracker は UI 依存が強いので自動テスト対象外（手動確認：UWP アプリ、タブ切替でタイトルが分かれる、記録外はタイトルが空）
- GetAppUsageStats：分母が収集時間、旧形式データはタイトル別に入らない
- GitCommitReader：LooksLikeRepository（.git ファイル）、Read の期間フィルタ（ローカル時刻）※git 実行はテストでは空を返す前提
- BuildRangeText の3パターン、Resolve の境界
- GapFill：学習の重み（時間）、30分未満はカテゴリ推測しない、既定タイトルの優先順
