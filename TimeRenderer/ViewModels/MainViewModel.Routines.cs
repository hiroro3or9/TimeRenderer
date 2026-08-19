using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using System.Media;
using System.Windows.Input;
using System.Windows.Threading;

using TimeRenderer.Models;
using TimeRenderer.Helpers;

namespace TimeRenderer.ViewModels;

/// <summary>
/// 定期予定（ルーティン）の管理。
/// 「記録開始を忘れる」対策として、決まった曜日・日付の予定（毎週／N週ごと／毎月／Nヶ月ごと）を
/// 自動生成し、開始時刻にはリマインダー通知（または設定に応じた自動記録開始）を行う。
/// </summary>
public partial class MainViewModel
{
    private List<RoutineScheduleItem> _routines = [];
    public List<RoutineScheduleItem> Routines
    {
        get => _routines;
        set
        {
            if (SetProperty(ref _routines, value))
            {
                SaveSettings();
                RebuildRoutineOccurrences(CurrentDate);
            }
        }
    }

    /// <summary>開始時刻に達し、まだユーザーの操作を待っているリマインダー対象のアイテム</summary>
    public ObservableCollection<ScheduleItem> PendingReminders { get; } = [];

    /// <summary>同一セッション内で既にリマインダー判定（通知 or 自動開始）を行ったアイテム</summary>
    private readonly HashSet<ScheduleItem> _remindedRoutineItems = [];

    private DateTime? _lastRoutineGenerationDate;

    private string? _autoStartNotice;
    /// <summary>自動記録開始が行われたことを一時的に知らせるメッセージ（数秒で自動的に消える）</summary>
    public string? AutoStartNotice
    {
        get => _autoStartNotice;
        set
        {
            if (SetProperty(ref _autoStartNotice, value))
            {
                OnPropertyChanged(nameof(HasAutoStartNotice));
            }
        }
    }

    public bool HasAutoStartNotice => !string.IsNullOrEmpty(AutoStartNotice);

    private DispatcherTimer? _autoStartNoticeTimer;

    public ICommand AddRoutineCommand { get; private set; } = null!;
    public ICommand EditRoutineCommand { get; private set; } = null!;
    public ICommand DeleteRoutineCommand { get; private set; } = null!;
    public ICommand StartReminderCommand { get; private set; } = null!;
    public ICommand DismissReminderCommand { get; private set; } = null!;

    private void InitializeRoutineCommands()
    {
        AddRoutineCommand = new RelayCommand(_ =>
        {
            var result = _dialogService.ShowRoutineEditDialog(
                null, [.. Categories], GetTitleSuggestions(), ActiveProjectCodes, DefaultProjectCode);
            if (result != null)
            {
                var list = new List<RoutineScheduleItem>(Routines) { result };
                Routines = list;
            }
        });

        EditRoutineCommand = new RelayCommand(
            param =>
            {
                if (param is RoutineScheduleItem routine)
                {
                    var result = _dialogService.ShowRoutineEditDialog(
                        routine, [.. Categories], GetTitleSuggestions(),
                        GetSelectableProjectCodes(routine.ProjectCodeId), DefaultProjectCode);
                    if (result != null)
                    {
                        // 編集ダイアログは除外日を扱わないため、既存の除外日を引き継ぐ
                        result.ExcludedDates = routine.ExcludedDates;
                        var list = new List<RoutineScheduleItem>(Routines);
                        var index = list.FindIndex(r => r.Id == routine.Id);
                        if (index >= 0)
                        {
                            list[index] = result;
                        }
                        Routines = list;
                    }
                }
            },
            param => param is RoutineScheduleItem
        );

        DeleteRoutineCommand = new RelayCommand(
            param =>
            {
                if (param is RoutineScheduleItem routine)
                {
                    if (_dialogService.ShowConfirmationDialog(
                        $"定期予定「{routine.Title}」を削除しますか？\n（記録済み・個別編集済みのアイテムは残ります）", "削除確認"))
                    {
                        var list = new List<RoutineScheduleItem>(Routines);
                        list.RemoveAll(r => r.Id == routine.Id);
                        Routines = list;
                    }
                }
            },
            param => param is RoutineScheduleItem
        );

        StartReminderCommand = new RelayCommand(
            param =>
            {
                if (param is ScheduleItem item)
                {
                    PendingReminders.Remove(item);
                    // 予定アイテム自体を実績として使う（停止時に別アイテムを作らない）
                    StartRecordingFromItem(item);
                }
            },
            param => param is ScheduleItem
        );

        DismissReminderCommand = new RelayCommand(
            param =>
            {
                if (param is ScheduleItem item)
                {
                    PendingReminders.Remove(item);
                }
            },
            param => param is ScheduleItem
        );
    }

    /// <summary>
    /// 有効な定期予定について、指定日を中心とした一定期間（過去7日～先60日）に
    /// 未生成の「仮想アイテム」（IsVirtual=true、保存されない）があれば生成する。
    /// 実体アイテム（記録済み・個別編集済み）がある日と、除外日（ExcludedDates）、
    /// および定期予定の開始日（StartDate）より前の日には生成しない。
    /// </summary>
    private void EnsureRoutineOccurrences(DateTime aroundDate)
    {
        if (Routines.Count == 0) return;

        var windowStart = aroundDate.Date.AddDays(-7);
        var rangeEnd = aroundDate.Date.AddDays(60);

        var existingKeys = ScheduleItems
            .Where(i => i.IsPlanned && i.RoutineId != null)
            .Select(i => (i.RoutineId, i.StartTime.Date))
            .ToHashSet();

        var toAdd = new List<ScheduleItem>();
        foreach (var routine in Routines)
        {
            if (!routine.IsEnabled || !routine.IsValidRecurrence) continue;

            var categoryColor = routine.CategoryId != null
                ? Categories.FirstOrDefault(c => c.Id == routine.CategoryId)?.ColorCode
                : null;
            var excluded = routine.ExcludedDates.Select(d => d.Date).ToHashSet();

            // 開始日より前には生成しない（定期予定を作る前の過去に予定が現れないようにする）
            var rangeStart = routine.StartDate > windowStart ? routine.StartDate.Date : windowStart;

            for (var date = rangeStart; date <= rangeEnd; date = date.AddDays(1))
            {
                if (!routine.OccursOn(date)) continue;
                if (excluded.Contains(date)) continue;
                if (existingKeys.Contains((routine.Id, date))) continue;

                toAdd.Add(new ScheduleItem
                {
                    Id = $"routine:{routine.Id}:{date:yyyyMMdd}",
                    Kind = ScheduleItemKind.Planned,
                    Title = routine.Title,
                    StartTime = date.Add(routine.StartTime),
                    EndTime = date.Add(routine.EndTime),
                    ColorCode = categoryColor ?? routine.ColorCode,
                    CategoryId = routine.CategoryId,
                    ProjectCodeId = routine.ProjectCodeId ?? DefaultProjectCode?.Id,
                    RoutineId = routine.Id,
                    IsVirtual = true
                });
            }
        }

        if (toAdd.Count == 0) return;

        // ロード中と同様に、1件ずつの再計算を避けて最後にまとめて実行する
        var wasLoading = _isLoadingData;
        _isLoadingData = true;
        try
        {
            foreach (var item in toAdd)
            {
                ScheduleItems.Add(item);
            }
        }
        finally
        {
            _isLoadingData = wasLoading;
        }
        RecalculateLayout();
        // 仮想アイテムは保存対象外のため SaveData は不要
    }

    /// <summary>
    /// すべての仮想アイテムを取り除いてから生成し直す。
    /// 定期予定の追加・編集・削除・除外日の変更を表示へ反映するために呼ぶ。
    /// </summary>
    private void RebuildRoutineOccurrences(DateTime aroundDate)
    {
        var virtuals = ScheduleItems.Where(i => i.IsVirtual).ToList();

        var wasLoading = _isLoadingData;
        _isLoadingData = true;
        try
        {
            foreach (var item in virtuals)
            {
                ScheduleItems.Remove(item);
                PendingReminders.Remove(item);
                _remindedRoutineItems.Remove(item);
            }
        }
        finally
        {
            _isLoadingData = wasLoading;
        }

        EnsureRoutineOccurrences(aroundDate);
        RecalculateLayout();
    }

    /// <summary>
    /// 開始日を持たない旧データの移行：StartDate が未設定（既定値）の定期予定に当日を設定する。
    /// これにより、以前から登録されている定期予定も過去には表示されなくなる。
    /// 過去にも表示したい場合はダイアログで開始日を遡って設定できる。
    /// 起動時に1回だけ呼ぶ。
    /// </summary>
    private void MigrateRoutineStartDates()
    {
        var targets = Routines.Where(r => r.StartDate == default).ToList();
        if (targets.Count == 0) return;

        var today = DateTime.Today;
        foreach (var routine in targets)
        {
            routine.StartDate = today;
        }
        SaveSettings();
    }

    /// <summary>
    /// 旧方式からの移行：以前は定期予定から実体アイテムを生成・保存していた。
    /// テンプレートと完全に一致する未来の未編集アイテムを取り除き、仮想表示に置き換える。
    /// 過去の分（記録）と、時刻やタイトル・メモが編集されている分はそのまま残す。
    /// 起動時に1回だけ呼ぶ。
    /// </summary>
    private void MigrateGeneratedRoutineItems()
    {
        if (Routines.Count == 0) return;

        var now = DateTime.Now;
        var routinesById = Routines.ToDictionary(r => r.Id);

        var toRemove = ScheduleItems.Where(i =>
            !i.IsVirtual &&
            i.IsPlanned &&
            i.RoutineId != null &&
            i.StartTime > now &&
            routinesById.TryGetValue(i.RoutineId, out var r) &&
            i.Title == r.Title &&
            !i.IsAllDay &&
            string.IsNullOrWhiteSpace(i.Content) &&
            i.StartTime.Date == i.EndTime.Date &&
            i.StartTime.TimeOfDay == r.StartTime &&
            i.EndTime.TimeOfDay == r.EndTime &&
            r.OccursOn(i.StartTime.Date) &&
            !r.ExcludedDates.Contains(i.StartTime.Date)).ToList();

        if (toRemove.Count == 0) return;

        var wasLoading = _isLoadingData;
        _isLoadingData = true;
        try
        {
            foreach (var item in toRemove)
            {
                ScheduleItems.Remove(item);
            }
        }
        finally
        {
            _isLoadingData = wasLoading;
        }
        SaveData();
    }

    /// <summary>
    /// 仮想アイテムを実体化する。該当日を定期予定の除外日に加えることで、
    /// 同じ日に仮想アイテムが二重生成されるのを防ぎ、以後は通常のアイテムとして保存される。
    /// </summary>
    /// <param name="item">実体化する仮想アイテム</param>
    /// <param name="occurrenceDate">
    /// もともとの発生日。編集で日付が変わる場合があるため、変更前の日付を渡す。
    /// null なら item の現在の日付を使う。
    /// </param>
    private void MaterializeOccurrence(ScheduleItem item, DateTime? occurrenceDate = null)
    {
        if (!item.IsVirtual) return;

        var date = (occurrenceDate ?? item.StartTime).Date;
        var routine = Routines.FirstOrDefault(r => r.Id == item.RoutineId);
        if (routine != null && !routine.ExcludedDates.Contains(date))
        {
            routine.ExcludedDates.Add(date);
            SaveSettings();
        }

        item.IsVirtual = false;
        SaveData();
    }

    /// <summary>
    /// 定期予定由来の実体アイテムを削除したとき、その日を除外日に加える。
    /// 加えないと、削除直後の再生成で同じ日に仮想アイテムが現れて「復活」して見える。
    /// 定期予定由来でないアイテムには何もしない。
    /// </summary>
    private void AddRoutineExclusionFor(ScheduleItem item)
    {
        if (item.RoutineId == null) return;

        var routine = Routines.FirstOrDefault(r => r.Id == item.RoutineId);
        if (routine == null) return;

        var date = item.StartTime.Date;
        if (!routine.ExcludedDates.Contains(date))
        {
            routine.ExcludedDates.Add(date);
            SaveSettings();
        }
    }

    /// <summary>「この日だけ削除」：該当日を除外日に加えて仮想アイテムを取り除く</summary>
    private void DeleteOccurrenceForDay(ScheduleItem item)
    {
        var date = item.StartTime.Date;
        var routine = Routines.FirstOrDefault(r => r.Id == item.RoutineId);
        if (routine != null && !routine.ExcludedDates.Contains(date))
        {
            routine.ExcludedDates.Add(date);
            SaveSettings();
        }

        ScheduleItems.Remove(item);
        PendingReminders.Remove(item);
        _remindedRoutineItems.Remove(item);
    }

    /// <summary>
    /// 仮想アイテムの削除。「この日のみ／定期予定全体／キャンセル」をユーザーに確認する。
    /// 定期予定側（除外日・テンプレート）の変更のため、取り消し履歴には積まない。
    /// </summary>
    private void DeleteRoutineOccurrence(ScheduleItem item)
    {
        var routine = Routines.FirstOrDefault(r => r.Id == item.RoutineId);
        var scope = _dialogService.ShowRoutineScopeDialog(
            $"「{item.Title}」は定期予定です。どの範囲を削除しますか？", "定期予定の削除");

        switch (scope)
        {
            case Services.RoutineScope.ThisDay:
                DeleteOccurrenceForDay(item);
                break;

            case Services.RoutineScope.WholeSeries:
                if (routine != null)
                {
                    var list = new List<RoutineScheduleItem>(Routines);
                    list.RemoveAll(r => r.Id == routine.Id);
                    Routines = list; // setter が保存と仮想アイテムの再生成を行う
                }
                else
                {
                    // テンプレートが見つからない場合（異常系）はこの日の分だけ消す
                    DeleteOccurrenceForDay(item);
                }
                break;
        }
    }

    /// <summary>
    /// 仮想アイテムの編集。「この日のみ／定期予定全体／キャンセル」をユーザーに確認する。
    /// この日のみ：アイテムを実体化して編集内容を適用する（その日だけ独立したアイテムになる）。
    /// 全体：定期予定テンプレートの編集ダイアログを開く。
    /// </summary>
    private void EditRoutineOccurrence(ScheduleItem item)
    {
        var scope = _dialogService.ShowRoutineScopeDialog(
            $"「{item.Title}」は定期予定です。どの範囲を編集しますか？", "定期予定の編集");

        switch (scope)
        {
            case Services.RoutineScope.ThisDay:
            {
                var edited = _dialogService.ShowScheduleEditDialog(
                    item, [.. Categories], GetTitleSuggestions(),
                    GetSelectableProjectCodes(item.ProjectCodeId), DefaultProjectCode);
                if (edited == null) return;

                var originalDate = item.StartTime.Date;

                _isBatchUpdatingItem = true;
                try
                {
                    item.Title = edited.Title;
                    item.Content = edited.Content;
                    item.Kind = edited.Kind;
                    item.StartTime = edited.StartTime;
                    item.EndTime = edited.EndTime;
                    item.IsAllDay = edited.IsAllDay;
                    item.BackgroundColor = edited.BackgroundColor;
                    item.CategoryId = edited.CategoryId;
                    item.ProjectCodeId = edited.ProjectCodeId;
                    item.RemindAtStart = edited.RemindAtStart;
                    item.AutoStartRecording = edited.AutoStartRecording;
                    item.ForceStartRecording = edited.ForceStartRecording;
                }
                finally
                {
                    _isBatchUpdatingItem = false;
                }

                MaterializeOccurrence(item, originalDate);
                RecalculateLayout();
                break;
            }

            case Services.RoutineScope.WholeSeries:
            {
                var routine = Routines.FirstOrDefault(r => r.Id == item.RoutineId);
                if (routine == null) return;

                var result = _dialogService.ShowRoutineEditDialog(
                    routine, [.. Categories], GetTitleSuggestions(),
                    GetSelectableProjectCodes(routine.ProjectCodeId), DefaultProjectCode);
                if (result != null)
                {
                    // 編集ダイアログは除外日を扱わないため、既存の除外日を引き継ぐ
                    result.ExcludedDates = routine.ExcludedDates;
                    var list = new List<RoutineScheduleItem>(Routines);
                    var index = list.FindIndex(r => r.Id == routine.Id);
                    if (index >= 0)
                    {
                        list[index] = result;
                    }
                    Routines = list;
                }
                break;
            }
        }
    }

    /// <summary>
    /// 仮想アイテムのドラッグ（移動・伸縮）確定。「この日のみ／定期予定全体／キャンセル」を確認する。
    /// この日のみ：実体化して新しい時刻を確定する。
    /// 全体：アイテムは元に戻し、新しい時刻（時刻部分のみ）をテンプレートへ反映する。
    /// </summary>
    private void CommitVirtualItemDrag(ScheduleItem item, ItemSnapshot before)
    {
        var newStart = item.StartTime;
        var newEnd = item.EndTime;

        var scope = _dialogService.ShowRoutineScopeDialog(
            $"「{item.Title}」は定期予定です。どの範囲に時間の変更を適用しますか？", "定期予定の時間変更");

        switch (scope)
        {
            case Services.RoutineScope.ThisDay:
                MaterializeOccurrence(item, before.StartTime.Date);
                RecalculateLayout();
                break;

            case Services.RoutineScope.WholeSeries:
            {
                // アイテム側は元へ戻し、テンプレートの時刻を変更して再生成する
                UpdateItemTimesPreview(item, before.StartTime, before.EndTime);

                var routine = Routines.FirstOrDefault(r => r.Id == item.RoutineId);
                if (routine == null) break;

                if (newStart.Date != newEnd.Date || newEnd.TimeOfDay <= newStart.TimeOfDay)
                {
                    _dialogService.ShowMessage(
                        "日をまたぐ時間帯は定期予定全体には設定できません。", "定期予定の時間変更");
                    break;
                }

                routine.StartTime = newStart.TimeOfDay;
                routine.EndTime = newEnd.TimeOfDay;
                SaveSettings();
                RebuildRoutineOccurrences(CurrentDate);
                break;
            }

            default:
                // キャンセル：プレビューで動かした時刻を元に戻す
                UpdateItemTimesPreview(item, before.StartTime, before.EndTime);
                break;
        }
    }

    /// <summary>
    /// 開始時刻に達した予定を判定し、設定に応じて自動記録開始またはリマインダー通知
    /// （バナー表示＋通知音）を行う。対象は以下の2種類：
    /// ・定期予定から生成されたアイテム（RoutineId あり）→ ルーティン側の IsAutoStart 設定に従う
    /// ・手動登録の単発予定 → アイテム自身の RemindAtStart / AutoStartRecording フラグに従う
    /// 毎tick呼ばれる想定だが、判定済みのアイテムはセッション中は再判定しない。
    /// </summary>
    private void CheckReminders(DateTime now)
    {
        // 定期予定生成のローリングウィンドウを1日1回更新する（アプリを開きっぱなしにしていても先の予定が生成され続ける）
        if (_lastRoutineGenerationDate != now.Date)
        {
            _lastRoutineGenerationDate = now.Date;
            EnsureRoutineOccurrences(now.Date);
        }

        // 強制自動開始では、進行中の記録を停止して実績を ScheduleItems へ追加する場合がある。
        // 元コレクションが変わっても次の MoveNext で例外にならないよう、スナップショットを走査する。
        foreach (var item in ScheduleItems.ToList())
        {
            if (!item.IsPlanned || item.IsAllDay) continue;
            if (_remindedRoutineItems.Contains(item)) continue;
            if (item.StartTime.Date != now.Date) continue;
            if (now < item.StartTime) continue;

            // 対象判定：定期予定由来か、通知/自動開始フラグ付きの単発予定のみ
            bool autoStart;
            bool forceStart;
            if (item.RoutineId != null)
            {
                var routine = Routines.FirstOrDefault(r => r.Id == item.RoutineId);
                autoStart = routine?.IsAutoStart == true;
                forceStart = routine?.IsForceStart == true;
            }
            else if (item.AutoStartRecording || item.RemindAtStart)
            {
                autoStart = item.AutoStartRecording;
                forceStart = item.ForceStartRecording;
            }
            else
            {
                continue; // フラグなしの通常アイテム（記録済みログ等）は対象外
            }

            _remindedRoutineItems.Add(item);

            // アプリ起動直後などで開始からかなり時間が経っている場合は通知しない（古いリマインダーの氾濫防止）
            if (now - item.StartTime > TimeSpan.FromMinutes(15)) continue;

            // 強制開始が有効なら記録中でも開始する（StartRecordingFromItem が現在の記録を停止・保存する）
            if (autoStart && (forceStart || !IsRecording))
            {
                // 予定アイテム自体を実績として使う（停止時に別アイテムを作らない）
                StartRecordingFromItem(item);
                ShowAutoStartNotice($"「{item.Title}」の記録を自動開始しました");
            }
            else if (!PendingReminders.Contains(item))
            {
                PendingReminders.Add(item);
                SystemSounds.Asterisk.Play();
            }
        }
    }

    private void ShowAutoStartNotice(string message)
    {
        AutoStartNotice = message;

        _autoStartNoticeTimer ??= new DispatcherTimer { Interval = TimeSpan.FromSeconds(6) };
        _autoStartNoticeTimer.Stop();
        _autoStartNoticeTimer.Tick -= AutoStartNoticeTimer_Tick;
        _autoStartNoticeTimer.Tick += AutoStartNoticeTimer_Tick;
        _autoStartNoticeTimer.Start();
    }

    private void AutoStartNoticeTimer_Tick(object? sender, EventArgs e)
    {
        _autoStartNoticeTimer?.Stop();
        AutoStartNotice = null;
    }
}
