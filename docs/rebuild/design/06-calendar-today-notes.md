# 06. 月/スプリントカレンダー・今日ホーム・ふりかえり一覧

## 1. 表示日（UpdateVisibleDaysCore の月・スプリント部分）

- **月**：その月の1日を含む週の月曜（`diff = (7 + (1日.DayOfWeek - Monday)) % 7`）から **6週間**。有効曜日（`EnabledDaysOfWeek`）の日だけ追加
- **スプリント**：`sprint = SprintHelper.GetSprintForDate(ManualSprints, CurrentDate)`、`start = GetStartOfWeek(sprint.StartDate)`（月曜）、`end = GetStartOfWeek(sprint.EndDate) + 6`（日曜）。`SprintWeekRows = max(1, (int)((end-start).TotalDays + 1) / 7)`。週ごとに有効曜日の日を追加（end 超は break）
- 曜日ヘッダー `EnabledDayHeaders`：`DayOfWeekHelper.WeekOrder`（月→日）のうち有効な曜日を `record DayHeaderInfo(string Name, DayOfWeek DayOfWeek)`（Name は「月」「火」…）で返す。`EnabledDaysOfWeek` 変更時に `EnabledDaysCount` と一緒に通知

## 2. セルのデータ（MainViewModel.Layout.cs）

```csharp
public class CalendarCellViewModel(DateTime date, bool isCurrentMonth, bool isToday,
    IReadOnlyList<ScheduleItem> dailyItems, IReadOnlyList<TodoItem> dailyTodos)
{
    public DateTime Date { get; } = date;
    public string DayText => Date.Day.ToString();
    public DayOfWeek DayOfWeek => Date.DayOfWeek;
    public bool IsCurrentMonth { get; } = isCurrentMonth;
    public bool IsToday { get; } = isToday;
    public IReadOnlyList<ScheduleItem> DailyItems { get; } = dailyItems;
    public IReadOnlyList<TodoItem> DailyTodos { get; } = dailyTodos;
}

private void UpdateCalendarCells()        // RecalculateLayoutCore の末尾から呼ぶ
{
    if (CurrentViewMode != ViewMode.Month && CurrentViewMode != ViewMode.Sprint) return;
    var cells = new List<CalendarCellViewModel>();
    var sprint = CurrentViewMode == ViewMode.Sprint ? SprintHelper.GetSprintForDate(ManualSprints, CurrentDate) : null;
    var todosByDate = GetVisibleTodosByDueDate();      // design/09 §6（優先度降順→CreatedAt）
    foreach (var day in VisibleDays) {
        DailyScheduleItems.TryGetValue(day.Date, out var items); items ??= [];
        todosByDate.TryGetValue(day.Date, out var todos); todos ??= [];
        bool isCurrent = CurrentViewMode == ViewMode.Month
            ? day.Month == CurrentDate.Month && day.Year == CurrentDate.Year
            : sprint != null && day.Date >= sprint.StartDate.Date && day.Date <= sprint.EndDate.Date;
        cells.Add(new CalendarCellViewModel(day, isCurrent, day.Date == DateTime.Today, items, todos));
    }
    CalendarCells = cells;
}
```
`DailyScheduleItems`（日付→その日に掛かる表示対象アイテム）は `design/05` §3 の RecalculateLayoutCore で作る。

## 3. CalendarGridView（Views/CalendarGridView.xaml）

月とスプリントで共用する UserControl。`DependencyProperty Rows`（int、既定 6）。

```xml
<Grid>
  <Grid.RowDefinitions><RowDefinition Height="30"/><RowDefinition Height="*"/></Grid.RowDefinitions>
  <!-- 曜日ヘッダー -->
  <Border BorderBrush="{DynamicResource BorderBrush}" BorderThickness="0,0,0,1">
    <ItemsControl ItemsSource="{Binding DataContext.EnabledDayHeaders, RelativeSource={RelativeSource AncestorType=Window}}">
      <ItemsControl.ItemsPanel><ItemsPanelTemplate><UniformGrid Rows="1" Background="{DynamicResource BackgroundBrush}"/></ItemsPanelTemplate></ItemsControl.ItemsPanel>
      <ItemsControl.ItemTemplate><DataTemplate>
        <TextBlock Text="{Binding Name}" HorizontalAlignment="Center" VerticalAlignment="Center" FontWeight="SemiBold" Height="30" LineHeight="30">
          <!-- 既定 TextPrimaryBrush、Saturday→PrimaryBrush、Sunday→DangerBrush（DataTrigger） -->
        </TextBlock>
      </DataTemplate></ItemsControl.ItemTemplate>
    </ItemsControl>
  </Border>
  <!-- セル -->
  <controls:TransitioningContentControl Grid.Row="1" Content="{Binding CurrentDate}" TransitionDirection="{Binding TransitionDirection}"
      TransitionAxis="Vertical" HorizontalContentAlignment="Stretch" VerticalContentAlignment="Stretch">
    <controls:TransitioningContentControl.ContentTemplate><DataTemplate>
      <ItemsControl ItemsSource="{Binding DataContext.CalendarCells, RelativeSource={RelativeSource AncestorType=Window}}">
        <ItemsControl.CacheMode><BitmapCache EnableClearType="True" RenderAtScale="1"/></ItemsControl.CacheMode>
        <ItemsControl.ItemsPanel><ItemsPanelTemplate>
          <UniformGrid Rows="{Binding Rows, RelativeSource={RelativeSource AncestorType=UserControl}}"
                       Columns="{Binding DataContext.EnabledDaysCount, RelativeSource={RelativeSource AncestorType=Window}}"/>
        </ItemsPanelTemplate></ItemsControl.ItemsPanel>
        <ItemsControl.ItemTemplate><DataTemplate>
          <controls:CalendarMonthCellControl CellData="{Binding}"
              MutedBackgroundBrush="{DynamicResource MutedBackgroundBrush}" TextSecondaryBrush="{DynamicResource TextSecondaryBrush}"
              TextPrimaryBrush="{DynamicResource TextPrimaryBrush}" TodayBackgroundBrush="{DynamicResource PrimarySubtleBrush}"
              SundayForegroundBrush="{DynamicResource DangerBrush}" SaturdayForegroundBrush="{DynamicResource PrimaryBrush}"
              WeekdayForegroundBrush="{DynamicResource TextPrimaryBrush}"/>
        </DataTemplate></ItemsControl.ItemTemplate>
      </ItemsControl>
    </DataTemplate></controls:TransitioningContentControl.ContentTemplate>
  </controls:TransitioningContentControl>
</Grid>
```

MainWindow での配置（Grid.Row=2、`design/04`）：
- 月：`Rows="6"`、`Visibility=IsMonthMode`、`Background={SurfaceBrush}`
- スプリント：`Rows="{Binding SprintWeekRows}"`、`Visibility=IsSprintMode`
- どちらも `controls:CalendarMonthCellControl.ItemClicked="MonthCell_ItemClicked"`、`ItemRightClicked="MonthCell_ItemRightClicked"`、`TodoClicked="MonthCell_TodoClicked"`、`CellClicked="MonthCell_Clicked"` を付ける（ハンドラの中身は `design/04`）

**セル間の罫線は描かない**（背景色の差だけで区切りが見える）。

## 4. CalendarMonthCellControl（Controls/CalendarMonthCellControl.cs）

多数のセルを軽く描くため、`FrameworkElement` を継承して `OnRender` で直接描画する。

### 4.1 依存関係プロパティ（すべて `AffectsRender`）
`CellData`(CalendarCellViewModel), `MutedBackgroundBrush`, `TextSecondaryBrush`, `TextPrimaryBrush`, `TodayBackgroundBrush`, `SundayForegroundBrush`, `SaturdayForegroundBrush`, `WeekdayForegroundBrush`（いずれも Brush、既定 null）

未設定時の固定フォールバック（Freeze 済み）：Muted `#F9FAFB`、TextSecondary `#6B7280`、TextPrimary `#111827`、Today `#EFF6FF`、Sunday `#DC2626`、Saturday `#2563EB`、Weekday `#1F2937`。ToDo 期限超過色は常に `#DC2626`。

### 4.2 ルーティングイベント（Bubble）
- `CellClicked`（RoutedEventHandler）
- `ItemClicked` / `ItemRightClicked`（`EventHandler<ScheduleItemClickedEventArgs>`、`Item` を持つ）
- `TodoClicked`（`EventHandler<TodoClickedEventArgs>`、`Todo` を持つ）

### 4.3 定数・フォント
- Typeface：`Segoe UI` Normal、日付は **SemiBold**、項目は Normal
- `DayFontSize=12`, `ItemFontSize=11`, `ItemHeight=18`, `ItemMargin=2`, `ItemPadding=4`

### 4.4 行の組み立てとレイアウト（描画とヒットテストで共通）
```csharp
private readonly record struct CellRow(ScheduleItem? Item, TodoItem? Todo);
List<CellRow> BuildRows() => DailyItems を先に、その後 DailyTodos を並べる;

(double StartY, int DisplayCount, bool HasMore) GetItemLayout(double height, int rowCount)
{
    double pixelsPerDip = VisualTreeHelper.GetDpi(this).PixelsPerDip;       // 高DPIでのクリックずれ防止のため OnRender と同じ DPI を使う
    var dayText = new FormattedText(data.DayText, CurrentUICulture, LeftToRight, _dayTypeface, 12, Brushes.Black, pixelsPerDip);
    double startY = dayText.Height + 8;
    int maxItems = (int)((height - startY) / (ItemHeight + ItemMargin));
    int displayCount = Math.Clamp(rowCount, 0, Math.Max(0, maxItems));
    bool hasMore = rowCount > maxItems;
    if (hasMore && maxItems > 0) displayCount = maxItems - 1;               // 「+N 件」の分1つ減らす
    return (startY, displayCount, hasMore);
}
```

### 4.5 OnRender
1. 背景：`!IsCurrentMonth` → Muted、`IsToday` → Today、他 Transparent。`Rect(0,0,W,H)` を塗る（Transparent でも塗る＝ヒットテストが効く）
2. 日付文字：色は `!IsCurrentMonth`→TextSecondary、日曜→Sunday、土曜→Saturday、他 Weekday。位置 `(4, 4)`
3. 行：`i < displayCount` で `rect = Rect(2, y, W-4, 18)`、描いたら `y += 20`
   - **予定・実績** `DrawScheduleItem`：塗り = 予定なら `BrushToSubtleBackgroundConverter.CreateSubtleBrush(item.BackgroundColor)`、実績なら `BackgroundColor`。予定のみ枠 `Pen(BackgroundColor, 1.5)`。`DrawRoundedRectangle(…, 4, 4)`。文字は TextPrimary（実績は常に固定 `#111827`、予定は TextPrimaryBrush）11px、`MaxTextWidth = W_rect - 8`、`MaxTextHeight=18`、`CharacterEllipsis`、x=rect.X+4、縦中央
   - **ToDo** `DrawTodo`：枠色 = 期限超過なら `#DC2626`、他 `todo.Brush`。太さ = 優先度高なら 2、他 1。塗り Muted。角丸 9（=高さの半分、ピル形）。文字色 = 期限超過なら `#DC2626`、他 TextPrimary。`MaxTextWidth = W_rect - 12`、x=rect.X+6
4. `hasMore` なら「+{rows.Count - displayCount} 件」（11px, TextSecondary）を `(4, y)` に描く

### 4.6 マウス
- `OnMouseMove`：行の上なら Hand、それ以外 Arrow
- `OnMouseLeftButtonDown`：**`ClickCount == 2` のときだけ**。行が予定→`ItemClicked`、ToDo→`TodoClicked`、行以外→`CellClicked`。`e.Handled = true`
- `OnMouseRightButtonDown`：予定の行なら `ItemRightClicked` を発火し Handled（ToDo・空白は何もしない）

## 5. 今日ホーム（Views/TodayView.xaml / MainViewModel.Today.cs）

### 5.1 ViewModel
```csharp
public sealed record TodayPlanEntry(ScheduleItem Item, string TimeText, string DurationText, string CategoryName, bool IsNext, bool IsPast);
```
| プロパティ | 初期値 / 派生 |
| --- | --- |
| `TodayPlanItems`（IReadOnlyList） | 変更で `HasTodayPlans` 通知 |
| `TodayTodoItems`（IReadOnlyList&lt;TodoItem&gt;） | 変更で `HasTodayTodos` 通知 |
| `TodayNextPlan`（TodayPlanEntry?） | 変更で `HasTodayNextPlan` 通知 |
| `TodayRecordedSummaryText` | "0:00" |
| `TodayPlannedSummaryText` | "0:00" |
| `TodayGapSummaryText` | "なし" |
| `TodayWorkloadSummaryText` | "見積もり未設定" |
| `IsTodayWorkloadOver` | false |

```csharp
private void RebuildTodayOverview()
{
    if (ScheduleItems == null) return;
    var now = DateTime.Now; var dayStart = now.Date; var dayEnd = dayStart.AddDays(1);
    var plans = ScheduleItems.Where(i => i.IsPlanned && i.StartTime < dayEnd && i.EndTime > dayStart)
                             .OrderBy(i => i.StartTime).ThenBy(i => i.Title).ToList();
    var next = plans.FirstOrDefault(i => i.EndTime > now);
    TodayPlanItems = [.. plans.Select(item => new TodayPlanEntry(item,
        item.IsAllDay ? "終日" : $"{item.StartTime:HH:mm}–{item.EndTime:HH:mm}",        // 区切りは EN DASH
        FormatTodayDuration(ClipDuration(item, dayStart, dayEnd)),
        ResolveCategory(item)?.Name ?? "未分類",
        ReferenceEquals(item, next), item.EndTime <= now))];
    TodayNextPlan = TodayPlanItems.FirstOrDefault(p => p.IsNext);
    TodayTodoItems = [.. Todos.Where(t => !t.IsCompleted && t.IsPlannedToday)
        .OrderByDescending(t => t.Priority).ThenBy(t => t.DueDate ?? DateTime.MaxValue).ThenBy(t => t.SortOrder)];
    var recorded = ScheduleItems.Where(i => i.IsRecorded && !i.IsAllDay && i.StartTime < dayEnd && i.EndTime > dayStart)
                                .Sum(i => ClipDuration(i, dayStart, dayEnd).Ticks);
    var planned = plans.Where(i => !i.IsAllDay).Sum(i => ClipDuration(i, dayStart, dayEnd).Ticks);
    var todayGaps = UnrecordedGaps.Where(g => g.StartTime < dayEnd && g.EndTime > dayStart).ToList();
    var gapTicks = todayGaps.Sum(g => (Min(g.EndTime, dayEnd) - Max(g.StartTime, dayStart)).Ticks);
    TodayRecordedSummaryText = FormatTodayDuration(FromTicks(recorded));
    TodayPlannedSummaryText = FormatTodayDuration(FromTicks(planned));
    TodayGapSummaryText = todayGaps.Count == 0 ? "なし" : $"{FormatTodayDuration(FromTicks(gapTicks))}・{todayGaps.Count}件";
    TodayWorkloadSummaryText = TodayWorkload?.Text ?? "見積もり未設定";
    IsTodayWorkloadOver = TodayWorkload?.IsOver == true;
}
static string FormatTodayDuration(TimeSpan d) => $"{(int)d.TotalHours}:{d.Minutes:D2}";
```
呼び出し元：RecalculateLayoutCore の末尾（早期 return の経路も含む）、`UnrecordedGaps` の setter、`TodayWorkload` の setter、時計の分境界（`design/01` §4）、ToDo 読込後。

`OpenTodayDayViewCommand`：`CurrentDate = DateTime.Today; CurrentViewMode = ViewMode.Day;`

### 5.2 XAML
UserControl.Resources：`Style x:Key="TodayCardStyle" TargetType=Border`（Background Surface / BorderBrush Border / Thickness 1 / CornerRadius 8 / Padding 16）。

```
Grid Background={BackgroundBrush}
 ScrollViewer 縦Auto ScrollViewerHelper.EnableMiddleButtonScroll=True
  StackPanel Margin 24 MaxWidth 1080 HorizontalAlignment Center
   ① ヒーロー Border 地 PrimarySubtle 枠 Primary 1 CornerRadius 10 Padding 20 Margin 0,0,0,16
      Grid
       (IsRecording) Grid[*|Auto]
          StackPanel: 「記録中」11 SemiBold Primary ／ {RecordingTitle} 20 SemiBold Margin 0,4,0,2 TextPrimary ／
                      {RecordingDuration, StringFormat=経過 {0:hh\:mm\:ss}} TextSecondary
          Button「■ 停止」BaseButtonStyle Padding 16,8 縦中央 → ToggleRecordingCommand
       (!IsRecording) Grid
          (HasTodayNextPlan) Grid[*|Auto]
             StackPanel: 「次の予定」11 SemiBold Primary ／ {TodayNextPlan.Item.Title} 20 SemiBold Margin 0,4,0,2 ／
                         横並び [{TodayNextPlan.TimeText} TextSecondary ｜ {TodayNextPlan.CategoryName} Margin 12,0,0,0 TextMuted]
             Button「記録開始」PrimaryButtonStyle Padding 16,8 → StartRecordingFromItemCommand(TodayNextPlan.Item)
          (!HasTodayNextPlan) StackPanel: 「次の予定はありません」18 SemiBold TextPrimary ／
                         「「今日やる」ToDoから始めるか、上部の記録開始を使えます」Margin 0,4,0,0 TextSecondary
   ② UniformGrid Columns 4 Margin 0,0,0,16 — 4枚とも TodayCardStyle、右端以外 Margin 0,0,8,0
      「実績」11 TextMuted ／ {TodayRecordedSummaryText} 22 SemiBold Margin 0,4,0,0
      「予定」／ {TodayPlannedSummaryText} 22 SemiBold
      「未記録」／ {TodayGapSummaryText} 22 SemiBold
      x:Name=WorkloadCard「今日の負荷」／ x:Name=WorkloadText {TodayWorkloadSummaryText} 14 SemiBold Wrap Margin 0,5,0,0
   ③ Grid [*|16|*]
      左 Border TodayCardStyle VerticalAlignment Top
        Grid Margin 0,0,0,12: 「今日の予定」15 SemiBold ／ 右寄せ「枠線＝予定」10 TextMuted
        (!HasTodayPlans) 「予定はありません」Margin 0,10 中央 TextMuted
        (HasTodayPlans) ItemsControl {TodayPlanItems}:
          Border 枠 {Item.BackgroundColor} 2 CornerRadius 5 地 {Item.BackgroundColor→BrushToSubtleBackgroundConverter} Padding 10 Margin 0,0,0,8
            Grid[70|*|Auto]
              StackPanel: {TimeText} 12 SemiBold TextPrimary ／ {DurationText} 10 TextMuted
              StackPanel Margin 8,0: 横並び[{Item.Title} 13 SemiBold 省略 ｜ (IsNext) Border 地 PrimarySubtle CornerRadius 3 Padding 5,1 Margin 6,0,0,0 >「次」9 Primary]
                                     ／ {CategoryName} 10 Margin 0,3,0,0 TextMuted
              Button「開始」Padding 10,4 BaseButtonStyle → StartRecordingFromItemCommand(Item)
      右 Border TodayCardStyle VerticalAlignment Top
        Grid Margin 0,0,0,12: 「今日やる ToDo」15 SemiBold ／ 右寄せ {TodayTodoItems.Count, StringFormat={}{0}件} 10 TextMuted
        (!HasTodayTodos) 「「今日やる」にしたToDoはありません」Margin 0,10 中央 TextMuted
        (HasTodayTodos) ItemsControl {TodayTodoItems}:
          Border 下線1 Padding 4,8 > Grid[*|Auto|Auto]
            StackPanel: {Title} 13 SemiBold 省略 ／ 横並び Margin 0,3,0,0 [{ProgressDisplay} 10 TextMuted ｜ {DueDisplay} 10 Margin 8,0,0,0 TextMuted]
            Button「開始」Padding 9,4 Margin 6,0,0,0 Base → StartRecordingFromTodoCommand
            Button「完了」Padding 9,4 Margin 6,0,0,0 Base → ToggleTodoCompletedCommand
   ④ Button「日ビューで詳しく確認」BaseButtonStyle 中央 Margin 0,18,0,8 Padding 18,7 → OpenTodayDayViewCommand
```
注意：`ProgressDisplay` は見積もり無しでも「0:00 / 0:00」になる（そのまま再現する）。`IsTodayWorkloadOver` は現状 XAML で未使用。

## 6. ふりかえり一覧（Views/NotesView.xaml / MainViewModel.Notes.cs）

`WorkDayLog.Note`（退勤時の一言）を**全期間**新しい順に並べる。統計の期間に縛られない読み返し場所。

### 6.1 ViewModel
```csharp
public sealed record WorkDayNote(DateTime Date, string DateText, string WorkText, string Note, IReadOnlyList<string> Tags)
{ public bool HasTags => Tags.Count > 0; }
public sealed record NoteTagChip(string Tag, int Count, bool IsSelected)
{ public string Display => $"#{Tag}"; public string CountText => Count.ToString(); }
public sealed record NoteMonthGroup(string Header, IReadOnlyList<WorkDayNote> Entries)
{ public string CountText => $"{Entries.Count} 件"; }
```
| プロパティ | 内容 |
| --- | --- |
| `NoteGroups` | 変更で `HasNotes`, `NotesSummaryText`, `DateDisplay`（ヘッダーに件数を出す）を通知 |
| `HasNotes` | `NoteGroups.Count > 0` |
| `NoteTagChips` | 変更で `HasNoteTags` 通知 |
| `SelectedNoteTag`（private set） | 変更で `RebuildNoteGroups()` |
| `SelectNoteTagCommand(tag)` | 空、または現在と同じ（大文字小文字無視）なら null、それ以外そのタグ |
| `NotesSummaryText` | 絞り込み中「#{tag} {n} 件」、0件「ふりかえり」、他「ふりかえり {n} 件」 |

```csharp
private void RebuildNoteGroups()
{
    var all = _workDayLogs.Where(l => l.HasNote).OrderByDescending(l => l.StartTime).Select(ToWorkDayNote).ToList();
    RebuildNoteTagChips(all);                       // チップは絞り込み前の全件から作る（他のタグへ移れるように）
    var visible = SelectedNoteTag is { } tag ? all.Where(n => n.Tags.Any(t => string.Equals(t, tag, OrdinalIgnoreCase))) : all;
    NoteGroups = [.. visible.GroupBy(n => new DateTime(n.Date.Year, n.Date.Month, 1))
                            .Select(g => new NoteMonthGroup(g.Key.ToString("yyyy年M月"), [.. g]))];
}
private void RebuildNoteTagChips(IReadOnlyList<WorkDayNote> notes)
{
    var counts = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
    foreach (var note in notes) foreach (var tag in note.Tags) counts[tag] = counts.GetValueOrDefault(tag) + 1;
    NoteTagChips = [.. counts.OrderByDescending(kv => kv.Value).ThenBy(kv => kv.Key, StringComparer.CurrentCulture)
                             .Select(kv => new NoteTagChip(kv.Key, kv.Value, string.Equals(kv.Key, SelectedNoteTag, OrdinalIgnoreCase)))];
    if (SelectedNoteTag is { } selected && !counts.ContainsKey(selected)) { _selectedNoteTag = null; OnPropertyChanged(nameof(SelectedNoteTag)); }
}
private static WorkDayNote ToWorkDayNote(WorkDayLog log)
{
    var note = log.Note.Trim();
    return new WorkDayNote(log.StartTime.Date, log.StartTime.ToString("M/d(ddd)"),
        log.EndTime is { } end ? $"{log.StartTime:H:mm} - {end:H:mm} ・ {log.DurationText}" : $"{log.StartTime:H:mm} - 勤務中",
        note, NoteTagParser.Extract(note));
}
private void NotifyWorkDayNotesChanged() { RebuildNoteGroups(); UpdateStats(); }   // 一覧と統計を同時に更新
```

### 6.2 NoteTagParser（Helpers/NoteTagParser.cs）
```csharp
[GeneratedRegex(@"#([^\s#、。，．,\.!?！？:：;；「」『』（）\(\)\[\]【】]+)")] private static partial Regex TagRegex();
public static IReadOnlyList<string> Extract(string? note)   // 空白のみ→[]。書かれた順・重複除去（OrdinalIgnoreCase）・# は含めない
public static bool HasTag(string? note, string tag)          // Extract の結果に大文字小文字無視で含まれるか
```
本文からタグを取り除かない（書いたままを保存・表示する）。全角「＃」はタグにならない。

### 6.3 XAML
```
Grid Background={SurfaceBrush}
 ① (!HasNotes) StackPanel 中央 MaxWidth 420
    &#xE70B; Segoe MDL2 Assets 28 中央 Margin 0,0,0,12 TextMuted
    「まだふりかえりがありません」14 SemiBold 中央 Margin 0,0,0,8 TextSecondary
    「退勤したときに出るふりかえりで書いた一言が、ここに全部並びます。過去の日のぶんは、日/週ビューの勤務ラインをクリックしても書けます。」
      12 Wrap 中央揃え TextMuted
 ② (HasNotes) Grid [Auto|*]
    Row0 Border Padding 24,16,24,12 下線1 Border (HasNoteTags)
      ItemsControl {NoteTagChips} WrapPanel
        Button NoteTagChipStyle Margin 0,0,6,6 ToolTip「押すと絞り込み・もう一度押すと解除」
               Command=DataContext.SelectNoteTagCommand(AncestorType=ItemsControl) Parameter={Tag}
          横並び [{Display} 12 ｜ {CountText} 10 Margin 6,0,0,0 縦中央 Opacity 0.7]
    Row1 ScrollViewer 縦Auto EnableMiddleButtonScroll
      StackPanel Margin 24 MaxWidth 960 HorizontalAlignment Left
        ItemsControl {NoteGroups}
          StackPanel Margin 0,0,0,20
            Grid[Auto|Auto|*] Margin 0,0,0,10: {Header} 14 SemiBold TextPrimary ｜ {CountText} 11 Margin 10,0,12,0 TextMuted ｜ Border Height 1 地 Border
            ItemsControl {Entries} Width 700 左寄せ
              Button NoteCardStyle Margin 0,0,0,8 ToolTip「クリックで書き直す」
                     Command=DataContext.EditWorkDayCommand(AncestorType=ScrollViewer) Parameter={Date}
                StackPanel
                  横並び Margin 0,0,0,6: {DateText} 12 SemiBold TextPrimary Margin 0,0,10,0 ｜ {WorkText} 11 TextMuted
                  {Note} 13 Wrap TextPrimary
                  (HasTags) ItemsControl {Tags} Margin 0,8,0,0 WrapPanel:
                    Border 地 PrimarySubtle CornerRadius 3 Padding 6,2 Margin 0,0,4,4 > TextBlock 10 Primary「#」+{.}
```
`NoteTagChipStyle` の選択表示（IsSelected）は `design/03` のスタイル定義に従う。

ツールバーの日付表示（`DateDisplay`）はふりかえりモードで `NotesSummaryText` を出す（`design/01` §5）。

## 7. テスト観点

- VisibleDays：月＝有効曜日×6週、スプリント＝週数の行、無効曜日を除外
- NoteTagParser：日本語タグ、区切り記号で切れる、重複除去（大文字小文字）、`#` 単独は拾わない
- Notes：月グループの順序、タグ件数順、選択中のタグが消えたら解除、NotesSummaryText
- Today：次の予定＝終了が現在より後の最初の予定、終日表記、未記録の件数表記
