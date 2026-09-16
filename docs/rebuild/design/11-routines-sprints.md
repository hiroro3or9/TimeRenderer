# 11. スプリント・定期予定

## 1. スプリント（Helpers/SprintHelper.cs）

手動登録したスプリント（`AppSettings.ManualSprints`、`IsManual=true`）を基準に、隙間と前後を**14日単位の自動スプリント**で埋める。

```csharp
public static readonly DateTime DefaultReferenceDate = new(2026, 1, 5);   // 手動が無いときの基準（2026年最初の月曜）

public static List<SprintInfo> GetSprintsForRange(IReadOnlyList<SprintInfo> manualSprints, DateTime fromDate, DateTime toDate)
{
    var result = new List<SprintInfo>(); fromDate = fromDate.Date; toDate = toDate.Date;
    var manuals = manualSprints.Where(x => x.IsManual).OrderBy(x => x.StartDate.Date).ToList();
    var baseStart = manuals.Count > 0 ? manuals[0].StartDate.Date : DefaultReferenceDate;

    // 1. 基準より前（過去方向）
    if (fromDate < baseStart) {
        var temp = baseStart; var prev = new List<SprintInfo>(); int back = 1;
        while (temp > fromDate) {
            var s = temp.AddDays(-14); var e = temp.AddDays(-1);
            prev.Add(new() { Name = $"Sprint Pre-{back} (自動)", StartDate = s, EndDate = e, IsManual = false });
            temp = s; back++;
        }
        prev.Reverse(); result.AddRange(prev);
    }
    // 2. 基準から未来方向
    var cur = baseStart; int mi = 0; int autoNo = 1;
    while (cur <= toDate || mi < manuals.Count) {
        if (manuals.Count > 0 && mi < manuals.Count) {
            var m = manuals[mi];
            if (cur >= m.StartDate.Date) {
                result.Add(new() { Id = m.Id, Name = string.IsNullOrWhiteSpace(m.Name) ? $"Sprint {autoNo++}" : m.Name,
                                   StartDate = m.StartDate.Date, EndDate = m.EndDate.Date, IsManual = true });
                cur = m.EndDate.Date.AddDays(1); mi++;
            } else {
                var s = cur; var next = m.StartDate.Date;
                while (s < next) {
                    var e = s.AddDays(13); if (e >= next) e = next.AddDays(-1);
                    result.Add(new() { Name = $"Sprint {autoNo++} (自動)", StartDate = s, EndDate = e, IsManual = false });
                    s = e.AddDays(1);
                }
                cur = next;
            }
        } else {
            var s = cur; var e = s.AddDays(13);
            result.Add(new() { Name = $"Sprint {autoNo++} (自動)", StartDate = s, EndDate = e, IsManual = false });
            cur = e.AddDays(1);
        }
    }
    return [.. result.OrderBy(x => x.StartDate)];
}

// 高頻度で呼ばれるのでキャッシュ（ManualSprints はリスト再代入で更新されるので参照比較で判定できる）
public static SprintInfo GetSprintForDate(IReadOnlyList<SprintInfo> manualSprints, DateTime date)
{
    var target = date.Date;
    if (_cached == null || !ReferenceEquals(_cacheSource, manualSprints) || target <= _cacheFrom || target >= _cacheTo) {
        _cacheFrom = target.AddMonths(-3); _cacheTo = target.AddMonths(3);
        _cached = GetSprintsForRange(manualSprints, _cacheFrom, _cacheTo); _cacheSource = manualSprints;
    }
    return _cached.FirstOrDefault(s => target >= s.StartDate && target <= s.EndDate)
        ?? new SprintInfo { Name = "Sprint (仮)", StartDate = date.Date, EndDate = date.Date.AddDays(13), IsManual = false };
}
```

### 1.1 スプリントカレンダーの表示日（Layout.cs）
現在日のスプリントを取り、`start = そのスプリント開始日の週の月曜`、`end = 終了日の週の日曜`。`SprintWeekRows = max(1, (end-start+1日)/7)`（3週超の手動スプリントでも行が足りるように）。start から週ごとに有効曜日の日を追加（end 超は除外）。セルの `IsCurrentMonth` はスプリント期間内か。

### 1.2 スプリントの登録・編集（MainViewModel.Commands.cs）
フォーム状態：`IsAddSprintFormVisible`, `NewSprintName`, `NewSprintStartDate`, `NewSprintEndDate`, `EditingSprint`, `FormTitle`（EditingSprint が null「スプリントを追加」、他「スプリントを編集」）。

| コマンド | 動作 |
| --- | --- |
| `ShowAddSprintFormCommand` | EditingSprint=null。手動スプリントがあれば最後のものの翌日〜+14日（`EndDate+1`〜`EndDate+14`）、名前は最後の名前が「Sprint {数字}」なら「Sprint {n+1}」、それ以外「Sprint」。無ければ今日のスプリントの期間と「Sprint 1」。フォーム表示 |
| `HideAddSprintFormCommand` | 非表示、入力と EditingSprint をクリア |
| `SaveNewSprintCommand` | 名前空→`ShowMessage("スプリント名を入力してください。","入力エラー")`／日付未設定「開始日と終了日を設定してください。」／開始&gt;終了「開始日は終了日以前である必要があります。」／他の手動スプリントと期間重複（開始が既存内、終了が既存内、既存開始が新期間内のいずれか）「既存のスプリントと期間が重複しています。」→ 編集なら同じ Id で置き換えた新リスト、新規なら追加した新リストを `ManualSprints` に**再代入** → Hide |
| `EditManualSprintCommand(sprint)` | EditingSprint とフォーム値をセットして表示 |
| `DeleteManualSprintCommand(sprint)` | 確認「スプリント「{Name}」を削除しますか？\n削除すると自動分割の計算に戻ります。」→ 除いた新リストを再代入 |

管理パネルの UI は `design/13`。

## 2. 定期予定の発生判定（RoutineScheduleItem）

```csharp
public bool OccursOn(DateTime date)
{
    var day = date.Date;
    if (day < StartDate.Date) return false;
    var matches = Recurrence switch { MonthlyByDate => OccursOnMonthly(day), MonthlyByWeekday => OccursOnMonthlyWeekday(day), _ => OccursOnWeekly(day) };
    return matches && !IsSkipped(day);
}
bool OccursOnWeekly(DateTime day) {
    if (!DaysOfWeek.Contains(day.DayOfWeek)) return false;
    if (Interval == 1) return true;
    return ((StartOfWeek(day) - StartOfWeek(StartDate.Date)).Days / 7) % Interval == 0;   // 開始日を含む週を0週目
}
bool OccursOnMonthly(DateTime day) {
    if (day.Day != ResolveDayOfMonth(day.Year, day.Month)) return false;
    return Interval == 1 || MonthsFromStart(day) % Interval == 0;
}
bool OccursOnMonthlyWeekday(DateTime day) {
    if (!MatchesWeekdayPattern(day)) return false;
    return Interval == 1 || MonthsFromStart(day) % Interval == 0;
}
bool MatchesWeekdayPattern(DateTime day) {
    if (!DaysOfWeek.Contains(day.DayOfWeek)) return false;
    var nth = ((day.Day - 1) / 7) + 1;
    if (WeeksOfMonth.Contains(nth)) return true;
    return WeeksOfMonth.Contains(LastWeekOfMonth) && day.Day + 7 > DateTime.DaysInMonth(day.Year, day.Month);   // 最終週
}
bool IsSkipped(DateTime day) {
    if (SkipEvery < 2) return false;
    var index = OccurrenceIndex(day);
    return index >= 0 && index % SkipEvery == SkipIndex - 1;
}
int OccurrenceIndex(DateTime day) => Recurrence switch {   // 開始日以降の何回目か（0起点、休みの回も数える）
    MonthlyByDate => OccurrenceIndexMonthly(day), MonthlyByWeekday => OccurrenceIndexMonthlyWeekday(day), _ => OccurrenceIndexWeekly(day) };
int OccurrenceIndexWeekly(DateTime day) {
    var ordered = DayOfWeekHelper.WeekOrder.Where(DaysOfWeek.Contains).ToList();
    if (ordered.Count == 0) return 0;
    var cycles = (StartOfWeek(day) - StartOfWeek(StartDate.Date)).Days / 7 / Interval;
    var rank = ordered.IndexOf(day.DayOfWeek);
    var beforeStart = ordered.Count(d => WeekIndex(d) < WeekIndex(StartDate.DayOfWeek));   // 開始週で開始日より前の曜日
    return cycles * ordered.Count + rank - beforeStart;
}
int OccurrenceIndexMonthly(DateTime day) {
    var index = MonthsFromStart(day) / Interval;
    if (ResolveDayOfMonth(StartDate.Year, StartDate.Month) < StartDate.Day) index--;       // 開始月の該当日が開始日より前
    return index;
}
int OccurrenceIndexMonthlyWeekday(DateTime day) {
    var index = 0; var cursor = new DateTime(StartDate.Year, StartDate.Month, 1); var target = new DateTime(day.Year, day.Month, 1);
    while (cursor < target) { index += CountWeekdayOccurrences(cursor.Year, cursor.Month, int.MaxValue); cursor = cursor.AddMonths(Interval); }
    return index + CountWeekdayOccurrences(day.Year, day.Month, day.Day);
}
int CountWeekdayOccurrences(int y, int m, int maxDayExclusive) {
    var minDay = (y == StartDate.Year && m == StartDate.Month) ? StartDate.Day : 1;
    var lastDay = Math.Min(maxDayExclusive - 1, DateTime.DaysInMonth(y, m));
    int c = 0; for (var d = minDay; d <= lastDay; d++) if (MatchesWeekdayPattern(new DateTime(y, m, d))) c++; return c;
}
int MonthsFromStart(DateTime day) => (day.Year - StartDate.Year) * 12 + day.Month - StartDate.Month;
int ResolveDayOfMonth(int y, int m) => Math.Min(DayOfMonth, DateTime.DaysInMonth(y, m));   // 無い日は末日へ
static DateTime StartOfWeek(DateTime d) => d.Date.AddDays(-WeekIndex(d.DayOfWeek));
static int WeekIndex(DayOfWeek d) => ((int)d + 6) % 7;   // 月曜=0
```

### 2.1 表示文字列
- `RecurrenceDisplay` = 基本 + 休み注記（SkipEvery≥2 なら「（{n}回に1回休み）」）
- Weekly：曜日なし「（曜日未設定）」。days=7曜日なら「毎日」、他は月曜順に「・」区切り。Interval 1→（7曜日なら「毎日」、他「毎週 {days}」）、2→「隔週 {days}」、他「{n}週ごと {days}」
- MonthlyByDate：day=31 なら「末日」他「{d}日」。Interval 1「毎月{day}」、他「{n}ヶ月ごと {day}」
- MonthlyByWeekday：曜日なし「（曜日未設定）」、週なし「（週未設定）」。週は重複除去・最終を最後に「第1・第3・最終」。head＝1「毎月」/2「隔月」/他「{n}ヶ月ごと」。「{head} {weeks} {days}」
- `TimeRangeDisplay` =「HH:MM-HH:MM」（`(int)TotalHours:D2`）
- `StartDateDisplay` = default なら空、他「yyyy/MM/dd〜」

## 3. 仮想アイテムの生成（Helpers/RoutineOccurrencePlanner.cs）

```csharp
public const int LookBehindDays = 7, LookAheadDays = 60;
public static List<ScheduleItem> BuildVirtualItems(routines, existingItems, categories, DateTime aroundDate)
{
    var windowStart = aroundDate.Date.AddDays(-7); var rangeEnd = aroundDate.Date.AddDays(60);
    var existingKeys = existingItems.Where(i => i.IsPlanned && i.RoutineId != null).Select(i => (i.RoutineId!, i.StartTime.Date)).ToHashSet();
    var categoryColors = categories.GroupBy(c => c.Id).ToDictionary(g => g.Key, g => g.First().ColorCode);
    foreach routine (IsEnabled && IsValidRecurrence):
        excluded = ExcludedDates の Date 集合
        for date = max(StartDate.Date, windowStart) .. rangeEnd:
            if (!OccursOn(date) || excluded.Contains(date) || !existingKeys.Add((Id, date))) continue
            color = CategoryId が辞書にあればその色、無ければ routine.ColorCode
            yield new ScheduleItem { Id = $"routine:{routine.Id}:{date:yyyyMMdd}", Kind = Planned, Title, StartTime = date+StartTime,
                                     EndTime = date+EndTime, ColorCode = color, CategoryId, ProjectCodeId, RoutineId = routine.Id, IsVirtual = true }
}
```

## 4. ViewModel（MainViewModel.Routines.cs）

### 4.1 状態
- `Routines`（List、setter で SaveSettings ＋ `RebuildRoutineOccurrences(CurrentDate)`。**変更は新しいリストの再代入**）
- `PendingReminders`（ObservableCollection&lt;ScheduleItem&gt;）、`_remindedRoutineItems`（HashSet、セッション内で判定済み）、`_lastRoutineGenerationDate`
- `AutoStartNotice` / `HasAutoStartNotice` / `ShowAutoStartNotice(message)`（`design/04` §5.2）

### 4.2 コマンド
| コマンド | 動作 |
| --- | --- |
| `AddRoutineCommand` | `ShowRoutineEditDialog(null, [..Categories], GetTitleSuggestions(), ActiveProjectCodes, DefaultProjectCode)` → 追加した新リストを代入 |
| `EditRoutineCommand(routine)` | ダイアログ（`GetSelectableProjectCodes(routine.ProjectCodeId)`）→ **`result.ExcludedDates = routine.ExcludedDates`**（ダイアログは除外日を扱わない）→ 同 Id を置き換えた新リスト |
| `DeleteRoutineCommand(routine)` | 確認「定期予定「{Title}」を削除しますか？\n（記録済み・個別編集済みのアイテムは残ります）」→ 除いた新リスト |
| `StartReminderCommand(item)` | 通知から外して `StartRecordingFromItem(item)`（予定自体を実績化） |
| `DismissReminderCommand(item)` | 通知から外す |

### 4.3 生成と移行
- `EnsureRoutineOccurrences(aroundDate)`：定期予定が無ければ何もしない。Planner の結果を `_isLoadingData`（元の値を退避）の間に Add → `RecalculateLayout()`（仮想は保存しないので SaveData 不要）。CurrentDate の setter と `CheckReminders`（1日1回）と設定適用後に呼ぶ
- `RebuildRoutineOccurrences(aroundDate)`：全仮想アイテムを（_isLoadingData の間に）Remove し、PendingReminders・判定済みからも除く → Ensure → RecalculateLayout
- `MigrateRoutineStartDates()`（起動時1回）：StartDate が default のものを今日にして SaveSettings
- `MigrateGeneratedRoutineItems()`（起動時1回、旧方式の実体アイテム掃除）：非仮想・予定・RoutineId あり・**未来**・タイトル一致・非終日・内容が空白・同日内・時刻がテンプレートと一致・`OccursOn`・除外日でない、を全て満たすものを Remove → SaveData

### 4.4 実体化と除外日
- `MaterializeOccurrence(item, occurrenceDate = null)`：仮想でなければ何もしない。発生日（引数 ?? item.StartTime）を定期予定の ExcludedDates に追加（無ければ）→ SaveSettings → `item.IsVirtual = false` → SaveData
- `AddRoutineExclusionFor(item)`（実体アイテム削除時）：RoutineId のテンプレートがあればその日を除外日に（削除直後の再生成で「復活」しないように）
- `DeleteOccurrenceForDay(item)`：除外日に追加 → Remove → 通知・判定済みから除く

### 4.5 仮想アイテムへの操作（範囲の確認。取り消し履歴には積まない）
`ShowRoutineScopeDialog(message, title)` → `RoutineScope.ThisDay` / `WholeSeries` / null(キャンセル)。

| 操作 | message / title | この日のみ | 定期予定全体 |
| --- | --- | --- | --- |
| 削除 `DeleteRoutineOccurrence` | 「「{Title}」は定期予定です。どの範囲を削除しますか？」/「定期予定の削除」 | DeleteOccurrenceForDay | テンプレートを除いた新リスト（無ければこの日のみ） |
| 編集 `EditRoutineOccurrence` | 「「{Title}」は定期予定です。どの範囲を編集しますか？」/「定期予定の編集」 | 予定編集ダイアログ → 元の日付を控えて `_isBatchUpdatingItem` の間に全項目を書き戻し → `MaterializeOccurrence(item, 元の日付)` → RecalculateLayout | テンプレート編集ダイアログ（除外日引き継ぎ）→ 置き換え |
| ドラッグ確定 `CommitVirtualItemDrag(item, before)` | 「「{Title}」は定期予定です。どの範囲に時間の変更を適用しますか？」/「定期予定の時間変更」 | `MaterializeOccurrence(item, before.StartTime.Date)` → RecalculateLayout | 時刻を before に戻し、新しい時刻が日跨ぎまたは終了≤開始なら `ShowMessage("日をまたぐ時間帯は定期予定全体には設定できません。","定期予定の時間変更")`、それ以外テンプレートの StartTime/EndTime を更新 → SaveSettings → Rebuild |
| （キャンセル） | | | ドラッグは時刻を元に戻す |

### 4.6 開始時刻の通知・自動開始 `CheckReminders(now)`（10秒ごと）
```
if (_lastRoutineGenerationDate != now.Date) { _lastRoutineGenerationDate = now.Date; EnsureRoutineOccurrences(now.Date); }
foreach item in ScheduleItems.ToList():     // 強制開始でコレクションが変わるのでスナップショット
    if (!item.IsPlanned || item.IsAllDay) continue
    if (_remindedRoutineItems.Contains(item)) continue
    if (item.StartTime.Date != now.Date || now < item.StartTime) continue
    if (item.RoutineId != null) { routine = 検索; autoStart = routine?.IsAutoStart == true; force = routine?.IsForceStart == true }
    else if (item.AutoStartRecording || item.RemindAtStart) { autoStart = item.AutoStartRecording; force = item.ForceStartRecording }
    else continue
    _remindedRoutineItems.Add(item)
    if (now - item.StartTime > 15分) continue            // 起動直後の古いリマインダーを出さない
    if (autoStart && (force || !IsRecording)) { StartRecordingFromItem(item); ShowAutoStartNotice($"「{item.Title}」の記録を自動開始しました"); }
    else if (!PendingReminders.Contains(item)) { PendingReminders.Add(item); SystemSounds.Asterisk.Play(); }
```

## 5. 定期予定の編集ダイアログ（RoutineEditDialog）

`Title="定期予定の編集" Height=780 Width=420 CenterOwner NoResize 地 Background`。`ResultRoutine`。引数 `(RoutineScheduleItem? existing, categories, titleSuggestions, projectCodes, defaultProjectCode)`。
Grid Margin 20：Row0 ScrollViewer（縦 Auto）＞ StackPanel Margin 0,0,8,0、Row1 ボタン。各ブロック Margin 0,0,0,14。

1. 「タイトル」＋ TitleCombo（編集可 14px）
2. 「繰り返し」＋ RecurrenceCombo Width 200 左寄せ（「毎週（曜日で指定）」0 /「毎月（日付で指定）」1 /「毎月（第N曜日で指定）」2、DisplayMemberPath Label）
3. WeeklyPanel：「間隔」＋ WeekIntervalCombo Width 200（1「毎週」/2「隔週（2週ごと）」/3,4,6,8「{n}週ごと」）＋ 11px TextMuted「間隔の起点は下の開始日です（開始日を含む週を1週目として数えます）」
4. MonthlyWeekdayPanel：「間隔」＋ MonthWeekIntervalCombo（1「毎月」/2「隔月（2ヶ月ごと）」/3,4,6,12「{n}ヶ月ごと」）＋「繰り返す週」（Margin 0,12,0,0）＋ WrapPanel［第1〜第5・最終 CheckBox Margin 0,4,14,0］＋ 11px「第5週が無い月は「第5」の回を飛ばします。毎月必ず入れたい場合は「最終」を選びます」
5. MonthlyPanel：「間隔」＋ MonthIntervalCombo ＋「繰り返す日」＋ DayOfMonthCombo Width 200 MaxDropDown 240（1〜30「{d}日」、31「31日（末日）」）＋ 11px「指定した日が無い月（2月の30日など）は、その月の末日に繰り下げます。間隔の起点は下の開始日です」
6. DaysOfWeekPanel（MonthlyByDate 以外）：「繰り返す曜日」＋ WrapPanel［月〜日 CheckBox］
7. 「開始日」＋ StartDatePicker Width 160 ＋ 11px「この日より前には予定を表示しません（過去にも表示したい場合は日付を遡って設定します）」
8. CheckBox SkipEnabledCheck「一定の回数ごとに1回休む」＋ SkipDetailPanel（IsEnabled=チェック）横並び Margin 16,8,0,0［SkipEveryCombo Width 120（2,3,4,5,6,8,10,12「{n}回」）｜「のうち」Margin 8,0 ｜ SkipIndexCombo Width 120（1〜n「{i}回目」）｜「を休む」］＋ 11px Margin 16,6,0,0「回数は開始日以降の1回目から数えます（例: 「2回」「2回目」なら隔回で休みになります）」
9. 「開始時刻」＋ 時 Combo 80 ＋「:」＋ 分 Combo 80（**5分刻み**）
10. 「終了時刻」同上
11. 「カテゴリ」＋ ColorCombo Width 200（項目 Border 20×20 枠 #CCCCCC）
12. 「プロジェクトコード」＋ ProjectCodeCombo Width 280（先頭「（未設定）」＋渡されたコード）＋ 11px「この定期予定から生成される予定と、その予定から始めた実績に引き継がれます」
13. 下線 ／ CheckBox AutoStartCheckBox「予定時刻になったら自動で記録を開始する」＋ 11px「オフの場合は開始時刻にリマインダー通知を表示します（記録開始ボタンをワンクリックで押せます）」／ CheckBox ForceStartCheckBox Margin 16,0,0,4「記録中でも強制的に開始する」（IsEnabled=自動開始）＋ 11px Margin 16,0,0,12「オンの場合、自動開始の時刻に別の記録中でも現在の記録を停止・保存して開始します」／ CheckBox EnabledCheckBox「この定期予定を有効にする」
- ボタン：「キャンセル」100×36 Base ／「OK」100×36 Primary

初期化：
- 編集：各値。休みは SkipEvery≥2 でチェック。開始日 default なら今日。分は5分単位に切り捨て。カテゴリは Id→色→先頭。プロジェクトは一致なければ「（未設定）」
- 新規：毎週・間隔1・日=今日の日・休み周期2・第N週=今日の週番号だけチェック・開始日今日・開始=現在時:分(5分切り捨て)・終了=+1時間・先頭カテゴリ・既定プロジェクト（無ければ未設定）・有効
- 休む回の選択肢は周期に合わせて作り直し、選択値を 1〜周期に Clamp

OK：タイトル空「タイトルを入力してください。」／毎月日付以外で曜日0「曜日を1つ以上選択してください。」／第N曜日で週0「繰り返す週を1つ以上選択してください。」／開始日なし「開始日を選択してください。」／時刻なし「時刻を選択してください。」／終了≤開始「終了時刻は開始時刻より後にしてください。」→ `ResultRoutine{ Id=既存 or 新規, Title, Recurrence, Interval=種別に応じたコンボ, DaysOfWeek, DayOfMonth, WeeksOfMonth, SkipEvery=有効なら値 else 0, SkipIndex=有効なら値 else 1, StartDate, StartTime, EndTime, ColorCode=選択色.ToString() ?? Lavender, CategoryId, ProjectCodeId=ToStoredId, IsAutoStart, IsForceStart=自動&&強制, IsEnabled }`。

## 6. 範囲確認ダイアログ（RoutineScopeDialog）

`Height=200 Width=380 CenterOwner NoResize ToolWindow`、Title は引数。Grid Margin 20：MessageText 14 折返し 中央 ／ 右寄せ Margin 0,16,0,0［「この日のみ」IsDefault MinWidth 96 Margin 0,0,10,0 Primary ｜「定期予定全体」MinWidth 96 Margin 0,0,10,0 Base ｜「キャンセル」IsCancel MinWidth 80 Base］。`Result`（キャンセルは null）。

## 7. テスト観点

- SprintHelper：手動なし（2026/1/5 基準で14日）、手動スプリント間の隙間を埋める・次の手動の直前で切る、過去方向の Pre-n、名前空の手動、キャッシュの参照比較
- RoutineScheduleItem：隔週の起点、月末丸め（31→2月末）、第N/最終曜日、第5週が無い月、SkipEvery の回数の数え方（開始週の途中開始、開始月の該当日が開始日より前）
- RoutineOccurrencePlanner：除外日・既存の実体（予定）がある日・開始日前は生成しない、Id 形式、カテゴリ色の解決
