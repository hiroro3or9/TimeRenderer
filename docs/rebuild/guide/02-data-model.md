# 02. データの持ち方

## 予定と実績は同じ型

このアプリの中心は `ScheduleItem` です。**予定（これからやること）と実績（やったこと）を別の型にせず、種別のフラグで区別します**。記録を止めたときに予定をそのまま実績へ変える、という操作が主役になるので、型が違うと毎回作り直すことになるからです。

持っているのは、識別子、種別（予定 / 実績）、タイトル、内容、開始・終了時刻、終日かどうか、色、カテゴリ Id、プロジェクトコード Id、そして「どこから来たか」を示す紐づけ（定期予定の Id、元になった予定の Id、ToDo の Id）です。予定だけが持つ設定として、開始時刻に通知するか、自動で記録を開始するか、記録中でも強制的に開始するかがあります。

画面用の派生プロパティ（表示色のブラシ、所要時間、ツールチップ文言、重なり計算の列番号、選択中かどうか、仮想アイテムかどうか）は保存しません。JSON に出ないよう明示的に除外します。**保存されるプロパティ名と enum の数値は変えないでください**。既存データがそのまま読めるかどうかがここで決まります。

「仮想アイテム」は定期予定から自動生成された、まだファイルに存在しない予定です。触られた瞬間に実体化します（`11` 参照）。

## 色とカテゴリの二重管理

カテゴリは「名前の付いた色」です。アイテムはカテゴリ Id を持ちますが、**Id が無い古いデータのために色コードからの逆引きも残します**。つまりカテゴリの解決は「Id で引く → 見つからなければ色で引く → それでも無ければ未分類」の順です。この解決はレイアウト・統計・タイムラインのループから何度も呼ばれるので、辞書を作ってキャッシュし、カテゴリが変わったときだけ捨てます。

カテゴリには表示フィルタのフラグもありますが、これは**保存しません**。セッション内だけの絞り込みです（起動するたびに全部見える状態から始めたい）。

プロジェクトコードは案件単位の集計キーで、コード・名前・有効/無効を持ちます。「未設定」を表すために Id が空文字の特別なインスタンスを1つ用意しておき、コンボボックスの先頭に並べます。モデル側の null と UI 側の空文字を行き来させる小さなコンバーターも要ります。既定のプロジェクトコードは「未指定（先頭の有効なコードを使う）」と「明示的に未設定（コードを付けない）」を区別する必要があるので、null と空文字で意味を分けます。

## ToDo は派生プロパティの塊

`TodoItem` は保存する項目（タイトル、メモ、期限、通知日時、通知の相対指定、今日やる日、優先度、カテゴリ、色、見積もり分、記録済み時間、繰り返し設定、サブタスク、並び順、完了フラグと完了日時）に対して、表示用の派生プロパティが大量にぶら下がります。「期限の表示（今日/明日/N日超過）」「進捗の表示」「繰り返しの説明文」「ツールチップ」などです。

ここで効いてくるのが**通知の連鎖**です。期限を変えたら、期限の表示・超過かどうか・今日が期限か・ツールチップがまとめて変わります。setter ごとに関連する通知をまとめたメソッド（期限系・進捗系・繰り返し系・サブタスク系）を呼ぶようにしておくと、通知漏れで画面が古いまま、という事故が防げます。

「今日やる」は真偽値ではなく**日付**（`PlannedOn`）で持ちます。その日付が今日と一致するときだけ「今日やる」として扱うので、日をまたげば自動的に外れます。退勤時の「明日へ送る」は、この日付を翌日に書き換える操作です（`10`）。

完了フラグの setter だけは順序に注意が要ります。**完了日時を先に入れてから**通知します。逆にすると、通知を受けた側が「完了なのに完了日時が無い」状態を見ます。

繰り返しは「完了したときに次回分を作る」方式です。次回の期限は、既定では**元の期限から数え**、設定で「完了日から数える」にも切り替えられます。長期間放置されても過去日を返さないよう、経過日数・経過月数から次の発生を直接計算します。

## 定期予定のテンプレート

`RoutineScheduleItem` は「毎週月曜 10:00-10:30 の朝会」のようなテンプレートです。繰り返しの種類は3つあります。

- 曜日指定（毎週・N週ごと）
- 日付指定（毎月N日・Nヶ月ごと。存在しない日は末日へ繰り下げ）
- 第N曜日指定（第1・第3月曜、最終金曜など）

これに「開始日」「除外日の一覧」「N回に1回休む」が乗ります。除外日は、生成された予定を個別に編集・削除したときに増えていきます。発生判定のロジックは純粋関数にして、テストで固めてください（`11`）。

## 勤務記録・離席・使用アプリ

勤務記録は1日1件で、出勤時刻・退勤時刻・退勤をどう入れたか（手動 / 自動締め / 離席検知）・その日のひとことを持ちます。退勤が空なら勤務中です。

離席は「いつからいつまで、なぜ（無操作 / スリープ / ロック）」の記録ですが、**保存しません**。記録を停止したときに「この時間を除くか」を聞くための一時的な情報です。

使用アプリの履歴は「いつからいつまで、どのプロセスが前面にいたか」で、こちらは保存します。ただし**ウィンドウタイトルを残すのは記録中だけ**です。タイトルにはファイル名や URL が出るので、記録していない時間の中身まで残すのは踏み込みすぎだ、という判断です。

## 計算で出すもの、保存しないもの

未記録の帯（勤務時間のうち記録が無い区間）は保存しません。表示するたびに勤務記録と実績から計算します。状態として持つと、アイテムを編集するたびに実態とずれて信用できなくなります。

同じ理由で、git のコミット履歴も保存しません。必要になった瞬間に読み直します。

## 設定は1枚のクラスと1本の対応表

設定は `AppSettings` というプレーンなクラス1枚（46項目）に集約し、`appsettings.json` に保存します。読み込んだ値は必ず**正規化**を通します。範囲外の数値は既定値へ、未定義の enum 値は0へ、null のリストは空リストへ。ユーザーが手で書き換えたファイルや、古いバージョンのファイルでも起動できるようにするためです。

ViewModel との間は、**バインディング定義の一覧**でつなぎます。項目ごとに「設定へ書き出す関数」と「設定から読み込んで VM へ入れる関数」を1組にして、配列に並べるだけです。読み込み時は**プロパティの setter を通さず**バッキングフィールドへ直接入れます（setter を通すと、読み込んだ瞬間に保存が走ります）。

この形にしておくと、**設定の追加漏れをテストで検出できます**。`AppSettings` の全プロパティが一覧にちょうど1回ずつ現れることを確認する契約テストを1本書いてください。設定項目は増え続けるので、「追加したのに保存されない」を人間の注意力で防ぐのは無理です。

一覧の並び順にも意味があります。カテゴリはカテゴリ既定値より先、プロジェクトコードは既定コードより先に適用します。マスターが揃う前に「そのうちのどれか」を選ぼうとしても解決できません。全部の適用が終わった後に、マスターが揃って初めてできる処理（定期予定の生成、期間割り当ての取り込みなど）をまとめて実行します。

## 保存形式（互換のために守る名前と値）

ガイド版で表を載せているのはここだけです。**名前と数値はこのとおりにしてください**。変えると、元アプリのデータ（`%APPDATA%\TimeRenderer`）をコピーしても読めません。

### 共通の書き方

- `System.Text.Json` の既定（`WriteIndented = true` だけ指定）。命名ポリシーも `JsonPropertyName` も使わないので、**C# のプロパティ名がそのまま JSON のキー**になる
- enum は**数値**で保存する（文字列にしない）。`DayOfWeek` も数値（日曜=0 … 土曜=6）
- 日時は `DateTime` をそのまま（`2026-09-24T10:00:00` 形式）、時刻だけのものは `TimeSpan`（`"10:00:00"`）
- 色は `#AARRGGBB` の文字列（`Brush.ToString()` の形）
- 派生プロパティは `[JsonIgnore]` で除く。下の表に無いプロパティが JSON に出ていたら除外漏れ

### ファイル

| ファイル | 中身 |
| --- | --- |
| `schedules.json` | `ScheduleItem` の配列（仮想アイテムは含めない） |
| `todos.json` / `todos-archive.json` | `TodoItem` の配列（現役 / アーカイブ） |
| `workdays.json` | `WorkDayLog` の配列 |
| `appusage.json` | `AppUsageInterval` の配列 |
| `appsettings.json` | `AppSettings` 1件 |

バックアップは `<ファイル名>.bak`、日次スナップショットは `<ファイル名>.yyyy-MM-dd.bak`、書き込み途中の一時ファイルは `<ファイル名>.tmp`。

### モデルごとの保存項目

| 型 | 保存するプロパティ |
| --- | --- |
| `ScheduleItem` | `Id`, `Kind`, `SourcePlanId`, `Title`, `StartTime`, `EndTime`, `IsAllDay`, `Content`, `CategoryId`, `ProjectCodeId`, `RoutineId`, `TodoId`, `RemindAtStart`, `AutoStartRecording`, `ForceStartRecording`, `ColorCode` |
| `TodoItem` | `Id`, `Title`, `Content`, `DueDate`, `RemindAt`, `RemindOffsetDays`, `PlannedOn`, `Priority`, `IsCompleted`, `CompletedAt`, `CategoryId`, `ColorCode`, `RecordedTicks`（`long` の Ticks）, `EstimatedMinutes`, `SortOrder`, `Recurrence`, `RecurrenceInterval`, `RecurrenceDaysOfWeek`, `RecurrenceFromCompletion`, `Subtasks`, `CreatedAt` |
| `TodoSubtask` | `Id`, `Title`, `IsCompleted` |
| `WorkDayLog` | `Date`, `StartTime`, `EndTime`（null＝勤務中）, `EndSource`, `Note` |
| `AppUsageInterval` | `Start`, `End`, `ProcessName`, `AppName`, `WindowTitle`, `IsWindowTitleSpecific`（読み取り専用の `Duration` も書き出されるが、読み込みでは使わない） |
| `CategoryInfo` | `Id`, `Name`, `ColorCode` |
| `ProjectCodeInfo` | `Id`, `Code`, `Name`, `IsActive` |
| `RoutineScheduleItem` | `Id`, `Title`, `Recurrence`, `Interval`, `DaysOfWeek`, `DayOfMonth`, `WeeksOfMonth`, `SkipEvery`, `SkipIndex`, `StartDate`, `StartTime`（`TimeSpan`）, `EndTime`（`TimeSpan`）, `CategoryId`, `ProjectCodeId`, `ColorCode`, `IsAutoStart`, `IsForceStart`, `IsEnabled`, `ExcludedDates` |
| `SprintInfo` | `Id`, `Name`, `StartDate`, `EndDate`, `IsManual`, `UnrecordedTimeProjectCodeId`（旧形式。読み込み時の移行にだけ使う） |
| `GitRepositoryInfo` | `Id`, `Path`, `Name`, `IsEnabled`, `ProjectCodeId` |
| `UnrecordedTimeProjectAssignment` | `StartDate`, `ProjectCodeId` |

補足：

- `Id` は `Guid.NewGuid().ToString("N")`（ハイフンなし32桁）。読み込んだ `ScheduleItem` の `Id` が空なら採番し直す
- `RoutineScheduleItem.WeeksOfMonth` は 1〜5 が第1〜第5週、**-1 が最終週**
- `RoutineScheduleItem.SkipEvery` は 0 で休みなし、N（2以上）で「N回に1回休む」。`SkipIndex` は休む回の位置（1〜N）
- `TodoItem.RemindOffsetDays` は通知の相対指定（期限の何日前か）。null なら `RemindAt` を絶対日時として扱う。
  相対指定のときも `RemindAt` には計算結果を入れておき、通知の判定は常に `RemindAt` だけを見る（期限を動かしたら `RemindAt` を計算し直す）

### enum の数値

| enum | 値 |
| --- | --- |
| `ScheduleItemKind` | `Legacy`=0, `Planned`=1, `Recorded`=2 |
| `RecurrenceType`（定期予定） | `Weekly`=0, `MonthlyByDate`=1, `MonthlyByWeekday`=2 |
| `TodoPriority` | `Low`=0, `Normal`=1, `High`=2 |
| `TodoRecurrenceUnit` | `None`=0, `Day`=1, `Week`=2, `Month`=3 |
| `WorkEndSource` | `Manual`=0, `AwayDetected`=1, `AutoClosed`=2 |

`ScheduleItemKind.Legacy`（0）は種別を保存していなかった旧データです。読み込み時に一度だけ分類し直します：終日・定期予定由来・開始時通知あり・自動開始あり・終了が現在より後のどれかなら予定、それ以外は実績。

### AppSettings

設定の中の「モード」は enum ではなく **int** で保存しています。

| プロパティ | 型 | 既定値 | 値の意味 |
| --- | --- | --- | --- |
| `IsSettingsPanelVisible` / `IsManagementPanelVisible` / `IsTodoPanelVisible` | bool | false | |
| `ShowCompletedTodos` | bool | false | |
| `TodoSortMode` | int | 0 | 0 期限順 / 1 優先度順 / 2 追加順 / 3 手動 |
| `IsTodoDigestEnabled` | bool | true | 朝のまとめ |
| `TodoDigestHour` | int | 9 | |
| `LastTodoDigestDate` | string? | null | `yyyy-MM-dd` |
| `TodoArchiveRetentionDays` | int | 90 | |
| `IsTodoQuickSyntaxEnabled` | bool | true | |
| `TodoDefaultRemindHour` | int | 9 | |
| `IsTodoReminderSoundEnabled` | bool | true | |
| `TodoSnoozeMinutes` | int | 10 | |
| `ViewMode` | int | 7 | 0 日 / 1 週 / 2 月 / 3 スプリント / 4 タイムライン / 5 統計 / 6 ふりかえり / 7 今日（後から足したので末尾） |
| `DisplayStartHour` / `DisplayEndHour` | int | 0 / 24 | 0〜23 / 1〜24 |
| `IsDarkMode` | bool | false | |
| `TimelinePixelsPerDay` | double | 120 | 8〜960 |
| `TimelineGroupMode` | int | 0 | 0 詰める / 1 カテゴリ別 / 2 1件1行 |
| `TimelineSprintCount` | int | 5 | |
| `IsAwayDetectionEnabled` | bool | true | |
| `AwayThresholdMinutes` | int | 10 | |
| `AwayHandlingMode` | int | 0 | 0 毎回確認 / 1 常に除外 / 2 常にそのまま |
| `IsWorkEndDetectionEnabled` | bool | true | |
| `WorkEndThresholdMinutes` | int | 30 | |
| `WorkEndEarliestHour` | int | 17 | 0 は制限なし |
| `IsWorkEndReviewEnabled` | bool | true | |
| `IsAppUsageTrackingEnabled` | bool | true | |
| `IsMiniRecordingBarEnabled` | bool | true | |
| `MiniRecordingBarLeft` / `MiniRecordingBarTop` | double? | null | null は「まだ動かしていない」 |
| `IsGitCommitLookupEnabled` | bool | true | |
| `GitRepositories` | List | 空 | `GitRepositoryInfo` |
| `SnapMinutes` | int | 15 | |
| `IsMagnetSnapEnabled` | bool | true | |
| `ManualSprints` | List | 空 | `SprintInfo` |
| `Categories` | List | 空 | 空なら既定のカテゴリを使う |
| `RecordingCategoryId` | string? | null | |
| `ProjectCodes` | List | 空 | 空なら既定値を使う |
| `DefaultProjectCodeId` | string? | null | null＝未指定、空文字＝明示的に未設定 |
| `IsUnrecordedTimeProjectAggregationEnabled` | bool | false | |
| `UnrecordedTimeProjectCodeId` | string? | null | 期間割り当ての最初の行より前に使う加算先 |
| `UnrecordedTimeProjectAssignments` | List? | null | null＝旧形式から未移行、空＝ユーザーが全部消した |
| `PinnedTitles` | List\<string\>? | null | null＝既定値を使う |
| `RoutineSchedules` | List | 空 | `RoutineScheduleItem` |
| `EnabledDaysOfWeek` | List\<DayOfWeek\> | 全曜日 | |
