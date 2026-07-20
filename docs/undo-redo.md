# 取り消し・やり直し（Undo / Redo）

## 対象範囲

**対象**: 予定アイテム（`ScheduleItem`）に対する明示的なユーザー操作

| 操作 | 記録箇所 | 履歴の表示 |
|---|---|---|
| 追加（ダイアログ経由の全経路） | `AddViaDialog` | 「〜」の追加 |
| 削除 | `DeleteCommand` | 「〜」の削除 |
| 編集ダイアログでの変更 | `EditCommand` | 「〜」の編集 |
| ドラッグでの移動・伸縮（日/週/TL） | `CommitItemDrag` | 「〜」の時間の変更 |
| 記録停止による追加・予定の実績化 | `ToggleRecording` | 「〜」の追加 / 記録 |

**対象外**: 設定、カテゴリ、スプリント、定型タイトル、メモ、定期予定の自動生成。

対象を予定アイテムに絞ったのは、設定変更まで巻き戻ると
「Ctrl+Z で何が戻るのか」が予測できなくなり、
誤操作の救済という本来の目的から外れるため。
定期予定の自動生成を除いたのも同じ理由で、
ユーザーが起こしていない変更が履歴に混ざると Ctrl+Z の意味がぶれる。

## 構成

| ファイル | 役割 |
|---|---|
| `Models/ItemSnapshot.cs` | アイテムの編集可能な状態の写し。`Capture` / `ApplyTo` / `IsSameAs` |
| `Helpers/UndoableEdits.cs` | `IUndoableEdit` と具象3種（追加 / 削除 / 内容変更） |
| `Helpers/UndoManager.cs` | 履歴スタック（上限100件）。`IsApplying` フラグを持つ |
| `ViewModels/MainViewModel.Undo.cs` | コマンド、記録用ヘルパー、ドラッグの前後状態 |
| `Views/MainWindow.Undo.cs` | Ctrl+Z / Ctrl+Y のキーボード処理 |

## 設計上の判断

### 復元は必ず元のインスタンスへ書き戻す

アイテムを複製して置き換えると、復元後に別インスタンスになり、
選択状態や他の履歴エントリが持つ参照と食い違う。
そのため `ItemSnapshot` は状態だけを持ち、`ApplyTo` で
**元の `ScheduleItem` インスタンスに書き戻す**。

削除の取り消しも、保存しておいたインスタンスをそのまま
元のインデックスへ差し戻す（並び順が変わらない）。

### 適用中は再計算・保存を抑止する

Undo/Redo の実行中はコレクション変更とプロパティ変更が発火する。
そのまま通すと、1回の取り消しでレイアウト再計算とファイル保存が
プロパティの数だけ走る。

`UndoManager.IsApplying` を `MainViewModel.IsApplyingUndo` として公開し、
`OnScheduleItemsChanged` / `OnScheduleItemPropertyChanged` の先頭でガードする。
まとめての再計算と保存は `AfterUndoRedo()` が1回だけ行う。

これは既存の `_isLoadingData` / `_isBatchUpdatingItem` と同じ考え方。

### ドラッグは確定時に1回だけ積む

ドラッグ中は `UpdateItemTimesPreview` がマウス移動のたびに走るが、
その一つひとつを履歴にすると Ctrl+Z が数十回必要になる。

`BeginItemDragUndo` で開始前の状態を控え、
`CommitItemDrag` の中で開始前との差分を1件だけ積む。
ドラッグを取り消した場合は `ClearItemDragUndo` で捨てる。

### 変化がなければ積まない

`ItemSnapshot.IsSameAs` で前後を比較し、同じなら履歴に積まない。
編集ダイアログを開いて何も変えずに OK した場合や、
ドラッグしたが元の位置へ戻した場合に、空の履歴が溜まるのを防ぐ。

### テキスト入力中は横取りしない

`Window_PreviewKeyDownForUndo` は `Keyboard.FocusedElement is TextBoxBase` のとき
何もせず、TextBox 自身の取り消しに任せる。
メモ欄で Ctrl+Z を押して予定の編集が巻き戻ると、
何が起きたか分からず被害が大きいため。

### データ再読込で履歴を捨てる

`LoadData` はアイテムのインスタンスを丸ごと入れ替えるため、
履歴が指している参照が無効になる。`ClearUndoHistory()` で捨てる。

## 操作方法

- ツールバーの ↩ / ↪ ボタン（ツールチップに「元に戻す: 〜」と操作内容が出る）
- `Ctrl+Z` で取り消し、`Ctrl+Y` または `Ctrl+Shift+Z` でやり直し

## 既知の制約

- 履歴は起動中のみ保持し、終了時に破棄する（永続化しない）
- 上限100件。超えた分は古いものから捨てる
- 定期予定の自動生成は履歴に含まれないため、
  自動生成の直後に Ctrl+Z を押すと「その前のユーザー操作」が取り消される
