# 実装計画（18フェーズ）

- 1フェーズ＝1コミットを目安にする。各フェーズの終わりで**ビルドが通り、テストが全件成功し、アプリが起動する**
- 各節は単独で読めるように書いてある。AIには **AGENT_RULES.md ＋ この節 ＋「読む設計書」に挙げた節だけ**を渡す
- 「ステップ」はセッションを分けるときの区切り。ステップの終わりでもビルドが通るようにしてある
- 「スタブ」は後のフェーズで中身を入れる空実装。**名前とシグネチャは設計書どおり**にしておく（後で差し替えるだけにするため）
- 設定項目（`AppSettings`）は `design/02` §17 の「導入」列のフェーズで binding に追加する
- テストのメソッド名は日本語の文（例：`クイック追加をコマンドから実行してUndoとRedoできる`）

## 依存関係の早見表

```
P1 土台 → P2 モデル → P3 保存 → P4 VM骨格・メインウィンドウ
  → P5 日/週表示 → P6 日/週編集・Undo → P7 分類マスター・検索・フィルタ
  → P8 記録・トレイ・ホットキー・ミニバー → P9 離席 → P10 出退勤
  → P11 月/スプリント・今日ホーム → P12 定期予定
  → P13 ToDo 基本 → P14 ToDo 通知・連携
  → P15 タイムライン → P16 統計・退勤ふりかえり
  → P17 記録漏れ・使用アプリ・Git → P18 ふりかえり一覧・設定検索・総仕上げ
```

---

## P1. ソリューションの土台とテーマ

**目的**：空のメインウィンドウがライト配色で起動し、共通スタイルとコンバーターが揃っている。

**読む設計書**：`01` §1〜3 ／ `03` 全体

**作るファイル**
- `TimeRenderer.sln`、`TimeRenderer/TimeRenderer.csproj`、`TimeRenderer.Tests/TimeRenderer.Tests.csproj`
- `App.xaml(.cs)`（例外ハンドラ・`ApplyTheme`。CrashLogService はこのフェーズでは空の static クラスでよい）
- `AssemblyInfo.cs`、`Properties/AssemblyInfo.cs`（InternalsVisibleTo）、`Assets/AppIcon.ico`
- `Infrastructure/ObservableObject.cs`、`Helpers/RelayCommand.cs`、`Helpers/LayoutConstants.cs`、`Helpers/ThemeHelper.cs`、`Helpers/DayOfWeekHelper.cs`、`Converters/DateTimeHelper.cs`
- `Themes/Colors.xaml`、`Themes/DarkColors.xaml`、`Themes/Styles.xaml`、`Themes/Generic.xaml`
- `Converters/*`（`03` §4 のうち、モデルに依存しないもの：`BrushToContrastTextConverter`, `BrushToSubtleBackgroundConverter`, `InvertedBooleanToVisibilityConverter`, `DayOfWeekToBrushConverter`, `LShapeGeometryConverter`, `IndexToTopMarginConverter`, `DurationToHeightConverter`, `TimeToPositionConverter`, `ConverterIndices`）
- `Controls/TransitioningContentControl.cs`、`Helpers/ScrollViewerHelper.cs`
- `Views/MainWindow.xaml(.cs)`（`01` §3 の Window 設定のみ。中身は空の Grid）

**ステップ**
1. csproj 2つ・App・Infrastructure・RelayCommand → 起動して空ウィンドウ
2. Colors / DarkColors / Styles / Generic → スタイルを全部定義（使う画面はまだ無くてよい）
3. コンバーター・TransitioningContentControl・ScrollViewerHelper

**テスト**：`ObservableObjectTests`（SetProperty が同値で通知しない・戻り値）

**手動確認**
- 起動してウィンドウが出る（タイトル・最小サイズ・アイコン）
- 一時的に `App.ApplyTheme(true)` を呼ぶと背景がダーク配色になる（確認後に戻す）

**完了条件**：ビルド警告0、テスト成功、`Styles.xaml` のキーが `03` §3 の一覧と一致

---

## P2. データモデル

**目的**：保存対象・表示用のモデルクラスが揃い、単体で振る舞いを確認できる。

**読む設計書**：`02` §1〜16、§19

**作るファイル**
- `Models/`：`ScheduleItem`, `ScheduleItemKind`, `ScheduleSegment`, `ItemSnapshot`, `CategoryInfo`, `ProjectCodeInfo`, `RoutineScheduleItem`(+`RecurrenceType`), `SprintInfo`, `TodoItem`(+enum), `TodoSubtask`, `TodoSnapshot`, `WorkDayLog`(+`WorkEndSource`), `WorkDayEditResult`, `AwayPeriod`(+`AwayReason`), `AppUsageInterval`, `UnrecordedGap`, `UnrecordedTimeProjectAssignment`, `GitRepositoryInfo`, `GitCommit`
- `ViewModels/`：`ViewMode`, `TimerOption`, `TodoSortMode`, `AwayHandlingMode`

**ステップ**
1. ScheduleItem 系・CategoryInfo・ProjectCodeInfo・SprintInfo・WorkDayLog・AwayPeriod
2. RoutineScheduleItem（`11` §2 の発生判定も含めて**ここで全部**書く）
3. TodoItem 系（`09` §1 の派生プロパティ・繰り返しも**ここで全部**書く）、残りのモデル

**テスト**：`DayOfWeekOutputTests`、`TodoRecurrenceTests`（`09` §14）、RoutineScheduleItem の `OccursOn`（隔週・月末・第N曜日・休み）

**手動確認**：なし（UI なし）

**完了条件**：JSON 化したときのプロパティ名・enum 数値が `02` と一致（`[JsonIgnore]` の付け漏れがない）

---

## P3. 永続化・設定の読み書き・ログ

**目的**：データファイルを安全に保存・復旧でき、設定を読み書きできる。

**読む設計書**：`15` 全体 ／ `02` §17（表のみ）

**作るファイル**
- `Services/JsonFileRepository.cs`、`FilePersistenceService.cs`、`SettingsService.cs`、`CrashLogService.cs`（中身を実装）
- `Models/AppSettings.cs`（**46項目すべて**定義してよい）、`Services/AppSettingsNormalizer.cs`（全項目）
- `Services/IDialogService.cs`、`DefaultDialogService.cs`（このフェーズでは `ShowMessage` / `ShowConfirmationDialog` のみ実装し、他のメソッドは後のフェーズで追加）、`TimeRenderer.Tests/TestDialogService.cs`

**ステップ**
1. JsonFileRepository（アトミック保存・.bak・日次スナップショット7世代・多段復旧・LoadStatus）
2. FilePersistenceService（各ファイル・サンプルデータ）・SettingsService・Normalizer
3. CrashLogService、App の例外ハンドラをログ出力につなぐ

**テスト**：`JsonFileRepositoryTests`、`FilePersistenceServiceTests`、`AppSettingsTests`（`15` §7、`02` §17 の補正）

**手動確認**
- 起動 → `%APPDATA%\TimeRenderer\Logs` に起動ログが出る
- 一時ディレクトリで保存を2回 → `.bak` とスナップショットができる（テストで代替可）

**完了条件**：壊れた JSON を読ませたとき `.bak` → スナップショットの順に復旧し、全滅なら `Failed` になる

---

## P4. MainViewModel の骨格・メインウィンドウ・ナビゲーション

**目的**：ツールバーとナビから表示モードと日付を切り替えられ、設定パネル／管理パネルの枠が開閉し、ダークモードが切り替わる。予定データを読み込み・保存できる。

**読む設計書**：`01` §4〜7 ／ `02` §18 ／ `04` §1〜5（§5 は継続通知＝データ保全通知のみ） ／ `13` 冒頭の外枠と §1.1・§1.2①、§2.1 ／ `15` §5

**作るファイル**
- `ViewModels/MainViewModel.cs`（コンストラクタ・時計・主要プロパティ・`DateDisplay`・ScheduleItems 監視）
- `MainViewModel.Commands.cs`（前へ/次へ/今日/モード変更のみ）、`.Data.cs`（パネル開閉・ダーク・データ保存読込）、`.SettingsMapping.cs`
- `Views/MainWindow.xaml(.cs)`（ツールバー3グループ・2段化・ViewNavigation 配置・パネル配置）、`Views/ViewNavigation.xaml`、`Views/NotificationHost.xaml`（データ保全通知のみ）
- `Views/SettingsPanel.xaml`（外枠・ヘッダー・「外観」セクションのみ）、`Views/ManagementPanel.xaml`（外枠・ヘッダー・空のタブ3つ）

**設定の binding**：#1, #2, #14, #17

**スタブ**：`01` §4 のコンストラクタが呼ぶ `Initialize*` / `Load*` / `RebuildTodayOverview` / `RecalculateLayout` / `UpdateStats` などは空メソッド。ツールバーの記録・出退勤・ToDo・検索ボタンは置くが Command は未設定（または何もしないコマンド）。各ビュー領域はモード名を出すだけの仮 TextBlock

**ステップ**
1. MainViewModel.cs / Data.cs / SettingsMapping.cs（起動時の読込と保存まで）
2. MainWindow のツールバー・ViewNavigation・パネル枠
3. NotificationHost のデータ保全通知、ダークモード切替

**テスト**：`MainViewModelIntegrationTests` の「軽量起動経路は…初期化する」（`TestDialogService`・`startRuntime:false`）、`AppSettingsMappingContractTests`（**導入済みの項目だけ**を検査する形か、フェーズ18まで Skip）

**手動確認**
- ナビの各ボタンで表示モードが切り替わり、ツールバーの日付表示が `01` §5 の書式になる
- 前へ/次へ/今日で日付が動く。ウィンドウ幅を狭めるとツールバーが2段になる
- 設定パネルと管理パネルが排他的に開閉し、幅のアニメーションが 0.15 秒
- ダークモードを切り替え、再起動後も保持される

**完了条件**：`appsettings.json` と予定データファイルが作られ、再起動で状態が戻る

---

## P5. 日/週ビューの表示

**目的**：予定・実績が日/週ビューに正しい位置・重なり・色で表示される（編集はまだ）。

**読む設計書**：`05` §1〜3（§3.5 は後のフェーズ） ／ `03` §4（残りのコンバーター） ／ `13` §1.2②（表示時間範囲・表示曜日のみ） ／ `13` §3.1 のうち `LoadCategories` / `ResolveCategory` / `IsItemVisible`

**作るファイル**
- `Converters/`：`DateToPagePositionConverter`, `DateToPageVisibilityConverter`, `DateToVisibleDaysConverter`, `DateToBackgroundBrushConverter`
- `Helpers/ScheduleLayoutHelper.cs`
- `ViewModels/MainViewModel.Layout.cs`（表示時間範囲・VisibleDays（日/週のみ）・RecalculateLayoutCore・遅延再計算）
- `MainViewModel.Categories.cs`（読込・解決・表示判定のみ）、`MainViewModel.ProjectCodes.cs`（`LoadProjectCodes` / `ResolveProjectCode` のみ）
- `Views/DayWeekView.xaml(.cs)`（描画のみ）

**設定の binding**：#15, #16, #46（`EnabledDaysOfWeek`）

**ステップ**
1. ScheduleLayoutHelper と Layout.cs（セグメント・終日行の計算）
2. DayWeekView の時間グリッド・曜日ヘッダー・予定バー・終日行
3. 設定パネル「表示設定」の表示時間範囲・表示曜日、日付送りのスライド遷移

**テスト**：`ScheduleLayoutHelperTests`

**手動確認**
- サンプルデータが週ビューに並び、重なる予定が列に分かれる
- 予定（枠線・薄い塗り）と実績（塗り）の見分け、日跨ぎのL字表示、終日行
- 表示時間範囲・表示曜日を変えると即座に反映され、再起動後も保持

---

## P6. 日/週ビューの編集・取り消し・選択・複製

**目的**：ドラッグ・インライン入力・ダイアログで予定を作成/編集/削除でき、すべて Ctrl+Z / Ctrl+Y で戻せる。

**読む設計書**：`05` §4〜6 ／ `14` §1〜3 ／ `04` §8 ／ `13` §1.2②（刻み幅・吸着）

**作るファイル**
- `Helpers/MagnetSnapHelper.cs`、`Helpers/UndoManager.cs`、`Helpers/UndoableEdits.cs`
- `MainViewModel.Commands.cs`（追加/編集/削除）、`.Snapping.cs`、`.InlineEdit.cs`、`.Selection.cs`、`.Clipboard.cs`、`.Undo.cs`
- `Views/DayWeekView.Drag.cs`、`.Schedule.cs`、`.InlineEdit.cs`、`Views/ScheduleItemMenu.cs`、`Views/MainWindow.Undo.cs`
- `Views/Dialogs/ScheduleEditDialog.xaml(.cs)`、`IDialogService` に `ShowScheduleEditDialog` を追加
- `MainViewModel.Titles.cs`（`LoadPinnedTitles` と `GetTitleSuggestions` のみ。管理 UI は P7）

**設定の binding**：#34, #35

**スタブ**：定期予定の仮想アイテム分岐（`IsVirtual`）は条件だけ書いて中身は P12。ToDo のドロップは P13

**ステップ**
1. UndoManager・UndoableEdits・Undo.cs・Commands（ダイアログ経由の追加/編集/削除）
2. ドラッグ（移動・伸縮・Alt 複製）・吸着・範囲作成・ダブルクリック・インライン入力
3. 選択・キーボード操作・複製/コピー/貼り付け・コンテキストメニュー

**テスト**：`MagnetSnapHelperTests`、`UndoManagerTests`、統合テスト（ダイアログ追加→Undo→Redo、ドラッグ確定が1回で戻る）

**手動確認**
- 空白ドラッグで作成、端で伸縮、中央で移動、Alt+ドラッグで複製、隣の予定に吸い付く細線
- Enter でインライン確定、Esc で取り消し
- 削除・編集・ドラッグ・貼り付けがそれぞれ Ctrl+Z で1操作ずつ戻る。ツールチップに「元に戻す: …」
- 編集後 700ms で保存され、再起動しても残る

---

## P7. 分類マスター・管理パネル（分類タブ）・検索・表示フィルタ

**目的**：カテゴリ・プロジェクトコード・定型タイトルを管理でき、検索と表示フィルタが効く。

**読む設計書**：`13` §2.1〜2.2（未記録時間の加算部分を除く）、§3.1〜3.3 ／ `14` §4〜5 ／ `04` §2.2・§2.5（検索フライアウト・フィルタポップアップ）

**作るファイル**
- `MainViewModel.Categories.cs`（コマンド・既定カテゴリ・フィルタ）、`.ProjectCodes.cs`（期間割り当て以外）、`.Titles.cs`（コマンド）
- `MainViewModel.Search.cs`
- `Views/ManagementPanel.xaml` の「分類」タブ（カテゴリ・プロジェクトコード（未記録時間の3項目を除く）・定型タイトル）
- `Converters/ProjectCodeIdConverter.cs`
- MainWindow の検索フライアウト・フィルタポップアップ

**設定の binding**：#37, #38, #39, #40, #44

**スタブ**：検索結果の ToDo・ふりかえり種別は P13 / P18 で追加（種別の enum だけ先に定義）

**テスト**：統合テスト（カテゴリ追加→削除確認→Undo 対象外であること、プロジェクトコードの削除ガード）

**手動確認**
- カテゴリの色・名前変更が日/週ビューの色に即反映、最後の1件は削除ボタンが無効
- 使用中のプロジェクトコードは削除できずメッセージ、最後の有効コードは無効化できない
- 検索で予定がヒットし、結果クリックでその日へ移動・選択。フィルタでカテゴリを外すと非表示（再起動で元に戻る）

---

## P8. 記録（タイマー）・トレイ・ホットキー・ミニバー

**目的**：記録の開始/停止で実績が作られ、トレイ・ホットキー・ミニバーから操作できる。

**読む設計書**：`10` §1〜3 ／ `04` §5.2（一時通知）・§6・§7・§10

**作るファイル**
- `Helpers/RecordingStopHelper.cs`、`RecordingItemHelper.cs`、`AppIconHelper.cs`
- `MainViewModel.Recording.cs`、`.RecordingStop.cs`、`.MiniBar.cs`、`.Routines.cs` のうち `AutoStartNotice` / `ShowAutoStartNotice` のみ
- `Views/Dialogs/RecordingStartDialog.xaml(.cs)`、`Views/MiniRecordingBar.xaml(.cs)`、`Views/MainWindow.Hotkey.cs`
- MainWindow のトレイ（NotifyIcon）・状態アイコン・最小化/閉じる、NotificationHost の一時通知
- `SettingsPanel` の「記録中のミニバー」セクション

**設定の binding**：#29, #30, #31

**スタブ**：`TakeAwayPeriodsForRecording` は空リストを返す（P9）。`_recordingTodo` の積算は P14。`ClearAwayState` は空

**テスト**：`RecordingStopHelperTests`、`RecordingItemHelperTests`、`RecordingTransitionTests`（停止中に次の記録を開始しても前の記録が保存される）

**手動確認**
- ツールバーの記録ボタン → ダイアログ → 開始、経過時間とタスクトレイのアイコンが記録中表示
- タイマー（カウントダウン）が0で自動停止し実績ができる
- 予定を右クリック「この内容で記録開始」→ 停止で予定が実績に変わり、Ctrl+Z で予定に戻る
- ホットキー（`04` §7）で開始/停止、ミニバーが最前面・ドラッグ位置が次回も保持・クリックで入力先を奪わない

---

## P9. 離席検知

**目的**：記録中の無操作・スリープ・ロックを検知し、停止時に除外できる。

**読む設計書**：`10` §4〜6 ／ `13` §1.2④ ／ `04` §5.2（離席バナー）

**作るファイル**：`Services/AwayDetector.cs`、`MainViewModel.Away.cs`、`Views/Dialogs/AwayReviewDialog.xaml(.cs)`、NotificationHost の離席バナー、設定「離席・中断の検知」

**設定の binding**：#21, #22, #23

**スタブ**：`HandleAwayForWorkDay` は空（P10）

**テスト**：`RecordingStopHelperTests` に離席分割のケースを追加、統合テスト（`AlwaysExclude` で分割され1回の Undo で全部消える）

**手動確認**
- 閾値を3分にして記録 → 3分放置でバナー → 操作再開 → 停止で確認ダイアログ、「離席時間を除く」で2件に分割
- 「常に除外する」で通知が出て、Ctrl+Z でまとめて消える
- スリープ・画面ロックから復帰した分も同様に扱われる

---

## P10. 出退勤・勤務マーカー

**目的**：出勤/退勤を記録し、日/週ビューに線で表示・編集でき、押し忘れを救済する。

**読む設計書**：`10` §7〜8 ／ `05` §3.5（出退勤マーカーのみ） ／ `13` §1.2⑧（「退勤時にふりかえる」を除く）

**作るファイル**：`Helpers/WorkDayPolicy.cs`、`MainViewModel.WorkDay.cs`、`ViewModels/WorkDayMarker.cs`、`Views/Dialogs/WorkDayEditDialog.xaml(.cs)`、DayWeekView のマーカー、ツールバーの出退勤ボタン、設定「勤務の記録」

**設定の binding**：#24, #25, #26

**スタブ**：`ShowWorkEndReview` は空（P16）、`NotifyWorkDayNotesChanged` は空（P16/P18）、`ApplyAppUsageTrackingState` は空（P17）、`RebuildUnrecordedGaps` は空（P17）、`RebuildTodayWorkload` は空（P14）

**テスト**：`WorkDayPolicyTests`、統合テスト（出勤→退勤→同日再出勤で同じ記録が再開）

**手動確認**
- 出勤/退勤ボタンとホットキー、日ビューの横線とラベル、ラベルクリックで編集ダイアログ
- 前日を未退勤のまま翌日に起動 → 最終記録の時刻で自動締め＋通知、マーカーに「（自動）」
- 終了検知：閾値以上離席して「確認する時間帯」以降に戻ると退勤確認

---

## P11. 月/スプリントカレンダー・スプリント管理・今日ホーム

**目的**：月・スプリントのカレンダーと今日ホームが表示され、手動スプリントを登録できる。

**読む設計書**：`06` §1〜5 ／ `11` §1 ／ `13` §2.4 ／ `04` §9（月セルのイベント）

**作るファイル**：`Helpers/SprintHelper.cs`、`Controls/CalendarMonthCellControl.cs`、`ViewModels/CalendarCellViewModel.cs`、`Views/CalendarGridView.xaml(.cs)`、`MainViewModel.Layout.cs`（月/スプリントの表示日・`UpdateCalendarCells`）、`MainViewModel.Commands.cs`（スプリントフォーム）、`MainViewModel.Today.cs`、`Views/TodayView.xaml`、管理パネル「スプリント」タブ

**設定の binding**：#36

**スタブ**：`GetVisibleTodosByDueDate` は空の辞書（P13）、今日ホームの `Todos` 部分は空（P13）、`UnrecordedGaps` は空（P17）、`TodayWorkload` は null（P14）

**テスト**：`SprintHelperTests`

**手動確認**
- 月ビュー：6週×有効曜日、他月のセルが薄い背景、今日のセルが強調、「+N 件」、ダブルクリックで編集/新規
- スプリントを手動登録するとスプリントビューの行数が週数に合い、隙間は14日単位の自動スプリント
- 今日ホーム：記録中/次の予定/予定なしの3状態、4指標、「日ビューで詳しく確認」

---

## P12. 定期予定

**目的**：定期予定から仮想の予定が生成され、この日のみ/全体の編集・削除・時間変更と、開始時刻の通知・自動記録開始ができる。

**読む設計書**：`11` §3〜6 ／ `13` §2.3 ／ `04` §5.2（リマインダーカード）

**作るファイル**：`Helpers/RoutineOccurrencePlanner.cs`、`MainViewModel.Routines.cs`（全体）、`Views/Dialogs/RoutineEditDialog.xaml(.cs)`、`RoutineScopeDialog.xaml(.cs)`、管理パネル「定期予定」タブ、NotificationHost のリマインダー

**設定の binding**：#45

**テスト**：`RoutineOccurrencePlannerTests`、統合テスト（仮想アイテムの「この日のみ」削除で除外日が増え、再生成で復活しない）

**手動確認**
- 毎週・隔週・毎月（末日）・第N曜日・休みの予定が期待どおりの日に出る
- 仮想予定のドラッグで範囲確認、「定期予定全体」で他の日も動く、日跨ぎはエラー
- 開始時刻でリマインダー（音）→「記録開始」、自動開始・強制開始の挙動

---

## P13. ToDo の基本

**目的**：ToDo パネルで追加（記法つき）・編集・完了・並べ替え・サブタスクができ、日/週の終日行と月セルに期限の ToDo が出る。

**読む設計書**：`09` §2〜5、§7、§8（保存のみ）、§11〜12 ／ `14` §1（ToDo の Undo）

**作るファイル**：`Helpers/TodoQuickParser.cs`、`TodoOrderHelper.cs`、`TodoSubtaskHelper.cs`、`MainViewModel.Todos.cs`、`.Todos.Sorting.cs`、`.Todos.Subtasks.cs`、`.Todos.Persistence.cs`（読込・保存）、`ViewModels/TodoChip.cs`、`Views/TodoPanel.xaml(.cs)`、`Views/Dialogs/TodoEditDialog.xaml(.cs)`、DayWeekView の ToDo チップ、月セルへの ToDo 供給、検索への ToDo 追加、設定「ToDo」の記法の項目

**設定の binding**：#3, #4, #5, #10

**スタブ**：通知・まとめ・アーカイブ・見積もり傾向（`EstimateStats` は null を返す）は P14。`StartRecordingFromTodo` は P14

**テスト**：`TodoQuickParserTests`、`TodoOrderHelperTests`、`TodoSubtaskHelperTests`、統合テスト（クイック追加→Undo/Redo、完了＋次回分生成が1回で戻る）

**手動確認**
- 「資料作成 @明日 !高 #開発 ~30m」でプレビュー表示 → Enter で属性つき追加
- 完了で取り消し線、繰り返し ToDo は次回分が作られ通知が出る
- 手動並べ替え（ドラッグ・Ctrl+↑↓）、サブタスク全完了で親が完了
- 週ビューの終日行にピル型チップ、期限超過は赤枠、月セルにも表示

---

## P14. ToDo の通知・アーカイブ・見積もり・記録連携

**目的**：ToDo の通知・見逃し・朝のまとめ・アーカイブ・見積もり傾向・時間ブロック・記録からの積算・積みすぎ検知が動く。

**読む設計書**：`09` §6、§8（アーカイブ・見積もり）、§9、§10、§12（見積もり傾向表示）、§13 ／ `04` §5.2（ToDo 通知カード・スヌーズメニュー） ／ `13` §1.2⑦（記法以外）

**作るファイル**：`Helpers/TodoReminderHelper.cs`、`TodoDigestHelper.cs`、`TodoArchiveHelper.cs`、`TodoTimeBlockHelper.cs`、`ViewModels/TodoEstimateStats.cs`、`MainViewModel.Todos.Notifications.cs`、`.Todos.Recording.cs`、`.Todos.TimeBlocking.cs`、`.Workload.cs`、`Views/Dialogs/TodoPickerDialog.xaml(.cs)`、NotificationHost の ToDo 通知

**設定の binding**：#6, #7, #8, #9, #11, #12, #13

**スタブ**：`FillGapFromTodoPicker` の呼び出し元（未記録の帯）は P17

**テスト**：`TodoReminderHelperTests`、`TodoDigestHelperTests`、`TodoArchiveHelperTests`、`TodoTimeBlockHelperTests`、統合テスト（ToDo の時間ブロックを追加して Undo、ToDo から記録→停止で実績と積算が同じ Undo 単位）

**手動確認**
- 通知時刻にカード＋音、スヌーズ（▾メニュー）、15分超の遅れは「見逃した通知」1本
- 設定時刻以降の起動で1日1回のまとめ
- ToDo を週ビューへドラッグで時間ブロック、ToDo パネルの▶で記録開始→停止で実績時間が増える
- 見積もり5件以上＋手動退勤5回以上で「見込み H:MM ・ 退勤まで H:MM」が出る

---

## P15. スプリントタイムライン

**目的**：タイムラインビューでズーム・スクロール・レーン表示・ドラッグ編集ができ、大量データでも軽い。

**読む設計書**：`07` 全体

**作るファイル**：`Helpers/TimelineScale.cs`、`TimelineLaneHelper.cs`、`ViewModels/TimelineBar.cs`、`TimelineTick.cs`、`TimelineLaneGroup.cs`、`TimelineViewOptions.cs`、`MainViewModel.Timeline.cs`、`.TimelineDecorations.cs`、`.TimelineViewport.cs`、`Views/TimelineView.xaml(.cs)`

**設定の binding**：#18, #19, #20

**スタブ**：「この時間の使用アプリ」メニューの `ShowAppUsageCommand` は P17（それまでメニュー項目は無効）

**テスト**：`TimelineLaneHelperTests`、TimelineScale の往復

**手動確認**
- Ctrl+ホイールでカーソル位置を固定したままズーム、Shift+ホイールで横移動、プリセット4種
- 詰める/カテゴリ別/1件1行、カテゴリ別の左ラベル列と交互の帯
- バーの移動・伸縮（ズームに応じたスナップ）、空白の横ドラッグで作成、←→ Enter Delete F T
- 25スプリント表示で最大ズームにしてもスクロールが引っかからない

---

## P16. 統計・月次タイムシート・退勤時ふりかえり

**目的**：統計ビューで期間別の集計とタイムシートのコピーができ、退勤時にふりかえりと繰り越しができる。

**読む設計書**：`08` 全体（§4 はスタブ） ／ `10` §9 ／ `06` §6.1 のうち `WorkDayNote` と `ToWorkDayNote` ／ `06` §6.2 ／ `13` §1.2⑧（退勤時にふりかえる）

**作るファイル**：`Helpers/StatsAggregationHelper.cs`、`ViewModels/StatsTimesheetBuilder.cs`、`MainViewModel.Stats.cs`、`Views/StatsView.xaml(.cs)`、`Helpers/NoteTagParser.cs`、`MainViewModel.Notes.cs`（record と `ToWorkDayNote`・`NotifyWorkDayNotesChanged` の `UpdateStats` 呼び出しまで）、`MainViewModel.WorkEndReview.cs`、`ViewModels/WorkEndCarryOver.cs`、`WorkEndReviewResult.cs`、`Views/Dialogs/WorkEndReviewDialog.xaml(.cs)`

**設定の binding**：#27

**スタブ**：`AddUnrecordedTimeToProjectStats` は何もしない（P17）。`GetCommitsBetween` は空を返す（P17。コミット欄は出ない）

**テスト**：`StatsAggregationHelperTests`、`StatsTimesheetBuilderTests`、`NoteTagParserTests`

**手動確認**
- 週/月/スプリントの切替、プロジェクトコード別・カテゴリ別の棒、日別積み上げ（ツールチップ）
- 月のタイムシート：セルクリックで「1.25 をコピーしました」、コード列クリックでコードをコピー
- 退勤 → ふりかえりダイアログ：数字・一言・片付かなかった ToDo → 「N 件を明日へ」→ 通知と Undo
- 一言が統計の「ふりかえり」に出て、クリックで勤務編集が開く

---

## P17. 記録漏れの帯・使用アプリ・Git・未記録時間の加算

**目的**：勤務中の記録が無い時間が帯で見え、使用アプリ・コミット・ToDo・自由入力から埋められる。

**読む設計書**：`12` 全体 ／ `08` §4 ／ `13` §1.2⑤⑥、§2.2（未記録時間の3項目） ／ `05` §3.5（未記録の帯）

**作るファイル**：`Helpers/UnrecordedGapHelper.cs`、`UnrecordedTimeAssignmentHelper.cs`、`Services/ActiveWindowTracker.cs`、`GitCommitReader.cs`、`ViewModels/GapFillSuggestion.cs`、`MainViewModel.Gaps.cs`、`.GapFill.cs`、`.AppUsage.cs`、`.Git.cs`、`.ProjectCodes.cs`（期間割り当て）、`Views/Dialogs/AppUsageDialog.xaml(.cs)`、`GapFillDialog.xaml(.cs)`、DayWeekView の未記録の帯、統計の未記録加算、設定「使用アプリの記録」「Git のコミット履歴」、管理パネルの未記録時間の項目、`IDialogService.ShowFolderPicker`

**設定の binding**：#28, #32, #33, #41, #42, #43

**テスト**：`UnrecordedGapHelperTests`、`UnrecordedTimeAssignmentHelperTests`

**手動確認**
- 出勤中に15分以上記録しない → 破線の帯「未記録 N分」、左ドラッグはそのまま作成、右クリックに3つの埋め方
- 記録中にブラウザのタブを切り替え → 予定の右クリック「この時間の使用アプリ」でタイトル別の内訳
- UWP アプリ（電卓など）が ApplicationFrameHost ではなく実アプリ名で出る
- リポジトリ登録 → 帯の「使用アプリから埋める」にコミットが出て、タイトル候補の先頭になる
- 未記録時間の加算を有効にすると統計のプロジェクト別と月次表だけが増える（カテゴリ別は増えない）
- 今日ホームの「未記録」指標が「H:MM・N件」になる

---

## P18. ふりかえり一覧・設定検索・総仕上げ

**目的**：ふりかえり一覧と設定の検索を仕上げ、全機能を通しで確認する。

**読む設計書**：`06` §6（全体） ／ `13` §1.3 ／ `14` §4（ふりかえりの検索） ／ `02` §18（契約テスト）

**作るファイル**：`MainViewModel.Notes.cs`（一覧・タグ絞り込み）、`Views/NotesView.xaml(.cs)`、`SettingsPanel.xaml.cs`（検索）、検索へのふりかえり追加、`AppSettingsMappingContractTests` を全46項目で有効化

**テスト**：全テスト（元アプリは 239 件以上）。`dotnet run --project TimeRenderer.Tests -- --minimum-expected-tests <件数>`

**手動確認（通しの回帰）**
1. 初回起動（データフォルダ無し）→ サンプルデータ、今日ホームが表示
2. 予定作成 → ドラッグ → 編集 → 削除 → Ctrl+Z を5回 → Ctrl+Y
3. 記録開始 → 離席 → 停止（除外）→ 出勤/退勤 → ふりかえり → 繰り越し
4. 定期予定・ToDo（記法・繰り返し・通知）・時間ブロック
5. 月/スプリント/タイムライン/統計/ふりかえり の各ビュー、ダークモードで全ビューの配色崩れが無い
6. 設定検索「通知」で ToDo セクションだけが開き、「該当なし」表示も確認
7. アプリ終了 → データファイルを壊して起動 → バックアップから復旧の通知
8. 高 DPI（150%）で月セルのクリック位置がずれない

**完了条件**：`design/02` §17 の46項目すべてが binding に1回ずつ、ビルド警告0、全テスト成功、上記回帰がすべて期待どおり
