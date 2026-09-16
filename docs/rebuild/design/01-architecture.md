# 01. アーキテクチャ

## 1. ソリューション

```
TimeRenderer.slnx
global.json                         … {"test":{"runner":"Microsoft.Testing.Platform"}}
TimeRenderer/TimeRenderer.csproj
TimeRenderer.Tests/TimeRenderer.Tests.csproj
```

### TimeRenderer.csproj

```xml
<Project Sdk="Microsoft.NET.Sdk">
  <PropertyGroup>
    <OutputType>WinExe</OutputType>
    <TargetFramework>net10.0-windows</TargetFramework>
    <Nullable>enable</Nullable>
    <ImplicitUsings>enable</ImplicitUsings>
    <UseWPF>true</UseWPF>
    <UseWindowsForms>true</UseWindowsForms>
    <!-- LibraryImport ソースジェネレーターの生成コードが unsafe を使うため必要 -->
    <AllowUnsafeBlocks>true</AllowUnsafeBlocks>
    <ApplicationIcon>Assets\AppIcon.ico</ApplicationIcon>
  </PropertyGroup>
  <ItemGroup>
    <Resource Include="Assets\AppIcon.ico" />
  </ItemGroup>
</Project>
```

### TimeRenderer.Tests.csproj

```xml
<Project Sdk="Microsoft.NET.Sdk">
  <PropertyGroup>
    <TargetFramework>net10.0-windows</TargetFramework>
    <UseWPF>true</UseWPF>          <!-- モデルが Brush を持つため -->
    <OutputType>Exe</OutputType>   <!-- TUnit は実行可能ファイル。WinExe だと出力が読めない -->
    <Nullable>enable</Nullable>
    <ImplicitUsings>enable</ImplicitUsings>
    <IsPackable>false</IsPackable>
    <InvariantGlobalization>false</InvariantGlobalization>
  </PropertyGroup>
  <ItemGroup>
    <PackageReference Include="TUnit" Version="1.*" />  <!-- Microsoft.NET.Test.Sdk は入れない -->
  </ItemGroup>
  <ItemGroup>
    <ProjectReference Include="..\TimeRenderer\TimeRenderer.csproj" />
  </ItemGroup>
</Project>
```

テストの書き方：`[Test] public async Task 日本語の説明() { ... await Assert.That(x).IsEqualTo(y); }`
実行：`dotnet run --project TimeRenderer.Tests -- --minimum-expected-tests N`

## 2. ファイル一覧と役割

### ルート
| ファイル | 役割 |
| --- | --- |
| `App.xaml(.cs)` | StartupUri=`Views/MainWindow.xaml`。リソース：`Themes/Styles.xaml`, `Themes/Generic.xaml`。未処理例外ハンドラ、`ApplyTheme(bool)` |
| `AssemblyInfo.cs` | `[assembly: ThemeInfo(None, SourceAssembly)]` |
| `Properties/AssemblyInfo.cs` | `InternalsVisibleTo("TimeRenderer.Tests")` |
| `Assets/AppIcon.ico` | アプリアイコン |

### Infrastructure / Helpers（汎用）
| ファイル | 役割 |
| --- | --- |
| `Infrastructure/ObservableObject.cs` | `INotifyPropertyChanged` 基底。`OnPropertyChanged([CallerMemberName])`, `bool SetProperty<T>(ref T, T, [CallerMemberName])`（同値なら通知しない） |
| `Helpers/RelayCommand.cs` | `RelayCommand(Action<object?>, Func<object?,bool>? = null)`。`CanExecuteChanged` は `CommandManager.RequerySuggested` に委譲 |
| `Helpers/LayoutConstants.cs` | `PixelsPerHour = 60.0`（日/週ビューの縦スケール。ここ以外に 60 を書かない） |
| `Helpers/DayOfWeekHelper.cs` | `WeekOrder`（月〜日）、`GetShortJapaneseName(DayOfWeek)`（"日","月",…）、`TryParseShortJapaneseName` |
| `Helpers/ThemeHelper.cs` | `GetBrush(key, fallback)`：`Application.Current.Resources[key]` |
| `Helpers/ScrollViewerHelper.cs` | 添付プロパティ `EnableMiddleButtonScroll`（中ボタンのオートスクロール）→ `design/03` |
| `Helpers/AppIconHelper.cs` | トレイ/ウィンドウアイコン生成と状態オーバーレイ → `design/04` |
| `Converters/DateTimeHelper.cs` | `GetStartOfWeek(this DateTime)`（月曜始まり）、`GetShortDayOfWeek(DateTime)` |

### Models（`design/02`）
`ScheduleItem`, `ScheduleItemKind`, `ScheduleSegment`, `ItemSnapshot`, `CategoryInfo`, `ProjectCodeInfo`, `RoutineScheduleItem`(+`RecurrenceType`), `SprintInfo`,
`TodoItem`(+`TodoPriority`,`TodoRecurrenceUnit`), `TodoSubtask`, `TodoSnapshot`, `WorkDayLog`(+`WorkEndSource`), `WorkDayEditResult`,
`AwayPeriod`(+`AwayReason`), `AppUsageInterval`, `UnrecordedGap`, `UnrecordedTimeProjectAssignment`, `GitRepositoryInfo`, `GitCommit`, `AppSettings`

### Services
| ファイル | 役割 | 設計書 |
| --- | --- | --- |
| `JsonFileRepository.cs` | アトミック保存・バックアップ・多段復旧（static） | 15 |
| `FilePersistenceService.cs` | 各データファイルの保存/読込（static） | 15 |
| `SettingsService.cs` | `appsettings.json` の保存/読込 | 15 |
| `AppSettingsNormalizer.cs` | 読み込んだ設定の範囲補正 | 02 |
| `CrashLogService.cs` | 起動/終了/例外ログ | 15 |
| `IDialogService.cs` / `DefaultDialogService.cs` | ダイアログの抽象と実装、`RoutineScope` enum | 各機能 |
| `AwayDetector.cs` | 無操作・スリープ・ロック検知 | 10 |
| `ActiveWindowTracker.cs` | 前面アプリの使用期間収集 | 12 |
| `GitCommitReader.cs` | `git log` 読み出し | 12 |

### Helpers（純粋ロジック・テスト対象）
`ScheduleLayoutHelper`(05), `MagnetSnapHelper`(05), `SprintHelper`(11), `RoutineOccurrencePlanner`(11), `TimelineScale`(07), `TimelineLaneHelper`(07),
`StatsAggregationHelper`(08), `UnrecordedGapHelper`(12), `UnrecordedTimeAssignmentHelper`(12), `RecordingStopHelper`(10), `RecordingItemHelper`(10),
`WorkDayPolicy`(10), `TodoQuickParser`(09), `TodoOrderHelper`(09), `TodoReminderHelper`(09), `TodoDigestHelper`(09), `TodoArchiveHelper`(09),
`TodoSubtaskHelper`(09), `TodoTimeBlockHelper`(09), `NoteTagParser`(06), `UndoManager`/`UndoableEdits`(14)

### Converters / Controls（`design/03`, `05`, `06`）
`TimeToPositionConverter`, `DurationToHeightConverter`, `DateToPagePositionConverter`, `DateToPageVisibilityConverter`, `DateToVisibleDaysConverter`,
`IndexToTopMarginConverter`, `LShapeGeometryConverter`, `DateToBackgroundBrushConverter`, `DayOfWeekToBrushConverter`, `BrushToContrastTextConverter`,
`BrushToSubtleBackgroundConverter`, `InvertedBooleanToVisibilityConverter`, `ProjectCodeIdConverter`, `ConverterIndices`
`Controls/TransitioningContentControl`（スライド遷移）, `Controls/CalendarMonthCellControl`（月セル自前描画）

### ViewModels
| ファイル | 責務 | 設計書 |
| --- | --- | --- |
| `MainViewModel.cs` | 表示モード・日付・時計・記録タイマー・ScheduleItems 変更監視 | 01,04 |
| `.Commands.cs` | 追加/編集/削除/前後移動/今日/モード変更/スプリントフォーム | 05,11 |
| `.Data.cs` | パネル開閉、ダークモード、表示曜日、設定保存読込、予定データのデバウンス保存・読込 | 04,15 |
| `.SettingsMapping.cs` | AppSettings ⇔ VM の binding 一覧 | 02 |
| `.Layout.cs` | 表示時間範囲・刻み幅・表示日計算・セグメント再計算・カレンダーセル | 05,06 |
| `.Selection.cs` / `.Snapping.cs` / `.InlineEdit.cs` / `.Clipboard.cs` | 選択・吸着候補・インライン作成・複製/コピー | 05,14 |
| `.Undo.cs` | 取り消し/やり直し | 14 |
| `.Categories.cs` / `.ProjectCodes.cs` / `.Titles.cs` | 分類マスター | 13 |
| `.Search.cs` | 検索・表示フィルタ | 14 |
| `.Recording.cs` / `.RecordingStop.cs` | 記録セッション | 10 |
| `.Away.cs` | 離席検知の受け取り | 10 |
| `.WorkDay.cs` / `.WorkEndReview.cs` | 出退勤・退勤時ふりかえり | 10 |
| `.Routines.cs` | 定期予定・リマインダー・自動開始・一時通知 | 11 |
| `.Todos*.cs`（7ファイル） | ToDo | 09 |
| `.Workload.cs` | 今日やるの積みすぎ検知 | 09 |
| `.Timeline.cs` / `.TimelineDecorations.cs` / `.TimelineViewport.cs` | タイムライン | 07 |
| `.Stats.cs` / `StatsTimesheetBuilder.cs` | 統計 | 08 |
| `.Notes.cs` | ふりかえり一覧 | 06 |
| `.Today.cs` | 今日ホーム | 06 |
| `.Gaps.cs` / `.GapFill.cs` / `.AppUsage.cs` / `.Git.cs` | 未記録・使用アプリ・Git | 12 |
| `.MiniBar.cs` | ミニバー設定 | 04 |
| 小ファイル | `ViewMode`, `TimerOption`, `TodoSortMode`, `AwayHandlingMode`, `TimelineViewOptions`, `TimelineBar`, `TimelineLaneGroup`, `TimelineTick`, `CalendarCellViewModel`, `TodoChip`, `WorkDayMarker`, `WorkEndCarryOver`, `WorkEndReviewResult`, `GapFillSuggestion`, `TodoEstimateStats` | 各機能 |

### Views
`MainWindow.xaml(.cs)` + `MainWindow.Hotkey.cs` + `MainWindow.Undo.cs`, `ViewNavigation`, `TodayView`, `DayWeekView`(+`.Drag.cs`,`.Schedule.cs`,`.InlineEdit.cs`),
`CalendarGridView`, `TimelineView`, `StatsView`, `NotesView`, `NotificationHost`, `TodoPanel`, `SettingsPanel`, `ManagementPanel`, `MiniRecordingBar`, `ScheduleItemMenu.cs`
`Views/Dialogs/`：`ScheduleEditDialog`, `RecordingStartDialog`, `RoutineEditDialog`, `RoutineScopeDialog`, `TodoEditDialog`, `TodoPickerDialog`,
`AwayReviewDialog`, `WorkDayEditDialog`, `WorkEndReviewDialog`, `AppUsageDialog`, `GapFillDialog`

### Themes
`Colors.xaml`（ライト）, `DarkColors.xaml`（ダーク差し替え）, `Styles.xaml`（`Colors.xaml` をマージ＋共通スタイル＋共有コンバーター）, `Generic.xaml`（`TransitioningContentControl` の既定テンプレート）

## 3. 起動と終了

### App
- コンストラクタで `DispatcherUnhandledException` / `AppDomain.UnhandledException` / `TaskScheduler.UnobservedTaskException` を購読し、`CrashLogService.WriteLifecycle("Application started")`
- Dispatcher 例外：ログ（isTerminating: true）→ `MessageBox`「予期しないエラーが発生したため、TimeRenderer を終了します。\n\n原因調査用のログを次のフォルダーへ保存しました。\n{LogDirectory}」、タイトル「TimeRenderer エラー」、Error アイコン。`Handled` は false のまま（異常終了させる）
- UnobservedTask：ログ（isTerminating: false）＋ `SetObserved()`
- `OnExit`：`WriteLifecycle($"Application exited (ExitCode={code})")`、購読解除

### ApplyTheme(bool isDark)
`Application.Current.Resources.MergedDictionaries` から Source に `DarkColors.xaml` を含むものを全削除し、isDark なら `new ResourceDictionary{Source="Themes/DarkColors.xaml"}` を**末尾に Add**（後に追加したものが優先される）。

### MainWindow 生成
`DataContext = new MainViewModel(new DefaultDialogService(this))`。PropertyChanged・PendingReminders・PendingTodoReminders を購読、トレイ設定、状態アイコン更新、ミニバー更新、ポップアップ配置コールバック設定（`design/04`）。

## 4. MainViewModel の骨格

### コンストラクタ
```csharp
public MainViewModel(IDialogService dialogService) : this(dialogService, TimeProvider.System, startRuntime: true) {}

// テスト用：OS監視・実データ読込・時計を起動しない
internal MainViewModel(IDialogService dialogService, TimeProvider timeProvider, bool startRuntime)
{
    _dialogService = dialogService; _timeProvider = timeProvider; _isRuntimeActive = startRuntime;
    InitializeCommands(); InitializeCategoryCommands(); InitializeProjectCodeCommands(); InitializeGitCommands();
    InitializeStatsCommands(); InitializeSearchCommands(); InitializeTitleCommands(); InitializeRoutineCommands();
    InitializeTodoCommands(); InitializeWorkDayCommands(); InitializeUndo();
    if (startRuntime) { InitializeAwayDetection(); InitializeAppUsageTracking(); }
    LoadCategories(null); LoadProjectCodes(null); LoadPinnedTitles(null);   // 既定値（設定読込で上書き）
    ScheduleItems = []; ScheduleItems.CollectionChanged += OnScheduleItemsChanged;
    _selectedTimerOption = TimerOptions[0];
    CurrentDate = LocalToday;
    InitializeTimeLabels(); UpdateVisibleDays();
    if (!startRuntime) return;
    LoadData(); LoadSettings();
    LoadWorkDays();   // 予定読込の後（自動締めが実績を参照する）
    LoadAppUsage();
    LoadTodos();      // 設定読込の後（並べ替え設定を反映）
    RebuildTodayOverview(); StartClock();
    _isInitialized = true;
    if (_scheduleKindMigrationPending) { SaveData(); _scheduleKindMigrationPending = false; }
}
private DateTime LocalNow => _timeProvider.GetLocalNow().DateTime;
private DateTime LocalToday => LocalNow.Date;
```
`_isInitialized` が false の間、`SaveSettings` / `SaveData` / `ScheduleTodoSave` は何もしない。

> フェーズ途中では未実装の Initialize*/Load* は空メソッドのスタブにしておく。

### 時計（StartClock）
500ms の `DispatcherTimer`：
1. `CurrentTime = LocalNow`
2. `UpdateTimelineNowLine(CurrentTime)`（内部で30秒に間引き）
3. 前回から10秒以上経っていれば：`CheckReminders` → `UpdateWorkDayTick` → `UpdateUnrecordedGapTick` → `UpdateAppUsageTick` → `UpdateTodoTick` → `RebuildTodayOverview`
4. 記録中なら `RecordingDuration = now - RecordingStartTime`。カウントダウン中は残りを更新し、0以下で `CountdownRemaining = 0`、`SystemSounds.Exclamation.Play()`、`ToggleRecording()`（停止）

### 主要プロパティ（MainViewModel.cs）
| プロパティ | 内容 |
| --- | --- |
| `ViewModeOptions` | 今日/日/週/月/スプリント/タイムライン/統計/ふりかえり |
| `CurrentViewMode` | setter：`UpdateVisibleDays()` → `NotifyViewModeDependents()` → `SaveSettings()` |
| `IsDayMode`…`IsTodayMode`, `IsDayOrWeekMode` | モード判定 |
| `IsDateNavigationVisible` | Notes と Today 以外 |
| `IsTimeRangeSettingsVisible` | Day/Week |
| `IsSprintSettingsVisible` | Sprint/SprintTimeline |
| `IsDayOfWeekSettingsVisible` | SprintTimeline/Stats/Notes/Today 以外 |
| `CurrentDate` | setter：`UpdateVisibleDays()` → `EnsureRoutineOccurrences(value)` → 通知（CurrentDate, CurrentWeekStart, DateDisplay） |
| `DateDisplay` | 下表 |
| `TransitionDirection` | 前後移動時のスライド方向（Forward/Backward） |
| `CurrentTime` | 時計 |
| `TimerOptions` | 「カウントアップ」0 / 「15分」/「30分」/「45分」/「60分」 |
| `IsRecording`, `RecordingStartTime`, `RecordingDuration`, `RecordingTitle`, `IsCountdownMode`, `CountdownRemaining` | 記録（`design/10`） |
| `RecordingDurationText` | 記録中：カウントダウンなら「■ 停止 (残り hh:mm:ss)」、それ以外「■ 停止 (hh:mm:ss)」／非記録「● 記録開始」 |
| `RecordingElapsedText` | 「残り hh:mm:ss」または「hh:mm:ss」 |
| `RecordingDisplayTitle` | 空なら「（タイトル未入力）」 |
| `RecordingBrush` | `_recordingColorCode` があればその色、無ければ `RecordingCategory.Brush`、無ければ DarkOrange |
| `ManualSprints` | setter：`UpdateVisibleDays()` + `SaveSettings()`（リストは**再代入**で更新する） |
| `EnabledDayHeaders` | `EnabledDaysOfWeek` を月曜順に `DayHeaderInfo(Name, DayOfWeek)` |
| `StartHourOptions` 0〜23 / `EndHourOptions` 1〜24 | 表示時間範囲 |

`IsRecording` の setter：変更時に `RecordingDurationText`, `RecordingElapsedText`, `RecordingBrush`, `ShowAwayBanner` を通知し、`OnRecordingChangedForAppUsage(value)`, `RebuildUnrecordedGaps()`, `RebuildTodayOverview()`。

`DateDisplay`：
| モード | 表示 |
| --- | --- |
| Today | `今日  yyyy年M月d日 (ddd)`（空白2つ、LocalToday） |
| Day | `yyyy年M月d日 (ddd)` |
| Week | 同月：`yyyy年M月d日 - d日`、月跨ぎ：`yyyy年M月d日 - M月d日` |
| Month | `yyyy年M月` |
| Sprint | `{Name} (yyyy/MM/dd - MM/dd)` |
| Stats | `統計 [{週|月|スプリント名}] yyyy/MM/dd - MM/dd` |
| Notes | `NotesSummaryText` |
| SprintTimeline | `スプリントタイムライン (起点: {Name})` |

## 5. 変更監視・再計算・保存の流れ

### 抑止フラグ
| フラグ | 立つとき | 効果 |
| --- | --- | --- |
| `_isInitialized == false` | 起動中 | 保存しない。`RecalculateLayout` は即時実行 |
| `_isLoadingData` | 予定読込・仮想アイテム一括追加/削除 | コレクション変更で再計算・保存しない |
| `_isBatchUpdatingItem` | 編集ダイアログ結果の書き戻し、ドラッグプレビュー | プロパティ変更で再計算・保存しない |
| `IsApplyingUndo`（= `UndoManager.IsApplying`） | Undo/Redo 適用中 | 同上＋履歴に積まない。後で `AfterUndoRedo()` |
| `_isUpdatingTodo`, `_isLoadingTodos` | ToDo の一括更新・読込 | ToDo 側の再構築・保存を抑止 |

### ScheduleItems の監視
```csharp
void OnScheduleItemsChanged(...) {
  NewItems: PropertyChanged を -= してから +=
  OldItems: PropertyChanged -=、SelectedItem と同一なら SelectedItem = null
  if (_isLoadingData || IsApplyingUndo) return;
  RecalculateLayout(); SaveData();
}
void OnScheduleItemPropertyChanged(...) {
  if (PropertyName is ColumnIndex or IsSelected or ToolTipText or IsVirtual) return;
  if (_isBatchUpdatingItem || IsApplyingUndo) return;
  RecalculateLayout(); SaveData();
}
```

### RecalculateLayout（遅延・集約）
- 初期化前または `Application.Current?.Dispatcher == null` → `RecalculateLayoutCore()` を即時実行
- それ以外：`_isLayoutRecalculationPending` が false のときだけ true にして `Dispatcher.BeginInvoke(DispatcherPriority.Render, FlushPendingLayout)`
- `FlushPendingLayout()`：保留中なら実行。**計算結果（`DailyScheduleItems` など）を同期的に読む処理の先頭で呼ぶ**（選択移動・吸着候補）
- 日付送り/モード変更（`UpdateVisibleDays`）は遅延させず `RecalculateLayoutCore()` を即時実行
- `RecalculateLayoutCore()` の末尾で `UpdateCalendarCells()`, `UpdateTimelineItems()`, `UpdateStats()`, `RebuildUnrecordedGaps()`, `RebuildTodayOverview()` を呼ぶ（それぞれ自モードでなければ即 return）

## 6. ビューと VM の結合

- ビューは `(MainViewModel)DataContext` を直接使ってよい（ドラッグ・フォーカス・スクロールのため）
- VM → ビューへの要求はイベントで行う：
  `ScrollToTimeRequested(DateTime)`（日/週ビュー）, `TimelineScrollToItemRequested(ScheduleItem)`, `TimelineFitToItemRequested(ScheduleItem)`, `QuickAddTodoFocusRequested`
- 各 UserControl は `Loaded` で購読、`Unloaded` で解除（二重購読防止に `_subscribedViewModel` を持つ）
- MainWindow の表示ビューは `Grid.Row=2` に全ビューを重ね、`Visibility` を `IsXxxMode` で切り替える
  （日/週ビューは `IsDayOrWeekMode` のときだけ Visible にする。裏で生きているとレイアウトコストがかかるため）

## 7. データの保存場所

| 種類 | パス |
| --- | --- |
| データ | `%APPDATA%\TimeRenderer\`（`schedules.json`, `appsettings.json`, `workdays.json`, `todos.json`, `todos-archive.json`, `appusage.json` とバックアップ） |
| ログ | `%LOCALAPPDATA%\TimeRenderer\Logs\application-yyyy-MM-dd.log`（30日保持） |
