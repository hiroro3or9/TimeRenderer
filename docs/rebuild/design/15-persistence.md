# 15. 永続化・データ保全・ログ

## 1. 問題意識（守るべき理由）

- `File.WriteAllText` は既存ファイルを切り詰めてから書く。途中で落ちると JSON が壊れる／0バイトになる
- 以前は「読めない＝初回起動」と誤認してサンプルデータを入れ、次の保存で実データを消していた
- → **アトミック書き込み**・**バックアップ**・**読込失敗を区別して保存を止める**、の3点を必ず守る

## 2. JsonFileRepository（static）

```csharp
public enum LoadStatus { NotFound, Loaded, RecoveredFromBackup, Failed }
public sealed class LoadResult<T> {
    public required LoadStatus Status { get; init; }
    public T? Value { get; init; }
    public string? SourceFile { get; init; }   // 実際に読んだファイル名
    public string? Message { get; init; }      // 復旧・失敗の説明
    public bool IsUsable => Status is LoadStatus.Loaded or LoadStatus.RecoveredFromBackup;
}
```

- `DataDirectory` = `Path.Combine(Environment.GetFolderPath(SpecialFolder.ApplicationData), "TimeRenderer")`
- JSON オプション：`new JsonSerializerOptions { WriteIndented = true }`
- パス：本体 `{name}`、直前世代 `{name}.bak`、一時 `{name}.tmp`、日次スナップショット `{name}.yyyy-MM-dd.bak`（7世代保持）

### 保存 `SaveToFileSync<T>(fileName, data)` → `SaveToDirectorySync(DataDirectory, ...)`
```
Directory.CreateDirectory(dir)
json = Serialize(data)
File.WriteAllText(tmp, json)            // ここで失敗しても本体は無傷
if (File.Exists(target)) {
    CreateDailySnapshotIfNeeded(target)  // 今日のスナップショットが無ければ target をコピー → 古い世代を削除（失敗は無視）
    ReplaceFile(tmp, target, bak)        // File.Replace(tmp, target, bak, ignoreMetadataErrors:true)
                                         // 例外時フォールバック：File.Copy(target, bak, true) → File.Delete(target) → File.Move(tmp, target)（退避を必ず先に）
} else File.Move(tmp, target)
return true
catch → Debug 出力、ファイルごとにセッション中1回だけ MessageBox：
  「データの保存に失敗しました: {fileName}\n{ex.Message}」タイトル「TimeRenderer - 保存エラー」(Error) → false
```
- 古いスナップショットの削除：`{name}.*.bak` のうち日付部分が `yyyy-MM-dd` として解釈できるものを文字列降順に並べ、8件目以降を削除
- `internal SaveToDirectorySync(dir, name, data, notifyOnFailure=true)` はテストで保存先を差し替えるための経路

### 読込 `LoadFromFileSync<T>(fileName)`
1. 旧バージョン移行：`DataDirectory\{name}` が無く、exe と同じフォルダ（`AppDomain.CurrentDomain.BaseDirectory`）に同名ファイルがあればコピー（失敗は無視）
2. `LoadFromDirectorySync<T>(DataDirectory, name)`：
   - 本体もバックアップ（`.bak` と日次スナップショット）も無い → `NotFound`
   - 本体を `TryRead` → 成功 `Loaded`
   - 失敗なら `.bak` → 日次スナップショット（新しい順）を順に `TryRead`。成功 → `RecoveredFromBackup`、Message「{name} を読み込めなかったため、バックアップ {候補ファイル名} から復元しました。\n理由: {最初のエラー}」
   - 全滅 → `Failed`、Message「{name} とそのバックアップをいずれも読み込めませんでした。\n理由: {最初のエラー}」
- `TryRead`：ファイル無し「ファイルがありません」／空白のみ「ファイルが空です」（**空は壊れている扱い**）／Deserialize が null「内容を解釈できませんでした」／例外は ex.Message

## 3. FilePersistenceService（static）

| ファイル | 保存 | 読込 | 読込時の扱い |
| --- | --- | --- | --- |
| `schedules.json`（`IEnumerable<ScheduleItem>`、**仮想アイテムは除外して渡す**） | `SaveData` | `LoadData()` → `ScheduleLoadResult(ObservableCollection<ScheduleItem> Items, LoadStatus Status, string? Message)` | NotFound→サンプルデータ、Loaded/Recovered→値、Failed→空＋Failed |
| `workdays.json`（`List<WorkDayLog>`） | `SaveWorkDays` | `LoadWorkDays()` | 失敗は空。`StartTime == default` を除外し StartTime 昇順 |
| `appusage.json`（`List<AppUsageInterval>`） | `SaveAppUsage` | `LoadAppUsage()` | 失敗は空。`End>Start && End >= 今日-60日 && ProcessName非空` を Start 昇順 |
| `todos.json`（`List<TodoItem>`） | `SaveTodos` | `LoadTodos()` → `TodoLoadResult(List<TodoItem> Items, LoadStatus Status, string? Message)` | タイトル空白の行を除外。Failed なら Items は空 |
| `todos-archive.json`（`List<TodoItem>`） | `SaveTodoArchive` | `LoadTodoArchive()` | 失敗は空。タイトル空白を除外 |

各メソッドに `internal ...ToDirectory(string dataDirectory, ...)` / `...FromDirectory(...)` 版を用意（テスト用）。`LoadAppUsageFromDirectory(dir, today)` は基準日も受ける。

サンプルデータ（NotFound 時のみ。今日=T）：
| Kind | Title | 時間 | Content | 色 |
| --- | --- | --- | --- | --- |
| Planned | 朝会 | T 9:00〜9:30 | 定例 | LightBlue |
| Planned | 週次レビュー | 明日 14:00〜15:30 | 進捗確認 | LightGreen |
| Recorded | 顧客訪問 | 昨日 10:00〜12:00 | 直行 | LightPink |
| Planned | 重複会議A | T 10:00〜11:00 | 重複テスト | Orange |
| Planned | 重複会議B | T 10:30〜11:30 | 重複テスト | Purple |

## 4. SettingsService（static）

`appsettings.json`。`SaveSettings(AppSettings)` = `SaveToFileSync`、`LoadSettings()` = `LoadFromFileSync<AppSettings>().Value`（null なら既定のまま）。

## 5. ViewModel 側の保存制御

### 予定データ（MainViewModel.Data.cs）
```csharp
static readonly TimeSpan DataSaveDebounceInterval = 700ms, DataSaveRetryInterval = 30s;
void SaveData() {
    if (!_isInitialized || _isLoadingData) return;
    if (IsDataLoadFailed) return;              // 壊れたファイルを空で確定させない
    _hasPendingDataSave = true;
    timer.Stop(); timer.Interval = 700ms; timer.Start();   // Tick で FlushDataSave
}
public void FlushDataSave() {
    timer?.Stop(); if (!_hasPendingDataSave) return;
    if (FilePersistenceService.SaveData(ScheduleItems.Where(i => !i.IsVirtual))) { _hasPendingDataSave = false; return; }
    timer.Interval = 30s; timer.Start();       // ディスク不足などは保留したまま再試行
}
```
確定させるタイミング：タイマー満了、ウィンドウを閉じる（トレイへ隠すときも）、アプリ終了時（`OnClosed`）。

### 読込結果の反映
- `LoadData()`：`_isLoadingData=true` の間に既存アイテムの購読解除 → Clear → 移行処理しつつ Add → false。`ApplyLoadStatus` → `ClearUndoHistory()` → `RecalculateLayout()`
- `ApplyLoadStatus`：
  - RecoveredFromBackup：`IsDataLoadFailed=false`, `DataNotice=Message`
  - Failed：`IsDataLoadFailed=true`, `DataNotice = (Message ?? "予定データを読み込めませんでした。") + "\nデータを保護するため、このセッションでは保存を停止しています。" + "\nデータフォルダのバックアップ（.bak）を確認してください。"`
  - それ以外：false / null
- ToDo も同様（`IsTodoLoadFailed`、Failed 文言は「ToDoデータを読み込めませんでした。」「…このセッションではToDoの保存を停止しています。」）。`DataNotice` が既にあれば `"\n\n"` で連結（`AppendDataNotice`）
- `HasDataNotice`、`DismissDataNoticeCommand`（DataNotice=null。保存停止は解除しない）、`OpenDataFolderCommand`（`Process.Start(new ProcessStartInfo{FileName=DataDirectory, UseShellExecute=true})`）
- 表示は `NotificationHost` の継続通知（`design/04`）

### ToDo
700ms デバウンス＋30秒再試行（`ScheduleTodoSave` / `FlushTodoSave`）。条件：`_isInitialized && !_isLoadingTodos && !IsTodoLoadFailed`。

### 勤務記録・使用アプリ・設定
- 勤務記録：変更のたびに即保存（`_workDaysLoaded` が true になるまで保存しない）
- 使用アプリ：5分ごと・記録開始/停止・退勤・終了時に全件保存
- 設定：変更のたびに同期保存（実測 2ms 程度のためデバウンス不要。タイムラインのズームだけ 600ms 遅延）

## 6. CrashLogService（static）

- `LogDirectory` = `%LOCALAPPDATA%\TimeRenderer\Logs`、ファイル `application-yyyy-MM-dd.log`（UTF-8 追記）
- `WriteLifecycle(message)`（LIFECYCLE）、`WriteException(source, ex, isTerminating)`（FATAL/ERROR）、`WriteUnhandledObject(source, obj, isTerminating)`（Exception でなければ「{source}: 非 Exception オブジェクト: {obj ?? "null"}」）
- 1件の形式：
  ```
  [{DateTimeOffset.Now:O}] [{LEVEL}] {message}
  ProcessId: {pid}
  AppVersion: {EntryAssembly Version or "unknown"}
  Runtime: {Environment.Version}
  {exception.ToString()（あれば）}
  (空行)
  ```
- `System.Threading.Lock` で排他。書き込み後、最終更新日が30日より前の `application-*.log` を削除。**すべての失敗を握りつぶす**（ログの失敗で本来の例外処理を妨げない）

## 7. テスト観点（フェーズ3）

- 保存→読込の往復で値が一致、`.tmp` が残らない
- 2回目の保存で `.bak` と当日スナップショットができる
- 本体を壊すと `.bak` から `RecoveredFromBackup`、`.bak` も壊すとスナップショットから、全部壊すと `Failed`
- 空ファイルは Failed 扱い、ファイルが1つも無ければ NotFound
- workdays の壊れ行除外、appusage の保持期間フィルタ、todos のタイトル空除外
