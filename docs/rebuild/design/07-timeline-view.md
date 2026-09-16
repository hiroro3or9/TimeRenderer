# 07. スプリントタイムラインビュー

時間軸は「1日 = `TimelinePixelsPerDay` ピクセル」の**連続スケール**。表示範囲を画面幅に押し込まず、横スクロールとズームで解像度を確保する。時刻を日付に丸めないので 30 分の記録は 30 分ぶんの幅になる。

## 1. 表示範囲（UpdateVisibleDaysCore の SprintTimeline 部分）

```csharp
var baseSprint = SprintHelper.GetSprintForDate(ManualSprints, CurrentDate);
int marginDays = 21 * (TimelineSprintCount + 1);
var sprints = SprintHelper.GetSprintsForRange(ManualSprints, baseSprint.StartDate.AddDays(-marginDays), baseSprint.EndDate.AddDays(marginDays));
int baseIdx = sprints.FindIndex(s => s.StartDate.Date == baseSprint.StartDate.Date); if (baseIdx < 0) baseIdx = 0;
int startIdx = Math.Max(0, baseIdx - (TimelineSprintCount / 2));      // 基準スプリントを中央に
int count = Math.Min(sprints.Count - startIdx, TimelineSprintCount);
var displaySprints = sprints.GetRange(startIdx, count);
TimelineSprints = displaySprints;
// VisibleDays = 先頭スプリント開始日〜末尾スプリント終了日の全日（曜日で絞らない）
```
前後移動（Navigate）は `design/05` §2 の `GetAdjacentSprintDate`。

## 2. モデル・定数

### 2.1 TimelineScale（Helpers/TimelineScale.cs）
```csharp
public sealed class TimelineScale(DateTime origin, DateTime end, double pixelsPerDay)
{
    public const double MinPixelsPerDay = 8.0, MaxPixelsPerDay = 960.0, DefaultPixelsPerDay = 120.0;
    public DateTime Origin { get; } = origin;
    public DateTime End { get; } = end < origin ? origin : end;
    public double PixelsPerDay { get; } = Math.Clamp(pixelsPerDay, MinPixelsPerDay, MaxPixelsPerDay);
    public double PixelsPerHour => PixelsPerDay / 24.0;
    public double TotalWidth => ToX(End);
    public double ToX(DateTime time) => (time - Origin).TotalDays * PixelsPerDay;
    public DateTime ToTime(double x) => Origin.AddDays(x / PixelsPerDay);
    public double ToWidth(TimeSpan duration) => duration.TotalDays * PixelsPerDay;
    public TimelineScale WithPixelsPerDay(double pixelsPerDay) => new(Origin, End, pixelsPerDay);
    public TimeSpan PixelsToDuration(double pixels) => TimeSpan.FromDays(pixels / PixelsPerDay);
}
```

### 2.2 選択肢（ViewModels/TimelineViewOptions.cs）
```csharp
public enum TimelineGroupMode { Packed, Category, Flat }
public sealed record TimelineGroupModeOption(TimelineGroupMode Mode, string Label) { public override string ToString() => Label; }
public sealed record TimelineZoomPreset(string Label, double PixelsPerDay) { public override string ToString() => Label; }
public sealed record TimelineSpanOption(int Count, string Label) { public override string ToString() => Label; }
```
- `TimelineGroupModeOptions`：Packed「詰める」/ Category「カテゴリ別」/ Flat「1件1行」
- `TimelineZoomPresets`：「時間」480 /「日」120 /「週」40 /「スプリント」12
- `TimelineSpanOptions`：3「3」/ 5「5」/ 9「9」/ 15「15」/ 25「25」

### 2.3 TimelineBar（ViewModels/TimelineBar.cs、INotifyPropertyChanged）
| メンバー | 内容 |
| --- | --- |
| `const BarHeight = 22`, `LaneHeight = 30`, `MinDrawWidth = 3`, `MinHitWidth = 14`, private `LabelPadding = 14` | |
| `required ScheduleItem Item`, `X`, `Y`, `ActualWidth`, `Lane`, `IsLabelInside`, `required string ToolTipText`（init） | |
| `DrawWidth` | `max(ActualWidth, 3)` |
| `HitWidth` | `max(ActualWidth, 14)` |
| `Height` | `{ get; } = BarHeight`（XAML バインド用のインスタンスプロパティ） |
| `Title` / `Background` | `Item.Title` / `Item.BackgroundColor` |
| `OutsideLabelMargin` | `Thickness(DrawWidth + 4, 0, 0, 0)` |
| `IsDimmed` | 変更通知あり（検索の減光） |
| `static EstimateLabelWidth(text, fontSize = 12)` | 空なら 0。全角 1.0・他 0.55 の合計 × fontSize + 14 |

全角判定：`0x1100–0x115F`, `0x2E80–0xA4CF`, `0xAC00–0xD7A3`, `0xF900–0xFAFF`, `0xFE30–0xFE6F`, `0xFF00–0xFF60`, `0xFFE0–0xFFE6`。

### 2.4 その他の表示用クラス（すべて init のみ）
- `TimelineTick { required Label; X; Width; IsEmphasized; IsToday }`
- `TimelineDayColumn { Date; X; Width; required string Kind /* Normal|Weekend|Disabled|Today */; IsWeekStart; ShowDayLine }`
- `TimelineDensityBar { X; Width; BarHeight; required ToolTipText; Brush = Transparent }`
- `TimelineLaneGroup { required Name; Y; Height; Brush = Transparent; required TotalText; Count; IsAlternate }`
- `TimelineSprintBand { required Name; required RangeText; X; Width; IsCurrent; required SummaryText; required TopCategoryText; ShowSummary => Width >= 120 }`

## 3. レーン割り当て（Helpers/TimelineLaneHelper.cs）

```csharp
private const double GapPixels = 6.0, MaxLabelReservation = 120.0;

public static int AssignLanes(IReadOnlyList<ScheduleItem> items /* StartTime 昇順 */, TimelineScale scale, Dictionary<ScheduleItem, int> lanes)
{
    if (items.Count == 0) return 0;
    var laneFreeFrom = new List<DateTime>();
    foreach (var item in items) {
        var occupiedUntil = GetOccupiedUntil(item, scale);
        int lane = laneFreeFrom.FindIndex(f => f <= item.StartTime);
        if (lane < 0) { lane = laneFreeFrom.Count; laneFreeFrom.Add(occupiedUntil); } else laneFreeFrom[lane] = occupiedUntil;
        lanes[item] = lane;
    }
    return laneFreeFrom.Count;
}
private static DateTime GetOccupiedUntil(ScheduleItem item, TimelineScale scale)
{
    var duration = item.EndTime - item.StartTime; if (duration < TimeSpan.Zero) duration = TimeSpan.Zero;
    double actualWidth = scale.ToWidth(duration);
    double occupied = Math.Max(actualWidth, TimelineBar.MinHitWidth);
    double labelWidth = TimelineBar.EstimateLabelWidth(item.Title);
    double drawWidth = Math.Max(actualWidth, TimelineBar.MinDrawWidth);
    if (labelWidth > drawWidth) occupied = Math.Max(occupied, drawWidth + Math.Min(labelWidth, MaxLabelReservation) + 4);
    return item.StartTime + scale.PixelsToDuration(occupied + GapPixels);
}
public static List<(string Name, List<ScheduleItem> Items, int LaneOffset, int LaneCount)> AssignLanesByGroup(
    IReadOnlyList<ScheduleItem> items, Func<ScheduleItem, string> groupSelector, TimelineScale scale, Dictionary<ScheduleItem, int> lanes)
{   // GroupBy → Key を StringComparer.CurrentCulture で昇順。群ごとに StartTime 順で AssignLanes し、offset を足して格納
}
```

## 4. ViewModel（MainViewModel.Timeline.cs）

### 4.1 設定値
| プロパティ | 仕様 |
| --- | --- |
| `TimelinePixelsPerDay`（既定 120） | Clamp(8, 960)、差 &lt; 0.01 は無視。変更で通知・`TimelineZoomText` 通知 → `InvalidateRealizedWindow()` → `UpdateTimelineItems()` → `RequestZoomSave()` |
| `RequestZoomSave()` | 初期化前は何もしない。600ms の DispatcherTimer を Stop→Start（操作が続く限り先送り）、Tick で Stop して `SaveSettings()` |
| `TimelineZoomText` | `$"{px:0}px/日"` |
| `TimelineSprintCount`（既定 5） | Clamp(1, 25)。変更で `SelectedTimelineSpanOption` 通知 → Invalidate → `UpdateVisibleDays()` → SaveSettings |
| `SelectedTimelineSpanOption` | 一致する選択肢、無ければ `TimelineSpanOptions[1]` |
| `CurrentTimelineGroupMode`（既定 Packed） | 変更で `IsTimelineCategoryMode`, `TimelineLabelColumnWidth`, `SelectedTimelineGroupModeOption` 通知 → UpdateTimelineItems → SaveSettings |
| `TimelineLabelColumnWidth` | カテゴリ別なら 150、他 0 |

### 4.2 計算結果
`CurrentTimelineScale`, `TimelineBars`, `TimelineLaneGroups`, `TimelineSprintBands`, `TimelineContentWidth`, `TimelineContentHeight`（既定 200）, `IsTimelineEmpty`（既定 true）。

### 4.3 コマンド
- `TimelineZoomInCommand`：`TimelinePixelsPerDay *= 1.5`／`TimelineZoomOutCommand`：`/= 1.5`
- `TimelineZoomPresetCommand(param)`：TimelineZoomPreset ならその値、double ならその値
- `TimelineFitToSelectionCommand`：選択があれば `TimelineFitToItemRequested` 発火
- `TimelineJumpOverflowCommand("before"|"after")`：表示対象（`IsItemVisible`）のうち before は `StartTime < Origin` の最も遅いもの、after は `StartTime >= End` の最も早いもの。`TransitionDirection` を Backward/Forward にし `CurrentDate = target.StartTime.Date` → `SelectAndReveal(target)`

### 4.4 選択
- `SelectedItem`：参照が同じなら何もしない。旧の `IsSelected=false`、新の `IsSelected=true`、通知、`RefreshBarStates()`（選択はアイテム自身が持つので日/週/月と共通）
- `SelectAndReveal(item)`：選択後、タイムラインなら `TimelineScrollToItemRequested`、日/週で非終日なら `ScrollToTimeRequested(item.StartTime)`（クリック選択ではスクロールさせない）
- `RefreshBarStates()`：検索語があれば、**全件**（`_allBars`）の `IsDimmed = !(Title か Content が語を含む、OrdinalIgnoreCase)`
- `OnSearchQueryChangedForTimeline()`：タイムラインモードのときだけ RefreshBarStates

### 4.5 UpdateTimelineItems
```
if (モード != SprintTimeline || TimelineSprints.Count == 0) { ClearTimeline(); return; }
origin = 先頭スプリント開始日; end = 末尾スプリント終了日 + 1日
scale = new TimelineScale(origin, end, px); _timelineScale = scale
items = ScheduleItems.Where(IsItemVisible).Where(x => x.EndTime >= origin && x.StartTime < end).OrderBy(StartTime)
if (!_isTimelineDragging) { BuildSprintBands(scale, items); UpdateTimelineDecorations(scale, items); }
IsTimelineEmpty = items.Count == 0
選択中が items に無ければ _selectedItem = null（IsSelected は触らない）して通知
if (items.Count == 0) { SetTimelineSources([], ticks, dayColumns, density); LaneGroups = []; _lastLanes = null;
                        ContentWidth = max(scale.TotalWidth, 1); ContentHeight = 200; return; }
if (_isTimelineDragging && CanReuseLanes(items)) { lanes = _lastLanes; laneCount = _lastLaneCount; }   // ドラッグ中はレーンを組み替えない
else switch mode:
   Flat:     lanes[items[i]] = i; laneCount = items.Count; LaneGroups = []
   Category: groups = AssignLanesByGroup(items, i => ResolveCategory(i)?.Name ?? "未分類", scale, lanes);
             laneCount = 最後の offset + count; LaneGroups = BuildLaneGroups(groups)
   Packed:   laneCount = AssignLanes(items, scale, lanes); LaneGroups = []
   _lastLanes = lanes; _lastLaneCount = laneCount
bars = BuildBars(items, lanes, scale, origin, end)
ドラッグ中なら SetTimelineBarsOnly(bars) else SetTimelineSources(bars, ticks, dayColumns, density)
ContentWidth = max(scale.TotalWidth, 1)
ContentHeight = max(200, laneCount * 30 + 8 + 12)
RefreshBarStates()
```
- `TimelineTopPadding = 8`
- `BuildBars`：範囲外は端で切り詰め（`start = max(StartTime, origin)`, `stop = min(EndTime, end)`, stop&lt;start なら start）。`X = ToX(start)`, `ActualWidth = ToX(stop) - X`, `Y = lane*30 + 8`, `IsLabelInside = EstimateLabelWidth(Title) <= max(width, 3)`, `ToolTipText = item.ToolTipText`
- `BuildLaneGroups`：`TotalText = $"{Σ実績の時間:0.#}h"`（実績のみ・負は0）、`Brush = 先頭アイテムの色`、`Y = offset*30 + 8`, `Height = count*30`, `Count = 件数`, `IsAlternate = i % 2 == 1`
- `BuildSprintBands`：`X = ToX(StartDate)`, `Width = max(ToX(EndDate+1日) - X, 0)`, `RangeText = "MM/dd - MM/dd"`, `IsCurrent = 今日が期間内`, `BuildSprintSummary`
- `BuildSprintSummary`：実績・非終日をスプリント期間でクリップして合計・件数・カテゴリ名（無ければ「未分類」）別。0件なら `("記録なし", "")`。`$"{total:0.#}h ・ {count}件"`、上位カテゴリ `$"{name} {value/total*100:0}%"`
- `BeginTimelineDragLayout()`：`_isTimelineDragging = true`／`EndTimelineDragLayout()`：ドラッグ中なら false にして UpdateTimelineItems
- `ClearTimeline()`：scale=null、全件・lastLanes クリア、表示コレクションは空でなければ空に、IsTimelineEmpty=true、`ClearTimelineDecorations()`

## 5. 装飾（MainViewModel.TimelineDecorations.cs）

`UpdateTimelineDecorations(scale, items)` = `BuildTicks` → `BuildDayColumns` → `BuildDensityBars` → `UpdateOverflowCounts` → `RefreshNowLine(DateTime.Now)`。

### 5.1 目盛り
- 粒度：px/日 ≥300 → 3時間、≥70 → 日、≥22 → 週、他 → 月
- 上限 `MaxTickCount = 1200`：推定数（3時間=総日数×8、日=総日数、週=総日数/7）が超える間、1段粗くする（月で止まる）
- 3時間：Origin から 3時間ごと。Label は 0時なら `M/d`、他は時の数字。`IsEmphasized = 0時`、`IsToday = 日付が今日`
- 日：Label `$"{d.Day}({短い曜日})"`（例「5(月)」）、`IsEmphasized = 月曜`
- 週：`GetStartOfWeek(Origin)` から7日ごと、Label `$"{d:M/d}週"`、`IsEmphasized = true`、今日を含む週が IsToday
- 月：Origin の月初から、Label `yyyy/MM`、`IsEmphasized = true`
- 各 `Width = ToX(次の区切り) - X`

### 5.2 背景の日列
各日：Kind = 今日 → "Today"、無効曜日 → "Disabled"、土日 → "Weekend"、他 "Normal"。`IsWeekStart = 月曜`。`ShowDayLine = px ≥ 18 || 月曜`。

### 5.3 現在時刻ライン
- `TimelineNowX`（変更で `TimelineNowMargin = Thickness(X,0,0,0)` 通知）、`IsTimelineNowVisible`、`TimelineNowText`
- `UpdateTimelineNowLine(now)`（時計から毎回呼ぶ）：タイムライン以外なら非表示にして return。前回更新から 30 秒未満なら return。`RefreshNowLine(now)`
- `RefreshNowLine`：scale 無しか範囲外なら非表示。それ以外 `X = ToX(now)`, `Text = HH:mm`, 表示

### 5.4 範囲外インジケータ
- `TimelineOverflowBefore`（`EndTime < Origin` の表示対象数）、`TimelineOverflowAfter`（`StartTime >= End`）
- `HasTimelineOverflowBefore/After`、`TimelineOverflowBeforeText = "◀ {n}件"`、`TimelineOverflowAfterText = "{n}件 ▶"`

### 5.5 密度ヒートバー
- `TimelineDensityHeight { get; } = 34.0`、棒の最大高さ 26
- 実績・非終日を範囲でクリップし、日ごとに按分して時間を合計。0件なら空
- `max = 最大値`（0以下なら1）。日付順に `X = ToX(日)`, `Width = max(1, w - min(2, w*0.2))`, `BarHeight = max(1, h/max*26)`, `ToolTipText = $"{日:MM/dd (ddd)}  {h:0.#} 時間"`（空白2つ）

## 6. 仮想化（MainViewModel.TimelineViewport.cs）

Canvas は仮想化しないため、見えている範囲だけを表示コレクションへ切り出す。

- `ViewportOverscan = 1.0`（ビューポート幅の前後に1枚ぶん）
- 状態：`_realizedStart`, `_realizedEnd = -1`（Start&gt;End は未設定）、`_hasViewport`, `_viewportOffset`, `_viewportWidth`、全件 `_allBars/_allTicks/_allDayColumns/_allDensityBars`
- `SetTimelineViewport(offset, width)`：width≤0 は無視。記録して `_hasViewport = true`。ビューポートが窓に完全に収まっていれば何もしない（ヒステリシス）、外れたら `RealizeViewport()`（窓 = `[max(0, offset - width), offset + width + width]`）→ `ApplyViewportFilter()`
- `ApplyViewportFilter()`：ビューポート未通知なら全件をそのまま表示。それ以外 `EnsureRealizedWindow()` 後、
  - バー：`X <= end && X + HitWidth + 200 >= start`（右へはみ出すラベルのぶん余裕）
  - 目盛り・日列・密度：`X <= end && X + Width >= start`
- `SetTimelineSources(bars, ticks, dayColumns, density)`：全件を差し替えて ApplyViewportFilter
- `SetTimelineBarsOnly(bars)`：バーだけ差し替え。**ドラッグ中またはビューポート未通知なら絞り込まない**（掴んだバーが消えないように）
- `EnsureRealizedWindow()`：無効なら現在のビューポートから張り直す
- `InvalidateRealizedWindow()`：`_realizedEnd = -1; _realizedStart = 0`（ズーム・スプリント数変更時）

## 7. ビュー（Views/TimelineView.xaml）

ルート `Grid Background={SurfaceBrush}`、行 `[Auto | 52 | * | Auto]`。

### 7.1 行0 操作バー
`Border 地 Background 下線1 Padding 10,6` ＞ 横並び：
1. 「行」11 TextSecondary Margin 0,0,6,0 ＋ ComboBox Width 110 `{TimelineGroupModeOptions}`/`{SelectedTimelineGroupModeOption}`
2. `Rectangle Width 1 Margin 14,3 Fill Border`
3. 「範囲」＋ ComboBox Width 60 ToolTip「表示するスプリント数」`{TimelineSpanOptions}`/`{SelectedTimelineSpanOption}` ＋「スプリント」11 Margin 4,0,0,0
4. 区切り線
5. 「表示幅」＋ ItemsControl `{TimelineZoomPresets}`（横並び）各 `Button Content={Label} Padding 9,3 Margin 0,0,4,0 Hand → DataContext.TimelineZoomPresetCommand(AncestorType=Window)`（スタイル指定なし＝暗黙の Button スタイル）
6. `Button「−」Width 26 Padding 0,3 Margin 10,0,3,0 ToolTip「ズームアウト」` ／ `Button「＋」Width 26 Padding 0,3 Margin 0,0,8,0 ToolTip「ズームイン」` ／ `{TimelineZoomText}` 11 TextSecondary
7. 11px TextSecondary Margin 18,0,0,0「Ctrl+ホイール ズーム / Shift+ホイール 横移動 / ←→ 選択 / Enter 編集 / F フィット / T 今日」

### 7.2 行1 ルーラー
`Grid [Auto|*]`
- 列0：`Border Width={TimelineLabelColumnWidth} 地 Background 枠 0,0,1,1`
- 列1：`ScrollViewer x:Name=TimelineHeaderScroll 横Hidden 縦Disabled 地 Background` ＞ `Grid Width={TimelineContentWidth}` 行 [30|22]
  - 行0 ItemsControl `{TimelineSprintBands}`（Canvas、Left=X, Top=0）：`Border Width={Width} Height 30 Padding 6,0 枠 Border 0,0,1,1`、地 Transparent（IsCurrent なら Surface）＞ Grid［中央 横並び `{Name}` Bold 12 省略 TextPrimary ＋ `{RangeText}` 10 Margin 6,0,0,0 TextSecondary ｜ 右寄せ 横並び（ShowSummary）`{SummaryText}` 10 ＋ `{TopCategoryText}` 10 Margin 6,0,0,0 TextSecondary］
  - 行1 ItemsControl `{TimelineTicks}`（Canvas）：`Border Width={Width} Height 22 BorderThickness 1,0,0,0`、枠 Transparent（IsEmphasized なら Border）、地 Transparent（IsToday なら Surface）＞ `{Label}` 10 Margin 4,0,0,0 省略、TextSecondary（IsToday なら TextPrimary + Bold）

### 7.3 行2 本体
`ScrollViewer x:Name=TimelineVerticalScroll 縦Auto 横Disabled Focusable=True PreviewKeyDown=TimelineRoot_PreviewKeyDown EnableMiddleButtonScroll` ＞ `Grid [Auto|*]`
- 列0 固定ラベル列：`Border Width={TimelineLabelColumnWidth} Height={TimelineContentHeight} 地 Background 枠 0,0,1,0` ＞ ItemsControl `{TimelineLaneGroups}`（Canvas、Left=0、Top=Y）：`Border Width 149 Height={Height} Padding 8,0 下線1` ＞ 横並び 縦中央［`Rectangle 4×20 Radius 2 Fill={Brush} Margin 0,0,8,0` ｜ StackPanel［`{Name}` Bold 12 省略 MaxWidth 110 TextPrimary ／ 10px TextSecondary「{TotalText} ・ {Count}件」］］
- 列1：`ScrollViewer x:Name=TimelineBodyScroll 横Auto 縦Disabled ScrollChanged=… PreviewMouseWheel=…` ＞ `Grid x:Name=TimelineSurface Width={ContentWidth} Height={ContentHeight} 地 Transparent MouseLeftButtonDown/MouseMove/MouseLeftButtonUp`。重ね順：
  1. 日列 ItemsControl（Canvas Left=X）：`Border Width={Width} Height={DataContext.TimelineContentHeight(Window)}`、Kind=Weekend→地 Background、Disabled→地 Background + Opacity 0.6、Today→地 Surface、ShowDayLine→枠 Border 1,0,0,0
  2. スプリント境界 ItemsControl `{TimelineSprintBands}`：`Border Width Height=Content 地 Transparent 枠 TextSecondary 1,0,0,0`
  3. 行帯 ItemsControl `{TimelineLaneGroups}`（Left 0, Top Y）：`Border Height={Height} Width={ContentWidth} 下線1`、IsAlternate なら地 Background
  4. バー ItemsControl `{TimelineBars}`（Left X, Top Y）：
     ```
     Grid BarRoot Width={HitWidth} Height={Height} 地 Transparent Hand ToolTip={ToolTipText} InitialShowDelay 250 ShowDuration 20000
          MouseLeftButtonDown=TimelineBar_MouseLeftButtonDown
       ContextMenu: 編集 / この内容で記録開始 / この時間の使用アプリ / 削除
       Border BarBody 左寄せ 縦中央 Width={DrawWidth} Height={Height} CornerRadius 3 枠1 {BorderBrush} 地 {Background}
         TextBlock BarTitleText {Title} Bold 12 Margin 6,0 縦中央 省略 (IsLabelInside) Foreground={Background→BrushToContrastTextConverter}
       TextBlock {Title} 12 左寄せ 縦中央 Margin={OutsideLabelMargin} TextPrimary (!IsLabelInside)
     Triggers: BarRoot.IsMouseOver → BarBody 枠 TextSecondary
               Item.IsPlanned → BarBody 地 {Background→BrushToSubtleBackgroundConverter}, 枠 {Background}, 太さ 2, BarTitleText TextPrimary
               Item.IsSelected → BarBody 枠 TextPrimary, 太さ 2
               IsDimmed → BarRoot Opacity 0.2
     ```
  5. `Border x:Name=TimelineRangePreview Collapsed 左寄せ 縦Stretch Width 0 IsHitTestVisible False 地 Border Opacity 0.35 枠 TextPrimary 1`
  6. 現在時刻：`Grid 左寄せ 縦Stretch Width 2 IsHitTestVisible False Margin={TimelineNowMargin} (IsTimelineNowVisible)` ＞ `Rectangle Fill #EF4444 Width 2` ＋ `Border 地 #EF4444 CornerRadius 2 Padding 3,1 上 左 > {TimelineNowText} 9 White`
- 行2に重ねるもの（本体と同じセル）：
  - `Button 左上 Margin 8,8,0,0 Padding 8,3 Hand 11px Content={TimelineOverflowBeforeText} ToolTip「表示範囲より前の記録へ移動」→ TimelineJumpOverflowCommand("before")`（HasTimelineOverflowBefore）
  - `Button 右上 Margin 0,8,20,0 … {TimelineOverflowAfterText} ToolTip「表示範囲より後の記録へ移動」→ ("after")`
  - 空状態 `「この期間に記録はありません」中央 TextSecondary`（IsTimelineEmpty）

### 7.4 行3 密度ヒートバー
`Grid 地 Background [Auto|*]`：
- 列0 `Border Width={LabelColumnWidth} 枠 0,1,1,0` ＞「記録量」10 Margin 8,0 TextSecondary（カテゴリ別以外は幅0で見えない）
- 列1 `ScrollViewer x:Name=TimelineDensityScroll 横Hidden 縦Disabled` ＞ `Border 上線1` ＞ ItemsControl `{TimelineDensityBars}` Width={ContentWidth} Height={TimelineDensityHeight}（Canvas、Left=X、**Bottom=4**）：`Border Width={Width} Height={BarHeight} 下寄せ CornerRadius 1 地 TextSecondary Opacity 0.55 ToolTip={ToolTipText}`

## 8. コードビハインド（Views/TimelineView.xaml.cs）

定数：`ZoomStepFactor = 1.15`, `HorizontalScrollStep = 120`, `DragThresholdX = 4`, `ResizeHandleMinBarWidth = 24`, `ResizeHandleWidth = 6`。
`enum TimelineDragMode { None, Move, ResizeStart, ResizeEnd, CreateRange }`。

- **Loaded/Unloaded**：VM の `TimelineScrollToItemRequested` / `TimelineFitToItemRequested` を購読／解除
- **メニュー**：`ScheduleItemMenu.ExecuteOnMenuTarget(sender, EditCommand | StartRecordingFromItemCommand | ShowAppUsageCommand | DeleteCommand)`
- **ScrollChanged**：まず `SetTimelineViewport(HorizontalOffset, ViewportWidth)`。`HorizontalChange == 0 && ExtentWidthChange == 0` なら return。ヘッダーと密度の ScrollViewer を同じ横オフセットへ
- **PreviewMouseWheel**：
  - Ctrl：`ZoomTimelineAtCursor(sv, マウスX, Delta>0 ? 1.15 : 1/1.15)`、Handled
  - Shift：`Delta>0 ? -120 : +120` 横スクロール、Handled
  - なし：Handled にして新しい `MouseWheelEventArgs`（RoutedEvent=MouseWheelEvent, Source=sv）を `TimelineVerticalScroll.RaiseEvent`（入れ子の ScrollViewer に飲まれないよう外側へ転送）
- **ZoomTimelineAtCursor(sv, cursorX, factor)**：`anchor = scale.ToTime(offset + cursorX)` → `px *= factor` → `newOffset = max(0, newScale.ToX(anchor) - cursorX)` → `SetTimelineViewport(newOffset, sv.ViewportWidth)`（先に移動先を教える）→ `Dispatcher.BeginInvoke(Loaded)` でスクロール
- **ScrollToItem**：`x = ToX(StartTime)`、margin 60。左にはみ出せば `max(0, x - 60)`、右なら `x - viewport + 60`
- **FitToItem**：`days = max(duration.TotalDays, 1/24)`、`px = viewport * 0.6 / days`、中心を `(ToX(Start)+ToX(End))/2 - viewport/2` へ（Loaded 優先度で）
- **PreviewKeyDown**（Ctrl 付きを先に判定）：
  - Ctrl + `OemPlus/Add` → ビューポート中央で ×1.15、`OemMinus/Subtract` → ÷1.15、D → DuplicateItemCommand、C → CopyItemCommand、V → `PasteItemCommand.Execute(null)`（貼り付け位置は VM 既定）、他の Ctrl キーは Handled にせず return
  - ← `MoveSelectionCommand(-1)`、→ `(+1)`、Enter `EditSelectedCommand`、Delete `DeleteSelectedCommand`、F `TimelineFitToSelectionCommand`、Home / T 今日を中央へ（範囲外なら何もしない）、他は return
- **スナップ単位** `GetSnapUnit(px)`：`floor = px≥240 ? 0 : px≥60 ? 60 : px≥20 ? 360 : 1440`（分）、`max(SnapMinutes, floor)` 分
- `SnapTimelineTime(t, unit)`：`Math.Round(t.Ticks / unit.Ticks) * unit.Ticks`
- **ゾーン** `GetTimelineZone(bar, xInBar)`：`DrawWidth < 24` → Move、`x ≤ 6` → ResizeStart、`x ≥ DrawWidth - 6` → ResizeEnd、他 Move
- **バー押下**：ダブルクリック → ドラッグ取消 → 選択 → `EditCommand`、Handled。シングル → 掴んだ情報（item, zone, 元の時刻, surface 上の X）を**先に**控えてから選択、`TimelineVerticalScroll.Focus()`、Handled
- **空き領域押下**：選択解除、Focus、mode=CreateRange、`_tlCreateAnchor = Snap(ToTime(x), unit)`
- **MouseMove**：mode=None なら return。左ボタンが離れていれば取消。未開始なら |dx| &lt; 4 で return、開始時に（範囲作成以外は `BeginTimelineDragLayout()` と `BeginItemDragUndo(item)`）、`Mouse.Capture(TimelineSurface)`、カーソル Move=SizeAll / CreateRange=Cross / 他 SizeWE。範囲作成は `UpdateRangePreview(x)`（Margin.Left=min、Width=|dx|、Visible）、それ以外 `ProcessTimelineDrag(x)`
- **ProcessTimelineDrag**：`delta = (x - startX) / px` 日。Move：`newStart = Snap(origStart + delta)`, `newEnd = newStart + 元の長さ`。ResizeStart：`Snap(origStart + delta)`、`origEnd - snap` を超えない。ResizeEnd：`Snap(origEnd + delta)`、`origStart + snap` 未満にしない → `UpdateItemTimesPreview`
- **MouseUp**：状態を控えて `EndTimelineDrag()`。未開始なら終わり。範囲作成なら `CommitRangeCreation(x)`：`dropped = Snap(ToTime(x))`、anchor と並べ替え、`end <= start` なら `end = start + snap`、`AddScheduleItemInRangeCommand.Execute((start, end))`（CanExecute 確認）。それ以外 `CommitItemDrag()`
- **EndTimelineDrag**：開始済みなら Capture 解除・カーソル戻し・`EndTimelineDragLayout()`。プレビューを隠し Width 0。状態リセット
- **CancelTimelineDrag**：End した後、開始済みの移動/伸縮なら元の時刻へ `UpdateItemTimesPreview`、`ClearItemDragUndo()`

## 9. テスト観点

- TimelineScale：ToX/ToTime の往復、Clamp、End&lt;Origin
- TimelineLaneHelper：重ならないものが同じレーン、細いバー＋長いラベルでレーンが分かれる、カテゴリ群のオフセット
- TimelineBar：EstimateLabelWidth（全角/半角）、HitWidth/DrawWidth 下限
- 仮想化：窓内スクロールで作り直さない、ズーム後に張り直す、ドラッグ中は絞り込まない
- 目盛り：粒度の境界（300/70/22）と上限 1200 での繰り上げ
