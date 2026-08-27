# リファクタリング調査（2026-08-27）

対象: `TimeRenderer/` 配下の .cs / .xaml 全 165 ファイル・32,627 行
（.cs 25,547 行 / .xaml 7,080 行）

前回レビュー（`CODE_REVIEW.md`・2026-07-12）時点は約 4,400 行。約 6 週間で 7.4 倍に増えた。

---

## 結論

**必要。ただし全面的な作り直しではなく、検証基盤と設定まわりを優先した局所的なもの。**

現行の partial 分割を全面的に置き換える根拠は見つからなかった。一方で、
private メンバーの参照数だけでは partial 間の独立性や MVVM 境界の健全性までは証明できない。
最優先の問題は、**規模に対して自動テストがゼロなこと**と、
**設定の保存・復元が複数箇所の手作業の同期に頼っていること**である。
その他の分割や共通化は、対象機能のテストを追加したうえで段階的に行うのがよい。

---

## 先に、直さなくてよい箇所

指摘の重みを測るために、健全だと確認できた点を先に挙げる。

### partial 分割は機能している

MainViewModel は 10,917 行・30 ファイルだが、単なるファイル分割ではない。

| 指標 | 値 |
| --- | --- |
| private フィールド | 201 個（うち他 partial から参照 62 個 = 30%） |
| private メソッド | 282 個（うち他 partial から呼出 111 個 = 39%） |
| public プロパティ | 228 個 |
| ICommand | 69 個 |

フィールドの 70% は宣言ファイル内で完結している。
partial 間の結合は特定のハブに集中していて、散らばってはいない。

| 共有されている識別子 | 宣言場所 | 参照ファイル数 |
| --- | --- | --- |
| `SaveSettings()` | `.Data.cs` | 15 |
| `SaveData()` | `.Data.cs` | 8 |
| `RecalculateLayout()` | `.Layout.cs` | 8 |
| `_dialogService` | `.Commands.cs` | 12 |
| `_isLoadingData` | `.cs` | 6 |

少なくとも private メンバーの直接参照は、共通基盤を中心にしたハブ&スポーク型に見える。
ただし、この集計は public プロパティ・コマンド・コレクション変更・イベント順序による
間接的な結合を測っていない。228 個の public プロパティと 69 個の ICommand を持つ以上、
「十分に疎結合」とまでは断定しない。

現時点で partial 分割の方針を全面変更する必要はないが、
各機能の変更時には対象 partial の特性テストを先に置く。

### View と ViewModel の役割分担は概ね明示されている

View コードビハインドは 5,866 行ある。`DataContext as MainViewModel` という構文は
`NotificationHost.xaml.cs:24` の 1 箇所だけだが、これは VM 直接参照の総数を表さない。
次の View は `(MainViewModel)DataContext` を保持し、多数のプロパティ・コマンド・メソッドを直接使う。

- `MainWindow.xaml.cs:24`
- `DayWeekView.xaml.cs:22`
- `TimelineView.xaml.cs:32`
- `TodoPanel.xaml.cs:34`

また、各 View は VM のイベントも購読している。直接参照そのものは MVVM 違反ではなく、
ドラッグ、ヒットテスト、フォーカス、スクロール、トレイ連携など View 固有の処理を
コードビハインドが担当する構成には合理性がある。

`VisualTreeHelper` 依存も 3 ファイル（Drag / InlineEdit / TodoPanel）に限られる。
したがって全面的な View 整理は不要だが、「直接参照が 1 箇所なので境界が守られている」
という評価は採らず、View 変更時に UI 処理と業務ロジックの混在を個別に確認する。

### その他

- 60 行を超えるメソッドは 18 個のみ。うち最長 2 つ（`InitializeCommands` 325 行、
  `InitializeTodoCommands` 206 行）はラムダの羅列で、実質的な複雑度は低い
- `TODO` / `HACK` / `FIXME` コメント **0 件**
- `SaveData()` はデバウンス（700ms）+ アトミック書き込み + 読み込み失敗時の保存停止まで実装済み

---

## A. 最優先 — テストが 1 行も存在しない

### 現状

- テストプロジェクト **0**（`.slnx` の登録は `TimeRenderer.csproj` のみ）
- xunit / NUnit / MSTest / Moq の参照 **0**
- そもそも `PackageReference` が **0**（MVVM も手書き）
- `CLAUDE.md` には「.NET SDK が無い」とあるが、現在の環境には
  .NET SDK 10.0.400 / 8.0.403 が導入されている
- 2026-08-27 に `TimeRenderer.slnx` をビルドし、警告 0・エラー 0 を確認した
  （通常のサンドボックス権限では Windows SDK の参照先を読めないため、権限付与が必要）

32,627 行のうち、日付境界・繰り返し規則・期間集計という
**間違えても画面上すぐには気づかない**種類のロジックが大きな割合を占める。
現状ここを守るものが何も無い。

### 効く理由 — 今すぐテストできるアルゴリズムロジックが約 1,500 行ある

`Helpers/` には、UI 操作や Dispatcher を必要としないロジックが揃っている。
既存の public API を使って、アプリ側の大きなリファクタリングなしにテストを始められる。

ただし「WPF 依存がゼロ」ではない。`TodoQuickParser` は `CategoryInfo`、
`UndoableEdits` などは `ScheduleItem` を経由して `System.Windows.Media` に依存する。
このためテストプロジェクトも Windows/WPF を参照する構成になる。

| ファイル | 行数 | 内容 |
| --- | --- | --- |
| `TodoQuickParser.cs` | 486 | `@ ! # ~ *` 記法の解釈。分岐が多く回帰しやすい |
| `SprintHelper.cs` | 209 | スプリント期間の自動補間 |
| `UndoableEdits.cs` | 207 | 取り消し操作の適用・巻き戻し |
| `TimelineLaneHelper.cs` | 126 | レーン割り当て |
| `UndoManager.cs` | 109 | 履歴スタック |
| `UnrecordedGapHelper.cs` | 77 | 未記録区間の検出 |
| `ScheduleLayoutHelper.cs` | 68 | 重なり予定の列割当 |
| `TimelineScale.cs` | 66 | ズーム倍率と目盛り |
| `MagnetSnapHelper.cs` | 62 | 吸着位置の決定 |
| `NoteTagParser.cs` | 56 | メモのタグ抽出 |

`ScheduleLayoutHelper` と `SprintHelper` は前回レビューで
「問題なしと確認した箇所」に挙がっているが、**静的レビューで確認しただけ**で、
その後の 7 倍の変更を通してもなお正しい保証は無い。

### 進め方

1. `TimeRenderer.Tests` を追加（`net10.0-windows` + WindowsDesktop/WPF 参照 —
   `UndoableEdits` が扱う `ScheduleItem` が `Brush` を持つため。D 項参照）
2. `TodoQuickParser` と `SprintHelper` から着手。この 2 つで約 700 行
3. `.slnx` に登録
4. 後続のリファクタリングごとに、その対象を守る特性テストを追加する

最初の 2 クラスのテストはテスト基盤の立ち上げには有効だが、
設定、統計、ToDo のリファクタリングを直接安全にするものではない。
それぞれ B / E 項に記す対象別テストを追加してから変更する。

**アプリ本体の振る舞いを変えずに始められる。着手順として最もコスパが良い。**

---

## B. 設定追加が 3 箇所同時更新を要求する

### 現状

`CLAUDE.md` に、こう書かれている。

> 設定項目の追加時は AppSettings / BuildSettings / ApplySettings の 3 箇所をセットで更新

`MainViewModel.Data.cs:166` にも同じ注意書きがある。

| 箇所 | 規模 |
| --- | --- |
| `Models/AppSettings.cs` | フラットなプロパティ 45 個 |
| `BuildSettings()`（`.Data.cs:177`） | 45 行の代入 |
| `ApplySettings()`（`.Data.cs:236`） | **174 行** |

### 問題

**設計で解決すべきことを、運用ルールで補っている。**
3 箇所のどれか 1 つを書き忘れても **コンパイルは通る**。
現れる症状は「設定が保存されない」「再起動で戻る」というサイレントな不具合で、
テストが無い現状では気づく手段が無い（A 項と直結する）。

45 項目 × 3 箇所 = 135 の同期点。今後も設定は増える。

さらに `ApplySettings` は 174 行の中に
「代入」「`Math.Clamp` による範囲検証」「`Enum.IsDefined` による妥当性検証」
「`OnPropertyChanged` の発火」が混在していて、1 メソッドとして読みにくい。

### 方向性（案）

- まず設定の保存→読み込み、既定値、不正な enum・範囲値、旧形式を対象にした
  特性テストを追加する
- 範囲検証と既定値補正を、専用の `AppSettings` 正規化処理へ抽出する。
  `DisplayStartHour` と `DisplayEndHour` のような複数項目にまたがる制約があるため、
  DTO の個別 setter だけへ分散させない
- `BuildSettings` / `ApplySettings` は、機能単位の小さなマッピングへ分割するか、
  設定を正とする状態保持へ段階移行する

VM が `AppSettings` インスタンスを直接保持する案は、単純な値型の設定には有効。
ただし現在は `ObservableCollection` と `List` の変換、ID の存在確認、パネルの排他、
テーマ適用、旧データ移行も `BuildSettings` / `ApplySettings` の周辺で行っている。
直接保持だけで `BuildSettings` が丸ごと不要になるわけではなく、
コレクション変更の追跡と副作用の置き場所も同時に設計する必要がある。

一度に全 45 項目を移さず、対象機能の往復テストを追加してから段階移行する。

### 2026-08-27 対応結果

- 既定値、旧形式、不正な enum・範囲値、JSON 保存往復のテストを追加した
- 補正処理を `Services/AppSettingsNormalizer.cs` へ分離した
- 45項目の保存と復元を `MainViewModel.SettingsMapping.cs` の共通 binding 一覧へ統合した
- `BuildSettings` と `ApplySettings` は同じ binding を順方向／逆方向に実行するだけにした
- `AppSettingsMappingContractTests` で、全 public プロパティが重複なく登録されていることを検証する

これにより、設定追加時の更新点は `AppSettings` と対応する binding になり、
binding の追加を忘れた場合はテストと起動時検証の両方で検出される。

---

## C. SaveSettings は同期保存 — デバウンスの要否は実測して判断

### 現状

`SaveData()` は 700ms のデバウンス付き（`.Data.cs:422`）。
一方 `SaveSettings()`（`.Data.cs:170`）は `SettingsService` を介して
`JsonFileRepository.SaveToFileSync` を呼び、**デバウンス無し・同期・UI スレッド・
毎回 45 項目全書き込み**。

呼び出しは 15 ファイルに分散し、大半がプロパティ setter の中にある。

`MainViewModel.MiniBar.cs:46` の `SaveMiniRecordingBarPosition()` も保存を呼ぶが、
`MiniRecordingBar.xaml.cs:154` の `DragMove()` が終了し、位置変更を確認した後に 1 回だけ実行される。
ドラッグ中のマウス移動ごとに書いているわけではない。

### 問題

同期 I/O が UI スレッドで行われるため、設定変更が短時間に連続する経路では
応答性へ影響する可能性がある。ただし、現状の設定操作の多くはチェック、選択、
ボタン操作などの離散操作で、ミニバーも 1 ドラッグにつき 1 回である。

したがって `SaveData` と同じ頻度問題が存在するとまでは確認できていない。
設定ファイルのサイズ、保存時間、連続呼び出し回数を計測して優先度を決める。

### 方向性

計測で問題が確認できた場合は、`SaveData` と同じデバウンス機構を
`SaveSettings` にも適用する。通常終了だけでなく、ウィンドウをトレイへ隠す経路でも
`FlushDataSave` / `FlushTodoSave` と対になる `FlushSettingsSave` を呼ぶ。

変更時は「最後の設定が保存される」「連続変更が 1 回にまとまる」
「トレイへ隠す／終了する前に確定する」のテストを用意する。
現時点では小規模な最適化候補であり、B より優先しない。

### 2026-08-27 実測結果

現在の `appsettings.json` は 4,881 バイト。元ファイルを変更しない隔離コピーで、
現行と同じ一時ファイル書き込み＋`File.Replace` を 250 回計測した結果は、
平均 1.973ms、中央値 1.877ms、95%点 2.248ms、最大 12.752ms だった
（JSON シリアライズ時間は含まない）。

連続入力になりうるタイムラインのズームは、すでに 600ms の遅延保存を持つ。
カテゴリ名とプロジェクトコード名は `UpdateSourceTrigger=LostFocus` で、その他の設定は
主にチェック・選択・ボタン操作である。このため、現時点では全設定を対象にした
デバウンスは追加しない。保存時間や呼び出し経路が増えたときに再計測する。

---

## D. INotifyPropertyChanged のボイラープレートが複数クラスに重複

### 現状

`INotifyPropertyChanged` の実装型は **9 個**ある。
うち 7 個は `PropertyChanged` / `OnPropertyChanged` / `SetProperty<T>` の
ほぼ同じ実装を持ち、残る 2 個も単一プロパティ向けの簡略版を持つ。

| クラス | 位置 | 形 |
| --- | --- | --- |
| `MainViewModel` | `MainViewModel.cs:657` | 共通形 |
| `ScheduleItem` | `Models/ScheduleItem.cs:315` | 共通形 |
| `TodoItem` | `Models/TodoItem.cs:812` | 共通形 |
| `CategoryInfo` | `Models/CategoryInfo.cs:88` | 共通形 |
| `GitRepositoryInfo` | `Models/GitRepositoryInfo.cs:96` | 共通形 |
| `ProjectCodeInfo` | `Models/ProjectCodeInfo.cs:76` | 共通形 |
| `TodoSubtask` | `Models/TodoSubtask.cs:49` | 共通形 |
| `TitleEntry` | `MainViewModel.Titles.cs:19` | 簡略形 |
| `TimelineBar` | `ViewModels/TimelineBar.cs:79` | 簡略形 |

加えて、手書きの `OnPropertyChanged(nameof(...))` が ViewModels 配下に **165 箇所**。

前回レビューの **B-5 で `CommunityToolkit.Mvvm` の導入が提案され、
唯一未対応のまま残っている**項目。当時より該当箇所は大幅に増えた。

### 判断が要る点

現在 `PackageReference` が 0 個。外部依存を持たない構成を意図して選んでいるなら、
導入は方針転換になる。**ここは好みの問題なので、判断を委ねる。**

- **導入する場合**: `[ObservableProperty]` / `[NotifyPropertyChangedFor]` で、
  通常の setter とその依存通知を削減できる。`RelayCommand`（21 行の自作）も置換可能。
  ただし `ApplySettings`、コレクション更新後の一括通知、任意メソッドからの通知は
  設定適用方式まで変えない限り残るため、165 箇所が自動的にほぼ消えるわけではない
- **導入しない場合**: 最低限、共通の `ObservableObject` 基底クラスを 1 つ自作して
  7 箇所の同型実装を減らす。ViewModel 専用ではない中立的な基底型として置くか、
  継承を増やさず現状を維持するかは要検討

### 2026-08-27 対応結果

外部依存を増やさず、中立的な `Infrastructure/ObservableObject.cs` を追加した。
`MainViewModel`、`ScheduleItem`、`TodoItem`、`CategoryInfo`、`GitRepositoryInfo`、
`ProjectCodeInfo`、`TodoSubtask` の同型実装をこの基底クラスへ移した。
同値代入では通知しないこと、呼び出し元のプロパティ名、既存の依存プロパティ通知を
テストで固定している。`CommunityToolkit.Mvvm` は、ソース生成による削減効果と
外部依存導入の釣り合いを改めて評価できる規模になるまで見送る。

### 補足 — Model が WPF に依存している

`ScheduleItem` は `System.Windows.Media.Brush` を状態として保持し、`TodoItem` / `CategoryInfo` は
`ColorCode` から表示用 Brush を返す。`ScheduleSegment` は元アイテムの Brush を公開し、
`ItemSnapshot` も Brush を保持する。`RoutineScheduleItem` は Brush 自体を保持しないが、
既定の `ColorCode` を作るため `Brushes` を参照する。
`Services/FilePersistenceService.cs` にも `Brushes` 参照がある。

`net10.0-windows` の単一アプリなので実害は小さいが、
A 項のテストプロジェクトも WPF 参照が必要になる。
色は `ColorCode`（文字列）を正とし、`Brush` は Converter 側で作る形にすれば
Model が純粋になる — が、**優先度は低い**。

---

## E. MainViewModel.Todos.cs が 1,606 行

partial 中の最大ファイル。中身は `// ===== ... =====` で
**すでに 9 つの関心事に自分で区切られている**。

| 区切り | おおよその位置 |
| --- | --- |
| パネルの状態 | :44 |
| クイック追加の記法 | :134 |
| 件数の表示 | :200 |
| コマンド | :233 |
| サブタスク | :530 |
| 変更の監視 | :640 |
| 手動並べ替え | :839 |
| 記録との連動 | :971 |
| 予定への割り当て（タイムブロッキング） | :1005 |

分割線はもう見えているので、`.Todos.cs` / `.Todos.Subtasks.cs` /
`.Todos.Sorting.cs` / `.Todos.TimeBlocking.cs` などへ機械的に分けられる。
ただし、A 項で最初に挙げた `TodoQuickParser` のテストだけではこの分割を保護できない。
並べ替え、サブタスク、記録連動、タイムブロッキングの特性テストを対象ごとに追加してから分ける。
**急がないが、次に ToDo 機能へ手を入れるときが好機。**

`UpdateStats()`（`.Stats.cs:304`・249 行）も同様で、
カテゴリ集計 / プロジェクトコード集計 / 月次タイムシート行列 の
3 つが 1 メソッドに同居している。カテゴリ別・プロジェクト別・月次の入力と期待値を
固定する特性テストを先に置けば、分割の効果が大きい。

---

## F. 細かい重複

### 曜日 → 和名のマッピングが 5 箇所

| 場所 | 形 |
| --- | --- |
| `Converters/DateTimeHelper.cs:19` | `switch` 式 |
| `Helpers/TodoQuickParser.cs:113` | `string[]`（日曜始まり） |
| `Models/RoutineScheduleItem.cs:166` | `Dictionary` |
| `Models/TodoItem.cs:575` | `Dictionary` |
| `ViewModels/MainViewModel.cs:641` | `switch` 式 |

`WeekOrder`（月〜日の `DayOfWeek[]`）も
`RoutineScheduleItem.cs:158` と `TodoItem.cs:567` で二重定義。

まとめるのは比較的簡単。ただし日曜始まり／月曜始まり、表示用途、解析用途の違いがあるため、
各呼び出し元の出力テストを置いてから共通化する。A 項のついでに片付けられる。

### 繰り返しルールが 2 系統ある

- `RecurrenceType`（`RoutineScheduleItem`）: Weekly / MonthlyByDate / MonthlyByWeekday
- `TodoRecurrenceUnit`（`TodoItem`）: Day / Week / Month

要件が違う（定期予定は「第 1・第 3 月曜」まで扱う、ToDo は「完了日起点」を持つ）ので
**無理に統合しなくてよい**。ただし `RecurrenceDisplay` の
文字列組み立てだけは似た処理が並んでいる。

---

## 推奨する順序

| 順 | 内容 | 規模 | リスク |
| --- | --- | --- | --- |
| 1 | テストプロジェクト追加 → `TodoQuickParser` / `SprintHelper` | 中 | ほぼ無し（アプリ本体の振る舞いは不変） |
| 2 | 曜日和名・`WeekOrder` の出力テスト追加 → 一元化（F） | 小 | 低 |
| 3 | 設定の往復・不正値・旧形式テスト追加（B） | 中 | 低 |
| 4 | 設定の正規化とマッピングを段階整理（B） | 大 | 中（3 の後にやる） |
| 5 | 統計 / ToDo の対象別特性テスト → `UpdateStats` / `.Todos.cs` の分割（E） | 中 | 中 |
| 6 | `SaveSettings` の頻度と所要時間を計測し、必要ならデバウンス化（C） | 小 | 低〜中 |
| 7 | MVVM ライブラリ導入の可否を判断（D） | 大 | 方針判断 |

テストプロジェクトが存在するだけでは、未テストの機能の変更リスクは下がらない。
4 は 3、5 は同じ行に記した対象別テストを完了条件とする。
1 と 2 は独立していて、いつでも着手できる。

---

## 手をつけなくてよいもの

- partial 分割の全面的な方針変更 — 現時点では必要性を示す根拠がない。
  ただしファイル分割だけで疎結合とは判断せず、変更対象の特性テストを先に置く
- View / コードビハインドの全面的な MVVM 化 — 直接 VM 参照は複数あるが、
  多くはドラッグ、ヒットテスト、フォーカス、スクロール、トレイ連携など View 固有の処理。
  機能変更時に業務ロジックの混入を個別確認すればよい
- DI コンテナの導入 — 単一ウィンドウのアプリで、`IDialogService` の
  コンストラクタ注入で足りている。`ActiveWindowTracker` と `AwayDetector` が
  VM 内で直接 `new` されている（`.AppUsage.cs:129` / `.Away.cs:129`）が、
  テストで差し替えたくなったときに初めてインターフェース化すればよい
- 静的サービス（`JsonFileRepository` / `FilePersistenceService` / `SettingsService`）の
  インスタンス化 — 同上。テストが必要になった時点で判断する

---

※ 2026-08-27、.NET SDK 10.0.400 で `TimeRenderer.slnx` をビルドし、
警告 0・エラー 0 を確認した。通常のサンドボックス権限では Windows SDK の参照先を
読めないため、ビルドには適切な権限が必要。
