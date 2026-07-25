using System;
using System.Collections.Generic;
using System.Linq;
using System.Windows.Input;

using TimeRenderer.Helpers;
using TimeRenderer.Models;
using TimeRenderer.Services;

namespace TimeRenderer.ViewModels;

/// <summary>
/// アプリ使用状況の統計1行分（プロセス単位に集計したもの）
/// </summary>
public sealed record AppUsageStat(string AppName, TimeSpan Duration, double Percent, string SampleTitle)
{
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
/// 記録中に前面にあったアプリを自動で控えておき、あとから予定アイテムの時間帯に
/// 対応する使用内訳を確認できるようにする。
///
/// 方針は離席検知と同じ:
/// - 収集するのは<b>記録中だけ</b>。記録していない時間は監視しない
/// - 記録そのものには一切手を加えない。あくまで裏付け情報として別に持つ
/// - データはローカル（%APPDATA%）にのみ保存し、保持期間を過ぎたら自動で消す
/// </summary>
public partial class MainViewModel
{
    private ActiveWindowTracker? _appUsageTracker;

    /// <summary>読み込み済みのアプリ使用記録（開始時刻順）</summary>
    private List<AppUsageInterval> _appUsageHistory = [];

    // ===== 設定 =====

    private bool _isAppUsageTrackingEnabled = true;
    /// <summary>記録中に前面アプリを自動記録するか</summary>
    public bool IsAppUsageTrackingEnabled
    {
        get => _isAppUsageTrackingEnabled;
        set
        {
            if (SetProperty(ref _isAppUsageTrackingEnabled, value))
            {
                // 記録の途中で切り替えられた場合もその場で反映する
                if (value)
                {
                    if (IsRecording) _appUsageTracker?.Start();
                }
                else
                {
                    // ここまでに集めた分は捨てずに保存しておく
                    CollectAndSaveAppUsage();
                }
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

    // ===== 記録との連動（IsRecording の変化から呼ばれる） =====

    private void OnRecordingChangedForAppUsage(bool isRecording)
    {
        if (_appUsageTracker == null) return;

        if (isRecording)
        {
            if (IsAppUsageTrackingEnabled) _appUsageTracker.Start();
        }
        else
        {
            CollectAndSaveAppUsage();
        }
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
                    "使用アプリは「記録」の実行中にだけ収集されます（設定でオン/オフできます）。",
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
                return new AppUsageStat(
                    appName,
                    duration,
                    duration.Ticks * 100.0 / totalTicks,
                    longest.WindowTitle);
            })
            .OrderByDescending(s => s.Duration)];
    }
}
