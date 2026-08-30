using System;
using System.Collections.Generic;
using System.Linq;

using TimeRenderer.Helpers;
using TimeRenderer.Models;
using TimeRenderer.Services;

namespace TimeRenderer.ViewModels;

/// <summary>
/// 離席・中断の検知と、記録への反映。
///
/// 記録を開始したまま席を離れる、PC がスリープする、画面をロックする、は日常的に起きる。
/// 何も検知しないと「8時間の設計作業」のような記録が残り、統計そのものが信用できなくなる。
///
/// 方針:
/// - 検知は常時行うが、<b>記録中に起きた離席だけ</b>を集める
/// - 勝手に記録を削らず、停止時に必ずユーザーへ確認する
/// - 「除く」を選んだ場合は、合計から引くのではなく<b>記録を分割する</b>。
///   いつ作業していたのかを残すため
/// </summary>
public partial class MainViewModel
{
    private AwayDetector? _awayDetector;

    /// <summary>現在の記録中に検知した離席（記録停止時に確認して消す）</summary>
    private readonly List<AwayPeriod> _awayDuringRecording = [];

    // ===== 設定 =====

    private bool _isAwayDetectionEnabled = true;
    /// <summary>離席検知を行うか</summary>
    public bool IsAwayDetectionEnabled
    {
        get => _isAwayDetectionEnabled;
        set
        {
            if (SetProperty(ref _isAwayDetectionEnabled, value))
            {
                ApplyAwaySettings();
                if (!value) ClearAwayState();
                SaveSettings();
            }
        }
    }

    public IReadOnlyList<AwayHandlingOption> AwayHandlingOptions { get; } =
    [
        new(AwayHandlingMode.Ask, "毎回確認する"),
        new(AwayHandlingMode.AlwaysExclude, "常に除外する"),
        new(AwayHandlingMode.AlwaysKeep, "常にそのまま記録する"),
    ];

    private AwayHandlingMode _awayHandlingMode = AwayHandlingMode.Ask;
    /// <summary>離席を検知したときの扱い（確認するか、自動で決めるか）</summary>
    public AwayHandlingMode CurrentAwayHandlingMode
    {
        get => _awayHandlingMode;
        set
        {
            if (SetProperty(ref _awayHandlingMode, value))
            {
                OnPropertyChanged(nameof(SelectedAwayHandlingOption));
                SaveSettings();
            }
        }
    }

    public AwayHandlingOption SelectedAwayHandlingOption
    {
        get => AwayHandlingOptions.FirstOrDefault(o => o.Mode == _awayHandlingMode) ?? AwayHandlingOptions[0];
        set
        {
            if (value != null) CurrentAwayHandlingMode = value.Mode;
        }
    }

    /// <summary>離席とみなすまでの無操作時間（分）の選択肢</summary>
    public static IReadOnlyList<int> AwayThresholdOptions { get; } = [3, 5, 10, 15, 30, 60];

    private int _awayThresholdMinutes = 10;
    /// <summary>この時間だけ無操作が続いたら離席とみなす</summary>
    public int AwayThresholdMinutes
    {
        get => _awayThresholdMinutes;
        set
        {
            var clamped = Math.Clamp(value, 1, 240);
            if (SetProperty(ref _awayThresholdMinutes, clamped))
            {
                ApplyAwaySettings();
                SaveSettings();
            }
        }
    }

    // ===== 表示状態 =====

    private bool _isAwayNow;
    /// <summary>いま離席中と判定されているか（記録中のみ意味を持つ）</summary>
    public bool IsAwayNow
    {
        get => _isAwayNow;
        private set
        {
            if (SetProperty(ref _isAwayNow, value))
            {
                OnPropertyChanged(nameof(ShowAwayBanner));
            }
        }
    }

    private string _awayBannerText = string.Empty;
    public string AwayBannerText
    {
        get => _awayBannerText;
        private set => SetProperty(ref _awayBannerText, value);
    }

    /// <summary>記録中かつ離席中のときだけバナーを出す</summary>
    public bool ShowAwayBanner => IsAwayNow && IsRecording;

    // ===== 初期化 =====

    /// <summary>コンストラクタから呼ぶ。設定の読み込み後に反映し直される</summary>
    private void InitializeAwayDetection()
    {
        _awayDetector = new AwayDetector
        {
            IsEnabled = ShouldRunAwayDetector,
            IdleThreshold = EffectiveIdleThreshold
        };

        _awayDetector.AwayDetected += OnAwayDetected;
        _awayDetector.AwayStarted += OnAwayStarted;
        _awayDetector.AwayEnded += OnAwayEnded;
    }

    /// <summary>
    /// 検知器を動かす条件。
    /// 記録の離席除外を使わなくても、勤務終了の検知だけ使いたい場合があるため、
    /// どちらか一方でも有効なら監視する。
    /// </summary>
    private bool ShouldRunAwayDetector => _isAwayDetectionEnabled || _isWorkEndDetectionEnabled;

    /// <summary>
    /// 検知器のしきい値。
    /// 検知器はこれより短い期間を通知しないため、
    /// 有効な機能のうち<b>短いほう</b>に合わせないと片方が取りこぼす。
    /// </summary>
    private TimeSpan EffectiveIdleThreshold
    {
        get
        {
            var minutes = (_isAwayDetectionEnabled, _isWorkEndDetectionEnabled) switch
            {
                (true, true) => Math.Min(_awayThresholdMinutes, _workEndThresholdMinutes),
                (true, false) => _awayThresholdMinutes,
                (false, true) => _workEndThresholdMinutes,
                _ => _awayThresholdMinutes
            };
            return TimeSpan.FromMinutes(minutes);
        }
    }

    /// <summary>設定を読み込んだあとに検知器へ反映する</summary>
    private void ApplyAwaySettings()
    {
        if (_awayDetector == null) return;
        _awayDetector.IsEnabled = ShouldRunAwayDetector;
        _awayDetector.IdleThreshold = EffectiveIdleThreshold;
    }

    /// <summary>アプリ終了時に呼ぶ</summary>
    public void DisposeAwayDetection()
    {
        if (_awayDetector == null) return;

        _awayDetector.AwayDetected -= OnAwayDetected;
        _awayDetector.AwayStarted -= OnAwayStarted;
        _awayDetector.AwayEnded -= OnAwayEnded;
        _awayDetector.Dispose();
        _awayDetector = null;
    }

    // ===== 検知の受け取り =====

    private void OnAwayStarted(object? sender, DateTime since)
    {
        // 勤務終了の検知だけを有効にしている場合、記録中バナーは出さない
        if (!IsAwayDetectionEnabled) return;
        if (!IsRecording) return;

        IsAwayNow = true;
        AwayBannerText = $"{since:HH:mm} から操作がありません。記録は続いています（停止時に除外できます）";
    }

    private void OnAwayEnded(object? sender, EventArgs e)
    {
        IsAwayNow = false;
        AwayBannerText = string.Empty;
    }

    private void OnAwayDetected(object? sender, AwayPeriod period)
    {
        // 同じ検知を「記録から除く離席」と「勤務の終了」の2つの用途で使う。
        // 前者は記録中だけ、後者は勤務中だけが対象で、条件が違うので別々に判定する
        HandleAwayForWorkDay(period);

        if (!IsAwayDetectionEnabled) return;

        // 記録していない時間の離席は関心の対象外
        if (!IsRecording || !RecordingStartTime.HasValue) return;

        var clipped = period.ClipTo(RecordingStartTime.Value, DateTime.Now);
        if (clipped == null) return;

        _awayDuringRecording.Add(clipped);
    }

    private void ClearAwayState()
    {
        _awayDuringRecording.Clear();

        // 記録開始より前から続いている離席を、記録中のものとして拾わないようにする
        _awayDetector?.DiscardPendingAway();

        IsAwayNow = false;
        AwayBannerText = string.Empty;
    }

    /// <summary>
    /// 記録停止直前に保留中の検知を確定し、今回の記録範囲に属する離席を取り出す。
    /// 返した離席は内部状態から消えるため、次の記録へ持ち越さない。
    /// </summary>
    private List<AwayPeriod> TakeAwayPeriodsForRecording(DateTime start, DateTime end)
    {
        // 離席から戻った直後にホットキーで停止しても、次のポーリングを待たずに拾う。
        _awayDetector?.FlushPendingAway();

        var periods = RecordingStopHelper.ClipAndSortAwayPeriods(
            start, end, _awayDuringRecording);
        _awayDuringRecording.Clear();
        return periods;
    }
}
