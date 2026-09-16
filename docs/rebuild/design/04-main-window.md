# 04. メインウィンドウ

## 1. Window

```
MainWindow: Background={BackgroundBrush}, Icon=pack://application:,,,/Assets/AppIcon.ico,
            Title="TimeRenderer", Height=700, Width=1100, MinWidth=840, PreviewKeyDown=Window_PreviewKeyDownForUndo
InputBindings: Ctrl+T → FocusQuickAddTodoCommand
Grid RowDefinitions: Auto(ツールバー) / Auto(ナビ) / *(表示領域)
```

表示領域（Row=2）に重ねる要素（上ほど奥）：

| 要素 | 表示条件 / 配置 | ZIndex |
| --- | --- | --- |
| `TodayView` | `IsTodayMode` | |
| `DayWeekView` | `IsDayOrWeekMode` | |
| `CalendarGridView`（月） | `IsMonthMode`、`Rows=6`、Background Surface | |
| `CalendarGridView`（スプリント） | `IsSprintMode`、`Rows={Binding SprintWeekRows}` | |
| `TimelineView` | `IsSprintTimelineMode` | |
| `StatsView` | `IsStatsMode` | |
| `NotesView` | `IsNotesMode` | |
| `NotificationHost` | 常時 | 80 |
| `TodoPanel` | HorizontalAlignment=Right | 90 |
| `SettingsPanel` | HorizontalAlignment=Right | 100 |
| `ManagementPanel` | HorizontalAlignment=Right | 110 |

月/スプリントの CalendarGridView には添付イベントを設定：`CalendarMonthCellControl.ItemClicked="MonthCell_ItemClicked"`, `ItemRightClicked`, `TodoClicked`, `CellClicked`（`design/06`）。

## 2. ツールバー（Row=0）

`Border`（地 Surface、下線 Border 0,0,0,1、Padding 12,8）＞ `Grid x:Name=ToolbarLayoutGrid`（2行×3列：Auto / * / Auto）。3つの StackPanel（Horizontal, VerticalAlignment Center）それぞれに `SizeChanged="ToolbarLayout_SizeChanged"`。

### 2.1 左：ToolbarNavigationGroup（Row0, Col0）
1. `Button`「＋ 追加」PrimaryButtonStyle, Margin 0,0,8,0, Click → `AddCommand`
2. `Button` `&#xE7A7;` BaseButtonStyle, MDL2 12px, Width 32, Padding 0, Margin 0,0,4,0, Command `UndoCommand`, ToolTip `{UndoToolTip}`
3. `Button` `&#xE7A6;` 同上 Margin 0,0,16,0, `RedoCommand`, ToolTip `{RedoToolTip}`
4. `StackPanel`（Visibility=`IsDateNavigationVisible`）：`Button`「今日」BaseButtonStyle Margin 0,0,8,0 → `TodayCommand` ／ `Button` `&#xE76B;` 11px Width 32 Padding 0 Margin 0,0,4,0 ToolTip「前へ」→ `PreviousCommand` ／ `&#xE76C;` Margin 0,0,12,0 ToolTip「次へ」→ `NextCommand`
5. `TextBlock {DateDisplay}` FontSize 17, Bold, TextPrimary, Margin 4,0,0,0

### 2.2 中：ToolbarSearchGroup（Row0, Col1, HorizontalAlignment Right, Margin 12,0,0,0）
1. `ToggleButton x:Name=SearchToggle` IconToggleButtonStyle `&#xE721;` ToolTip「検索」、Checked=SearchToggle_Checked、PreviewMouseLeftButtonDown=SearchToggle_PreviewMouseLeftButtonDown
2. `ToggleButton x:Name=FilterToggle` IconToggleButtonStyle Margin 2,0,0,0 ToolTip「表示フィルタ」：中身 Grid 32×32 に `&#xE71C;`（15px）と、右上に 7×7 の `Ellipse`（Fill Primary、Margin 0,4,4,0、Visibility=`IsDisplayFilterActive`）
3. `Popup x:Name=SearchFlyout`（PlacementTarget=SearchToggle, Placement=Custom, IsOpen⇔SearchToggle.IsChecked(TwoWay), StaysOpen=False, AllowsTransparency, PopupAnimation=Fade, Closed=SearchFlyout_Closed）
   - `Border` Width 340, MaxHeight 480, 地 Surface, 枠 Border 1, CornerRadius 8, `DropShadowEffect(Black, Blur 12, Depth 2, Opacity .15)`, PreviewKeyDown=SearchFlyout_PreviewKeyDown
   - Grid 3行：
     - 入力：Border Height 34, CornerRadius 6, Margin 10,10,10,8, 地 Background, 枠 Border 1 ＞ Grid 3列［`&#xE721;` 12px TextSecondary Margin 8,0,4,0 ｜ `TextBox x:Name=SearchTextBox` Text=`{SearchQuery, PropertyChanged}` 枠0 透明 13px ＋ 同じセルにプレースホルダ TextBlock「タイトル・内容を検索」13px TextMuted（`SearchQuery==""` のときだけ Visible、IsHitTestVisible False、Margin 2,0,0,0） ｜ クリアボタン「✕」22×22 IconButtonStyle Margin 0,0,4,0 → `ClearSearchCommand`（SearchQuery が空なら Collapsed）］
     - 件数：Border Padding 12,0,12,8（SearchQuery が空なら Collapsed）＞ `{SearchResultCountText}` 11px TextSecondary
     - 結果：ScrollViewer（Visibility=`HasSearchResults`）＞ ItemsControl `{SearchResults}`。項目＝`Button` SearchResultButtonStyle, Command=`DataContext.JumpToSearchResultCommand`(AncestorType=Window), CommandParameter={Binding}, Click=SearchResultButton_Click ＞ Grid 3列［色 Border 10×10 CornerRadius 2 地 `{Brush}` 枠 Border 1 Margin 0,3,6,0 Top ｜ `{Glyph}` MDL2 11px TextSecondary Margin 0,2,6,0 ｜ StackPanel［`{Title}` SemiBold 13 TextPrimary 省略 ／ `{Content}` 11 TextSecondary 省略 Margin 0,1,0,0（HasContent）／ 横並び `{DateText}` 10 TextMuted・`{TimeText}` 10 TextMuted Margin 8,0,0,0］］
4. `Popup x:Name=FilterPopup`（同様の設定、Closed=FilterPopup_Closed）
   - `Border` Width 280, MaxHeight 440, 同じ見た目
   - Grid 3行：ヘッダー Border Padding 12,8 下線 ＞「表示フィルタ」SemiBold 12 ／ ScrollViewer MaxHeight 330 ＞ StackPanel Margin 6［「カテゴリ」11 SemiBold TextSecondary Margin 6,4,6,2 ／ ItemsControl `{Categories}`：`CheckBox IsChecked={IsFilterEnabled}` Margin 6,4 ＞ 横並び［Border 14×14 CornerRadius 3 地 `{Brush}` 枠 Border 1 Margin 2,0,8,0 ｜ `{Name}` 13］／ 区切り Border 高さ1 Margin 6,7 ／「プロジェクトコード」11 SemiBold Margin 6,2,6,2 ／ ItemsControl `{ProjectCodes}`：CheckBox `{IsFilterEnabled}` ToolTip DisplayName ＞ `{DisplayName}` 13 省略］／ `Button`「すべて表示」BaseButtonStyle Margin 10 Stretch → `ResetDisplayFilterCommand`

### 2.3 右：ToolbarActionsGroup（Row0, Col2, HorizontalAlignment Right）
1. `TextBlock {WorkStatusText}` 11px TextSecondary Margin 0,0,8,0
2. `Button x:Name=WorkDayButton` Content `{WorkDayButtonText}` Width 64 Margin 0,0,4,0 Command `ToggleWorkDayCommand` ToolTip「仕事の開始・終了を登録します」（ホットキー登録後に差し替え→§6）
   - Style BasedOn BaseButtonStyle：Foreground/BorderBrush `{WorkStartBrush}`、`IsWorking=True` で `{WorkEndBrush}`
   - ContextMenu：「勤務時間を編集・追加…」→ `EditWorkDayCommand`
3. `Border` ToolbarSeparatorStyle
4. `TextBox` `{RecordingTitle, PropertyChanged}` Width 160 Margin 0,0,8,0 InputTextBoxStyle FontSize 12 ToolTip「記録中のタイトル（停止時にこの内容で保存されます）」 Visibility=`IsRecording`
5. 記録ボタン `Button` Command `ToggleRecordingCommand`, BorderBrush `{DangerBrush}`, Style BasedOn Base（Foreground Danger）、独自テンプレート：Border（CornerRadius 6）。MouseOver→地 DangerSubtle。`DataTrigger IsRecording=True`→地 Danger・文字 White。記録中＋MouseOver→地 DangerDark・文字 White。中身 `TextBlock {RecordingDurationText}` Width 140 中央揃え
6. `Border` ToolbarSeparatorStyle
7. `ToggleButton` IsChecked⇔`IsTodoPanelVisible` IconToggleButtonStyle ToolTip `{TodoSummaryText}` Margin 0,0,2,0：Grid 32×32［`&#xE73A;` 15px ｜ 右上 Ellipse 7×7 Fill `{TodoOverdueBrush}` Margin 0,4,4,0 Visibility=`HasOverdueTodos`］
8. `ToggleButton` IsChecked⇔`IsManagementPanelVisible` BaseToggleButtonStyle Padding 8,5 Margin 2,0 ToolTip「分類・定期予定・スプリントを管理」：横並び［`&#xE74C;` 13px Margin 0,0,5,0 ｜「管理」12px］
9. `ToggleButton` `&#xE713;` IsChecked⇔`IsSettingsPanelVisible` IconToggleButtonStyle ToolTip「設定」

### 2.4 幅が足りないときの2段化（コードビハインド）
```csharp
void ToolbarLayout_SizeChanged(...) {
  if (ToolbarLayoutGrid.ActualWidth <= 0) return;
  // * 列は幅不足で 0 まで潰れるので、各 StackPanel の子の DesiredSize.Width 合計（＋Margin）で判定
  double required = Width(Navigation) + Width(Search) + Width(Actions);
  bool compact = ToolbarLayoutGrid.ActualWidth < required;
  if (_isToolbarCompact == compact) return; _isToolbarCompact = compact;
  if (compact) { Actions: Row=1, Column=0, ColumnSpan=3, HorizontalAlignment=Left, Margin=0,4,0,0 }
  else         { Actions: Row=0, Column=2, ColumnSpan=1, HorizontalAlignment=Right, Margin=0 }
}
```

### 2.5 ポップアップの挙動（コードビハインド）
- 両 Popup の `CustomPopupPlacementCallback` = トグル右端揃え：`new CustomPopupPlacement(new Point(targetSize.Width - popupSize.Width, targetSize.Height + 4), PopupPrimaryAxis.Horizontal)`
- StaysOpen=False のポップアップはトグル自身のクリックで「閉じる→即再オープン」になる。`Closed` で `DateTime.UtcNow` を記録し、トグルの PreviewMouseLeftButtonDown で 250ms 以内なら `e.Handled=true`
- `SearchToggle_Checked`：`Dispatcher.BeginInvoke(Input)` で SearchTextBox.Focus() + SelectAll()
- `SearchFlyout_PreviewKeyDown`：Esc で SearchToggle.IsChecked=false
- `SearchResultButton_Click`：`Dispatcher.BeginInvoke` で SearchToggle.IsChecked=false（コマンド実行後に閉じる）

## 3. ViewNavigation（Row=1、UserControl）

`Border`（地 Surface、下線、Padding 12,5）＞ `ScrollViewer`（横 Auto、縦 Disabled）＞ StackPanel 横・中央揃え。4グループを縦線（`Border Width=1 地 Border Margin 0,3,12,2`）で区切る。
各グループ＝StackPanel［グループ名 TextBlock（ViewNavigationGroupLabelStyle）／ 横並びの RadioButton（GroupName="MainViewMode"、ViewNavigationItemStyle、`IsChecked={Binding IsXxxMode, Mode=OneWay}`、`Command=ChangeViewModeCommand`、`CommandParameter="ViewMode名"`）］、グループ間 Margin 0,0,12,0。

| グループ | 項目（Content → CommandParameter / IsChecked） |
| --- | --- |
| ホーム | 今日 → Today / IsTodayMode |
| カレンダー | 日 → Day ／ 週 → Week ／ 月 → Month |
| スプリント | カレンダー → Sprint ／ タイムライン → SprintTimeline |
| 分析 | 統計 → Stats ／ ふりかえり → Notes |

`ChangeViewModeCommand`：引数が `ViewMode` ならそのまま、文字列なら `Enum.TryParse` して `CurrentViewMode` に設定。

## 4. パネルの排他（MainViewModel.Data.cs / Todos.cs）

右側オーバーレイの3パネル（ToDo・設定・管理）は同時に1つだけ開く。いずれかの setter で `value==true` のとき、他の2つのバッキングフィールドを false にして通知してから自分を設定し、変化があれば `SaveSettings()`。
`ToggleSettingsPanelCommand` / `ToggleManagementPanelCommand` / `ToggleTodoPanelCommand` は反転するだけ。

各パネルの開閉アニメーション（`design/13`）：Border の Width を既定値（ToDo 340 / 設定 400 / 管理 440）に置き、`DataTrigger IsXxxPanelVisible=False` の EnterActions で Width→0（0.15秒、QuadraticEase EaseOut）、ExitActions で既定値へ戻す。ClipToBounds=True、左枠1、`DropShadowEffect(Black, Blur 10, Depth -4, Opacity .1)`。

## 5. NotificationHost（UserControl、IsTabStop=False）

背景を持たない Grid（通知以外はクリックが背面に通る）。2行：Auto / *。

### 5.1 継続通知（Row0）
`Border` WarningNotificationCardStyle, Margin 12,10,12,0, Visibility=`HasDataNotice` ＞ Grid 3列：
［`&#xE7BA;` 16px `{WarningBrush}` Top Margin 0,1,10,0 ｜ StackPanel［「データに関するお知らせ」12 SemiBold `{WarningTextBrush}` ／ `{DataNotice}` 12 折返し Margin 0,2,0,0 WarningText］｜ 横並び Margin 12,0,0,0［Button「データフォルダを開く」Padding 10,4 Margin 0,0,6,0 → `OpenDataFolderCommand` ／ Button「✕」Width 26 Padding 0,4 ToolTip「この通知を閉じる」→ `DismissDataNoticeCommand`］］
（このボタン2つは Style 指定なし＝既定ボタン）

### 5.2 一時通知（Row1）
`ScrollViewer` Width 520, MaxHeight 430, Margin 12, 右上寄せ, 横 Disabled/縦 Auto ＞ StackPanel に上から：

| # | 表示条件 | 見た目・内容 |
| --- | --- | --- |
| 1 | ItemsControl `PendingReminders`（予定の開始） | NotificationCardStyle。Grid 2行×3列：[0,0] `&#xE7BA;` 15px Primary Top Margin 0,2,10,0 ／ [0,1]「予定の開始時刻です」11 TextSecondary ＋ `{Title}` 13 SemiBold 省略 ／ [0,2]「✕」24×24 IconButtonStyle → `DismissReminderCommand` ／ [1,1-2]「記録開始」Primary Padding 12,4 右寄せ Margin 0,8,0,0 → `StartReminderCommand` |
| 2 | ItemsControl `PendingTodoReminders` | NotificationCardStyle＋地 `{TodoReminderSubtleBrush}`。`&#xEA8F;` `{TodoDueTodayBrush}` ／「ToDo の通知」＋ `{Title}` ／「✕」ToolTip「この通知を閉じる（ToDo は残ります）」→ `DismissTodoReminderCommand` ／ 下段右寄せ横並び：「記録開始」Primary Padding 10,4 Margin 0,0,6,0 → `StartRecordingFromTodoReminderCommand`、「完了」Base → `CompleteTodoReminderCommand`、`{TodoSnoozeLabel}` Base ToolTip「この時間だけ先送りして、もう一度通知します」→ `SnoozeTodoReminderCommand`、`&#xE70D;` 8px 24×26 IconButtonStyle Margin 2,0,0,0 ToolTip「先送りする時間を選ぶ」Click でその ContextMenu を Bottom に開く：「5分後」「15分後」「30分後」「1時間後」「3時間後」／区切り／「明日の朝」（Tag=5,15,30,60,180,tomorrow。tomorrow は `SnoozeTodoReminderUntilTomorrow`、数値は `SnoozeTodoReminder(todo, 分)`） |
| 3 | `HasTodoMissedNotice` | 地 TodoReminderSubtle。`&#xE7BA;` `{TodoOverdueBrush}` ／ `{TodoMissedNotice}` 13 折返し ／「✕」→ `DismissTodoMissedNoticeCommand` ／「確認する」Primary → `ShowMissedTodosCommand` |
| 4 | `HasTodoDigest` | 地 TodoReminderSubtle。`&#xE8FD;` TodoDueToday ／ `{TodoDigestNotice}` ／「✕」ToolTip「この通知を閉じる」→ `DismissTodoDigestCommand` ／「ToDo を開く」Primary → `FocusQuickAddTodoCommand` |
| 5 | `HasAutoStartNotice` | 地 PrimarySubtle・枠 Primary。横並び［`&#xE768;` 15 Primary Margin 0,0,10,0 ｜ `{AutoStartNotice}` 13 折返し］（6秒で自動的に消える） |
| 6 | `ShowAwayBanner` | WarningNotificationCardStyle。横並び［`&#xE823;` 15 Warning ｜ `{AwayBannerText}` 12 折返し WarningText］ |

`AutoStartNotice` は多目的の一時通知（`ShowAutoStartNotice(message)`：設定して 6秒 DispatcherTimer で null に戻す。テスト用起動ではタイマーを作らない）。

## 6. タスクトレイ・状態アイコン（MainWindow.xaml.cs）

### 6.1 NotifyIcon（WinForms）
- `_trayIcon = AppIconHelper.CreateTrayIcon()`（`SystemInformation.SmallIconSize` のフレーム。失敗時 `SystemIcons.Application`）
- 状態アイコンを事前生成：出勤中 / 記録中 / 出勤中＋記録中（トレイ用とウィンドウ用。ウィンドウ用は `SystemInformation.IconSize` から作り `CreateImageSource` で WPF 化）
- `Text="TimeRenderer"`, `Visible=true`。DoubleClick と BalloonTipClicked → `ShowWindow()`（Show, WindowState=Normal, Activate）
- ContextMenuStrip：
  1. 「表示」
  2. `_recordMenuItem`：記録中「■ 停止」／停止中「● 記録開始」＋ホットキーがあれば「 (Ctrl+Alt+R)」。Click → `QuickToggleRecording()`（ダイアログなし）
  3. `_workDayMenuItem`：「出勤」/「退勤」＋登録済みホットキー。Click → ClockIn/ClockOut して `ShowTrayBalloon("出勤を登録しました"|"退勤を登録しました", WorkStatusText)`
  4. 区切り
  5. 「終了」：記録中なら `ToggleRecordingCommand` で停止・保存 → `_isExiting=true` → アイコン非表示 → `Application.Current.Shutdown()`
- `NotifyIcon.Text`：記録中「TimeRenderer - {出勤中・記録中|勤務外・記録中} (hh:mm:ss)」（カウントダウンは「(残り hh:mm:ss)」）、停止中「TimeRenderer - 出勤中」/「TimeRenderer - 勤務外」
- `ShowTrayBalloon(title, text)`：`ShowBalloonTip(5000, title, text, ToolTipIcon.Info)`

### 6.2 アイコンの状態オーバーレイ（AppIconHelper.CreateStatusIcon）
base.ToBitmap() に AntiAlias で描画し `GetHicon` → Clone → `DestroyIcon`（LibraryImport）。失敗時は base の Clone。
- **出勤中**：緑の角丸枠。s=短辺、線幅 `max(1.2, s*0.055)`、inset `線幅/2 + max(0.25, s*0.01)`、角丸半径 `s*0.18`、色 ARGB(240,34,197,94)、LineJoin Round
- **記録中**：右下の赤丸バッジ。直径 `max(6.5, s*0.39)`、余白 `max(0.6, s*0.035)`、白縁幅 `max(1.1, s*0.065)`。影（ARGB(90,0,0,0)、下へ `max(0.5, s*0.02)` ずらし `max(0.2, s*0.01)` 膨らませる）→ 白丸 → 内側に ARGB(245,220,38,38)

`UpdateStatusIndicator()`：(IsWorking, IsRecording) で トレイの Icon と Window.Icon を切り替え（どちらも false なら元のアイコン）。

### 6.3 VM 変更への反応（ViewModel_PropertyChanged）
| プロパティ | 処理 |
| --- | --- |
| IsRecording / IsWorking | `UpdateStatusIndicator()`、IsRecording なら `UpdateMiniRecordingBar()`、`UpdateContextMenu()` |
| RecordingDurationText | `UpdateContextMenu()` |
| IsMiniRecordingBarEnabled | `UpdateMiniRecordingBar()` |
| AutoStartNotice | 非null かつウィンドウ非アクティブ → バルーン「自動記録開始」 |
| TodoDigestNotice | 同 →「今日の ToDo」 |
| TodoMissedNotice | 同 →「見逃した ToDo の通知」 |

`PendingReminders` の追加（非アクティブ時のみ）：バルーン「「{Title}」の開始時刻です」「クリックすると記録を開始できます」
`PendingTodoReminders` の追加（非アクティブ時のみ）：「ToDo「{Title}」」「{期限 {DueDisplay}・（期限あり時）}クリックすると記録開始・完了を選べます」

### 6.4 最小化・閉じる・終了
- `OnStateChanged`：Minimized なら `Hide()`（トレイ常駐）
- `OnClosing`：`_isExiting` でなければ `FlushDataSave()` と `FlushTodoSave()` を呼んでから `e.Cancel=true; Hide()`
- `OnClosed`：ホットキー解除 → ミニバー破棄 → NotifyIcon を非表示・Dispose → 生成したアイコンを Dispose → VM の `FlushDataSave`, `FlushTodoSave`, 購読解除, `DisposeAwayDetection`, `DisposeAppUsageTracking`

## 7. グローバルホットキー（MainWindow.Hotkey.cs）

- `OnSourceInitialized` で HwndSource にフックを追加し、候補を順に `RegisterHotKey`（MOD_NOREPEAT 0x4000 付き → 無しの順）。成功した表示名を保持
  | ID | 用途 | 候補 |
  | --- | --- | --- |
  | 0x5452 | 記録の開始/停止 | Ctrl+Alt+R → Ctrl+Alt+Shift+R → Ctrl+Alt+F9 |
  | 0x5453 | 出勤 | Ctrl+Alt+S → Ctrl+Alt+Shift+S → Ctrl+Alt+F10 |
  | 0x5454 | 退勤 | Ctrl+Alt+E → Ctrl+Alt+Shift+E → Ctrl+Alt+F11 |
  （MOD: Alt=1, Control=2, Shift=4。Win+Shift+R は Snipping Tool と競合するので使わない）
- 登録後 `UpdateContextMenu()`、出勤ボタンの ToolTip を「仕事の開始・終了を登録します（日/週ビューに線で表示されます）\n出勤: {キー or 未登録}　退勤: {…}\n右クリック、または日/週ビューの線のラベルから時刻を編集できます」に
- 全滅したら ApplicationIdle でバルーン「ホットキーを登録できませんでした」「他のアプリ、またはトレイに残った旧インスタンスと競合している可能性があります」
- WM_HOTKEY(0x0312)：
  - 記録：`QuickToggleRecording()` → 記録中なら「記録を開始しました」「「{RecordingTitle}」\nタイトルはウィンドウ上部で変更できます」、停止したら「記録を保存しました」「タイムラインに追加しました」
  - 出勤：`ClockIn()` 成功「出勤を登録しました」/失敗「すでに勤務中です」（本文 WorkStatusText）
  - 退勤：`ClockOut()` 成功「退勤を登録しました」(WorkStatusText) / 失敗「出勤が登録されていません」「先に出勤を登録してください（記録の開始とは別の操作です）」
- 出勤と退勤をトグル1つにしないのは押し間違い防止のため

## 8. Undo のキー（MainWindow.Undo.cs）

`Window_PreviewKeyDownForUndo`：Ctrl が押されていなければ無視。`Keyboard.FocusedElement is TextBoxBase` なら無視（テキストの取り消しを優先）。Ctrl+Z（Shift なし）→ `UndoCommand`、Ctrl+Y または Ctrl+Shift+Z → `RedoCommand`、`e.Handled=true`。

## 9. 月セルのイベント（MainWindow.xaml.cs）

- `MonthCell_ItemClicked`（ダブルクリック）→ `EditCommand`
- `MonthCell_TodoClicked` → `EditTodoCommand`
- `MonthCell_ItemRightClicked` → その場で ContextMenu（「編集」「この内容で記録開始」「削除」）を生成して IsOpen
- `MonthCell_Clicked`（空白ダブルクリック）→ `e.Source` の `CalendarMonthCellControl.CellData.Date` で `AddScheduleItemAtDateCommand`

## 10. ミニ記録バー（MiniRecordingBar ウィンドウ）

### 10.1 表示制御（MainWindow）
- `UpdateMiniRecordingBar()`：`IsMiniRecordingBarEnabled && IsRecording` なら表示、そうでなければ Hide
- 初回だけ生成（`DataContext=vm`、`SetSavedPosition(vm.MiniRecordingBarLeft, vm.MiniRecordingBarTop)`、イベント購読）。以後は Show/Hide のみ（位置を保つ）
- **Owner を設定しない**（本体をトレイへ隠している間こそ必要）。代わりに `OnClosed` で確実に Close
- イベント：`MainWindowRequested` → ShowWindow、`PositionChanged` → `vm.SaveMiniRecordingBarPosition(bar.Left, bar.Top)`、`DisableRequested` → `vm.IsMiniRecordingBarEnabled=false`

### 10.2 VM（MainViewModel.MiniBar.cs）
- `IsMiniRecordingBarEnabled`（setter で SaveSettings）
- `MiniRecordingBarLeft/Top`（読み取り専用）、`SaveMiniRecordingBarPosition(left, top)`：NaN/∞ なら無視、保存

### 10.3 ウィンドウ
```
Title="記録中" SizeToContent=WidthAndHeight WindowStyle=None AllowsTransparency=True Background=Transparent
ShowInTaskbar=False ShowActivated=False Topmost=True ResizeMode=NoResize UseLayoutRounding SnapsToDevicePixels
WindowStartupLocation=Manual Opacity=0
```
- `Border BarRoot` Margin 8（影の余白）, 地 Surface, 枠 Border 1, CornerRadius 6, `DropShadowEffect(Blur 12, Depth 1, Direction 270, Opacity .28, Black)`, ToolTip「クリックで TimeRenderer を前面に／ドラッグで移動／右クリックでメニュー」, MouseLeftButtonDown=BarRoot_MouseLeftButtonDown
- ContextMenu：「TimeRenderer を表示」／区切り／「このバーを表示しない」
- Grid 4列：［色帯 Border Width 4 CornerRadius 2 Margin 5,5,0,5 地 `{RecordingBrush}` ｜ `{RecordingDisplayTitle}` MaxWidth 220 省略 Margin 8,0,0,0 12px TextPrimary ｜ `{RecordingElapsedText}` Margin 12,0,4,0 12px FontFamily "Consolas, Segoe UI" TextSecondary ｜ 停止ボタン Margin 4,4,5,4 → `ToggleRecordingCommand` ToolTip「記録を停止する」］
- 停止ボタンスタイル `MiniBarStopButtonStyle`：26×26, Padding 0, Hand, Focusable False, Foreground Danger。テンプレート Border back（CornerRadius 4 透明）＞ TextBlock「■」10px。MouseOver→地 DangerSubtle、Pressed→地 Danger・文字 White
- `OnSourceInitialized`：拡張スタイルに `WS_EX_TOOLWINDOW(0x80) | WS_EX_NOACTIVATE(0x08000000)` を追加（`GetWindowLongPtrW`/`SetWindowLongPtrW`、GWL_EXSTYLE=-20。失敗は無視）→ フォーカスを奪わず Alt+Tab に出ない
- `OnContentRendered`：Opacity&lt;1 なら `PlaceWindow()` して Opacity=1
- `PlaceWindow()`：保存位置があればそれ、無ければ `WorkArea` 右下から 24px 内側。仮想画面（VirtualScreenLeft/Top/Width/Height）の内側へ余白 8px で Clamp（はみ出す場合は min 側）
- クリックとドラッグの判別：押下時の Left/Top を控え `DragMove()`（InvalidOperationException は無視して return）→ 2px 以上動いていれば `PositionChanged`、そうでなければ `MainWindowRequested`
