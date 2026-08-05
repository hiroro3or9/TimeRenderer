using System;
using System.Collections.Generic;
using System.Linq;
using System.Windows.Input;

using TimeRenderer.Helpers;
using TimeRenderer.Models;
using TimeRenderer.Services;

namespace TimeRenderer.ViewModels;

/// <summary>
/// 1つのアプリ内で、同じウィンドウタイトルが前面にあった時間の合計。
/// タイトルを保存する記録中の区間だけを集計する。
/// </summary>
/// <param name="Title">ウィンドウタイトル</param>
/// <param name="Duration">合計使用時間</param>
public sealed record AppUsageTitleStat(string Title, TimeSpan Duration)
{
    public string DurationText => Duration.TotalHours >= 1
        ? $"{(int)Duration.TotalHours}時間{Duration.Minutes}分"
        : Duration.TotalMinutes >= 1
            ? $"{(int)Duration.TotalMinutes}分"
            : $"{Math.Max(1, (int)Duration.TotalSeconds)}秒";
}

/// <summary>
/// アプリ使用状況の統計1行分（プロセス単位に集計したもの）
/// </summary>
/// <param name="ProcessName">プロセス名。過去の記録との突き合わせに使う（表示はしない）</param>
/// <param name="AppName">表示用のアプリ名</param>
/// <param name="Duration">合計使用時間</param>
/// <param name="Percent">収集できた時間に対する割合</param>
/// <param name="SampleTitle">代表のウィンドウタイトル（記録中でなければ空）</param>
public sealed record AppUsageStat(
    string ProcessName,
    string AppName,
    TimeSpan Duration,
    double Percent,
    string SampleTitle)
{
    /// <summary>記録中の区間をタイトル別に集計した内訳</summary>
    public IReadOnlyList<AppUsageTitleStat> TitleStats { get; init; } = [];

    /// <summary>タイトルを残していない時間帯もあるため、表示の出し分けに使う</summary>
    public bool HasSampleTitle => !string.IsNullOrWhiteSpace(SampleTitle);

    /// <summary>正確なタイトル別時間を表示できるか</summary>
    public bool HasTitleStats => TitleStats.Count > 0;

    /// <summary>旧形式の代表タイトルだけがあり、正確なタイトル別時間が無いか</summary>
    public bool HasLegacySampleTitle => HasSampleTitle && !HasTitleStats;

    public string TitleTrackedDurationText
    {
        get
        {
            var duration = TimeSpan.FromTicks(TitleStats.Sum(s => s.Duration.Ticks));
            return duration.TotalHours >= 1
                ? $"{(int)duration.TotalHours}時間{duration.Minutes}分"
                : duration.TotalMinutes >= 1
                    ? $"{(int)duration.TotalMinutes}分"
                    : $"{(int)duration.TotalSeconds}秒";
        }
    }

    public string DurationText => Duration.TotalHours >= 1
        ? $"{(int)Duration.TotalHours}時間{Duration.Minutes}分"
        : Duration.TotalMinutes >= 1
            ? $"{(int)Duration.TotalMinutes}分"
            : $"{(int)Duration.TotalSeconds}秒";

    public string PercentText => $"{Percent:0}%";
}

/// <summary>
/// 使用アプリの自動記録。
///
/// 記録を止めたあとに「この2時間、実際は何をしていたのか」を思い出せないことがある。
/// 前面にあったアプリを自動で控えておき、予定アイテムの時間帯に対応する内訳や、
/// 記録が抜けている時間帯の裏付けとして使う。
///
/// 収集するのは<b>勤務中</b>（出勤〜退勤）。当初は記録中だけにしていたが、それだと
/// 記録漏れの時間帯には材料が一切残らず、未記録の帯を埋める助けにできなかった。
///
/// ただし<b>ウィンドウタイトルを残すのは記録中だけ</b>にしている。
/// タイトルにはファイル名・URL・チャット相手などの中身が出るため、
/// 記録していない時間のぶんまで残すのは踏み込みすぎだと判断した。
///
/// その他の方針は離席検知と同じ:
/// - 記録そのものには一切手を加えない。あくまで裏付け情報として別に持つ
/// - データはローカル（%APPDATA%）にのみ保存し、保持期間を過ぎたら自動で消す
/// </summary>
public partial class MainViewModel
{
    /// <summary>収集した使用期間をファイルへ吐き出す間隔（途中で落ちた日のぶんを失わないため）</summary>
    private static readonly TimeSpan AppUsageFlushInterval = TimeSpan.FromMinutes(5);

    private DateTime _lastAppUsageFlush = DateTime.MinValue;

    private ActiveWindowTracker? _appUsageTracker;

    /// <summary>読み込み済みのアプリ使用記録（開始時刻順）</summary>
    private List<AppUsageInterval> _appUsageHistory = [];

    // ===== 設定 =====

    private bool _isAppUsageTrackingEnabled = true;
    /// <summary>勤務中に前面アプリを自動記録するか</summary>
    public bool IsAppUsageTrackingEnabled
    {
        get => _isAppUsageTrackingEnabled;
        set
        {
            if (SetProperty(ref _isAppUsageTrackingEnabled, value))
            {
                // 勤務の途中で切り替えられた場合もその場で反映する
                ApplyAppUsageTrackingState();
                SaveSettings();
            }
        }
    }

    // ===== 初期化・終了 =====

    /// <summary>コンストラクタから呼ぶ</summary>
    private void InitializeAppUsageTracking()
    {
        _appUsageTracker = new ActiveWindowTracker();
    }

    private void LoadAppUsage()
    {
        _appUsageHistory = Services.FilePersistenceService.LoadAppUsage();
    }

    /// <summary>アプリ終了時に呼ぶ。記録中なら収集済み分を確定して保存する</summary>
    public void DisposeAppUsageTracking()
    {
        if (_appUsageTracker == null) return;

        CollectAndSaveAppUsage();
        _appUsageTracker.Dispose();
        _appUsageTracker = null;
    }

    // ===== 収集状態の制御 =====

    /// <summary>
    /// 収集すべき状態かを判定して、トラッカーを合わせる。
    ///
    /// 収集の可否は「設定 × 勤務中か」、タイトルを残すかは「記録中か」で決まる。
    /// 出勤・退勤・記録の開始終了・設定変更のどれからでもここを通せば辻褄が合うよう、
    /// 個別に Start/Stop を呼ばずこの1本にまとめている。
    /// </summary>
    private void ApplyAppUsageTrackingState()
    {
        if (_appUsageTracker == null) return;

        if (IsAppUsageTrackingEnabled && IsWorking)
        {
            _appUsageTracker.SetCaptureWindowTitles(IsRecording);
            if (!_appUsageTracker.IsCollecting)
            {
                _appUsageTracker.Start();
                _lastAppUsageFlush = DateTime.Now;
            }
        }
        else
        {
            CollectAndSaveAppUsage();
        }
    }

    /// <summary>記録の開始・終了から呼ばれる（タイトルを残すかどうかが変わる）</summary>
    private void OnRecordingChangedForAppUsage(bool isRecording)
    {
        _ = isRecording; // 判定は ApplyAppUsageTrackingState 側で IsRecording を見る
        ApplyAppUsageTrackingState();

        // モード切替で確定した区間をすぐ保存する。
        // 記録停止直後に使用アプリを開いても、直前のタイトル内訳を見られるようにする。
        DrainAndSaveAppUsage();
    }

    /// <summary>
    /// 時計から定期的に呼ぶ。収集を止めずに、確定済みの期間だけをファイルへ吐き出す。
    /// 退勤まで書き出さないと、途中で落ちた日のぶんがまるごと消えるため。
    /// </summary>
    private void UpdateAppUsageTick(DateTime now)
    {
        if (_appUsageTracker == null || !_appUsageTracker.IsCollecting) return;
        if (now - _lastAppUsageFlush < AppUsageFlushInterval) return;

        _lastAppUsageFlush = now;

        DrainAndSaveAppUsage();
    }

    /// <summary>収集を続けたまま、確定済みの使用期間を履歴へ足して保存する</summary>
    private void DrainAndSaveAppUsage()
    {
        if (_appUsageTracker == null || !_appUsageTracker.IsCollecting) return;

        var intervals = _appUsageTracker.Drain();
        if (intervals.Count == 0) return;

        _appUsageHistory.AddRange(intervals);
        Services.FilePersistenceService.SaveAppUsage(_appUsageHistory);
    }

    /// <summary>収集中の使用期間を確定し、履歴へ足して保存する</summary>
    private void CollectAndSaveAppUsage()
    {
        if (_appUsageTracker == null || !_appUsageTracker.IsCollecting) return;

        var intervals = _appUsageTracker.Stop();
        if (intervals.Count == 0) return;

        _appUsageHistory.AddRange(intervals);
        Services.FilePersistenceService.SaveAppUsage(_appUsageHistory);
    }

    // ===== 表示（予定アイテムの時間帯に対応する内訳） =====

    /// <summary>予定アイテムの時間帯に使っていたアプリの内訳を表示する</summary>
    public ICommand ShowAppUsageCommand => _showAppUsageCommand ??= new RelayCommand(
        param =>
        {
            if (param is not ScheduleItem item) return;

            var stats = GetAppUsageStats(item.StartTime, item.EndTime);
            if (stats.Count == 0)
            {
                _dialogService.ShowMessage(
                    "この時間帯のアプリ使用記録はありません。\n" +
                    "使用アプリは出勤から退勤までの間だけ収集されます（設定でオン/オフできます）。",
                    "使用アプリ");
                return;
            }

            _dialogService.ShowAppUsageDialog(item.Title, item.StartTime, item.EndTime, stats);
        },
        param => param is ScheduleItem);
    private RelayCommand? _showAppUsageCommand;

    /// <summary>
    /// 指定範囲と重なる使用期間をプロセス単位に集計する。
    /// 割合の分母は「範囲の長さ」ではなく「範囲内で収集できていた合計時間」。
    /// 記録より広い予定に対しても、収集できていた部分の内訳として意味が通るようにする。
    /// </summary>
    internal List<AppUsageStat> GetAppUsageStats(DateTime start, DateTime end)
    {
        var clipped = _appUsageHistory
            .Select(i => i.ClipTo(start, end))
            .Where(i => i != null)
            .Select(i => i!)
            .ToList();

        if (clipped.Count == 0) return [];

        var totalTicks = clipped.Sum(i => i.Duration.Ticks);
        if (totalTicks <= 0) return [];

        return [.. clipped
            .GroupBy(i => i.ProcessName)
            .Select(g =>
            {
                var duration = TimeSpan.FromTicks(g.Sum(i => i.Duration.Ticks));
                // 代表タイトルは、一番長く使っていた期間のもの
                var longest = g.OrderByDescending(i => i.Duration).First();
                var appName = string.IsNullOrEmpty(longest.AppName) ? g.Key : longest.AppName;

                // 代表タイトルは、タイトルが残っている期間の中で一番長いものを採る。
                // 記録中でない時間は空なので、単に最長の期間から取ると空になってしまう
                var sampleTitle = g
                    .Where(i => !string.IsNullOrWhiteSpace(i.WindowTitle))
                    .OrderByDescending(i => i.Duration)
                    .FirstOrDefault()?.WindowTitle ?? string.Empty;

                // 旧データは「期間中の最後のタイトル」しか持たず、期間全体をそのタイトルへ
                // 計上すると誤った内訳になる。タイトル変更で区切った新形式だけを集計する。
                var titleStats = g
                    .Where(i => i.IsWindowTitleSpecific && !string.IsNullOrWhiteSpace(i.WindowTitle))
                    .GroupBy(i => i.WindowTitle.Trim(), StringComparer.Ordinal)
                    .Select(titleGroup => new AppUsageTitleStat(
                        titleGroup.Key,
                        TimeSpan.FromTicks(titleGroup.Sum(i => i.Duration.Ticks))))
                    .OrderByDescending(s => s.Duration)
                    .ToList();

                return new AppUsageStat(
                    g.Key,
                    appName,
                    duration,
                    duration.Ticks * 100.0 / totalTicks,
                    sampleTitle)
                {
                    TitleStats = titleStats
                };
            })
            .OrderByDescending(s => s.Duration)];
    }
}
