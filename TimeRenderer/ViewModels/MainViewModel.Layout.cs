using System;
using System.Linq;

using TimeRenderer.Models;
using TimeRenderer.Helpers;
using TimeRenderer.Services;

namespace TimeRenderer.ViewModels;

public partial class MainViewModel
{
    private int _displayStartHour = 0;
    public int DisplayStartHour
    {
        get => _displayStartHour;
        set
        {
            var clamped = Math.Clamp(value, 0, _displayEndHour - 1);
            if (SetProperty(ref _displayStartHour, clamped))
            {
                OnPropertyChanged(nameof(ScheduleGridHeight));
                InitializeTimeLabels();
                SaveSettings();
            }
        }
    }

    private int _displayEndHour = 24;
    public int DisplayEndHour
    {
        get => _displayEndHour;
        set
        {
            var clamped = Math.Clamp(value, _displayStartHour + 1, 24);
            if (SetProperty(ref _displayEndHour, clamped))
            {
                OnPropertyChanged(nameof(ScheduleGridHeight));
                InitializeTimeLabels();
                SaveSettings();
            }
        }
    }

    public double ScheduleGridHeight => (_displayEndHour - _displayStartHour) * LayoutConstants.PixelsPerHour;

    /// <summary>時刻の刻み幅の選択肢（分）</summary>
    public static IReadOnlyList<int> SnapMinutesOptions { get; } = [5, 10, 15, 30];

    private int _snapMinutes = 15;
    /// <summary>
    /// ドラッグでの移動・伸縮・範囲作成で時刻を丸める単位（分）。
    ///
    /// 5分単位で記録するチームもあれば30分ブロックで管理するチームもあるため、
    /// 決め打ちにせず選べるようにしている。
    /// </summary>
    public int SnapMinutes
    {
        get => _snapMinutes;
        set
        {
            var clamped = Math.Clamp(value, 1, 60);
            if (SetProperty(ref _snapMinutes, clamped))
            {
                SaveSettings();
            }
        }
    }

    private int _sprintWeekRows = 3;
    /// <summary>スプリントビューのグリッド行数（スプリントの週数に追随）</summary>
    public int SprintWeekRows
    {
        get => _sprintWeekRows;
        private set => SetProperty(ref _sprintWeekRows, value);
    }

    // タイムラインビューの状態は MainViewModel.Timeline.cs に集約している
    // （TimelineBars / TimelineLaneGroups / TimelineSprintBands）

    private double _allDayPanelHeight = 30;
    public double AllDayPanelHeight
    {
        get => _allDayPanelHeight;
        set => SetProperty(ref _allDayPanelHeight, value);
    }

    private void InitializeTimeLabels()
    {
        var labels = new List<string>();
        for (int i = _displayStartHour; i <= _displayEndHour; i++)
        {
            labels.Add($"{i}:00");
        }
        TimeLabels = labels;
    }

    private void UpdateVisibleDays()
    {
        UpdateVisibleDaysCore();
        RebuildWorkDayMarkers(); // 出退勤マーカーは表示範囲ぶんだけ作り直す
    }

    /// <summary>
    /// 描画対象の日付範囲 [Start, End)。VisibleDays の最小日〜最大日の翌日。
    ///
    /// セグメントや日別インデックスをこの範囲に絞ることで、
    /// 記録が何年ぶん溜まっても1画面ぶんの要素しか生成しないようにしている
    /// （以前は全期間ぶんのビジュアルを作ってから Visibility で隠していた）。
    /// </summary>
    private (DateTime Start, DateTime End)? GetLayoutRange()
    {
        var days = VisibleDays;
        if (days.Count == 0) return null;

        var min = days[0].Date;
        var max = min;
        foreach (var day in days)
        {
            var d = day.Date;
            if (d < min) min = d;
            if (d > max) max = d;
        }
        return (min, max.AddDays(1));
    }

    private void UpdateVisibleDaysCore()
    {
        var days = new List<DateTime>();
        if (CurrentViewMode == ViewMode.Day)
        {
            days.Add(CurrentDate);
        }
        else if (CurrentViewMode == ViewMode.Week)
        {
            var start = CurrentWeekStart;
            for (int i = 0; i < 7; i++)
            {
                var day = start.AddDays(i);
                if (EnabledDaysOfWeek.Contains(day.DayOfWeek))
                {
                    days.Add(day);
                }
            }
        }
        else if (CurrentViewMode == ViewMode.Month)
        {
            // 月初の1日を取得
            var firstDayOfMonth = new DateTime(CurrentDate.Year, CurrentDate.Month, 1);
            // その月を含む週の月曜日を取得（カレンダーの左上）
            var diff = (7 + (firstDayOfMonth.DayOfWeek - DayOfWeek.Monday)) % 7;
            var start = firstDayOfMonth.AddDays(-1 * diff).Date;
            
            // 6週間分ループし、有効な曜日のみを追加
            for (int w = 0; w < 6; w++)
            {
                var weekStart = start.AddDays(w * 7);
                for (int d = 0; d < 7; d++)
                {
                    var day = weekStart.AddDays(d);
                    if (EnabledDaysOfWeek.Contains(day.DayOfWeek))
                    {
                        days.Add(day);
                    }
                }
            }
        }
        else if (CurrentViewMode == ViewMode.Sprint)
        {
            var sprint = Helpers.SprintHelper.GetSprintForDate(ManualSprints, CurrentDate);
            // スプリント開始日の週の月曜日
            var start = Converters.DateTimeHelper.GetStartOfWeek(sprint.StartDate);
            // スプリント終了日の週の日曜日
            var end = Converters.DateTimeHelper.GetStartOfWeek(sprint.EndDate).AddDays(6);

            // グリッド行数をスプリントの実際の週数に合わせる（3週間超の手動スプリント対応）
            SprintWeekRows = Math.Max(1, (int)((end - start).TotalDays + 1) / 7);

            // 週ごとにループして、有効な曜日のみを追加する
            for (var d = start; d <= end; d = d.AddDays(7))
            {
                for (int i = 0; i < 7; i++)
                {
                    var day = d.AddDays(i);
                    if (day > end) break;
                    if (EnabledDaysOfWeek.Contains(day.DayOfWeek))
                    {
                        days.Add(day);
                    }
                }
            }
        }
        else if (CurrentViewMode == ViewMode.SprintTimeline)
        {
            // 基準スプリントを中心に TimelineSprintCount 個のスプリントを表示する
            var baseSprint = Helpers.SprintHelper.GetSprintForDate(ManualSprints, CurrentDate);

            // 必要な数のスプリントを確実に拾えるよう、前後に余裕をもって取得する
            // （1スプリント約3週間として、要求数ぶん＋1スプリント分を上乗せする）
            int marginDays = 21 * (TimelineSprintCount + 1);
            var sprints = Helpers.SprintHelper.GetSprintsForRange(
                ManualSprints,
                baseSprint.StartDate.AddDays(-marginDays),
                baseSprint.EndDate.AddDays(marginDays));

            int baseIdx = sprints.FindIndex(s => s.StartDate.Date == baseSprint.StartDate.Date);
            if (baseIdx < 0) baseIdx = 0;

            // 基準スプリントが中央に来るように前方へずらす
            int startIdx = Math.Max(0, baseIdx - (TimelineSprintCount / 2));
            int count = Math.Min(sprints.Count - startIdx, TimelineSprintCount);
            var displaySprints = sprints.GetRange(startIdx, count);

            TimelineSprints = displaySprints;

            if (displaySprints.Count > 0)
            {
                var start = displaySprints[0].StartDate.Date;
                var end = displaySprints[^1].EndDate.Date;
                for (var d = start; d <= end; d = d.AddDays(1))
                {
                    days.Add(d);
                }
            }
        }
        VisibleDays = days;

        // 表示範囲が変わるとセグメント・日別インデックスの対象も変わるため、
        // カレンダー等の派生結果を作り直す前にレイアウトから組み直す
        // （末尾で UpdateCalendarCells / UpdateTimelineItems / UpdateStats を呼ぶ）
        //
        // ここは日付送りやビュー切り替えという単発の操作なので、遅延させず即時に確定させる。
        // 遅らせると切り替え直後の1フレームで前の範囲の内容が残りうる
        RecalculateLayoutCore();
    }

    /// <summary>
    /// ドラッグ操作中のプレビュー反映。保存はせず、再レイアウトのみ行う
    /// （マウス移動のたびにファイル書き込みが発生するのを防ぐ）。
    /// </summary>
    public bool UpdateItemTimesPreview(ScheduleItem item, DateTime newStart, DateTime newEnd)
    {
        if (item.StartTime == newStart && item.EndTime == newEnd) return false;

        _isBatchUpdatingItem = true;
        try
        {
            item.StartTime = newStart;
            item.EndTime = newEnd;
        }
        finally
        {
            _isBatchUpdatingItem = false;
        }
        RecalculateLayout();
        return true;
    }

    /// <summary>ドラッグ確定時（マウスアップ）に、履歴へ積んでからデータを保存する</summary>
    public void CommitItemDrag()
    {
        // 定期予定の仮想アイテムは「この日のみ／全体」を確認してから確定する
        // （定期予定側の変更になるため、取り消し履歴には積まない）
        if (_dragUndoItem is { IsVirtual: true, RoutineId: not null } virtualItem &&
            _dragUndoBefore is { } before)
        {
            ClearItemDragUndo();
            CommitVirtualItemDrag(virtualItem, before);
            return;
        }

        CommitItemDragUndo();
        SaveData();
    }

    /// <summary>再計算の予約中フラグ（同一フレーム内の重複要求をまとめるため）</summary>
    private bool _isLayoutRecalculationPending;

    /// <summary>
    /// レイアウトの再計算を要求する。
    ///
    /// 1回の操作で複数のプロパティが動く場面（ドラッグ、取り消し、カテゴリ編集など）では
    /// このメソッドが連続で呼ばれる。都度フル再計算すると、そのたびに
    /// セグメント・カレンダーセル・タイムライン・統計を作り直してバインディングが
    /// 総入れ替えになるため、描画直前の1回にまとめる。
    /// </summary>
    private void RecalculateLayout()
    {
        // 初期化前（コンストラクタ内）は Dispatcher に積んでも走る保証がないため即時実行する
        var dispatcher = System.Windows.Application.Current?.Dispatcher;
        if (!_isInitialized || dispatcher == null)
        {
            RecalculateLayoutCore();
            return;
        }

        if (_isLayoutRecalculationPending) return;
        _isLayoutRecalculationPending = true;

        // Render 優先度＝レイアウト・描画の直前。この操作で積まれた要求はここで1回に集約される
        dispatcher.BeginInvoke(
            System.Windows.Threading.DispatcherPriority.Render,
            new Action(FlushPendingLayout));
    }

    /// <summary>
    /// 予約済みの再計算があれば、その場で実行して結果を確定させる。
    /// 計算結果（DailyScheduleItems など）を同期的に読む処理の先頭で呼ぶ。
    /// </summary>
    private void FlushPendingLayout()
    {
        if (!_isLayoutRecalculationPending) return;
        _isLayoutRecalculationPending = false;
        RecalculateLayoutCore();
    }

    private void RecalculateLayoutCore()
    {
        _isLayoutRecalculationPending = false;

        var newAllDayItems = new List<ScheduleItem>();
        var newSegments = new List<ScheduleSegment>();

        var range = GetLayoutRange();

        // 表示日を持たないモード（統計）では日/週・カレンダーのどちらも描かないため、
        // セグメントも日別インデックスも作らずに派生結果の更新だけを行う
        if (range is null)
        {
            if (StandardItems.Count > 0) StandardItems = [];
            if (AllDayItems.Count > 0) AllDayItems = [];
            if (DailyScheduleItems.Count > 0) DailyScheduleItems = new Dictionary<DateTime, List<ScheduleItem>>();

            UpdateCalendarCells();
            UpdateTimelineItems();
            UpdateStats();
            return;
        }

        var (rangeStart, rangeEnd) = range.Value;

        // 色フィルタで非表示のカテゴリを除き、さらに表示範囲に重なるものだけを描画対象にする
        var visibleItems = new List<ScheduleItem>();
        foreach (var item in ScheduleItems)
        {
            if (!IsItemVisible(item)) continue;
            if (item.EndTime < rangeStart || item.StartTime >= rangeEnd) continue;
            visibleItems.Add(item);
        }

        // セグメント（と終日パネル）は日/週ビュー専用。
        // 月・スプリント・タイムライン・統計のときは組み立てても誰も描画しないので飛ばす
        var segmentSources = IsDayOrWeekMode ? visibleItems : new List<ScheduleItem>();

        foreach (var item in segmentSources)
        {
            if (item.IsAllDay)
            {
                newAllDayItems.Add(item);
            }
            else
            {
                // 日付をまたぐアイテムは日単位のセグメントに分割する
                // （例: 23:00→翌1:00 は「23:00-24:00」と「0:00-1:00」の2つとして描画）
                var start = item.StartTime;
                var end = item.EndTime;
                if (end <= start)
                {
                    newSegments.Add(new ScheduleSegment(item, start, start));
                    continue;
                }

                // 範囲外の日ぶんのセグメントは作らない
                // （数か月にまたがるアイテムが1件あるだけで日数ぶんの要素が生まれるのを防ぐ）
                var firstDay = start.Date < rangeStart ? rangeStart : start.Date;
                var lastDay = end.Date >= rangeEnd ? rangeEnd.AddDays(-1) : end.Date;

                for (var d = firstDay; d <= lastDay; d = d.AddDays(1))
                {
                    var segStart = d == start.Date ? start : d;
                    var segEnd = end < d.AddDays(1) ? end : d.AddDays(1);
                    if (segEnd <= segStart) continue; // 終了がちょうど0:00の場合の空セグメントを除外
                    newSegments.Add(new ScheduleSegment(item, segStart, segEnd));
                }
            }
        }

        var allDayGrouped = newAllDayItems.GroupBy(x => x.StartTime.Date);
        int maxStackIndex = 0;

        foreach (var group in allDayGrouped)
        {
            int index = 0;
            foreach (var item in group.OrderBy(x => x.Title))
            {
                item.ColumnIndex = index;
                index++;
            }
            if (index > maxStackIndex) maxStackIndex = index;
        }

        AllDayPanelHeight = Math.Max(30, (maxStackIndex * 24) + 6);

        foreach (var group in newSegments.GroupBy(x => x.StartTime.Date))
        {
            var sortedSegments = group.OrderBy(x => x.StartTime).ThenByDescending(x => x.EndTime).ToList();
            Helpers.ScheduleLayoutHelper.CalculateClustersAndAssignColumns(sortedSegments);
        }

        var dailyItems = new Dictionary<DateTime, List<ScheduleItem>>();
        foreach (var item in visibleItems)
        {
            var start = item.StartTime.Date;
            // 終端がちょうど0:00の日は実際にはまたがっていないため含めない
            // （終日イベントは 0:00〜翌0:00 で保存されるため、翌日に重複表示されるのを防ぐ）
            var end = (item.EndTime.TimeOfDay == TimeSpan.Zero && item.EndTime > item.StartTime)
                ? item.EndTime.Date.AddDays(-1)
                : item.EndTime.Date;

            // セグメントと同じく、表示範囲の外の日はインデックスに入れない
            if (start < rangeStart) start = rangeStart;
            if (end >= rangeEnd) end = rangeEnd.AddDays(-1);

            for (var d = start; d <= end; d = d.AddDays(1))
            {
                if (!dailyItems.TryGetValue(d, out var list))
                {
                    list = [];
                    dailyItems[d] = list;
                }
                list.Add(item);
            }
        }

        foreach (var key in dailyItems.Keys.ToList())
        {
            dailyItems[key] = [.. dailyItems[key]
                .OrderBy(x => x.IsAllDay ? 0 : 1)
                .ThenBy(x => x.StartTime)];
        }

        DailyScheduleItems = dailyItems;
        StandardItems = newSegments;
        AllDayItems = newAllDayItems;

        UpdateCalendarCells();
        UpdateTimelineItems();
        UpdateStats();
    }

    // UpdateTimelineItems は MainViewModel.Timeline.cs に移動した
    // （スケール・レーン割り当て・バー座標の計算をまとめて扱うため）

    private void UpdateCalendarCells()
    {
        if (CurrentViewMode != ViewMode.Month && CurrentViewMode != ViewMode.Sprint) 
            return;

        var cells = new List<CalendarCellViewModel>();
        var sprint = CurrentViewMode == ViewMode.Sprint ? Helpers.SprintHelper.GetSprintForDate(ManualSprints, CurrentDate) : null;

        foreach (var day in VisibleDays)
        {
            DailyScheduleItems.TryGetValue(day.Date, out var items);
            items ??= [];

            bool isCurrent = false;
            if (CurrentViewMode == ViewMode.Month)
            {
                isCurrent = day.Month == CurrentDate.Month && day.Year == CurrentDate.Year;
            }
            else if (CurrentViewMode == ViewMode.Sprint && sprint != null)
            {
                isCurrent = day.Date >= sprint.StartDate.Date && day.Date <= sprint.EndDate.Date;
            }

            bool isToday = day.Date == DateTime.Today;

            cells.Add(new CalendarCellViewModel(day, isCurrent, isToday, items));
        }
        CalendarCells = cells;
    }
}
