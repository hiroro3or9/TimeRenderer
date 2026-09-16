# 08. 統計ビュー

期間内の**実績**（`Kind=Recorded`、終日を除く）をプロジェクトコード別・カテゴリ別・日別に集計する。月のときだけ転記用のタイムシート表を出す。

## 1. 期間（MainViewModel.Stats.cs）

```csharp
public enum StatsPeriodMode { Week, Month, Sprint }
private const double DailyChartHeight = 180;
```
- `StatsPeriod`（既定 Week）：変更で `IsStatsWeekPeriod/IsStatsMonthPeriod/IsStatsSprintPeriod`・`DateDisplay` 通知 → `UpdateStats()`
- `ChangeStatsPeriodCommand(string)`：`Enum.TryParse` できれば設定
- `GetStatsRange()`（終端排他）：月＝月初〜翌月初、スプリント＝`GetSprintForDate` の開始日〜終了日+1、週＝`GetStartOfWeek(CurrentDate)`〜+7日
- `GetStatsRangeDisplay()`（`DateDisplay` の統計モード用）：`$"統計 [{label}] {start:yyyy/MM/dd} - {last:MM/dd}"`、label は「週」「月」、スプリントはスプリント名、`last = end - 1日`
- 前後移動は週=±7日、月=±1か月、スプリント=隣のスプリント（`design/05` §2）

## 2. 基礎集計（Helpers/StatsAggregationHelper.cs、純粋ロジック）

```csharp
public sealed record StatsCategoryDisplayInfo(string Name, string ColorCode);
public sealed class StatsAggregationResult {
    public Dictionary<string, double> CategoryTotals { get; } = [];
    public Dictionary<string, double> ProjectCodeTotals { get; } = [];
    public Dictionary<DateTime, Dictionary<string, double>> DailyCategoryTotals { get; } = [];
    public Dictionary<DateTime, Dictionary<string, double>> DailyProjectCodeTotals { get; } = [];
    public Dictionary<string, StatsCategoryDisplayInfo> CategoryDisplayInfo { get; } = [];
    public Dictionary<string, string> ProjectCodeDisplayNames { get; } = [];
    public int ItemCount { get; internal set; }
}
public const string UnassignedProjectKey = "__unassigned_project__";

public static StatsAggregationResult Aggregate(IEnumerable<ScheduleItem> items, IReadOnlyList<CategoryInfo> categories,
                                               IReadOnlyList<ProjectCodeInfo> projectCodes, DateTime rangeStart, DateTime rangeEnd)
```
1. 引数 null は `ArgumentNullException`。`rangeEnd <= rangeStart` なら空の結果
2. カテゴリ辞書：Id→カテゴリ、ColorCode→カテゴリ（**最初の1件**、TryAdd）。プロジェクト辞書：Id→（TryAdd）
3. 各アイテム：実績でない・終日は skip。`[start, end)` にクリップ、空なら skip。`ItemCount++`
4. プロジェクト：`key = ProjectCodeId ?? UnassignedProjectKey`、合計に加算、表示名 = マスターにあれば `DisplayName`、Id が null なら「（未設定）」、それ以外「（不明なプロジェクトコード）」
5. カテゴリ：CategoryId で解決 → 無ければ色で解決。`key = category?.Id ?? $"color:{item.ColorCode}"`。表示情報（初回だけ登録）= カテゴリ名と色、無ければ「未分類」とアイテムの色
6. 日ごとに分割（`date = start.Date` から `date < end` まで）して、カテゴリ合計・日別カテゴリ・日別プロジェクトに加算（カテゴリ合計はここで加算する）

## 3. UpdateStats

```
if (CurrentViewMode != ViewMode.Stats) return
TimesheetCopyStatusText = ""
(rangeStart, rangeEnd) = GetStatsRange()
agg = StatsAggregationHelper.Aggregate(ScheduleItems, Categories, ProjectCodes, rangeStart, rangeEnd)
displayInfo = agg.CategoryDisplayInfo → (Name, CategoryInfo.CreateBrush(ColorCode))
AddUnrecordedTimeToProjectStats(rangeStart, rangeEnd, projectTotals, dailyProjectTotals, projectDisplayNames)   // §4
orderedKeys = Categories の登録順で合計にあるもの ++ それ以外（未分類）を時間の多い順、Distinct
grandTotal = Σカテゴリ合計; maxCategoryHours = 最大（無ければ 0）
orderedProjectKeys = ProjectCodes の登録順 ++ それ以外（未設定・不明）を時間の多い順
maxProjectHours, projectGrandTotal
StatsProjectCodeItems = orderedProjectKeys → ProjectCodeStat(表示名, hours, max(maxProjectHours, 0.001)) { PercentText = $"{percent:0.#}%" }
if (月) matrix = StatsTimesheetBuilder.Build(rangeStart, rangeEnd, orderedProjectKeys, dailyProjectTotals, ProjectCodes) → 4プロパティへ
else 4プロパティを空（GrandTotalText は ""）
StatsCategoryItems = orderedKeys → CategoryStat(name, brush, hours, max(maxCategoryHours, 0.001)) { PercentText = $"{percent:0.#}%" }
maxDayHours = 日別カテゴリの日合計の最大
compact = (rangeEnd - rangeStart).Days > 14
for d in [rangeStart, rangeEnd):
   segments = []
   if (その日のデータあり && maxDayHours > 0)
      foreach key in orderedKeys.Reverse():          // 先頭カテゴリが一番下に来るよう逆順で積む
         hours ≤ 0 は skip
         segments.Add(DailyStatSegment(brush, max(hours / maxDayHours * 180, 2), $"{name}: {FormatHours(hours)}"))
   label = compact ? d.Day.ToString() : d.ToString("M/d(ddd)")
   dailyStats.Add(DailyStat(d, label, d.Date == Today, dayTotal, segments))
StatsDailyItems = dailyStats
StatsNoteItems = BuildStatsNoteItems(rangeStart, rangeEnd)   // 出勤日が期間内でメモありを新しい順に ToWorkDayNote（design/06 §6）
HasRecordedStatsData = grandTotal > 0
HasStatsData = grandTotal > 0 || projectGrandTotal > 0
StatsSummaryText = projectGrandTotal > grandTotal + 0.000001
    ? $"記録 {FormatHours(grandTotal)} ／ {ItemCount} 件（プロジェクト集計 {FormatHours(projectGrandTotal)}）"
    : $"合計 {FormatHours(grandTotal)} ／ {ItemCount} 件"
```

表示用 record：
```csharp
public record CategoryStat(string Name, Brush Brush, double Hours, double MaxHours)
{ public string HoursText => StatsFormatting.FormatHours(Hours); public string PercentText { get; init; } = ""; }
public record ProjectCodeStat(string DisplayName, double Hours, double MaxHours)
{ public string HoursText => StatsFormatting.FormatHours(Hours); public string PercentText { get; init; } = ""; }
public record DailyStatSegment(Brush Brush, double HeightPx, string ToolTipText);
public record DailyStat(DateTime Date, string Label, bool IsToday, double TotalHours, IReadOnlyList<DailyStatSegment> Segments)
{ public string TotalText => TotalHours > 0 ? StatsFormatting.FormatHours(TotalHours) : ""; }
```
`StatsNoteItems` の setter は `HasStatsNotes` も通知する。

`UpdateStats()` の呼び出し元：RecalculateLayoutCore の末尾、期間変更、ふりかえり変更（`NotifyWorkDayNotesChanged`）、未記録時間集計の設定変更（`design/13`）。

## 4. 未記録時間のプロジェクト加算

設定 `IsUnrecordedTimeProjectAggregationEnabled` が true のときだけ。実績や勤務記録は変えず、**プロジェクトコード別の数字にだけ**足す（カテゴリ別・日別チャートには足さない）。

```
now = DateTime.Now
foreach log in _workDayLogs:
    workStart = max(log.StartTime, rangeStart)
    rawEnd = log.EndTime ?? now; if (rawEnd > now) rawEnd = now
    workEnd = min(rawEnd, rangeEnd)
    if (workEnd <= workStart) continue
    gaps = UnrecordedGapHelper.Detect(workStart, workEnd, CollectCoveredRanges(workStart, workEnd), TimeSpan.Zero)   // 最小長 0
    foreach gap, foreach segment in UnrecordedGapHelper.SplitByDay(gap):
        date = segment.StartTime.Date
        projectCode = ResolveUnrecordedTimeProjectCode(date)      // design/12 §7。null なら skip
        合計・日別に segment.Duration.TotalHours を加算、表示名 = projectCode.DisplayName
```

## 5. 月次タイムシート（ViewModels/StatsTimesheetBuilder.cs）

```csharp
public static class StatsFormatting {
    public static string FormatHours(double hours) { var s = TimeSpan.FromHours(hours); return $"{(int)s.TotalHours}:{s.Minutes:D2}"; }
    public static double RoundHoursToQuarter(double hours) => Math.Round(hours * 4, MidpointRounding.AwayFromZero) / 4;
    public static string FormatDecimalHours(double hours) => $"{FormatDecimalHoursValue(hours)}h";
    public static string FormatDecimalHoursValue(double hours) => hours.ToString("0.##", CultureInfo.InvariantCulture);
}
public record TimesheetMatrixDateColumn(DateTime Date) {
    public string DateText => Date.Day.ToString(CultureInfo.InvariantCulture);
    public string DayText => Date.ToString("ddd");
    public bool IsToday => Date.Date == DateTime.Today;
    public bool IsWeekend => Date.DayOfWeek is DayOfWeek.Saturday or DayOfWeek.Sunday;
}
public record TimesheetMatrixCell(bool HasValue, double ActualHours, double RoundedHours) {
    public string ActualHoursText => StatsFormatting.FormatHours(ActualHours);
    public string CopyValue => StatsFormatting.FormatDecimalHoursValue(RoundedHours);
    public string RoundedHoursText => HasValue ? $"{CopyValue}h" : "";
    public string ToolTipText => HasValue ? $"実績 {ActualHoursText} → 15分単位 {RoundedHoursText}" : "記録なし";
}
public record TimesheetMatrixRow(string ProjectKey, string HeaderText, string DetailText, string CopyValue,
                                 bool IsInactive, bool IsWarning, IReadOnlyList<TimesheetMatrixCell> Cells) {
    public string RoundedTotalText => StatsFormatting.FormatDecimalHours(Cells.Sum(c => c.RoundedHours));
}
public sealed record TimesheetMatrixResult(IReadOnlyList<TimesheetMatrixDateColumn> Columns, IReadOnlyList<TimesheetMatrixRow> Rows,
                                           IReadOnlyList<TimesheetMatrixCell> TotalCells, string GrandTotalText);
```
`Build(rangeStart, rangeEnd, orderedProjectKeys, dailyProjectCodeTotals, projectCodes)`：
- 列 = 期間の日数ぶん
- 各行：日×プロジェクトの合計が無ければ `Cell(false, 0, 0)`、あれば `Cell(true, hours, RoundHoursToQuarter(hours))`（**日・コード単位で合算してから丸める**。個々の記録を先に丸めない）
- 行の見出し：
  - マスターにある：Header = Code（空なら Name、それも空なら「コード未入力」）、Detail = Name（無効なら「{Name}（無効）」、Name 空なら「無効」）、CopyValue = Code、IsInactive = !IsActive、IsWarning = Code が空
  - `UnassignedProjectKey`：「未設定」「コードなし」CopyValue ""、Warning
  - その他：「不明」「マスターに存在しません」""、Warning
- 合計行のセル：`HasValue = いずれかあり`、実績の和、丸め後の和
- GrandTotalText = `FormatDecimalHours(全行の丸め後の和)`

コピー：
- `CopyTimesheetHoursCommand(TimesheetMatrixCell)`：CanExecute = `HasValue`。`CopyValue`（例 "1.25"）をコピー
- `CopyTimesheetProjectCodeCommand(TimesheetMatrixRow)`：CanExecute = `CopyValue.Length > 0`
- `CopyTimesheetValue(value)`：`Clipboard.SetText` → `TimesheetCopyStatusText = "{value} をコピーしました"`。例外時「コピーできませんでした」＋ `ShowMessage("クリップボードへコピーできませんでした。少し待ってからもう一度お試しください。", "コピーエラー")`（タイムシートは Windows クリップボードを使う。予定のコピーとは別）

## 6. ビュー（Views/StatsView.xaml）

```
Grid Background={SurfaceBrush}
 ScrollViewer 縦Auto EnableMiddleButtonScroll
  StackPanel Margin 24 MaxWidth 960        ← 左右中央（HorizontalAlignment 既定 Stretch だが MaxWidth で中央寄せ）
   ① Grid[Auto|*|Auto] Margin 0,0,0,20
       横並び: RadioButton「週」SegmentedLeftStyle / 「月」SegmentedMiddleStyle / 「スプリント」SegmentedRightStyle
               GroupName=StatsPeriod IsChecked={IsStats…Period, Mode=OneWay} Command=ChangeStatsPeriodCommand Parameter=Week|Month|Sprint
       列2: {StatsSummaryText} 15 SemiBold 縦中央 TextPrimary
   ② (!HasStatsData) 「この期間に記録はありません（終日の予定は集計対象外です）」13 TextMuted Margin 0,8,0,24
   ③ (HasStatsNotes) StackPanel
       「ふりかえり」SemiBold 14 TextPrimary Margin 0,0,0,12
       ItemsControl {StatsNoteItems} Margin 0,0,0,28:
         Button NoteCardStyle Margin 0,0,0,8 ToolTip「クリックで書き直す」→ DataContext.EditWorkDayCommand(AncestorType=ItemsControl) Parameter={Date}
           StackPanel: 横並び Margin 0,0,0,6 [{DateText} 12 SemiBold Margin 0,0,10,0 ｜ {WorkText} 11 TextMuted] ／ {Note} 13 Wrap
           （ふりかえりビューと違いタグ表示は無い）
   ④ (HasStatsData) StackPanel
       「プロジェクトコード別作業時間」SemiBold 14 Margin 0,0,0,12
       ItemsControl {StatsProjectCodeItems} Margin 0,0,0,28:
         Grid[220|*|64|56] Margin 0,0,0,8:
           {DisplayName} 13 省略 縦中央 ｜ ProgressBar Value={Hours} Maximum={MaxHours} Height 18 Foreground={PrimaryBrush} StatsBarStyle
           ｜ {HoursText} 13 SemiBold 右 ｜ {PercentText} 12 TextSecondary 右
       (IsStatsMonthPeriod) StackPanel Margin 0,0,0,28      ← 月次タイムシート
         「月次タイムシート（15分単位）」SemiBold 14 Margin 0,0,0,4
         Grid[*|Auto] Margin 0,0,0,12: 「プロジェクトコードが縦、1日から月末が横です。左のコードと時間セルをクリックしてコピーできます」12 TextMuted Wrap
                                       ｜ {TimesheetCopyStatusText} 12 Margin 12,0,0,0 Primary 上寄せ
         ScrollViewer 横Auto 縦Disabled > Border 枠 Border 1 CornerRadius 6 > StackPanel
           見出し行 Border 地 HoverBackground 下線1 > 横並び:
             Border Width 160 Padding 10,8 >「プロジェクトコード」11 SemiBold TextSecondary
             ItemsControl {StatsTimesheetMatrixColumns}（横並び）: Border Width 64 左線1 Padding 4,5 > 中央 StackPanel
                 {DateText} 11 SemiBold（既定 TextPrimary、IsWeekend→TextMuted、IsToday→Primary。後勝ち）／ {DayText} 9 Margin 0,1,0,0 TextMuted
             Border Width 90 左線1 Padding 10,8 >「月合計」11 SemiBold TextSecondary 右
           行 ItemsControl {StatsTimesheetMatrixRows}: Border 下線1 > 横並び:
             Border Width 160 Padding 2 > Button GhostButtonStyle Padding 6,4 左揃え ToolTip「プロジェクトコードをコピー」→ CopyTimesheetProjectCodeCommand
               StackPanel: 横並び [{HeaderText} 12 SemiBold MaxWidth 116 省略（既定 TextPrimary、IsInactive→TextMuted、IsWarning→Danger）
                                   ｜ &#xE8C8; Segoe MDL2 Assets 9 Margin 4,0,0,0]
                           ／ {DetailText} 9 Margin 0,2,0,0 TextMuted MaxWidth 142 省略
             ItemsControl {Cells}（横並び）: Border Width 64 左線1 Padding 2 >
               Button Content={RoundedHoursText} GhostButtonStyle Padding 3 11 SemiBold Foreground Primary IsEnabled={HasValue}
                      ToolTip={ToolTipText} → CopyTimesheetHoursCommand
             Border Width 90 左線1 Padding 10,7 > {RoundedTotalText} 12 SemiBold TextPrimary 右
           合計行 Border 地 PrimarySubtle > 横並び:
             Border Width 160 Padding 10,8 >「日合計」12 SemiBold
             ItemsControl {StatsTimesheetMatrixTotalCells}: Border Width 64 左線1 Padding 4,8 > {RoundedHoursText} 11 SemiBold 右
             Border Width 90 左線1 Padding 10,8 > {StatsTimesheetMatrixGrandTotalText} 12 Bold Primary 右
       (HasRecordedStatsData) StackPanel
         「カテゴリ別作業時間」SemiBold 14 Margin 0,0,0,12
         ItemsControl {StatsCategoryItems} Margin 0,0,0,28:
           Grid[150|*|64|56] Margin 0,0,0,8:
             横並び 縦中央 [Border 14×14 地 {Brush} CornerRadius 3 枠 Border 1 Margin 0,0,8,0 ｜ {Name} 13 省略]
             ｜ ProgressBar Hours/MaxHours Height 18 Foreground={Brush} StatsBarStyle ｜ {HoursText} 13 SemiBold 右 ｜ {PercentText} 12 TextSecondary 右
         「日別の推移」SemiBold 14 Margin 0,0,0,12
         Border 下線1 > ItemsControl {StatsDailyItems} UniformGrid Rows=1:
           Grid Margin 1,0 行[16|180|Auto]
             {TotalText} 9 TextSecondary 中央 下寄せ Margin 0,0,0,2
             ItemsControl {Segments} 下寄せ Margin 3,0: Border Height={HeightPx} 地 {Brush} 枠 Surface 0,0,0,1 ToolTip={ToolTipText}
             {Label} 10 Margin 0,4,0,0 中央 省略（既定 TextSecondary、IsToday→Primary Bold）
```
`GhostButtonStyle` の地・ホバー色はハードコード（`design/03`）。`StatsBarStyle` は角丸の棒（`design/03`）。

## 7. テスト観点

- StatsAggregationHelper：期間境界でのクリップ、日跨ぎの按分、終日・予定の除外、CategoryId 優先→色、未設定/不明のプロジェクトキーと表示名、`color:` キー
- StatsTimesheetBuilder：合算後に 15分丸め（AwayFromZero：0.125→0.25）、見出し（コード無し・無効・未設定・不明）、合計行、Invariant 形式
- UpdateStats：カテゴリの並び（マスター順→未分類は時間降順）、compact ラベル、未記録時間の加算がプロジェクトだけに入る、サマリー文言の切り替え
