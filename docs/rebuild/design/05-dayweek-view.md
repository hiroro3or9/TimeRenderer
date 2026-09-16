# 05. 日/週ビューと予定の編集

## 1. 表示範囲と日付計算（MainViewModel.Layout.cs / Commands.cs）

### 1.1 表示時間範囲
- `DisplayStartHour`（0〜`DisplayEndHour-1`に Clamp）、`DisplayEndHour`（`Start+1`〜24）。変更で `ScheduleGridHeight` 通知・`InitializeTimeLabels()`・SaveSettings
- `ScheduleGridHeight = (End - Start) * 60`
- `TimeLabels`：Start〜End（両端含む）の `"{h}:00"`
- `SnapMinutes`（1〜60 に Clamp、選択肢 `SnapMinutesOptions = [5,10,15,30]`、SaveSettings）

### 1.2 VisibleDays（UpdateVisibleDaysCore）
| モード | 日付 |
| --- | --- |
| Day | `[CurrentDate]` |
| Week | 月曜始まりの週のうち `EnabledDaysOfWeek` に含まれる日 |
| Month | その月1日を含む週の月曜から6週（42日）のうち有効曜日 |
| Sprint | `design/11` |
| SprintTimeline | `design/07` |
| その他 | 空 |

`UpdateVisibleDays()` = Core → `RebuildWorkDayMarkers()`。Core の最後で `RecalculateLayoutCore()` を即時実行。
`GetLayoutRange()`：VisibleDays の最小日〜最大日+1日（空なら null）。

### 1.3 ナビゲーション
- `Navigate(amount)`：`TransitionDirection` を方向に合わせ、Day→`GetNextActiveDay`（有効曜日だけを数えて移動）、Week→±7日、Month→±1月、Sprint/SprintTimeline→`GetAdjacentSprintDate`、Stats→期間種別（月→±1月、スプリント→隣接スプリント、週→±7日）、それ以外は変えない
- `GetAdjacentSprintDate(amount)`：現在スプリントの前後3か月の一覧で現在位置を探し、`idx+amount` を 0〜Count-1 に Clamp した開始日。見つからなければ ±14日
- `TodayCommand`：CurrentDate が今日より前なら Forward、後なら Backward にしてから `CurrentDate = DateTime.Today`
- `OpenTodayDayViewCommand`：CurrentDate=今日、CurrentViewMode=Day

## 2. レイアウト計算（RecalculateLayoutCore）

```
range = GetLayoutRange()
if range == null:
    StandardItems/AllDayItems/TodoChips/DailyScheduleItems を空に（件数>0 のときだけ代入）
    UpdateCalendarCells(); UpdateTimelineItems(); UpdateStats(); RebuildUnrecordedGaps(); RebuildTodayOverview(); return
visibleItems = ScheduleItems.Where(IsItemVisible).Where(!(End < rangeStart || Start >= rangeEnd))
segmentSources = IsDayOrWeekMode ? visibleItems : []
foreach item in segmentSources:
    if IsAllDay: allDay.Add(item); continue
    if End <= Start: segments.Add(new(item, Start, Start)); continue
    firstDay = max(Start.Date, rangeStart); lastDay = End.Date >= rangeEnd ? rangeEnd-1日 : End.Date
    for d in firstDay..lastDay:
        segStart = d == Start.Date ? Start : d; segEnd = End < d+1 ? End : d+1
        if segEnd <= segStart: continue      // 終了がちょうど0:00の空セグメント
        segments.Add(new(item, segStart, segEnd))
// 終日行の段
allDayRowCounts = {}; maxStack = 0
foreach group in allDay.GroupBy(StartTime.Date): Title 順に ColumnIndex = 0,1,2…; rowCounts[date]=件数; maxStack=max
maxStack = max(maxStack, RebuildTodoChips(rangeStart, rangeEnd, allDayRowCounts))   // design/09
AllDayPanelHeight = max(30, maxStack*24 + 6)
foreach group in segments.GroupBy(StartTime.Date):
    ScheduleLayoutHelper.CalculateClustersAndAssignColumns(group.OrderBy(Start).ThenByDescending(End).ToList())
// 日別インデックス（月セル・選択移動・吸着で使う）
foreach item in visibleItems:
    start = Start.Date; end = (End.TimeOfDay==0 && End>Start) ? End.Date-1日 : End.Date   // 終日 0:00〜翌0:00 を翌日に出さない
    start/end を range で Clamp; 各日の list に追加
各日の list = OrderBy(IsAllDay ? 0 : 1).ThenBy(StartTime)
DailyScheduleItems = daily; StandardItems = segments; AllDayItems = allDay
UpdateCalendarCells(); UpdateTimelineItems(); UpdateStats(); RebuildUnrecordedGaps(); RebuildTodayOverview();
```

### ScheduleLayoutHelper.CalculateClustersAndAssignColumns（重なり列割り当て）
```csharp
// 入力は開始昇順・終了降順に並んだ同じ日のセグメント
clusters: 先頭から走査し、segment.Start < clusterEnd なら同じクラスタ（clusterEnd = max）、そうでなければ新クラスタ
各クラスタ: columnEndTimes = []
  foreach seg: col = columnEndTimes.FindIndex(end => end <= seg.Start); 無ければ末尾に追加、あれば更新; seg.ColumnIndex = col
  クラスタ内全 seg.MaxColumnIndex = columnEndTimes.Count - 1
```

## 3. 画面構成（DayWeekView.xaml）

```
Grid 2行: Auto / *
Row0: StackPanel(地 Surface)
  ├ Border(下線) > Grid 3列[60 | * | Auto]
  │   Col2: Border Width={x:Static SystemParameters.VerticalScrollBarWidth}（下のスクロールバー幅と列を揃える）
  │   Col1: TransitioningContentControl(Content={CurrentDate}, TransitionDirection={TransitionDirection}, Stretch)
  │         Template: ItemsControl Height=30, ItemsSource=MultiBinding DateToVisibleDaysConverter[., Window.DataContext.CurrentViewMode, .EnabledDaysOfWeek]
  │                   UniformGrid Rows=1。各日: Border(右線, 地=DateToBackgroundBrushConverter[., IsDarkMode])
  │                   > TextBlock Text={Binding StringFormat='MM/dd (ddd)'} 中央 SemiBold Language="ja-JP" Foreground=DayOfWeekToBrushConverter[., IsDarkMode]
  ├ Grid 3列[60 | * | Auto]（終日エリア）
  │   Col2: スクロールバー幅スペーサー
  │   Col0: Border(右線, 地 Background) > 「終日」11px TextSecondary 右寄せ Margin 0,4,8,4
  │   Col1: TransitioningContentControl(Height={AllDayPanelHeight}) > Grid に Canvas を2枚重ねる（終日イベント / ToDo チップ）
  └ Border(下線)
Row1: ScrollViewer x:Name=MainScrollViewer 縦 Auto, Focusable=True, PreviewKeyDown=ScheduleView_PreviewKeyDown, 中ボタンスクロール有効
  > Grid Height={ScheduleGridHeight} 地 Surface 2列[60 | *]
     Col0: ItemsControl {TimeLabels}（StackPanel縦）: Border Height=60 上線 > TextBlock 右上 Margin 0,4,8,0 TextMuted 12
     Col1: TransitioningContentControl(Content={CurrentDate}) > 描画面 Grid（下記）
```

### 3.1 描画面（DataTemplate 内の Grid）
`Grid Background=Transparent AllowDrop=True Tag="ScheduleSurface"`、イベント：MouseLeftButtonDown/MouseMove/MouseLeftButtonUp=ScheduleBackground_*, ContextMenuOpening, DragOver, Drop。ContextMenu：「ここに貼り付け」。
子要素（下ほど手前）：

1. **縦罫線**：ItemsControl（DateToVisibleDays、UniformGrid Rows=1）各 Border 右線、地＝DateToBackgroundBrush（今日は PrimarySubtle）
2. **横罫線**：ItemsControl `Window.DataContext.TimeLabels`、Border Height 60 上線
3. **未記録の帯**（`design/12`）：ItemsControl `UnrecordedGaps`、Canvas ClipToBounds
4. **予定バー**：ItemsControl `x:Name=ScheduleItemsControl` `StandardItems`、ItemsPanel `Canvas x:Name=ScheduleCanvas ClipToBounds=True`
5. **出退勤マーカー**：ItemsControl `WorkDayMarkers`、Canvas ClipToBounds
6. **現在時刻**：Canvas ClipToBounds［Border 上線 Danger 2px Height 2 Width=Canvas幅、Canvas.Top=TimeToPosition[CurrentTime, DisplayStartHour]、`DropShadowEffect Color={DynamicResource DangerColor} Blur 4 Depth 0 Opacity .5` ／ Ellipse 8×8 Fill Danger Canvas.Left=-4 Top 同 TranslateTransform Y=-4］

### 3.2 各アイテムの配置（ItemContainerStyle）
Canvas 上に置くアイテム（予定・帯・マーカー・終日・チップ）は共通して ContentPresenter に：
- `Visibility` = DateToPageVisibilityConverter[日付, ItemsControl.DataContext(=CurrentDate), Window.DataContext.CurrentViewMode, EnabledDaysOfWeek]（FallbackValue Collapsed）
- `Canvas.Top` = TimeToPositionConverter[日付時刻, DisplayStartHour]（終日・チップは `IndexToTopMarginConverter(ColumnIndex|RowIndex)`）
- `Canvas.Left` / `Width` = DateToPagePositionConverter[開始, ColumnIndex, MaxColumnIndex, CurrentDate, ViewMode, Canvas.ActualWidth, IsAllDay, EnabledDays]（WIDTH パラメータで幅）。帯・マーカーは Column に `{StaticResource Zero}`、IsAllDay に `FalseValue`。終日は Column/Max に Zero。チップは IsAllDay に `TrueValue`

### 3.3 予定バーのテンプレート
```
Grid x:Name=ItemRoot Cursor=Hand Margin=1,0,1,0 MouseLeftButtonDown=ScheduleItem_MouseLeftButtonDown
     Height=DurationToHeight(DurationHours, ADD_EXTENSION)   ← 実時間+15px（L字の足）
     ToolTip={Item.ToolTipText} InitialShowDelay=250 ShowDuration=20000
  ContextMenu: 編集 / 名前の変更 (F2) / ─ / 複製 (Ctrl+D) / コピー (Ctrl+C) / ─ / この内容で記録開始 / この時間の使用アプリ / 削除
  Path x:Name=ItemShape Fill={BackgroundColor} Stroke={BorderBrush} StrokeThickness=1 Opacity=0.9
       Effect=DropShadow(Black, Blur 6, Depth 2, Opacity .1)
       Data=LShapeGeometryConverter[ItemRoot.ActualWidth, ItemRoot.ActualHeight]
  Border Padding=8,6 Margin=0,0,0,15 Top > StackPanel
       TextBlock ItemTitleText {Title} Bold 省略 Foreground=BrushToContrastText(BackgroundColor)
       TextBlock ItemContentText {Content} 折返し 11px 省略 Margin 0,2,0,0 Foreground=BrushToContrastText(…, Muted)
       TextBlock ItemTimeText 10px Margin 0,2,0,0 Muted: Run {Item.StartTime:HH:mm} " - " {Item.EndTime:HH:mm}（分割されても元の時間帯）
Triggers（この順）:
  ItemRoot.IsMouseOver → ItemShape Opacity 1, Stroke {TextSecondaryBrush}
  Item.IsPlanned=True  → Fill=BrushToSubtleBackground(BackgroundColor), Stroke={BackgroundColor}, StrokeThickness 2, Effect null,
                          Title=TextPrimary, Content=TextSecondary, Time=TextMuted
  Item.IsSelected=True → Opacity 1, Stroke {TextPrimaryBrush}, StrokeThickness 2.5
```
（予定＝枠線＋薄塗り、実績＝フル塗り＋影。選択は最優先の太枠）

### 3.4 終日イベントのテンプレート
`Border x:Name=AllDayItemRoot` 地 `{BackgroundColor}` CornerRadius 6 Padding 6,2 Height 24 Opacity .95 枠 White 1 Hand、Effect DropShadow(Blur 4, Depth 1, Opacity .1)、MouseLeftButtonDown=ScheduleItem_MouseLeftButtonDown。
ContextMenu：編集 / この内容で記録開始 / この時間の使用アプリ / 削除。中身 `TextBlock AllDayItemText {Title}` 12px 省略 Foreground=ContrastText。
`IsPlanned=True`：地 Subtle、枠 `{BackgroundColor}` 2、Effect null、文字 TextPrimary。

### 3.5 ToDo チップ・未記録の帯・出退勤マーカー
- チップ：`design/09` §7
- 帯：`design/12` §2
- マーカー（`WorkDayMarker(Time, IsStart, Label, IsAuto, Date, Note)`）：
  ```
  Grid Height 18 Top RenderTransform TranslateY=-16（線が Canvas.Top に来るよう上へ吹き出す）
    Border MarkerLine Bottom Height 2 IsHitTestVisible=False 地 {WorkStartBrush} Opacity .9
    Border MarkerChip Left/Bottom Margin 2,0,0,1 地 {WorkStartBrush} CornerRadius 3 Padding 5,1 Opacity .95 Hand
           ToolTip={ToolTipText} MouseLeftButtonDown=WorkDayMarker_MouseLeftButtonDown
           ContextMenu: 勤務時間を編集 / この日の勤務記録を削除
      TextBlock {Label} 10px SemiBold White
  IsStart=False → Line/Chip 地 {WorkEndBrush}
  IsAuto=True   → Line Opacity .55, Chip Opacity .75
  ```

## 4. 操作（コードビハインド）

### 4.1 クリック・選択（DayWeekView.xaml.cs）
- `ScheduleItem_MouseLeftButtonDown`：
  - ClickCount==2 → ドラッグ候補を破棄 → `EditCommand(item)`（DataContext が ScheduleSegment / ScheduleItem のどちらでも `ScheduleItemMenu.ResolveScheduleItem` で解決）、Handled
  - ClickCount==1 かつ ScheduleSegment → **先に** `BeginPotentialDrag`（選択の再レイアウトで要素がツリーから外れる前に親 Canvas を掴む）→ `SelectedItem = segment.Item` → `MainScrollViewer.Focus()`
- マーカーのクリック → `EditWorkDayCommand(marker)`、Handled（背景ドラッグを始めない）
- `Loaded`：`ScrollToTimeRequested` と `PropertyChanged` を購読、初回だけ現在時刻へスクロール
- `ScrollDayViewToTime(t)`：`y = (t.Hour - DisplayStartHour)*60 + t.Minute`、`offset = max(0, y - ViewportHeight/2)`
- `OnScrollToTimeRequested`：Dispatcher（Loaded 優先度）で上を実行
- PropertyChanged で CurrentDate/CurrentViewMode/VisibleDays が変わったら、インライン入力中なら確定して閉じる

### 4.2 予定バーのドラッグ（DayWeekView.Drag.cs）
定数：`DragThresholdPx=4`, `MagnetTolerancePx=6`, `FineSnapMinutes=1`。モード：`None, Move, ResizeTop, ResizeBottom`。

- `BeginPotentialDrag(element, segment, e)`：親 Canvas、アイテム、元の Start/End、Canvas 上の開始位置、ゾーン判定（`GetZone`）、掴んだ日の VisibleDays インデックスを控える
- `GetZone(element, y)`：h≤0→Move。上端 `min(8, h/3)` 以内→ResizeTop、下端 `min(23, h/3)` 以内（L字の足15pxを含む）→ResizeBottom、他 Move
- ビュー全体の `PreviewMouseMove`：ドラッグ候補が無ければホバーカーソルだけ更新（バーの Grid を親方向に探し、ゾーンが Move なら Hand、端なら SizeNS）。左ボタンが離れていればキャンセル。閾値を超えたらドラッグ開始：
  - Move かつ Alt 押下 → `ViewModel.BeginItemCopyDrag(item)`（元位置に写しを置く。成功で複製ドラッグ）
  - 複製でなければ `BeginItemDragUndo(item)`
  - Canvas に MouseMove/MouseLeftButtonUp/LostMouseCapture を購読し `Mouse.Capture(canvas)`
  - `Mouse.OverrideCursor`：伸縮 SizeNS / 複製 Cross / 移動 SizeAll
- `ProcessDragMove(pos)`：`deltaHours = (pos.Y - start.Y) / 60`。`fine = Alt && !複製`、`step = fine ? 1 : SnapMinutes`
  - Move：`rawStart = origStart + delta`。列数&gt;1 なら `newCol = clamp((int)(pos.X/列幅))` とし、`VisibleDays[newCol] - VisibleDays[origCol]` の日数差を加算。`magnet = fine ? null : TrySnapRange(rawStart, rawStart+長さ)`。`newStart = magnet ? rawStart+Offset : SnapTime(rawStart, step)`、`newEnd = newStart+長さ`、ガイド=magnet.GuideTime
  - ResizeTop：`rawStart` を TrySnapEdge → 無ければ SnapTime。`newStart > origEnd - step分` なら `origEnd - step分` に（ガイド消す）
  - ResizeBottom：同様に終了側。`newEnd < origStart + step分` なら補正
  - `ShowMagnetGuide(guide)` → `ViewModel.UpdateItemTimesPreview(item, newStart, newEnd)`
- **吸着 → 刻み幅の順**に見る（刻みに乗っていない隣の端 10:23 などへ吸着できるように）
- `SnapTime(t, step)`：step≤1 は分に四捨五入、それ以外 `Math.Round(分/step)*step`
- `TrySnapEdge`/`TrySnapRange`：`IsMagnetSnapEnabled` が false なら null。候補は `ViewModel.GetSnapTargets(date, dragItem)` を日付ごとにキャッシュ（Range は終了日が違えば両日の候補を連結）。許容 = 6px/60 時間
- 吸着ガイド：描画面（Tag="ScheduleSurface" を親方向に探す）に `Border`（HitTest なし、高さ2、地 `PrimaryBrush` を SetResourceReference）を足し、`Width=列幅`, `Margin=(列*列幅, top-1)`、top=(時刻-DisplayStartHour)*60
- 終了（`EndDrag(commit)`）：フラグを先に下ろし、Canvas の購読解除 → Capture(null)・OverrideCursor=null → ガイド非表示。開始済みなら commit で `CommitItemDrag()`、キャンセルで `CancelItemDrag(item, origStart, origEnd)`。PreviewMouseLeftButtonUp は `EndDrag(commit: started)`。LostMouseCapture は開始済みなら commit
- 親をたどる `GetParentSafe`：Visual/Visual3D は VisualTreeHelper、FrameworkContentElement は `.Parent`、他は LogicalTreeHelper（TextBlock 内の Run 対策）

### 4.3 VM 側のドラッグ受け口
- `UpdateItemTimesPreview(item, s, e)`：同じなら false。`_isBatchUpdatingItem` の間に代入 → `RecalculateLayout()`（保存しない）
- `CommitItemDrag()`：ドラッグ対象が仮想アイテム（RoutineId あり）なら `ClearItemDragUndo()` → `CommitVirtualItemDrag(item, before)`（`design/11`）。それ以外は `CommitItemDragUndo()` → `SaveData()`
- `BeginItemCopyDrag(item)`：仮想なら false。`CreateCopy(Capture(item), item.StartTime)` を ScheduleItems に Add、`_dragCopyClone` に保持、`BeginItemDragUndo(item)`、true
- `CancelItemDrag(item, s, e)`：時刻を戻し、写しがあれば Remove、`ClearItemDragUndo()`
- `GetSnapTargets(date, exclude)`（Snapping.cs）：`FlushPendingLayout()` → その日の DailyScheduleItems のうち終日と exclude を除き、Start がその日なら Start、End がその日なら End ／ その日の勤務記録の Start・End ／ 今日なら現在時刻（秒を落とす）

### 4.4 MagnetSnapHelper
```csharp
public sealed record Result(TimeSpan Offset, DateTime GuideTime);
public static Result? SnapEdge(DateTime edge, IReadOnlyList<DateTime> targets, TimeSpan tolerance)
{   // 距離が tolerance 以下で最も近いもの。同距離は先に渡された候補
    Result? best = null;
    foreach (var t in targets) { var off = t - edge; var d = off.Duration();
        if (d > tolerance) continue; if (best != null && d >= best.Offset.Duration()) continue; best = new(off, t); }
    return best;
}
public static Result? SnapRange(DateTime start, DateTime end, IReadOnlyList<DateTime> targets, TimeSpan tol)
{   var s = SnapEdge(start, targets, tol); var e = SnapEdge(end, targets, tol);
    if (s == null) return e; if (e == null) return s;
    return s.Offset.Duration() <= e.Offset.Duration() ? s : e; }
```

### 4.5 空き領域の操作（DayWeekView.Schedule.cs）
- `SnapHours(y, stepsPerHour)`：`hours = y/60 + DisplayStartHour`、`Math.Floor(hours*steps)/steps` を 0〜`24-1/steps` に Clamp（steps = 60/SnapMinutes）
- `ResolveDateFromX(grid, x)`：`VisibleDays[clamp((int)(x/(幅/日数)))]`
- `ScheduleBackground_MouseLeftButtonDown`：バー上なら無視。インライン入力中なら確定。`MainScrollViewer.Focus()`
  - ClickCount==2 → 範囲ドラッグ取消 → 開始=SnapHours、`StartInlineCreate(grid, start, start+1h)`、Handled
  - それ以外 → `ClearSelectionCommand` → 範囲ドラッグ候補（開始Y、アンカー時刻）
- MouseMove：左ボタンが離れたら取消。Y 差 5px 超でドラッグ開始（Capture、Cross カーソル）→ プレビュー矩形更新
- プレビュー矩形：描画面に実行時追加する `Border`（HitTest なし、Top、Opacity .35、枠1、地 `PrimaryBrush`・枠 `TextPrimaryBrush` を SetResourceReference）。親が変わっていたら作り直す。週ビューはアンカーの日の列だけ（Left, Width=列幅, Margin=(列*列幅, top)）、日ビューは Stretch
- MouseLeftButtonUp：開始済みなら確定：日付はアンカーの日で固定、`dropped = 日付 + SnapHours(y)`、小さい方を開始、`end<=start` なら `start + SnapMinutes`、`StartInlineCreate(grid, start, end)`
- ContextMenuOpening：右クリック位置の時刻を控え、メニュー項目を `HasClipboardItem && 時刻あり` で有効化、Header を「ここに貼り付け（{ClipboardItemTitle}）」/「ここに貼り付け」
- 「ここに貼り付け」→ `PasteItemCommand(時刻)`
- DragOver：`TodoItem` データなら Copy。Drop：落とした位置の時刻で `ViewModel.BlockTimeForTodo(todo, start)`

### 4.6 キーボード（ScheduleView_PreviewKeyDown）
Day/Week モード以外、または `FocusedElement is TextBoxBase` なら無視。

| キー | 動作 |
| --- | --- |
| Ctrl+D | `DuplicateItemCommand` |
| Ctrl+C | `CopyItemCommand` |
| Ctrl+V | `PasteItemCommand(マウスが描画面上ならその時刻, それ以外 null)` |
| その他の Ctrl 併用 | 素通し |
| ↑ / ↓ | `MoveSelectionCommand(-1 / 1)` |
| ← / → | `PreviousCommand` / `NextCommand` |
| Enter | `EditSelectedCommand` |
| F2 | 選択中なら `StartInlineRename` |
| Delete | `DeleteSelectedCommand` |
| Esc | `ClearSelectionCommand` |
| T | `TodayCommand` |

### 4.7 インライン入力（DayWeekView.InlineEdit.cs ＋ MainViewModel.InlineEdit.cs）
- `StartInlineCreate(surface, start, end)`：既存入力を確定 → `ViewModel.BeginInlineCreate(start,end)` → 入力欄を開く。開けなければ `CancelInlineCreate` して `AddScheduleItemInRangeCommand((start,end))`
- `StartInlineRename(item)`：終日・仮想なら編集ダイアログ。描画面が無ければダイアログ。`_inlineBefore = BeginInlineRename(item)` で入力欄を開く（失敗ならダイアログ）
- 入力欄：`TextBox`（Left/Top、Margin=(列左+2, top)、Width=max(60, 列幅-4)、Height 26、12px Bold、Padding 6,2、VerticalContentAlignment Center、BorderThickness 2、MaxLength 200、地 Surface・文字 TextPrimary・枠 Primary を SetResourceReference）。top=(開始時刻-DisplayStartHour)*60 を 0〜`ScheduleGridHeight-26` に Clamp
- 画面外なら `VerticalOffset = max(0, top - ViewportHeight/3)`。Dispatcher（Input）で Focus+SelectAll
- Enter：確定（Ctrl+Enter なら確定後に編集ダイアログ）／Esc：取消／LostKeyboardFocus：確定。再入防止フラグ
- 閉じる：入力欄を外し、新規なら `CommitInlineCreate(item, text)`（空なら取りやめ）・取消なら `CancelInlineCreate`、名称変更なら `CommitInlineRename(item, before, text)`。`MainScrollViewer.Focus()`
- VM：
  - `BeginInlineCreate(start, end)`：`end<=start` なら `start+SnapMinutes`。Kind は **end ≤ 現在 なら Recorded、それ以外 Planned**、Title=""、先頭カテゴリの色と Id、`DefaultProjectCode?.Id`。Add し SelectedItem に（**履歴にはまだ積まない**）
  - `CommitInlineCreate(item, title)`：Trim が空なら Cancel して false。Title 設定 → `RecordAdd` → SaveData → true
  - `CancelInlineCreate(item)`：Remove、選択解除、SaveData
  - `CommitInlineRename(item, before, title)`：空か同じなら何もしない。設定 → `RecordModify(item, before, "タイトルの変更")` → SaveData

### 4.8 コンテキストメニュー
`ScheduleItemMenu.ExecuteOnMenuTarget(sender, command)`：`MenuItem.Parent is ContextMenu && PlacementTarget is FrameworkElement` の DataContext から `ResolveScheduleItem`（ScheduleItem / ScheduleSegment.Item / TimelineBar.Item）→ CanExecute なら Execute。
「名前の変更 (F2)」は SelectedItem にしてから `StartInlineRename`。

## 5. 予定の追加・編集・削除（MainViewModel.Commands.cs）

- `AddCommand` → `AddViaDialog(null)`
- `AddScheduleItemAtDateCommand(DateTime)`：Planned、その日 9:00〜10:00、Title「新しい予定」、先頭カテゴリの色/Id、既定プロジェクトコード
- `AddScheduleItemAtTimeCommand(DateTime)`：同上で開始〜+1時間
- `AddScheduleItemInRangeCommand((DateTime, DateTime))`：`end<=start` なら +1時間
- `AddViaDialog(template)`：`ShowScheduleEditDialog(template, [..Categories], GetTitleSuggestions(), GetSelectableProjectCodes(template?.ProjectCodeId), DefaultProjectCode)` → 結果を Add → `RecordAdd`
- `EditCommand(item)`：仮想アイテムは `EditRoutineOccurrence`（`design/11`）。それ以外はダイアログ → `before=Capture` → `_isBatchUpdatingItem` の間に Title, Content, Kind, StartTime, EndTime, IsAllDay, BackgroundColor, CategoryId, ProjectCodeId, RemindAtStart, AutoStartRecording, ForceStartRecording を書き戻し → `RecordModify(item, before, "編集")` → RecalculateLayout → SaveData
- `DeleteCommand(item)`：仮想は `DeleteRoutineOccurrence`。確認「「{Title}」を削除しますか？」タイトル「削除確認」→ `RecordRemove` → Remove → `AddRoutineExclusionFor(item)`（定期予定由来ならその日を除外日に）
- `StartRecordingFromItemCommand(item)`（`design/10`）

## 6. 予定・実績の編集ダイアログ（ScheduleEditDialog）

`Title="予定・実績の編集" Height=700 Width=560 CenterOwner NoResize 地 Background`。`ResultItem`（OK 時の新インスタンス）。
コンストラクタ引数：`(ScheduleItem? existing, IReadOnlyList<CategoryInfo>? categories, IReadOnlyList<string>? titleSuggestions, IReadOnlyList<ProjectCodeInfo>? projectCodes, ProjectCodeInfo? defaultProjectCode)`

### 6.1 レイアウト（Grid Margin 20、7行）
1. Grid［`TextBlock DialogHeadingText`（文言は `$"{kindLabel}を{(編集モード ? "編集" : "追加")}"`、kindLabel は実績なら「実績」他「予定」。種別ラジオの切替でも更新）20px SemiBold ｜ 右：RadioButton「予定」(PlannedKindRadio, Width 86, SegmentedLeftStyle, 既定 Checked) ＋「実績」(RecordedKindRadio, 86, SegmentedRightStyle)］Margin 0,0,0,18
2. 「タイトル」FieldLabel ＋ `ComboBox TitleCombo IsEditable IsTextSearchEnabled=False MaxDropDownHeight 240 FontSize 14 Padding 6,6`（Margin 0,0,0,14）
3. 「内容」＋ `TextBox ContentTextBox` InputTextBoxStyle Height 72 折返し AcceptsReturn 縦スクロール Auto
4. Border 地 `MutedBackgroundBrush` CornerRadius 8 Padding 12 Margin 0,0,0,14：
   - 1段目 Grid[220|Auto]：「日付」＋ `DatePicker` Padding 4,4 ｜ `CheckBox AllDayCheckBox`「終日」Margin 16,0,0,4 Bottom
   - 2段目 Grid[Auto|30|Auto|*] Margin 0,12,0,0：StartTimePanel［「開始」＋ 時 Combo 58px ＋「:」16px Bold Margin 5,0 ＋ 分 Combo 58px］｜「→」TextMuted 下寄せ ｜ EndTimePanel［「終了」…］｜ Margin 14,0,0,0 下寄せ［「所要」＋ 横並び［Border 地 PrimarySubtle CornerRadius 10 Padding 8,4 ＞ `DurationText`「1時間」SemiBold Primary ｜ `NextDayBadge` 地 WarningSubtle CornerRadius 10 Padding 8,4 Margin 6,0,0,0 ＞「翌日」SemiBold WarningText（Collapsed）］］
5. Grid[220|*]：「カテゴリ」＋ `ColorCombo`（項目：Border 20×20 CornerRadius 3 地 Brush 枠 Border 1 Margin 0,0,8,0 ＋ Name）｜「プロジェクトコード」＋ `ProjectCodeCombo` DisplayMemberPath=DisplayName
6. `ReminderPanel`（Top）：下線 Border Margin 0,0,0,12 ／「開始時の動作」／ Grid 3等分［「何もしない」Left(既定) ｜「通知」Middle ｜「自動記録」Right］／ `CheckBox ForceStartCheckBox`「記録中でも切り替える（現在の記録は停止・保存）」Margin 8,10,0,0（Visibility = AutoStartActionRadio.IsChecked）
7. 右寄せ Margin 0,16,0,0：「キャンセル」100×36 Margin 0,0,12,0 Base ／「OK」100×36 Primary

### 6.2 初期化
- 時の選択肢 "00"〜"23"、分は **"00"〜"59"（1分刻み）**
- カテゴリ選択肢：`categories` が空なら既定カテゴリ。`ColorOption(Name, Brush, CategoryId)`。既存アイテムの CategoryId も色もどれにも一致しなければ「（現在の色）」を追加
- プロジェクトコード選択肢：先頭「（未設定）」(null) ＋ 渡されたコード。既存の Id が無ければ「（不明なプロジェクトコード）」を追加
- 編集：各値をセット。カテゴリは Id 一致 → 色一致 → 先頭。開始時の動作は AutoStart→「自動記録」、RemindAtStart→「通知」、他「何もしない」。Kind が Recorded なら「実績」
- 新規（existing==null）：日付=今日、開始=現在の時:分、終了=(時+1)%24:分、先頭カテゴリ、既定プロジェクトコード、予定、何もしない
- `UpdateTimePanelState()`：見出し「{予定|実績}を{追加|編集}」、終日なら時刻パネル無効、ReminderPanel は「時刻あり かつ 予定」のときだけ Visible
- `UpdateTimeSummary()`：終日→「終日」。時分が揃わなければ「—」。差分分数≤0 なら +24h して「翌日」バッジ。表示「{h}時間{m}分」/「{h}時間」/「{m}分」

### 6.3 OK
- タイトル空白→MessageBox「タイトルを入力してください。」「入力エラー」(OK, Warning)。日付未選択→「日付を選択してください。」。時刻未選択→「時刻を選択してください。」
- 終日：開始=日付 0:00、終了=翌日 0:00
- 時刻あり：`ComposeDateTime(date, h, m, original)`＝時・分が元と同じなら**元の TimeOfDay（秒含む）を維持**。`end <= start` なら end+1日
- `ResultItem = new ScheduleItem{ Kind, Title=Trim, Content=Trim, Start, End, IsAllDay, BackgroundColor=選択色(無ければ LightBlue), CategoryId, ProjectCodeId, RemindAtStart = 予定&&!終日&&通知, AutoStartRecording = 予定&&!終日&&自動記録, ForceStartRecording = 予定&&!終日&&自動記録&&強制 }`、`DialogResult=true`
- キャンセル：`DialogResult=false`
