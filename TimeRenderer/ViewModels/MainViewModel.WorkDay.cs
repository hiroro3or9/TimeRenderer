using System;
using System.Collections.Generic;
using System.Linq;
using System.Windows.Input;

using TimeRenderer.Helpers;
using TimeRenderer.Models;

namespace TimeRenderer.ViewModels;

/// <summary>
/// 勤務（出勤・退勤）の記録。
///
/// 作業内容の記録（ScheduleItem）とは別の軸で、「その日の仕事をいつ始めていつ終えたか」を持つ。
/// 作業記録は取り忘れても後から補えるが、勤務時間は後から思い出しにくいので、
/// ホットキー1つで押せること・押し忘れても自動で救えることを優先する。
///
/// 方針:
/// - 未退勤の記録は<b>同時に1件だけ</b>。出勤時に古い未退勤があれば先に締める
/// - 自動で入れた退勤には <see cref="WorkEndSource"/> で印を付け、マーカー上で手動と区別する
/// - 勝手に確定させるのは「もう日付が変わっている」場合だけ。
///   当日中の離席・スリープは<b>確認してから</b>確定する（一時的な離席と区別が付かないため）
/// </summary>
public partial class MainViewModel
{
    private List<WorkDayLog> _workDayLogs = [];

    /// <summary>未退勤の勤務記録（無ければ null）</summary>
    private WorkDayLog? _activeWorkLog;

    /// <summary>勤務終了の確認ダイアログを表示中か（多重表示の抑止）</summary>
    private bool _isAskingWorkEnd;

    /// <summary>勤務記録の読み込みが済んだか（済むまで保存しない）</summary>
    private bool _workDaysLoaded;

    // ===== 設定 =====

    private bool _isWorkEndDetectionEnabled = true;
    /// <summary>離席・スリープからの復帰時に勤務終了を確認するか</summary>
    public bool IsWorkEndDetectionEnabled
    {
        get => _isWorkEndDetectionEnabled;
        set
        {
            if (SetProperty(ref _isWorkEndDetectionEnabled, value))
            {
                ApplyAwaySettings(); // 検知器の稼働条件・しきい値が変わる
                SaveSettings();
            }
        }
    }

    /// <summary>勤務終了とみなすまでの離席時間（分）の選択肢</summary>
    public static IReadOnlyList<int> WorkEndThresholdOptions { get; } = [15, 30, 45, 60, 90, 120];

    private int _workEndThresholdMinutes = 30;
    /// <summary>この時間だけ離席・スリープが続いたら勤務終了かどうかを尋ねる</summary>
    public int WorkEndThresholdMinutes
    {
        get => _workEndThresholdMinutes;
        set
        {
            var clamped = Math.Clamp(value, 5, 480);
            if (SetProperty(ref _workEndThresholdMinutes, clamped))
            {
                ApplyAwaySettings();
                SaveSettings();
            }
        }
    }

    // ===== 表示状態 =====

    /// <summary>いま勤務中か（出勤済みで未退勤）</summary>
    public bool IsWorking => _activeWorkLog != null;

    public string WorkDayButtonText => IsWorking ? "退勤" : "出勤";

    /// <summary>ツールバーに出す勤務状況（勤務中は経過時間、退勤後はその日の実績）</summary>
    public string WorkStatusText
    {
        get
        {
            if (_activeWorkLog != null)
            {
                return $"出勤 {_activeWorkLog.StartTime:H:mm} ・ {_activeWorkLog.DurationText}";
            }

            var todays = FindLogByDate(DateTime.Today);
            if (todays is { EndTime: not null })
            {
                return $"{todays.StartTime:H:mm} - {todays.EndTime.Value:H:mm} ・ {todays.DurationText}";
            }

            return "未出勤";
        }
    }

    private IReadOnlyList<WorkDayMarker> _workDayMarkers = [];
    /// <summary>日/週ビューに描く出勤・退勤の横ライン（表示中の期間ぶんだけ作る）</summary>
    public IReadOnlyList<WorkDayMarker> WorkDayMarkers
    {
        get => _workDayMarkers;
        private set => SetProperty(ref _workDayMarkers, value);
    }

    // ===== コマンド =====

    public ICommand ClockInCommand { get; private set; } = null!;
    public ICommand ClockOutCommand { get; private set; } = null!;
    /// <summary>1つのボタン・ホットキーで出勤／退勤を切り替える</summary>
    public ICommand ToggleWorkDayCommand { get; private set; } = null!;

    /// <summary>
    /// 勤務時間の編集ダイアログを開く。
    /// パラメータはマーカー（日/週ビューのラインをクリック）か日付。
    /// null の場合は表示中の日を対象にする（押し忘れた日の追加に使う）。
    /// </summary>
    public ICommand EditWorkDayCommand { get; private set; } = null!;

    /// <summary>マーカーの右クリックメニューからの削除</summary>
    public ICommand DeleteWorkDayCommand { get; private set; } = null!;

    private void InitializeWorkDayCommands()
    {
        ClockInCommand = new RelayCommand(_ => ClockIn(), _ => !IsWorking);
        ClockOutCommand = new RelayCommand(_ => ClockOut(), _ => IsWorking);
        ToggleWorkDayCommand = new RelayCommand(_ =>
        {
            if (IsWorking) ClockOut();
            else ClockIn();
        });

        EditWorkDayCommand = new RelayCommand(param => EditWorkDay(ResolveWorkDayDate(param)));
        DeleteWorkDayCommand = new RelayCommand(param =>
        {
            var date = ResolveWorkDayDate(param);
            if (FindLogByDate(date) == null) return;

            // メニューからの削除は取り消せないため、ここで一度止める
            if (_dialogService.ShowConfirmationDialog($"{date:M月d日}の勤務記録を削除しますか？", "削除確認"))
            {
                DeleteWorkDay(date);
            }
        });
    }

    /// <summary>コマンドパラメータ（マーカー / 日付 / null）から対象の勤務日を求める</summary>
    private DateTime ResolveWorkDayDate(object? param) => param switch
    {
        WorkDayMarker marker => marker.Date,
        DateTime date => date.Date,
        _ => CurrentDate.Date
    };

    // ===== 編集・削除 =====

    /// <summary>
    /// 勤務時間の編集ダイアログを開き、結果を反映する。
    /// 記録が無い日なら新規追加として扱う（押し忘れをあとから埋められるように）。
    /// </summary>
    public void EditWorkDay(DateTime date)
    {
        var existing = FindLogByDate(date);

        var result = _dialogService.ShowWorkDayEditDialog(
            date.Date, existing?.StartTime, existing?.EndTime, canDelete: existing != null);

        if (result == null) return;

        if (result.IsDeleted)
        {
            DeleteWorkDay(existing?.Date ?? result.Date);
            return;
        }

        ApplyWorkDayEdit(existing, result);
    }

    /// <summary>指定日の勤務記録を削除する</summary>
    public void DeleteWorkDay(DateTime date)
    {
        var log = FindLogByDate(date);
        if (log == null) return;

        _workDayLogs.Remove(log);
        RefreshActiveWorkLog();
        SaveWorkDays();
        NotifyWorkDayChanged();
    }

    /// <summary>
    /// 編集結果を反映する。
    /// 日付ごと動かせるようにしているため、移動先に既存の記録があれば置き換える
    /// （1日に複数の勤務記録は持たない）。
    /// </summary>
    private void ApplyWorkDayEdit(WorkDayLog? original, WorkDayEditResult result)
    {
        if (original != null) _workDayLogs.Remove(original);

        var conflict = FindLogByDate(result.Date);
        if (conflict != null) _workDayLogs.Remove(conflict);

        _workDayLogs.Add(new WorkDayLog
        {
            Date = result.Date,
            StartTime = result.StartTime,
            EndTime = result.EndTime,
            // 手で直した時刻は「自動で入れた値」ではなくなるので印を外す
            EndSource = WorkEndSource.Manual
        });

        _workDayLogs.Sort((a, b) => a.StartTime.CompareTo(b.StartTime));

        RefreshActiveWorkLog();
        SaveWorkDays();
        NotifyWorkDayChanged();
    }

    /// <summary>
    /// 未退勤の記録から「勤務中」の状態を組み直す。
    /// 編集で退勤を消した／入れた場合に、ボタンやホットキーの状態を追随させる。
    /// </summary>
    private void RefreshActiveWorkLog()
    {
        _activeWorkLog = _workDayLogs
            .Where(l => l.EndTime == null)
            .OrderBy(l => l.StartTime)
            .LastOrDefault();
    }

    // ===== 読み込み =====

    /// <summary>コンストラクタから呼ぶ。読み込み後、持ち越された未退勤があればここで締める</summary>
    private void LoadWorkDays()
    {
        _workDayLogs = Services.FilePersistenceService.LoadWorkDays();

        // 未退勤が複数あるのは異常（過去の不整合）。最新以外は締めておく
        var open = _workDayLogs.Where(l => l.EndTime == null).OrderBy(l => l.StartTime).ToList();
        for (int i = 0; i < open.Count - 1; i++)
        {
            CloseWithLastActivity(open[i]);
        }
        _activeWorkLog = open.LastOrDefault();
        _workDaysLoaded = true;

        CloseStaleWorkLog(DateTime.Now);
        NotifyWorkDayChanged();
    }

    /// <summary>
    /// 日付をまたいで残っている未退勤を、最終操作時刻で締める。
    ///
    /// 「スリープしたまま放置して翌日に開いた」ときに、
    /// 前日の勤務が延々と伸び続けるのを防ぐ。当日ぶんには手を出さない。
    /// </summary>
    private void CloseStaleWorkLog(DateTime now)
    {
        if (_activeWorkLog == null) return;
        if (_activeWorkLog.StartTime.Date >= now.Date) return;

        var log = _activeWorkLog;
        CloseWithLastActivity(log);
        _activeWorkLog = null;
        SaveWorkDays();
        NotifyWorkDayChanged();

        ShowAutoStartNotice(
            $"{log.Date:M月d日}の退勤が登録されていなかったため、" +
            $"{log.EndTime:H:mm}（最終の記録）で締めました。必要なら設定→勤務記録から直せます");
    }

    /// <summary>
    /// その日の「最後に何かしていた時刻」で締める。
    /// 判断材料は同じ日の作業記録の終了時刻。何も無ければ出勤時刻のまま（勤務時間0）にする。
    /// </summary>
    private void CloseWithLastActivity(WorkDayLog log)
    {
        var dayEnd = log.StartTime.Date.AddDays(1);

        var lastActivity = ScheduleItems
            .Where(i => !i.IsAllDay && i.EndTime > log.StartTime && i.EndTime < dayEnd)
            .Select(i => i.EndTime)
            .DefaultIfEmpty(log.StartTime)
            .Max();

        log.EndTime = lastActivity < log.StartTime ? log.StartTime : lastActivity;
        log.EndSource = WorkEndSource.AutoClosed;
    }

    // ===== 出勤・退勤 =====

    /// <summary>出勤を登録する。すでに勤務中なら何もしない</summary>
    /// <returns>登録できたら true</returns>
    public bool ClockIn(DateTime? at = null)
    {
        if (IsWorking) return false;

        var now = at ?? DateTime.Now;

        // 同じ日に一度退勤していて、また働き始めた場合は前の記録を伸ばす。
        // 1日に何本もマーカーが並ぶと「いつからいつまで働いたか」が読みにくくなるため
        var existing = FindLogByDate(now.Date);
        if (existing != null)
        {
            existing.EndTime = null;
            existing.EndSource = WorkEndSource.Manual;
            _activeWorkLog = existing;
        }
        else
        {
            var log = new WorkDayLog
            {
                Date = now.Date,
                StartTime = now
            };
            _workDayLogs.Add(log);
            _activeWorkLog = log;
        }

        SaveWorkDays();
        NotifyWorkDayChanged();
        return true;
    }

    /// <summary>退勤を登録する。勤務中でなければ何もしない</summary>
    /// <param name="at">退勤時刻（既定は現在時刻）</param>
    /// <param name="source">どう入った退勤か</param>
    /// <returns>登録できたら true</returns>
    public bool ClockOut(DateTime? at = null, WorkEndSource source = WorkEndSource.Manual)
    {
        var log = _activeWorkLog;
        if (log == null) return false;

        var end = at ?? DateTime.Now;
        if (end < log.StartTime) end = log.StartTime;

        log.EndTime = end;
        log.EndSource = source;
        _activeWorkLog = null;

        SaveWorkDays();
        NotifyWorkDayChanged();
        return true;
    }

    private void SaveWorkDays()
    {
        // 読み込み前に書くと、読めなかった空リストで上書きしてしまう
        if (!_workDaysLoaded) return;
        Services.FilePersistenceService.SaveWorkDays(_workDayLogs);
    }

    private WorkDayLog? FindLogByDate(DateTime date)
        => _workDayLogs.FirstOrDefault(l => l.StartTime.Date == date.Date);

    /// <summary>勤務の状態が変わったときに、依存する表示をまとめて更新する</summary>
    private void NotifyWorkDayChanged()
    {
        OnPropertyChanged(nameof(IsWorking));
        OnPropertyChanged(nameof(WorkDayButtonText));
        OnPropertyChanged(nameof(WorkStatusText));
        RebuildWorkDayMarkers();
    }

    /// <summary>時計から定期的に呼ぶ：経過時間の表示更新と、日付またぎの自動締め</summary>
    private void UpdateWorkDayTick(DateTime now)
    {
        CloseStaleWorkLog(now);
        if (IsWorking) OnPropertyChanged(nameof(WorkStatusText));
    }

    // ===== 表示用マーカー =====

    /// <summary>
    /// 表示中の期間に重なる勤務記録から、日/週ビュー用のマーカーを作る。
    /// 全期間ぶんを持たせると年々増えていくため、表示範囲に絞る。
    /// </summary>
    private void RebuildWorkDayMarkers()
    {
        if (VisibleDays.Count == 0)
        {
            WorkDayMarkers = [];
            return;
        }

        var from = VisibleDays[0].Date;
        var to = VisibleDays[^1].Date;

        var markers = new List<WorkDayMarker>();
        foreach (var log in _workDayLogs)
        {
            var date = log.StartTime.Date;
            if (date < from || date > to) continue;

            markers.Add(new WorkDayMarker(
                log.StartTime, IsStart: true, $"出勤 {log.StartTime:H:mm}", IsAuto: false, date));

            if (log.EndTime.HasValue)
            {
                var auto = log.EndSource != WorkEndSource.Manual;
                var suffix = auto ? "（自動）" : string.Empty;
                markers.Add(new WorkDayMarker(
                    log.EndTime.Value, IsStart: false,
                    $"退勤 {log.EndTime.Value:H:mm}{suffix} ・ {log.DurationText}", auto, date));
            }
        }

        WorkDayMarkers = markers;
    }

    // ===== 離席検知からの勤務終了 =====

    /// <summary>
    /// 離席・スリープを検知したときに呼ばれる。
    ///
    /// 一時的な離席と「そのまま帰った」の区別は付かないので、確定はせずに確認する。
    /// 確認は復帰後（＝この通知が来た時点）に出るため、
    /// スリープさせたまま放置しても、次に開いたときに正しい時刻で締められる。
    /// </summary>
    private void HandleAwayForWorkDay(AwayPeriod period)
    {
        if (!IsWorkEndDetectionEnabled) return;
        if (_isAskingWorkEnd) return;

        var log = _activeWorkLog;
        if (log == null) return;

        // 出勤より前の離席（前日から続くスリープなど）は対象外
        if (period.Start <= log.StartTime) return;
        if (period.Duration < TimeSpan.FromMinutes(_workEndThresholdMinutes)) return;

        var proposedEnd = period.Start;

        // SystemEvents（スリープ復帰）の通知内で待たせるとシステム側の処理を止めてしまうため、
        // ダイアログはUIの手が空いてから出す
        _isAskingWorkEnd = true;
        var dispatcher = System.Windows.Application.Current?.Dispatcher;
        if (dispatcher == null)
        {
            _isAskingWorkEnd = false;
            return;
        }

        dispatcher.BeginInvoke(new Action(() =>
        {
            try
            {
                // 待っている間に自分で退勤していたら何も聞かない
                if (!ReferenceEquals(_activeWorkLog, log)) return;

                var message =
                    $"{period.ReasonText}が {period.DurationText} 続きました（{period.RangeText}）。\n\n" +
                    $"{proposedEnd:H:mm} を退勤時刻として記録しますか？\n" +
                    "「いいえ」を選ぶと勤務は続いたままになります。";

                if (_dialogService.ShowConfirmationDialog(message, "勤務の終了"))
                {
                    ClockOut(proposedEnd, WorkEndSource.AwayDetected);
                    ShowAutoStartNotice($"{proposedEnd:H:mm} で退勤を記録しました");
                }
            }
            finally
            {
                _isAskingWorkEnd = false;
            }
        }), System.Windows.Threading.DispatcherPriority.ApplicationIdle);
    }
}
