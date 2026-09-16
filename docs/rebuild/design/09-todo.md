# 09. ToDo

予定（時刻を持つ）とは別の「やること」。期限日を持つものだけが日/週の終日行・月セルに並ぶ。ToDo から記録を始めると停止時に時間が積算される。

## 1. TodoItem の挙動（Models/TodoItem.cs）

### 1.1 setter の連鎖
| setter | 追加処理 |
| --- | --- |
| `Title` | 通知 ToolTipText |
| `Content` | 通知 HasContent, ToolTipText |
| `DueDate`（`.Date`化） | `RefreshRelativeReminder()` → `NotifyDueStateChanged()` |
| `RemindAt` | 通知 HasReminder, RemindDisplay, ToolTipText |
| `RemindOffsetDays`（0〜365） | `RefreshRelativeReminder()`、通知 RemindDisplay, ToolTipText |
| `PlannedOn`（`.Date`化） | 通知 IsPlannedToday, ToolTipText |
| `Priority` | 通知 IsHighPriority, IsLowPriority, ToolTipText |
| `IsCompleted` | 値が同じなら何もしない。**先に `CompletedAt = value ? DateTime.Now : null`** → 通知 IsCompleted → `NotifyDueStateChanged()` |
| `ColorCode`（空は無視） | 通知 Brush |
| `RecordedTicks`（負→0） | 通知 RecordedDuration, HasRecorded, RecordedDisplay, ShowRecordedOnly → `NotifyProgressChanged()` |
| `EstimatedMinutes`（0〜10080） | `NotifyProgressChanged()` |
| `Recurrence` / `RecurrenceInterval`(1〜99) / `RecurrenceFromCompletion` | `NotifyRecurrenceChanged()` |
| `RecurrenceDaysOfWeek`（null→[]） | 常に通知＋`NotifyRecurrenceChanged()` |
| `Subtasks`（null→[]） | 常に通知＋`NotifySubtasksChanged()` |

```csharp
private void RefreshRelativeReminder()
{
    if (_remindOffsetDays is not { } offset) return;
    if (_dueDate is not { } due) { _remindOffsetDays = null; OnPropertyChanged(nameof(RemindOffsetDays)); RemindAt = null; return; }
    var time = _remindAt?.TimeOfDay ?? TimeSpan.FromHours(Math.Clamp(DefaultRemindHour, 0, 23));
    RemindAt = due.Date.AddDays(-offset) + time;   // 時刻は保ったまま日付だけずらす
}
```
- `NotifyDueStateChanged()`：HasDueDate, IsOverdue, IsDueToday, IsPlannedToday, DueDisplay, ToolTipText
- `NotifyProgressChanged()`：HasEstimate, ShowRecordedOnly, EstimatedDuration, EstimateDisplay, ProgressPercent, IsOverEstimate, ProgressDisplay, ToolTipText
- `NotifyRecurrenceChanged()`：HasRecurrence, RecurrenceDisplay, ToolTipText
- `NotifySubtasksChanged()`（public）：HasSubtasks, SubtaskTotalCount, SubtaskDoneCount, AreAllSubtasksDone, SubtaskProgressText, SubtaskProgressPercent, ToolTipText

### 1.2 派生表示（すべて [JsonIgnore]）
| プロパティ | 定義 |
| --- | --- |
| `HasContent` / `HasDueDate` / `HasRecorded` / `HasReminder` | 空白でない / DueDate あり / Ticks&gt;0 / RemindAt あり |
| `HasSubtasks`, `SubtaskTotalCount`, `SubtaskDoneCount`, `AreAllSubtasksDone`（1件以上かつ全完了） | |
| `SubtaskProgressText` | 「{done}/{total}」（無ければ空） |
| `SubtaskProgressPercent` | done/total*100 |
| `ShowRecordedOnly` | HasRecorded && !HasEstimate |
| `RemindDisplay` | RemindAt 無し→空。Offset 0「当日 HH:mm」、1「前日 HH:mm」、&gt;1「{n}日前 HH:mm」、null `M/d HH:mm` |
| `IsHighPriority` / `IsLowPriority` | |
| `IsOverdue` | 未完了 && DueDate &lt; `DateTime.Today` |
| `IsDueToday` | 未完了 && DueDate == Today |
| `IsPlannedToday` | 未完了 && PlannedOn == Today |
| `DueDisplay` | 期限なし空。差分日数 0「今日」、1「明日」、-1「昨日」、負「{n}日超過」、他 `M/d (ddd)` |
| `RecordedDisplay` | HasRecorded なら `H:MM` |
| `HasEstimate`, `EstimatedDuration`, `EstimateDisplay`(`H:MM`) | |
| `ProgressPercent` | 見積もりありなら 記録分/見積分*100 を 0〜100 に Clamp |
| `IsOverEstimate` | 見積もりあり && 記録 &gt; 見積 |
| `ProgressDisplay` | 「{記録H:MM} / {見積H:MM}」 |
| `HasRecurrence` / `HasRecurrenceDays`（Week かつ曜日あり） | |
| `RecurrenceDisplay` | 下記 |
| `ToolTipText` | 下記 |

`RecurrenceDisplay`：None→空。unit=日/週/ヶ月。Interval 1→「毎日」(Day)/「毎{unit}」、2→「2日ごと」(Day)/「隔{unit}」、他「{n}{unit}ごと」。曜日指定ありなら「 {曜日}」を付ける（7曜日すべてなら「毎日」、他は月曜順に「・」区切り）。`RecurrenceFromCompletion && !曜日指定` なら末尾「（完了日から）」。

`ToolTipText`（`\n` 区切り）：タイトル（空なら「(無題)」）／期限あり→「期限 yyyy/MM/dd (ddd)」（超過なら「（{DueDisplay}）」を付ける）／今日やる→「今日やる」／通知→「通知 yyyy/MM/dd (ddd) HH:mm」＋相対なら「（期限の当日）」「（期限の前日）」「（期限の{n}日前）」／優先度が標準以外→「優先度: 高」「優先度: 低」／サブタスク→「サブタスク {progress}」／繰り返し→「繰り返し: {display}」／見積もり→「実績 {ProgressDisplay}」（超過なら「（見積もり超過）」）、見積もり無しで記録あり→「記録済み {RecordedDisplay}」／メモ→空行＋60文字まで。

### 1.3 繰り返しの次回分
```csharp
public TodoItem? CreateNextOccurrence(DateTime completedOn)
{
    if (!HasRecurrence) return null;
    var baseDate = (RecurrenceFromCompletion ? completedOn : DueDate ?? completedOn).Date;
    var next = FindNextOccurrence(baseDate, completedOn.Date);
    return new TodoItem {
        Title, Content, DueDate = next, RemindAt = ShiftReminder(next), RemindOffsetDays, Priority, CategoryId, ColorCode,
        EstimatedMinutes, Recurrence, RecurrenceInterval, RecurrenceDaysOfWeek = [.. RecurrenceDaysOfWeek],
        RecurrenceFromCompletion, SortOrder,
        Subtasks = [.. Subtasks.Select(s => new TodoSubtask { Title = s.Title })],   // 手順は引き継ぎ、完了は外す
    };
}
DateTime FindNextOccurrence(DateTime baseDate, DateTime notBefore)
{
    if (HasRecurrenceDays) return FindNextWeekday(baseDate, notBefore);
    if (RecurrenceFromCompletion) return AddRecurrenceIntervals(baseDate, 1);
    return Recurrence == Month ? FindNextMonthlyOccurrence(baseDate, notBefore) : FindNextDayBasedOccurrence(baseDate, notBefore);
}
DateTime FindNextDayBasedOccurrence(DateTime baseDate, DateTime notBefore)
{   // 何年放置されても過去日を返さないよう、経過日数から直接求める
    var intervalDays = Recurrence == Day ? RecurrenceInterval : 7 * RecurrenceInterval;
    var elapsed = Math.Max(0, (notBefore - baseDate).Days);
    var count = Math.Max(1, elapsed / intervalDays);
    var next = baseDate.AddDays((long)count * intervalDays);
    return next <= notBefore ? next.AddDays(intervalDays) : next;
}
DateTime FindNextMonthlyOccurrence(DateTime baseDate, DateTime notBefore)
{   // 元の期限日をアンカーにする（1/31→2/28 の次を 3/28 にしない）
    var elapsedMonths = Math.Max(0, (notBefore.Year - baseDate.Year) * 12 + notBefore.Month - baseDate.Month);
    var count = Math.Max(1, elapsedMonths / RecurrenceInterval);
    var next = AddRecurrenceIntervals(baseDate, count);
    return next <= notBefore ? AddRecurrenceIntervals(baseDate, count + 1) : next;
}
DateTime FindNextWeekday(DateTime baseDate, DateTime notBefore)
{
    var anchorWeek = StartOfWeek(baseDate);                // 月曜始まり
    var from = baseDate > notBefore ? baseDate : notBefore;
    for (var day = from.AddDays(1); day <= from.AddDays(400); day = day.AddDays(1)) {
        if (!RecurrenceDaysOfWeek.Contains(day.DayOfWeek)) continue;
        if (RecurrenceInterval > 1 && ((StartOfWeek(day) - anchorWeek).Days / 7) % RecurrenceInterval != 0) continue;
        return day;
    }
    return AddRecurrenceIntervals(baseDate, 1);
}
DateTime AddRecurrenceIntervals(DateTime baseDate, int count)
{
    if (Recurrence == Month) {
        var first = new DateTime(baseDate.Year, baseDate.Month, 1).AddMonths(checked(RecurrenceInterval * count));
        var day = Math.Min(baseDate.Day, DateTime.DaysInMonth(first.Year, first.Month));
        return new DateTime(first.Year, first.Month, day, baseDate.Hour, baseDate.Minute, baseDate.Second, baseDate.Millisecond, baseDate.Kind);
    }
    var intervalDays = Recurrence == Day ? RecurrenceInterval : 7 * RecurrenceInterval;
    return baseDate.AddDays((long)intervalDays * count);
}
DateTime? ShiftReminder(DateTime nextDue) => RemindAt is not { } r ? null
    : DueDate.HasValue ? nextDue.Date + (r - DueDate.Value.Date) : nextDue.Date + r.TimeOfDay;
```

## 2. クイック追加の記法（Helpers/TodoQuickParser.cs）

`static int DefaultRemindHour = 9`、`const string SyntaxHint = "@明日 ｜ !高 ｜ #カテゴリ ｜ ~30m ｜ *9:00"`。

```csharp
public sealed class TodoQuickParseResult {
    public required string Title { get; init; }
    public DateTime? DueDate, RemindAt; public int? RemindOffsetDays; public TodoPriority? Priority;
    public CategoryInfo? Category; public int? EstimatedMinutes;
    public required IReadOnlyList<string> Summary { get; init; }
    public bool HasAttributes => Summary.Count > 0;
}
public static TodoQuickParseResult Parse(string? input, IReadOnlyList<CategoryInfo>? categories, DateTime now)
```

### 2.1 正規表現（GeneratedRegex。記号は「行頭か空白の直後」だけで効く＝`Head`）
```csharp
const string Head = @"(?:^|(?<=[\s　]))";
Due      = Head + @"[@＠](?:(?<ymd>\d{4}/\d{1,2}/\d{1,2})|(?<md>\d{1,2}/\d{1,2})|\+(?<plus>\d{1,3})(?<plusunit>[dwDW日週])?"
                + @"|(?<word>明後日|あさって|今週末|週末|再来週|来週|来月|今日|きょう|本日|明日|あした|あす|tomorrow|today)"
                + @"|(?<dow>[月火水木金土日])(?:曜日?)?)"                                  // IgnoreCase
Priority = Head + @"[!！](?:(?<jp>高|低|中|標準)|(?<en>high|normal|low|h|n|l)(?=$|[\s　#＃~〜@＠*＊]))"   // IgnoreCase
Category = Head + @"[#＃](?<v>[^\s　#＃!！~〜@＠*＊]{1,20})"
Estimate = Head + @"[~〜](?:(?<h>\d{1,3}(?:\.\d{1,2})?)(?:時間|h)(?:(?<hm>\d{1,2})(?:分|m)?)?|(?<m>\d{1,4})(?:分|m)"
                + @"|(?<mbare>\d{1,4})(?=$|[\s　#＃!！@＠*＊]))"                            // IgnoreCase
Remind   = Head + @"[*＊](?:(?<rel>当日|前日)|(?<nd>\d{1,2})日前|(?<ymd>\d{4}/\d{1,2}/\d{1,2})|(?<md>\d{1,2}/\d{1,2})"
                + @"|(?<word>明後日|あさって|今日|きょう|本日|明日|あした|あす)|(?<dow>[月火水木金土日])曜日?)?"
                + @"(?:(?<th>\d{1,2}):(?<tm>\d{2})|(?<th2>\d{1,2})時(?:(?<tm2>\d{1,2})分?)?|(?<th3>\d{1,2})(?=$|[\s　#＃!！~〜@＠]))?"
Space    = @"[ \t　]{2,}"
```

### 2.2 処理順
1. **期限**（Due を Replace）：1つ目だけ採用。解決できたら " " に置換、できなければ原文のまま（2つ目以降も原文）
   - ymd：`yyyy/M/d` を InvariantCulture で厳密解析／md：今年の月日。**今日より180日超前なら翌年**。月が1〜12外・存在しない日は失敗
   - plus：単位 w/W/週 なら n*7日、他 n日
   - word：今日/きょう/本日/today→今日、明日/あした/あす/tomorrow→+1、明後日/あさって→+2、来週→今週月曜+7、再来週→+14、来月→翌月1日、今週末/週末→今週の土曜（過ぎていれば来週）
   - dow：次に来るその曜日（**今日と同じ曜日なら7日後**）
2. **優先度**：jp 高→High、低→Low、中/標準→Normal。en high/h→High、low/l→Low、normal/n→Normal
3. **見積もり**：h（小数可）→`round(h*60)` ＋ hm 分／m・mbare → 分。0以下は原文のまま
4. **通知**：日付も時刻も無い裸の「*」は原文のまま。1つ目を控えて " " に置換
5. **カテゴリ**（categories が1件以上のときだけ、最後に処理）：`v` の長い方から前方一致で名前の完全一致（大文字小文字無視）を探し、一致した文字数だけ消費して残りは本文へ戻す（`" " + v[consumed..]`）。完全一致が無ければ `v` を名前の先頭に持つカテゴリが**1つだけ**なら採用。無ければ原文
6. 通知日時の解決（期限確定後）：
   - 時刻：`th:tm` / `th2時tm2分` / `th3` を 0〜23・0〜59 に Clamp。無ければ `DefaultRemindHour:00`
   - rel（当日=0/前日=1）/ nd（n日前）：期限が無ければ `今日+時刻`（相対なし）、あれば `期限-n日+時刻`、Offset=n
   - ymd/md/dow/word（今日/明日/明後日）で日付指定 → その日＋時刻（相対なし）
   - 日付指定なし：期限があれば `期限+時刻`（**Offset=0**）、無ければ `今日+時刻`、それが now 以前なら翌日
7. Summary（この順）：「期限 {M/d(曜)}」／「優先度 高|低|標準」／「カテゴリ {Name}」／「見積もり {n分|n時間|n時間m分}」／通知「通知 期限の当日 HH:mm」「通知 期限の前日 HH:mm」「通知 期限の{n}日前 HH:mm」「通知 {M/d(曜)} HH:mm」
8. Title＝連続空白を1つにして Trim

### 2.3 例
`資料作成 @明日 !高 #開発 ~30m *前日` → Title「資料作成」、期限=明日、High、カテゴリ「開発」、30分、通知=期限の前日 9:00（Offset 1）
`@明日資料をまとめる` → 期限=明日／Title「資料をまとめる」（後ろは続けて書ける）
`9時~10時 打ち合わせ` → `~` の前が空白でないので記法にならない
`#1 の対応` / `@会議室で相談` / `終わった!` → 本文のまま

## 3. ViewModel（MainViewModel.Todos*.cs）

### 3.1 状態
| プロパティ | 内容 |
| --- | --- |
| `Todos` | ObservableCollection（完了済み含む全件） |
| `VisibleTodos` | 表示用（絞り込み・並べ替え済み） |
| `TodoChips` | 日/週終日行のチップ |
| `IsTodoPanelVisible` | パネル排他（`design/04` §4） |
| `ShowCompletedTodos` | 変更で `RebuildVisibleTodos()` + SaveSettings |
| `TodoSortOptions` | 期限順 / 優先度順 / 追加順 / 手動 |
| `CurrentTodoSortMode` / `SelectedTodoSortOption` | 変更で再構築 + 保存 |
| `SelectedTodo` | 一覧の選択 |
| `NewTodoTitle` | 即時追加欄。変更で `UpdateQuickAddPreview()` |
| `IsTodoQuickSyntaxEnabled` | 変更でプレビュー更新、`TodoQuickAddHint` 通知、保存 |
| `TodoQuickAddHint` | 記法有効なら `SyntaxHint`、無効なら「やることを入力して Enter」 |
| `TodoQuickAddPreview` / `HasTodoQuickAddPreview` | 解釈結果「{title or （タイトルが空です）} ／ {Summary を「・」連結}」。記法無効・空・属性なしなら null |
| `TodoActiveCount`, `TodoOverdueCount`, `TodoCompletedCount`, `HasCompletedTodos`, `HasOverdueTodos` | |
| `TodoSummaryText` | 超過あり「未完了 {n} 件・期限超過 {m} 件」、なし「未完了 {n} 件」 |

`NotifyTodoCountsChanged()`：上記件数系を通知し `RebuildTodayWorkload()`。

### 3.2 コマンド
| コマンド | 動作 |
| --- | --- |
| `ToggleTodoPanelCommand` | 反転 |
| `AddQuickTodoCommand` | `BuildQuickTodo(NewTodoTitle)` が null でなければ `AddTodo` → NewTodoTitle="" |
| `AddTodoCommand` | `ShowTodoEditDialog(null, [..Categories], GetTitleSuggestions(), EstimateStats)` → AddTodo |
| `EditTodoCommand(todo)` | ダイアログ → before → `_isUpdatingTodo` の間に Title, Content, **RemindOffsetDays=null → DueDate → RemindAt → RemindOffsetDays**, Priority, CategoryId, ColorCode, EstimatedMinutes, Recurrence, RecurrenceInterval, RecurrenceDaysOfWeek, RecurrenceFromCompletion, Subtasks を書き戻す → `RecordTodoModify(…, "編集")` → `OnTodoChanged()` |
| `DeleteTodoCommand(todo)` | 確認「ToDo「{Title}」を削除しますか？」「削除確認」→ RecordTodoRemove → Remove → 記録中の ToDo なら紐づけ解除 |
| `ToggleTodoCompletedCommand(todo)` | `SetTodoCompleted(todo, !IsCompleted)` |
| `StartRecordingFromTodoCommand(todo)` | §9 |
| `SetTodoDueTodayCommand` / `SetTodoDueTomorrowCommand` / `ClearTodoDueCommand` | `SetTodoDue(todo, 今日 / 明日 / null)`：同じなら何もしない、Modify「期限の変更」 |
| `TogglePlannedTodayCommand(todo)` | PlannedOn が今日なら null、それ以外 今日。Modify「今日やるの取り消し」/「今日やる」 |
| `ClearCompletedTodosCommand`（CanExecute=HasCompletedTodos） | 確認「完了済みの ToDo {n} 件を削除しますか？\n（Ctrl+Z で元に戻せます）」「完了済みの削除」→ 1件ずつ直前のインデックスで RemoveTodoEdit を作って Remove → Composite |
| `StartRecordingFromTodoReminderCommand` | 通知から外して記録開始 |
| `CompleteTodoReminderCommand` | 完了にする（通知からも外れる） |
| `SnoozeTodoReminderCommand` | `SnoozeTodoReminder(todo, TodoSnoozeMinutes分)` |
| `DismissTodoReminderCommand` | 通知から外す |
| `MoveTodoUpCommand` / `MoveTodoDownCommand` | `MoveTodoBy(todo, ∓1)` |
| `FocusQuickAddTodoCommand` | パネルを開き `QuickAddTodoFocusRequested` 発火 |
| `DismissTodoDigestCommand` | TodoDigestNotice=null |
| `DismissTodoMissedNoticeCommand` | 見逃し一覧をクリア |
| `ShowMissedTodosCommand` | 見逃しの未完了1件目を探す → パネルを開き選択 → クリア |

`BuildQuickTodo(input)`：
- 記法無効：Trim が空なら null。先頭カテゴリの色/Id で Title だけの ToDo
- 記法有効：Parse → Title 空なら null。カテゴリ＝解釈結果 ?? 先頭。`Title, ColorCode, CategoryId, Priority ?? Normal, EstimatedMinutes ?? 0, DueDate, RemindAt, RemindOffsetDays` の順に初期化（相対を最後に）

`AddTodo(todo, record = true)`：`SortOrder = Todos.Count==0 ? 0 : Max(SortOrder)+1` → Add → record なら RecordTodoAdd。

`SetTodoCompleted(todo, completed)`：同じなら何もしない。before → `_isUpdatingTodo` の間に IsCompleted 設定と（完了時）`SpawnNextOccurrence` → 完了なら通知一覧から外す → Modify「完了」/「完了の取り消し」＋次回分の AddTodoEdit を `PushEdits(…, "ToDo「{Title}」の完了")` → OnTodoChanged。
`SpawnNextOccurrence(completed)`：`CreateNextOccurrence(CompletedAt ?? 今)` が null なら null。**完了した方の Recurrence を None にする**（付け直すたびに次回分が増えないように）→ `AddTodo(next, record:false)` → `ShowAutoStartNotice("「{Title}」の次回分（期限 {M/d}）を作成しました")`。

### 3.3 変更監視
- `OnTodosChanged`：追加分に PropertyChanged 購読、削除分は購読解除し PendingTodoReminders・見逃し一覧から除く（見逃しが0件になれば TodoMissedNotice=null）。読込中・Undo 適用中は return。それ以外 `OnTodoChanged()`
- `OnTodoPropertyChanged`：派生プロパティ（Brush, CompletedAt, DueDisplay, RecordedDisplay, RecordedDuration, HasRecorded, HasContent, HasDueDate, HasReminder, RemindDisplay, IsOverdue, IsDueToday, IsPlannedToday, HasRecurrenceDays, IsHighPriority, IsLowPriority, HasEstimate, EstimatedDuration, EstimateDisplay, ProgressPercent, ProgressDisplay, IsOverEstimate, HasRecurrence, RecurrenceDisplay, HasSubtasks, SubtaskTotalCount, SubtaskDoneCount, AreAllSubtasksDone, SubtaskProgressText, SubtaskProgressPercent, IsExpanded, NewSubtaskTitle, ToolTipText）は無視。IsCompleted が true になったら通知一覧から外す。`_isUpdatingTodo`・読込中・Undo 中は return。それ以外 `OnTodoChanged()`
- `OnTodoChanged()`：`RebuildVisibleTodos(); NotifyTodoCountsChanged(); InvalidateEstimateStats(); RecalculateLayout(); ScheduleTodoSave();`

## 4. 並べ替え（TodoOrderHelper / MainViewModel.Todos.Sorting.cs）

`RebuildVisibleTodos()`：`ShowCompletedTodos ? Todos : 未完了` を `TodoOrderHelper.Order(source, mode)`。

| モード | 並び（すべて「未完了→完了」「今日やるを先頭」が先） |
| --- | --- |
| DueDate | 期限あり→なし、期限昇順、優先度降順、CreatedAt |
| Priority | 優先度降順、期限昇順（なしは最後）、CreatedAt |
| Manual | SortOrder、CreatedAt |
| Created | CreatedAt |

手動並べ替え：
- `EnsureManualSort()`：手動でなければ今見えている順を `ApplyManualOrder` してから Manual に切り替え
- `ApplyManualOrder(ordered)`：`_isUpdatingTodo` の間に `TodoOrderHelper.ApplyManualOrder(Todos, ordered)`（表示順に 0,1,2… を振り、表示外の ToDo は元コレクション順に続きの番号）
- `MoveTodoTo(moved, target)`（ドラッグ）：before=`CaptureTodoOrder()` → EnsureManualSort → VisibleTodos のコピーで from を抜いて to に挿入 → Apply → `RecordTodoReorder(before)`（変化なしは積まない）→ OnTodoChanged
- `MoveTodoBy(todo, delta)`：同様。範囲外なら何もしない。最後に SelectedTodo=todo

## 5. サブタスク（MainViewModel.Todos.Subtasks.cs）

- `ToggleTodoExpanded(todo)`（static）：IsExpanded 反転
- `AddSubtask(parent)`：`NewSubtaskTitle.Trim()` が空なら何もしない → `ApplyToSubtasks(parent, "サブタスクの追加", () => TodoSubtaskHelper.Add(parent, title))` → NewSubtaskTitle="" → IsExpanded=true
- `RemoveSubtask(parent, sub)` / `ToggleSubtask(parent, sub)`：label「サブタスクの削除」/「サブタスクの完了」
- `ApplyToSubtasks(parent, label, change)`：before → `_isUpdatingTodo` の間に change()、**未完了かつ全サブタスク完了なら親を完了＋次回分生成** → 完了なら通知から外す → Modify(label)＋次回分 Add を「ToDo「{Title}」の{label}」→ OnTodoChanged
- `TodoSubtaskHelper`：Add（Trim、空なら null、Subtasks に追加し NotifySubtasksChanged）、Remove、ToggleCompleted（反転＋Notify）

## 6. 通知（MainViewModel.Todos.Notifications.cs / TodoReminderHelper）

- 定数：`TodoReminderGrace = 15分`、`TodoMissedWindow = 3日`
- `TodoReminderHelper.Classify(todo, now, grace, window)`：完了・RemindAt無し・now&lt;RemindAt → NotDue。遅れ≤grace → Due、≤window → Missed、それ以上 Expired
- `BuildKey(todo)` = `"{Id}|{RemindAt:O}"`（通知日時を変えれば別キー＝再通知される）
- `CheckTodoReminders(now)`：全 ToDo を Classify。NotDue 以外はキーを `_remindedTodoKeys` に追加（既にあれば skip）。Due なら `PendingTodoReminders` に追加し、`IsTodoReminderSoundEnabled` なら `SystemSounds.Asterisk.Play()`。Missed は集めて `AddMissedTodoReminders`。Expired は捨てる
- `BuildMissedNotice(list)`：1件「通知時刻を過ぎた ToDo があります：{title}」、複数「通知時刻を過ぎた ToDo が {n} 件あります（{先頭title} ほか）」
- `SnoozeTodoReminder(todo, delay)`：通知から外す → `RemindOffsetDays=null` → `RemindAt = 今 + delay`（相対指定は外れる）
- `SnoozeTodoReminderUntilTomorrow(todo)`：`RemindAt = 明日 + TodoDefaultRemindHour時`
- 設定：`TodoSnoozeMinutes`（1〜1440 に Clamp、選択肢 `[5,10,15,30,60,120]`）、`TodoSnoozeLabel = FormatSnoozeLabel(m)`（60の倍数かつ60以上「{h}時間後」、他「{m}分後」）、`IsTodoReminderSoundEnabled`、`TodoDefaultRemindHour`（0〜23、変更で `ApplyDefaultRemindHour()`、選択肢 0〜23）
- **朝のまとめ**：`IsTodoDigestEnabled`、`TodoDigestHour`（0〜23、選択肢 0〜23）、`_lastTodoDigestDate`（設定に `yyyy-MM-dd` で保存）
  - `TodoDigestHelper.ShouldRun(enabled, last, now, hour)` = enabled && last.Date != now.Date && now.Hour ≥ hour
  - `CheckTodoDigest(now)`：ShouldRun なら last=今日・SaveSettings → 今日期限の未完了数・超過数で `BuildNotice`：両方0→null、超過のみ「期限を過ぎた ToDo が {n} 件 あります。」、今日のみ「今日が期限の ToDo が {n} 件 あります。」、両方「今日が期限の ToDo が {a} 件、期限を過ぎた ToDo が {b} 件 あります。」
- `UpdateTodoTick(now)`（10秒ごと）：CheckTodoReminders → CheckTodoDigest → 日付が変わっていたら全 ToDo の `NotifyDueStateChanged()` → RebuildVisibleTodos → NotifyTodoCountsChanged → RecalculateLayout
- `GetVisibleTodosByDueDate()`：未完了・期限あり・`IsTodoVisible` を期限日ごとに、優先度降順→CreatedAt（月セル用）

## 7. 日/週の終日行チップ

`RebuildTodoChips(rangeStart, rangeEnd, allDayRowCounts)`：日/週モードでなければ空で0。未完了・期限が範囲内・`IsTodoVisible` を期限日でグループ化し、各日で優先度降順→CreatedAt に `TodoChip(todo, date, baseRow + i)`（baseRow=その日の終日イベント数）。戻り値は最大段数。

チップのテンプレート（DayWeekView 終日エリアの2枚目の Canvas）：
```
Border ChipRoot Height 22 Margin 2,1,2,0 CornerRadius 11 Padding 7,0 地 {SurfaceBrush} 枠 {Todo.Brush} 1.5 Hand
       ToolTip {Todo.ToolTipText} InitialShowDelay 250 MouseLeftButtonDown=TodoChip_MouseLeftButtonDown(ダブルクリックで編集)
  ContextMenu: 完了にする / この ToDo で記録開始 / ─ / 編集 / 期限を明日にする / 期限を外す / 削除
  横並び: ChipGlyph &#xE73A; 10px TextSecondary Margin 0,0,5,0 ｜ ChipText {Todo.Title} 11px 省略 TextPrimary
Triggers: MouseOver→地 Hover ／ Todo.IsOverdue→枠・文字・グリフ {TodoOverdueBrush} ／ Todo.IsHighPriority→枠 2.5
```

## 8. 保存・アーカイブ・見積もり傾向（Todos.Persistence.cs）

- `LoadTodos()`：`_isLoadingTodos` の間に旧購読解除・通知状態（Pending/キー/見逃し）クリア・Todos 入れ替え → `ApplyTodoLoadStatus` → `_archivedTodos = LoadTodoArchive()` → `ArchiveOldTodos()` → `InvalidateEstimateStats()` → `_lastTodoDueRefreshDate = 今日` → RebuildVisibleTodos → NotifyTodoCountsChanged → RecalculateLayout
- `TodoArchiveRetentionDays`（7〜3650、選択肢 `[30,60,90,180,365]`）
- `ArchiveOldTodos()`（起動時1回、読込失敗時はしない）：`TodoArchiveHelper.GetArchiveTargets(Todos, 今日, 日数)` = 完了済みかつ `CompletedAt.Date < 今日-日数`。0件ならファイルに触らない。対象を Todos から外してアーカイブへ → `SaveTodoArchive` → `SaveTodos`
- `EstimateStats`（遅延生成、`InvalidateEstimateStats()` で破棄）= `TodoEstimateStats.Build(Todos ∪ アーカイブ)`

`TodoEstimateStats`：
- 標本 = 完了済み && 見積もりあり && 記録あり、比 = 記録分 / 見積分
- カテゴリ別：CategoryId が空でない群のうち**3件以上**を中央値で `TodoEstimateAccuracy(SampleCount, Ratio)`
- 全体：3件以上なら中央値
- `For(categoryId)`：カテゴリ別があればそれ、無ければ全体、無ければ null
- `Display`：Ratio&lt;0.9「これまでは見積もりの約 {0.0} 倍で終わっています（{n} 件）」、&gt;1.1「これまでは見積もりの約 {0.0} 倍かかっています（{n} 件）」、他「これまではほぼ見積もり通りです（{n} 件）」。`IsOverrunning` = Ratio&gt;1.1

## 9. 記録・予定との連動

- `StartRecordingFromTodo(todo)`：記録中なら `StopRecording(() => StartRecordingFromTodo(todo))`。RecordingTitle=Title、色=ColorCode、カテゴリ=CategoryId ?? 色から解決、プロジェクト=既定、`_recordingSourceItem=null`、`_recordingTodo=todo` → `BeginRecording(false)`
- 停止時の積算は `design/10` §2
- `FindTodoById(id)`：空なら null、無ければ null
- `BlockTimeForTodo(todo, start)`（ToDo を日/週へドロップ）：長さ = 見積もりあり→見積もり、無し→1時間。`TodoTimeBlockHelper.CreateScheduleItem(todo, start, end, ResolveCategory(CategoryId, ColorCode)?.Id, DefaultProjectCode?.Id)`（Planned、Title、色、`CategoryId ?? resolved`、プロジェクト、`TodoId`）→ Add → RecordAdd → `ShowAutoStartNotice("「{Title}」を {M/d HH:mm} に {n 時間|n 分} で置きました")`（1時間以上は `{0.#} 時間`）
- `FillGapWithTodo(todo, start, end)`：同じく作り Kind=Recorded、Content=「記録時間: H:MM」。`_isUpdatingTodo` の間に Add と `AccumulateRecordedTime(todo, [(start,end)])` → Composite「「{Title}」で記録漏れを埋める」（Add＋「実績の追加」）→ OnTodoChanged
- `FillGapFromTodoPicker(gap)`：未完了が0件なら `ShowMessage("未完了の ToDo がありません。", "ToDo から埋める")`。`ShowTodoPickerDialog("{M/d (ddd) HH:mm} 〜 {HH:mm} の記録として残す ToDo を選んでください。", 未完了)` → FillGapWithTodo
- `TodoTimeBlockHelper.AccumulateRecordedTime(todo, segments)`：合計 ticks≤0 なら false、それ以外 `RecordedTicks += ticks` で true

## 10. 今日やるの積みすぎ検知（MainViewModel.Workload.cs）

`record WorkloadNotice(string Text, bool IsOver, string ToolTipText)`、`TodayWorkload`（変更で HasTodayWorkload 通知・RebuildTodayOverview）。

`RebuildTodayWorkload()`（出退勤・件数変化・10秒tick で呼ぶ）：
1. 勤務中でなければ null
2. `planned = Todos.Where(IsPlannedToday)`、0件なら null
3. 見積もりのある件数 counted が0なら null。必要時間 = Σ `max(0, 見積−記録)` × `EstimateStats.For(CategoryId)?.Ratio ?? 1.0`
4. 残り時間 = いつもの退勤オフセットが取れなければ null。`target = 出勤日0時 + オフセット`、`max(0, target - now)`
   - いつもの退勤オフセット：`EndSource == Manual` かつ End&gt;Start の勤務の `(End - Start.Date)` のうち 0&lt;o&lt;30時間 を昇順、**5件未満なら null**、中央値（偶数は平均）。深夜退勤を 26:00 として扱うため TimeOfDay ではなく経過時間
5. `IsOver = 残り ≤ 0 || 必要分 > 残り分 * 1.0`
6. Text「見込み {H:MM} ・ 退勤まで {H:MM}」（0以下は「0:00」）
7. ToolTip「今日やる {n} 件のうち、見積もりのある {m} 件の残りを合計し、これまでの実績（見積もりに対して実際どれだけかかったか）で補正しています。」＋（見積もり無しがあれば）「\n見積もりの無い {k} 件は含めていません。」＋「\n退勤時刻は、自分で押した退勤の中央値から見ています。」

## 11. ToDo パネル（Views/TodoPanel.xaml）

オーバーレイ（`design/04` §4、幅340）。`Border` 地 Surface・左枠1・ClipToBounds・影 ＞ `Grid Width=340` 5行（Auto/Auto/Auto/*/Auto）。

1. **ヘッダー** Border 地 Background Padding 10 下線：Grid［StackPanel［「ToDo」SemiBold TextPrimary ／ `{TodoSummaryText}` 11 TextSecondary Margin 0,2,0,0 ／ `{TodayWorkload.Text}` 11 Margin 0,3,0,0 ToolTip `{TodayWorkload.ToolTipText}` Visibility=HasTodayWorkload、既定 TextMuted、`IsOver` で Danger＋SemiBold］｜「×」IconButtonStyle 24×24 Top TextSecondary → ToggleTodoPanelCommand］
2. **即時追加** StackPanel Margin 10,10,10,6：
   - Border Height 34 CornerRadius 6 地 Background 枠 Border ＞ Grid［`&#xE710;` 12 TextSecondary Margin 8,0,4,0 ｜ `TextBox x:Name=QuickAddTextBox` `{NewTodoTitle, PropertyChanged}` 枠0 透明 13 KeyDown=QuickAddTextBox_KeyDown ＋ プレースホルダ「やることを入力して Enter」13 TextMuted Margin 2,0,0,0 IsHitTestVisible False（NewTodoTitle=="" で表示）］
   - `{TodoQuickAddHint}` 10 TextMuted Margin 4,4,0,0 省略 ToolTip「@ 期限／! 優先度／# カテゴリ／~ 見積もり／* 通知。解釈できない記号はそのまま本文に残ります」（プレビューがあるときは隠す）
   - Border CornerRadius 4 Padding 6,3 Margin 0,4,0,0 地 PrimarySubtle（プレビューがあるとき）＞ `{TodoQuickAddPreview}` 10 折返し TextSecondary
3. **絞り込み** Grid Margin 10,0,10,8：CheckBox「完了済みも表示」`{ShowCompletedTodos}` 12 TextSecondary ｜ ComboBox Width 110 12px `{TodoSortOptions}`/`{SelectedTodoSortOption}` DisplayMemberPath=Label ToolTip「並べ替え」
4. **一覧** `ListBox x:Name=TodoList` Margin 6,0,6,6 `{VisibleTodos}` SelectedItem⇔`SelectedTodo` 透明 枠0 横スクロール無効 PreviewKeyDown=TodoList_PreviewKeyDown。ItemContainerStyle：Padding/Margin 0、Stretch、テンプレートは ContentPresenter のみ（既定の選択色を消す）。項目：
   ```
   Border RowRoot CornerRadius 6 Margin 0,1 透明 Hand AllowDrop ToolTip {ToolTipText} InitialShowDelay 400
          MouseLeftButtonDown(ダブルクリック編集) PreviewMouseLeftButtonDown/PreviewMouseMove(ドラッグ開始) DragOver Drop
     ContextMenu: 編集 / この ToDo で記録開始 / ─ / 今日やる (T) / サブタスクを追加… / ─ / 期限を今日にする / 期限を明日にする / 期限を外す / ─ / 上へ移動 (Ctrl+↑) / 下へ移動 (Ctrl+↓) / ─ / 削除
     Grid 4列[Auto|Auto|*|Auto]
       0: Border 幅3 CornerRadius 2 Margin 2,4 地 {Brush}
       1: Button CheckButton 18×18 Padding 0 Margin 8,0,6,0 TodoCheckButtonStyle ToolTip「完了にする (Space)」→ ToggleTodoCompletedCommand
            > TextBlock CheckGlyph &#xE73E; 10px Opacity 0 TextSecondary
       2: StackPanel Margin 0,6,4,6
            横並び: PlannedGlyph &#xE735; 10px {TodoDueTodayBrush} Margin 0,0,4,0 (IsPlannedToday) ｜ TitleText {Title} 13 省略 TextPrimary
            横並び Margin 0,3,0,0:
              DueChip Border CornerRadius 3 Padding 5,1 Margin 0,0,6,0 地 MutedBackground (HasDueDate) > DueText {DueDisplay} 10 TextSecondary
              (HasReminder) &#xEA8F; 9px TextMuted Margin 0,0,3,0 + {RemindDisplay} 10 TextMuted, Margin 0,0,6,0
              (HasRecurrence) &#xE895; 9px + {RecurrenceDisplay} 10 TextMuted
              (IsHighPriority) 「優先度 高」10 {TodoHighPriorityBrush} Margin 0,0,6,0
              (ShowRecordedOnly) &#xE916; 9px + {RecordedDisplay} 10 TextMuted
              (HasEstimate) &#xE916; 9px + ProgressText {ProgressDisplay} 10 TextMuted
            ProgressBar(HasEstimate) Height 4 Margin 0,5,0,1 0〜100 {ProgressPercent} StatsBarStyle Foreground {Brush}
            (HasSubtasks) 横並び Margin 0,4,0,0: ExpandButton 16×16 IconButtonStyle TextSecondary ToolTip「サブタスクの表示を切り替える」> ExpandGlyph &#xE76C; 8px ｜ {SubtaskProgressText} 10 Margin 4,0,6,0 TextMuted ｜ ProgressBar Width 60 Height 4 {SubtaskProgressPercent} StatsBarStyle TextMuted
            SubtaskPanel (Collapsed) Margin 0,4,0,0:
              ItemsControl {Subtasks}: Grid[Auto|*|Auto] Margin 0,1
                 Button 14×14 TodoCheckButtonStyle Margin 0,0,6,0 Click=SubtaskCheck_Click > SubCheckGlyph &#xE73E; 8px Opacity 0
                 SubTitle {Title} 12 省略 TextPrimary ｜ Button「✕」18×18 IconButtonStyle TextMuted ToolTip「このサブタスクを削除」Click=SubtaskDelete_Click
                 IsCompleted → SubCheckGlyph Opacity 1, SubTitle 取り消し線・TextMuted
              TextBox SubtaskAddBox Margin 20,3,0,0 12px InputTextBoxStyle {NewSubtaskTitle, PropertyChanged} ToolTip「サブタスクを入力して Enter」KeyDown=SubtaskAddBox_KeyDown
       3: Button RecordButton &#xE768; 10px 26×26 Margin 0,0,4,0 Padding 0 Opacity 0 IconButtonStyle TextSecondary ToolTip「この ToDo で記録を開始する」→ StartRecordingFromTodoCommand
   Triggers（この順）:
     RowRoot.IsMouseOver → 地 Hover, RecordButton Opacity 1
     IsCompleted → CheckGlyph Opacity 1, TitleText 取り消し線・TextMuted, RowRoot Opacity .7
     未完了 && CheckButton.IsMouseOver → CheckGlyph Opacity .5
     IsDueToday → DueText {TodoDueTodayBrush} SemiBold
     IsOverdue → DueText {TodoOverdueBrush} SemiBold
     IsOverEstimate → ProgressBar/ProgressText {TodoOverdueBrush}
     IsExpanded → ExpandGlyph &#xE70D;, SubtaskPanel Visible
     ListBoxItem.IsSelected → RowRoot 地 {PressedBackgroundBrush}
   ```
5. **フッター** Border Padding 10 上線：「＋ 詳細を指定して追加」Base 12 左 ToolTip「期限・優先度・カテゴリを指定して追加します」→ AddTodoCommand ｜「完了済みを削除」Ghost 12 TextSecondary（HasCompletedTodos）→ ClearCompletedTodosCommand

### コードビハインド
- `Loaded`：`QuickAddTodoFocusRequested` 購読 → Dispatcher（Loaded）で QuickAddTextBox.Focus + SelectAll
- QuickAdd の Enter → AddQuickTodoCommand
- 一覧のキー（OriginalSource が TextBox なら無視、SelectedTodo が必要）：Space→完了切替、Enter→編集、Delete→削除、T（Ctrl なし）→今日やる、Ctrl+↑/↓→並べ替え（修飾なし↑↓は ListBox に任せる）
- ドラッグ：PreviewMouseLeftButtonDown で ButtonBase 内から始まった操作は除外（RowRoot まで遡る）、`SystemParameters.MinimumHorizontal/VerticalDragDistance` を超えたら `DragDrop.DoDragDrop(sender, new DataObject(typeof(TodoItem), todo), Move|Copy)`。行の DragOver は TodoItem なら Move、Drop で `MoveTodoTo(moved, target)`。日/週ビューへ落とすと時間ブロック（Copy）
- サブタスク：Expand→`ToggleTodoExpanded`、チェック/削除は親 ToDo を VisualTree で遡って `ToggleSubtask`/`RemoveSubtask`、追加欄の Enter→`AddSubtask`、メニュー「サブタスクを追加…」→ IsExpanded=true・選択 → Dispatcher で `SubtaskAddBox` を探してフォーカス

## 12. ToDo 編集ダイアログ（TodoEditDialog）

`Title="ToDo の編集"`（新規は「ToDo の追加」）`Height=910 Width=460 CenterOwner NoResize 地 Background`。`ResultTodo`。引数 `(TodoItem? existing, categories, titleSuggestions, TodoEstimateStats? stats)`。Title はコードで `existing == null ? "ToDo の追加" : "ToDo の編集"`。
Grid Margin 20、10行（0〜7 Auto、8 *、9 Auto）。1〜8 はそれぞれ `StackPanel Margin 0,0,0,16`（サブタスクとメモは VerticalAlignment Top、メモ行は下マージンなし）。コンボは FontSize 14 Padding 6,6（通知の行は 13）、DatePicker は Padding 4,4 VerticalContentAlignment Center。

1. 「タイトル」＋ TitleCombo（編集可 14px Padding 6,6 MaxDropDown 240）
2. 「期限」＋ 横並び［DatePicker Width 160 ｜「今日」56×30 Base Margin 8,0,0,0 ｜「明日」56×30 Margin 4 ｜「なし」56×30 ToolTip「期限を外す（一覧には残ります）」］
3. 「優先度」＋［「高」Left ｜「標準」Middle ｜「低」Right］
4. 「見積もり時間」＋ 横並び［EstimateCombo Width 140 DisplayMemberPath Label ｜ RecordedText 12 TextSecondary Margin 12,0,0,0（記録があれば「記録済み {RecordedDisplay}」）］＋ AccuracyText 11 折返し Margin 0,6,0,0（傾向があるときだけ。IsOverrunning なら文字色 `TodoHighPriorityBrush`、他 `TextSecondaryBrush` を SetResourceReference）
   - 見積もり選択肢：なし0 / 15分 / 30分 / 45分 / 1時間 / 1時間30分 / 2時間 / 3時間 / 4時間 / 6時間 / 8時間。既存値が無ければ「{n}分」を追加して昇順
5. 「繰り返し」＋ 横並び［RecurrenceCombo Width 110（しない/日ごと/週ごと/月ごと）｜「間隔」13 TextSecondary Margin 12,0,6,0 ｜ RecurrenceIntervalCombo Width 70（1〜12）］／ RecurrenceDaysPanel（週ごとのみ）横並び Margin 0,8,0,0［「曜日」＋ 月〜日 CheckBox Margin 0,0,6,0］／ CheckBox「完了した日から次回の期限を数える」Margin 0,8,0,0 ／ RecurrenceHint 11 折返し
   - 無効時：間隔・完了日基準を無効、ヒント非表示。曜日を1つでも選んだら完了日基準を無効
   - ヒント：曜日あり「完了すると、指定した曜日のうち次に来る日を期限にした次回分が作られます。」／他「完了すると次回分が自動で作られます。期限日から数えるので、遅れて完了しても曜日や日付はずれません。」
6. 「カテゴリ」＋ ColorCombo Width 200（項目 Border 20×20 CornerRadius 3 枠 #CCCCCC）。一致しない既存色は「（現在の色）」を追加。変更で AccuracyText 更新
7. 通知：下線 ／ CheckBox RemindCheckBox「通知する」Margin 0,0,0,8 ／ RemindPanel（IsEnabled=チェック）横並び［RemindTimingCombo Width 112 13px ToolTip「「期限の前日」などにすると、期限を変えたときに通知も一緒に動きます」｜ RemindDatePicker Width 122 Margin 8,0,0,0 ｜ RemindComputedText Width 122 13 TextSecondary（相対時のみ）｜ 時 Combo 60 Margin 8,0,0,0 ｜「:」Margin 4,0 16px Bold TextPrimary ｜ 分 Combo 60］＋ RemindHint 11 折返し
   - タイミング：日時を指定(null) / 期限の当日(0) / 期限の前日(1) / 期限の2日前(2) / 期限の3日前(3) / 期限の1週間前(7)
   - 分は5分刻み（既存の分が刻みに無ければ追加）
   - 初期値：既存 RemindAt、無ければ `(期限 ?? 今日) + DefaultRemindHour`。期限が無いときは相対を選べない（日時指定に倒す）
   - チェックを入れたとき日付が空なら期限（無ければ今日）で埋める
   - 相対：DatePicker を隠し、`期限 - n日` を `yyyy/M/d (ddd)` で表示
   - ヒント：相対＋期限あり「期限を変えると、通知日も一緒に動きます。」／相対＋期限なし「期限が未設定のため、この指定は使えません。期限を決めてください。」／日時指定「期限とは別に、思い出したい日時を指定します。「期限の前日」などにすると期限に追随します。」
   - 期限を外したら相対指定は日時指定に戻す
8. 「サブタスク」：ItemsControl MaxHeight 120（ScrollViewer テンプレート）各行 Grid［CheckBox `{IsCompleted}` ｜ TextBox `{Title, PropertyChanged}` InputTextBoxStyle 13 ｜「✕」24×24 IconButtonStyle ToolTip「このサブタスクを削除」］＋ 横並び［SubtaskAddBox Width 300 13 InputTextBoxStyle ToolTip「サブタスクを入力して Enter」｜「追加」60×30 Base］（**複製したリストを編集**し OK で返す。キャンセルで元に影響させない）
9. 「メモ」＋ ContentTextBox Height 90 折返し AcceptsReturn
10. 右寄せ Margin 0,16,0,0：「キャンセル」100×36 Margin 0,0,12,0 IsCancel Base ／「OK」100×36 IsDefault Primary

OK：タイトル空→ `MessageBox.Show("タイトルを入力してください。", "入力エラー", OK, Warning)` で中断。`ResultTodo = new TodoItem{ Id/CreatedAt/RecordedTicks/SortOrder/IsCompleted/CompletedAt/PlannedOn は既存を引き継ぐ, Title, Content, DueDate, RemindAt(チェック時のみ), RemindOffsetDays(チェックかつ期限ありのとき選択値), Priority, CategoryId, ColorCode, EstimatedMinutes, Recurrence, RecurrenceInterval, RecurrenceDaysOfWeek(週ごとのみ), RecurrenceFromCompletion, Subtasks(タイトル空を除く) }`。Loaded で TitleCombo にフォーカス。

## 13. ToDo 選択ダイアログ（TodoPickerDialog）

`Title="ToDo を選ぶ" Height=440 Width=400 CenterOwner NoResize`。`SelectedTodo`。Grid Margin 20 行[Auto|*|Auto]：MessageText 13 折返し Margin 0,0,0,12 TextPrimary ／ ListBox（地 Surface 枠 Border 1 横スクロール無効、ダブルクリックで確定）項目 Grid Margin 2,4［Border 幅3 CornerRadius 2 地 Brush Margin 0,1,8,1 ｜ Title 13 縦中央 省略 TextPrimary ｜ DueDisplay 10 縦中央 TextMuted Margin 8,0,4,0］／ 右寄せ Margin 0,16,0,0［「キャンセル」100×36 Margin 0,0,12,0 IsCancel Base ｜「OK」100×36 IsDefault Primary］。
先頭を選択した状態で開き、ListBox にフォーカス。未選択で OK →「ToDo を選んでください。」「選択エラー」。

## 14. テスト観点

- TodoQuickParser：各記法、全角記号、Head 制約、解釈できない記号が本文に残る、カテゴリの前方一致、月日の年送り、相対通知
- 繰り返し：日/週/月、完了日基準、曜日指定＋隔週、月末アンカー、長期放置で過去日にならない、通知の平行移動
- TodoOrderHelper、TodoReminderHelper（境界 15分・3日）、TodoDigestHelper、TodoArchiveHelper、TodoSubtaskHelper、TodoTimeBlockHelper
- VM 経由：クイック追加→Undo/Redo、完了＋次回分が1回で戻る、サブタスク全完了で親完了
