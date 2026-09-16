# 14. 取り消し/やり直し・選択・検索・表示フィルタ・複製/コピー

## 1. 取り消し/やり直し

### 1.1 方針
- 対象：予定アイテムの追加/削除/内容変更（編集ダイアログ・ドラッグ・インライン名称変更・記録停止）、ToDo の追加/削除/完了/内容変更/並べ替え/記録時間積算
- 対象外：設定・カテゴリ・スプリント・定期予定（テンプレート・除外日・自動生成）・勤務記録（何が戻るか予測しづらくなるため）
- 予定と ToDo で履歴は**1本**（2系統にすると今どちらが戻るか分からない）
- 復元は**元のインスタンスへ書き戻す**（複製で置き換えると選択状態や他の履歴の参照が食い違う）
- 変化のない編集は積まない（`IsSameAs`）。履歴は起動中のみ・上限100件

### 1.2 UndoableEdits.cs
```csharp
public sealed record UndoContext(ObservableCollection<ScheduleItem> Items, ObservableCollection<TodoItem> Todos);
public interface IUndoableEdit { string Description { get; } void Undo(UndoContext c); void Redo(UndoContext c); }
```
| クラス | Description | Undo | Redo |
| --- | --- | --- | --- |
| `AddItemEdit(item)` | 「{title}」の追加 | Items.Remove | 含まれていなければ Add |
| `RemoveItemEdit(item, index)` | 「{title}」の削除 | 含まれていなければ `Insert(clamp(index,0,Count))` | Remove |
| `ModifyItemEdit(item, before, after, label)` | 「{title}」の{label} | before.ApplyTo | after.ApplyTo |
| `AddTodoEdit(todo)` | ToDo「{title}」の追加 | Todos.Remove | 無ければ Add |
| `RemoveTodoEdit(todo, index)` | ToDo「{title}」の削除 | Insert | Remove |
| `ModifyTodoEdit(todo, before, after, label)` | ToDo「{title}」の{label} | ApplyTo | ApplyTo |
| `ChangeTodoRecordedTimeEdit(todo, beforeTicks, afterTicks, label)` | ToDo「{title}」の{label} | RecordedTicks=before | =after |
| `ReorderTodosEdit(before, after)` ((Todo, Order) のリスト) | ToDo の並べ替え | 各 SortOrder=before | =after |
| `CompositeEdit(edits, description)` | 指定 | **逆順**に Undo | 順に Redo |

タイトルは空白なら「(無題)」（`AddItemEdit.Describe` / `AddTodoEdit.Describe`）。

### 1.3 UndoManager
```csharp
const int MaxDepth = 100;
LinkedList<IUndoableEdit> _undo; Stack<IUndoableEdit> _redo;
public bool IsApplying { get; private set; }
public bool CanUndo => _undo.Count > 0; public bool CanRedo => _redo.Count > 0;
public string UndoDescription => CanUndo ? _undo.Last.Value.Description : "";
public string RedoDescription => CanRedo ? _redo.Peek().Description : "";
public event EventHandler? Changed;
Push(edit): if (IsApplying) return; AddLast; 100件を超えたら先頭から捨てる; _redo.Clear(); Changed
Undo(ctx): if (!CanUndo) return false; edit=Last; IsApplying=true; try { edit.Undo(ctx) } finally { IsApplying=false }
           // 成功後に移す（例外時は履歴に残す）
           RemoveLast; _redo.Push(edit); Changed; return true
Redo(ctx): 同様（Peek → Redo → Pop → AddLast）
Clear(): 両方空なら何もしない。それ以外 Clear して Changed
```

### 1.4 VM（MainViewModel.Undo.cs）
- `UndoTarget => new(ScheduleItems, Todos)`、`IsApplyingUndo => _undo.IsApplying`、`CanUndo`, `CanRedo`
- `UndoToolTip`：可能なら「元に戻す: {UndoDescription} (Ctrl+Z)」、不可「元に戻す (Ctrl+Z)」／`RedoToolTip`：「やり直す: {…} (Ctrl+Y)」／「やり直す (Ctrl+Y)」
- `InitializeUndo()`：`Changed` で CanUndo/CanRedo/UndoToolTip/RedoToolTip を通知
- `UndoCommand`（CanExecute=CanUndo）→ `_undo.Undo(UndoTarget)` 成功で `AfterUndoRedo()`／Redo 同様
- `AfterUndoRedo()`：`RecalculateLayout(); SaveData(); RebuildVisibleTodos(); NotifyTodoCountsChanged(); ScheduleTodoSave();`
- ヘルパー：`RecordAdd(item)`, `RecordRemove(item)`（**削除前に**インデックスを取る）, `RecordModify(item, before, label)`（変化なしは積まない）, `RecordTodoAdd/Remove/Modify`, `PushEdits(edits, description)`（0件なら何もしない、1件ならそのまま、複数なら Composite）, `PushRecordingEdits(title, segmentCount, edits)`（複数なら「「{title}」の記録（{n}区間）」）
- ドラッグ：`BeginItemDragUndo(item)`（item と Capture を控える）、`ClearItemDragUndo()`（控えと `_dragCopyClone` を捨てる）、`CommitItemDragUndo()`：
  - 控えが無ければクリアして終了
  - 複製ドラッグ（`_dragCopyClone` あり）：`[AddItemEdit(clone)]` ＋ 変化があれば `ModifyItemEdit(item, before, after, "時間の変更")` を `PushEdits(…, "「{title}」の複製")`
  - 通常：`RecordModify(item, before, "時間の変更")`
- `ClearUndoHistory()`：`_undo.Clear()` ＋ `ClearItemDragUndo()`（データ再読込でインスタンスが入れ替わるとき）

### 1.5 積む操作と label 一覧
| 操作 | 積み方 |
| --- | --- |
| ダイアログで追加 | AddItemEdit |
| 削除 | RemoveItemEdit |
| 編集ダイアログ | Modify「編集」 |
| ドラッグ移動/伸縮 | Modify「時間の変更」 |
| Alt ドラッグ複製 | Composite「「X」の複製」 |
| インライン新規 | 確定時に AddItemEdit |
| インライン名称変更 | Modify「タイトルの変更」 |
| 複製/貼り付け | AddItemEdit |
| 記録停止 | 元予定の Modify「記録」／AddItemEdit／ChangeTodoRecordedTimeEdit「実績時間の追加」を Composite |
| 記録漏れを ToDo で埋める | Composite「「X」で記録漏れを埋める」（AddItemEdit ＋ 「実績の追加」） |
| 使用アプリから埋める | 「「X」で記録漏れを埋める」 |
| ToDo 追加 | AddTodoEdit |
| ToDo 削除 | RemoveTodoEdit |
| 完了済みを一括削除 | Composite「完了済みの ToDo {n} 件の削除」 |
| ToDo 編集 | Modify「編集」 |
| 期限の変更 | Modify「期限の変更」 |
| 今日やる | Modify「今日やる」/「今日やるの取り消し」 |
| 完了切替 | Modify「完了」/「完了の取り消し」＋次回分 AddTodoEdit を Composite「ToDo「X」の完了」 |
| サブタスク | Modify「サブタスクの追加/削除/完了」(+親完了・次回分) を「ToDo「X」の{label}」 |
| 並べ替え | ReorderTodosEdit |
| 明日へ繰り越し | Composite「ToDo {n} 件を明日へ繰り越し」 |
| 時間ブロック | AddItemEdit |

### 1.6 キー
`design/04` §8（テキスト入力中は横取りしない）。ツールバーの ↩/↪ ボタン。

## 2. 選択（MainViewModel.Timeline.cs / Selection.cs）

- `SelectedItem`（ScheduleItem?）：参照が同じなら何もしない。旧アイテムの `IsSelected=false`、新アイテムの `IsSelected=true`、通知、`RefreshBarStates()`（タイムラインの減光再計算）
- `SelectAndReveal(item)`：SelectedItem に設定し、タイムラインモードなら `TimelineScrollToItemRequested`、日/週かつ終日でなければ `ScrollToTimeRequested(item.StartTime)`（クリック選択では表示を動かさないため setter とは分ける）
- `MoveSelectionCommand(param)`：param が int / "-1" 文字列 / それ以外(=+1)。候補：
  - タイムライン：全バー（仮想化前 `AllTimelineBars`）のうち減光されていないもの
  - それ以外：`FlushPendingLayout()` → VisibleDays の各日の `DailyScheduleItems` を重複除去して StartTime 順
  - 現在位置が無ければ direction&gt;0 で先頭/それ以外で末尾、あれば `clamp(current+dir)` → `SelectAndReveal`
- `EditSelectedCommand`：選択があり `EditCommand.CanExecute` なら実行
- `DeleteSelectedCommand`：実行後、アイテムが残っていなければ選択解除（確認でキャンセルされることがある）
- `ClearSelectionCommand`：SelectedItem=null
- ScheduleItems から消えたアイテムが選択中なら null にする（`OnScheduleItemsChanged`）

## 3. 複製・コピー・貼り付け（MainViewModel.Clipboard.cs）

- Windows のクリップボードは使わない（他アプリとやり取りする内容ではない）。写しは `ItemSnapshot` で持つ（参照だと元の編集で結果が変わる）
- `HasClipboardItem`, `ClipboardItemTitle`（空白なら「(無題)」）
- 対象解決：`param as ScheduleItem ?? SelectedItem`
- `CopyItemCommand`：`_clipboardItem = Capture(item)`、2プロパティ通知
- `DuplicateItemCommand`：開始 = 終日なら `item.StartTime`、それ以外 `item.EndTime`（直後に置く）→ `AddCopy`
- `PasteItemCommand(param)`：`param is DateTime` ならその時刻、それ以外 `CurrentDate.Date + snapshot.StartTime.TimeOfDay` → `AddCopy`（CanExecute は写しがあるとき）
- `AddCopy(snapshot, start)`：`CreateCopy` → Add → `RecordAdd` → `SelectedItem=copy` → SaveData
- `CreateCopy(snapshot, start)`：長さを保って開始だけ差し替え。Kind, Title, Content, IsAllDay, BackgroundColor, CategoryId, ProjectCodeId, RemindAtStart, AutoStartRecording, ForceStartRecording を引き継ぐ。**Id は新規、RoutineId/SourcePlanId/TodoId は引き継がない**（TodoId を引き継ぐと実績が二重に積まれる）

## 4. 検索（MainViewModel.Search.cs）

### 4.1 SearchResultVm（予定・ToDo・ふりかえりを1つの一覧に混ぜる）
| 種別 | Title | Content | Brush | DateText | TimeText | Glyph | SortKey |
| --- | --- | --- | --- | --- | --- | --- | --- |
| 予定 `ForItem` | 空白なら「(無題)」 | Content | BackgroundColor | `yyyy/MM/dd (ddd)` | 終日「終日」/ `HH:mm - HH:mm` | `` | StartTime |
| ToDo `ForTodo` | 同 | Content | todo.Brush | 「期限 yyyy/MM/dd (ddd)」/「期限なし」 | 「完了」/「ToDo」 | `` | DueDate ?? MinValue |
| ふりかえり `ForWorkDayNote(log)` | NoteSingleLine の40文字（超えたら40文字＋「…」） | 切り詰めた場合のみ全文、他は空 | 固定 #94A3B8（Freeze） | `yyyy/MM/dd (ddd)` | 「ふりかえり」 | `` | StartTime |
`Item`/`Todo`/`NoteDate` のどれか1つを持つ。`HasContent`。

### 4.2 動作
- `SearchQuery` setter：`UpdateSearchResults()` → `OnSearchQueryChangedForTimeline()`
- `UpdateSearchResults()`：Trim が空なら空。予定（Title か Content に大文字小文字無視の部分一致）＋ToDo（同）＋ふりかえり（`HasNote && Note.Contains`、本文のみ）を連結 → SortKey 降順 → 先頭100件
- `HasSearchResults`、`SearchResultCountText`：「{n} 件」/「該当なし」
- `ClearSearchCommand`：SearchQuery=""
- `JumpToSearchResultCommand(param)`：SearchResultVm の種別、または ScheduleItem / TodoItem を直接受ける
  - 予定：日付が CurrentDate より前なら Backward／後なら Forward にして `CurrentDate = 日付`（**モードは変えない**）。終日でなく日/週なら `ScrollToTimeRequested(StartTime)`
  - ToDo：`IsTodoPanelVisible=true`, `SelectedTodo=todo`、期限があれば同様に日付移動
  - ふりかえり：その日へ日付移動のみ
- 検索 UI は `design/04` §2.2

## 5. 表示フィルタ

- `CategoryInfo.IsFilterEnabled` / `ProjectCodeInfo.IsFilterEnabled`（保存しない）
- `IsItemVisible(item)`：解決したカテゴリが `IsFilterEnabled==false` なら非表示。プロジェクトコードが解決でき `IsFilterEnabled==false` なら非表示。**解決できない（未分類・未設定）は常に表示**
- `IsTodoVisible(todo)`：カテゴリのフィルタのみ。**ToDo パネルには効かせない**（日/週チップと月セルだけ。パネルで ToDo が消えたように見えるのを防ぐ）
- `IsDisplayFilterActive`：どれか1つでも false
- `ResetDisplayFilterCommand`：全カテゴリ・全コードを true
- フィルタ変更時（カテゴリ/コードの PropertyChanged で `IsFilterEnabled`）：保存せず `IsDisplayFilterActive` 通知＋`RecalculateLayout()`
- 統計の集計は表示フィルタの影響を受けない（`design/08`）、タイムラインは受ける
