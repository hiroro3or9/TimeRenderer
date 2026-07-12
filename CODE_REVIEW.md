# TimeRenderer コードレビュー（バグ調査 + リファクタリング調査）

調査日: 2026-07-12 / 対象: 全 .cs / .xaml（約4,400行）

> **✅ 2026-07-12 全項目対応済み**
> A-1〜A-12 のバグ、B-1〜B-5 のリファクタリング（B-5 の CommunityToolkit.Mvvm 導入を除く）をすべて修正しました。
> 主な構造変更:
> - `Models/ScheduleSegment.cs` 新規追加（日またぎ予定の日単位分割描画、A-8対応）
> - `Views/CalendarGridView.xaml(.cs)` 新規追加（月/スプリントビューの共通化、A-6/B-2対応）
> - データ保存先を exe 隣 → `%APPDATA%\TimeRenderer` に変更（旧ファイルは初回起動時に自動移行、A-5対応）
> - 削除: 未使用コンバーター4種 / SimpleTextInputDialog / 各種デッドコード（B-1対応）
> - ビルド確認は未実施（環境に .NET SDK なし）。初回ビルドで動作確認を推奨。

---

## A. バグ（動作に実害あり・優先度順）

### A-1. 月／スプリントビューで「タイトル・内容・色のみ」の編集が画面に反映されない
`MainViewModel.OnScheduleItemPropertyChanged` は StartTime / EndTime / IsAllDay の変更時のみ
`RecalculateLayout()`（→ `UpdateCalendarCells()`）を呼ぶ。Title などの変更は SaveData のみ。
`CalendarMonthCellControl` は OnRender で自前描画しており INPC を購読しないため、
時刻を変えずにタイトルだけ編集すると古い表示のまま残る（`BitmapCache` がさらに固定化）。
**修正案:** Title / Content / BackgroundColor の変更時も `UpdateCalendarCells()` を呼ぶ、
または CellData 変更検知を項目レベルで行う。

### A-2. 日ビューで「非表示曜日」の日付を表示すると予定がすべて消える
`DateToPageVisibilityConverter` はモード判定より先に `enabledDays.Contains(itemDate.DayOfWeek)`
で Collapsed を返す。`TodayCommand` は曜日設定を無視して今日へジャンプするため、
例えば日曜を非表示にして日曜に「今日」を押すと、列は表示されるのに予定が1件も出ない。
**修正案:** Day モードでは曜日フィルタをスキップする。

### A-3. TL（スプリントタイムライン）ビューで範囲外の予定が「空行」として残る
タイムラインの ItemsSource が全 `ScheduleItems`。範囲外アイテムはバーを
Margin=-10000 で画面外へ飛ばすだけで、行の Grid（高さ35+マージン）は残る。
→ 過去の予定が増えるほど空行だらけになり、縦スクロールが無意味に伸びる。
並び順も追加順のままで StartTime 順でない。
**修正案:** VM 側で表示範囲内のアイテムをフィルタ・ソートしたコレクションを公開する。

### A-4. 起動時 LoadData で schedules.json が一瞬「空」で上書きされる
`LoadData()` の `ScheduleItems.Clear()` が CollectionChanged(Reset) を発火し
`SaveData()`（空リストを書き込み）が走る。その後 Add のたびに 1件ずつ全保存。
アイテム N 件で起動時に N+1 回のファイル書き込み。Clear 直後にクラッシュ／強制終了すると
**全データ消失**の窓がある。
**修正案:** ロード中は保存を抑止するフラグを立てる（`_isInitialized` を SaveData でも参照するだけでほぼ解決）。

### A-5. 保存失敗が完全に握りつぶされる + 保存先が exe と同じフォルダ
`JsonFileRepository` は例外を `Debug.WriteLine` のみで無視。保存先は
`AppDomain.CurrentDomain.BaseDirectory`。Program Files 等の書込不可な場所に置くと
**作業記録が黙って一切保存されない**。
**修正案:** 保存先を `%APPDATA%\TimeRenderer` へ。失敗時はユーザーに通知。

### A-6. 3週間を超える手動スプリントで Sprint ビューが崩れる
XAML 側は `UniformGrid Rows="3"` 固定だが、`UpdateVisibleDays()` はスプリントの
実際の週数分セルを生成する。4週以上の手動スプリントを登録すると 4行目以降のセルが
グリッド外にあふれる。
**修正案:** Rows を週数にバインドする（EnabledDaysCount と同様の仕組み）。

### A-7. 入力エラー表示に「はい/いいえ」ダイアログを流用
`SaveNewSprintCommand` のバリデーションエラーを `ShowConfirmationDialog`
（MessageBoxButton.YesNo）で表示。エラー通知に はい/いいえ ボタンが出る。
**修正案:** `IDialogService.ShowMessage()`（OKのみ）を追加。

### A-8. 日付をまたぐ予定が翌日側に表示されず、グリッド下端をはみ出す
編集ダイアログは「終了 ≤ 開始」で終了を+1日にする（23:00→翌1:00 が作れる）。
深夜またぎの記録でも同様のアイテムが生まれる。しかし週/日ビューの描画は
StartTime の日付列にのみ配置し、高さは DurationHours 分そのまま伸ばすため、
24:00 のグリッド下端を突き抜けて描画され、翌日列には何も出ない。
**修正案:** 日またぎアイテムを日単位に分割して描画する（Recalculate 時に表示用セグメント生成）。

### A-9. 表示時間範囲外の予定が負の Y 座標に描画される
`TimeToPositionConverter` はクランプしないため、DisplayStartHour=9 のとき 8:00 の予定は
Canvas.Top = -60 となり上部にはみ出して描画される（Canvas は ClipToBounds ではない）。
**修正案:** 範囲外は非表示にするか、Canvas に ClipToBounds を設定。

### A-10. 月セルのクリック判定が高 DPI でずれる
`CalendarMonthCellControl.GetItemAtPosition` は FormattedText を `PixelsPerDip=1` 固定で
生成するが、OnRender は実際の DPI を使う。125%/150% 環境で日付テキスト高さが変わり、
アイテムのヒット判定が数 px ずれる。OnRender とレイアウト定数・計算も丸ごと重複している。
**修正案:** レイアウト計算を共通メソッド化し、DPI も統一。

### A-11. 編集ダイアログが時刻を5分単位に丸めてしまう
記録機能で作った予定（例 10:23:45）を編集して OK すると 10:20:00 に静かに変わる。
秒も消える。
**修正案:** コンボの選択肢にない場合は元の分を保持する（編集時は Text で保持等）。

### A-12.（軽微）24時間超の記録で表示が桁落ち
`ToggleRecording` の `Content = $"記録時間: {(endTime - startTime):hh\\:mm}"` は
TimeSpan の日数部分を表示しない。長時間放置した記録で不正確になる。

---

## B. リファクタリング推奨

### B-1. 未使用コード（削除可能・確認済み）
- `MainViewModel.AddScheduleItemAtDate()`（Commands.cs のコマンドと完全重複、呼び出し無し。インデントも崩れている）
- `MainViewModel.SelectedItem` プロパティ
- `JsonFileRepository.SaveToFileAsync / LoadFromFileAsync`
- `AppSettings.MemoText`（保存・読込どちらも未使用）
- `SimpleTextInputDialog`（xaml + cs、参照なし）
- MainWindow.xaml のリソース宣言のみで未使用のコンバーター4つ:
  `DateToScheduleItemsConverter` / `IsCurrentMonthConverter` /
  `CurrentMonthForegroundConverter` / `CurrentMonthBackgroundConverter`（クラス本体ごと削除可）
- `MainViewModel.UpdateRecordingCommandState()`（中身がコメントアウト）

### B-2. 重複コードの統合
- `EnabledDayHeaders` と `EnabledDayNames`: 曜日→和名マッピングが二重定義。
  `EnabledDayNames` は `EnabledDayHeaders.Select(h => h.Name)` で導出可能。
- `MainViewModel.CurrentWeekStart` と `DateTimeHelper.GetStartOfWeek()`: 同一ロジックの二重実装。
- MainWindow.xaml: 月ビューとスプリントビューのカレンダー部が約70行×2でほぼコピー
  （差分は Rows と Visibility のみ）→ UserControl または共有 DataTemplate 化。
  曜日ヘッダー部も同様に2回コピーされている。
- `CalendarMonthCellControl`: OnRender と GetItemAtPosition のレイアウト計算重複（A-10 と同根）。
- Commands.cs の `AddScheduleItemAtDateCommand` と `AddCommand`: 新規アイテム生成の重複。

### B-3. 保存処理の効率化
- メモ: `UpdateSourceTrigger=PropertyChanged` + setter 内 `SaveMemos()` で
  **1キーストロークごとに全メモをファイル書き込み**。デバウンス（例: 1秒）推奨。
- `EditCommand` はプロパティを6個逐次代入 → RecalculateLayout / SaveData が編集1回で複数回発火。
  一括更新メソッド（または変更通知の抑止）を用意する。
- `ScheduleItem.DurationHours`（読み取り専用）が JSON に毎回シリアライズされる → `[JsonIgnore]`。

### B-4. パフォーマンス
- `SprintHelper.GetSprintForDate` は呼ばれるたびに±3ヶ月分のスプリント一覧を再生成。
  `DateDisplay` / `UpdateVisibleDays` / `UpdateCalendarCells` / ナビゲーションから頻繁に呼ばれるため、
  (ManualSprints, 対象期間) をキーにしたキャッシュを推奨。

### B-5. 設計改善（任意）
- `LoadSettings` / `CurrentViewMode` setter の手動 `OnPropertyChanged` 羅列（10行前後×2箇所）。
  CommunityToolkit.Mvvm の `[ObservableProperty]` + `[NotifyPropertyChangedFor]` 導入で大幅に削減可能
  （RelayCommand も置き換えられる）。
- `FilePersistenceService` / `SettingsService` が全 static でインスタンスクラスの形だけ残っている。
  static class にするか、テスト可能にするなら IDialogService と同様にインターフェース化して DI。
- `LoadSettings` の `(ViewMode)settings.ViewMode` は範囲検証なし（壊れた JSON で不正 enum になる）。
- `LoadMemos` の `DateTime.Parse(k.Key)` はカルチャ依存 → `ParseExact("yyyy-MM-dd", InvariantCulture)`。
- 記録中にトレイの「終了」を押すと記録が黙って破棄される → 確認ダイアログ or 自動保存を検討。

---

## C. 問題なしと確認した箇所
- `ScheduleLayoutHelper` の列割当（クラスタリング）ロジック
- `SprintHelper.GetSprintsForRange` の自動補間（重複バリデーション前提なら停止性も問題なし）
- テーマ切替（DarkColors.xaml の動的差し替え）
- `TransitioningContentControl` のアニメーション（transitionId による競合対策も適切）
- ScrollViewerHelper の中ボタンスクロール（イベント購読/解除の対応も取れている）
- 週メモの週跨ぎ切替（setter バイパスで保存ループなし）

※ サンドボックスに .NET SDK がないためビルドは未実施。上記は静的レビューによるものです。
