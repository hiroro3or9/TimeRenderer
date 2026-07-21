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

    // ===== 記録停止時の反映 =====

    /// <summary>
    /// 記録の [start, end) から離席分を除いた実作業区間を求める。
    /// 離席が記録の途中にある場合、区間は複数に分かれる。
    /// </summary>
    internal static List<(DateTime Start, DateTime End)> SubtractAway(
        DateTime start, DateTime end, IReadOnlyList<AwayPeriod> awayPeriods)
    {
        var result = new List<(DateTime, DateTime)>();
        if (end <= start) return result;

        // 記録範囲に切り詰め、開始順に並べ、重なりを畳んでおく
        var clipped = awayPeriods
            .Select(p => p.ClipTo(start, end))
            .Where(p => p != null)
            .Select(p => p!)
            .OrderBy(p => p.Start)
            .ToList();

        var merged = new List<(DateTime Start, DateTime End)>();
        foreach (var period in clipped)
        {
            if (merged.Count > 0 && period.Start <= merged[^1].End)
            {
                // 重なる／隣接するものは1つにまとめる
                if (period.End > merged[^1].End)
                {
                    merged[^1] = (merged[^1].Start, period.End);
                }
            }
            else
            {
                merged.Add((period.Start, period.End));
            }
        }

        var cursor = start;
        foreach (var (awayStart, awayEnd) in merged)
        {
            if (awayStart > cursor) result.Add((cursor, awayStart));
            if (awayEnd > cursor) cursor = awayEnd;
        }
        if (cursor < end) result.Add((cursor, end));

        return result;
    }

    /// <summary>
    /// 記録停止時に離席の扱いを決める。
    /// </summary>
    /// <returns>
    /// 実際に記録すべき区間の一覧。
    /// 離席が無い・除外しない場合は [start, end) の1件だけを返す。
    /// </returns>
    private List<(DateTime Start, DateTime End)> ResolveRecordingSegments(
        string title, DateTime start, DateTime end)
    {
        // ポーリング待ちの離席を先に確定させる
        // （離席から戻った直後にホットキーで停止すると取りこぼすため）
        _awayDetector?.FlushPendingAway();

        var periods = _awayDuringRecording
            .Select(p => p.ClipTo(start, end))
            .Where(p => p != null)
            .Select(p => p!)
            .OrderBy(p => p.Start)
            .ToList();

        _awayDuringRecording.Clear();

        if (!IsAwayDetectionEnabled || periods.Count == 0)
        {
            return [(start, end)];
        }

        bool exclude;
        switch (_awayHandlingMode)
        {
            case AwayHandlingMode.AlwaysExclude:
                exclude = true;
                // 黙って記録が変わると戸惑うので、何をしたかは知らせる
                ShowAutoStartNotice(BuildAutoExcludeNotice(periods));
                break;

            case AwayHandlingMode.AlwaysKeep:
                exclude = false;
                break;

            default:
                exclude = _dialogService.ShowAwayReviewDialog(title, start, end, periods);
                break;
        }

        if (!exclude) return [(start, end)];

        // すべてが離席だった場合は空になる。
        // ユーザーが「除く」を選んだ結果なので、そのまま何も記録しない
        // （呼び出し側が理由を通知する）
        return SubtractAway(start, end, periods);
    }

    /// <summary>
    /// 記録の区間をアイテムとして保存する。
    ///
    /// 予定アイテムから始めた記録は、最初の区間でその予定自体を更新し、
    /// 2区間目以降を新しいアイテムとして足す（元の予定を残すため）。
    /// 1回の停止操作なので、取り消し履歴には1件としてまとめて積む。
    /// </summary>
    private void SaveRecordingSegments(
        string title, ScheduleItem? source, List<(DateTime Start, DateTime End)> segments)
    {
        var edits = new List<IUndoableEdit>();
        bool useSource = source != null && ScheduleItems.Contains(source);

        for (int i = 0; i < segments.Count; i++)
        {
            var (start, end) = segments[i];
            var durationText = FormatRecordingDuration(end - start);

            if (i == 0 && useSource)
            {
                var before = ItemSnapshot.Capture(source!);

                _isBatchUpdatingItem = true;
                try
                {
                    source!.Title = title;
                    source.StartTime = start;
                    source.EndTime = end;
                    // ユーザーが予定に書いたメモは残し、空の場合のみ記録時間を書き込む
                    if (string.IsNullOrWhiteSpace(source.Content))
                    {
                        source.Content = durationText;
                    }
                }
                finally
                {
                    _isBatchUpdatingItem = false;
                }

                var after = ItemSnapshot.Capture(source!);
                if (!before.IsSameAs(after))
                {
                    edits.Add(new ModifyItemEdit(source!, before, after, "記録"));
                }
                continue;
            }

            ScheduleItem newItem = new()
            {
                Title = title,
                Content = durationText,
                StartTime = start,
                EndTime = end,
                ColorCode = _recordingColorCode ?? RecordingCategory?.ColorCode ?? Models.CategoryInfo.CreateBrush("DarkOrange").ToString(),
                CategoryId = _recordingCategoryId ?? RecordingCategory?.Id,
                ColumnIndex = 0
            };

            ScheduleItems.Add(newItem);
            edits.Add(new AddItemEdit(newItem));
        }

        PushRecordingEdits(title, segments.Count, edits);

        RecalculateLayout();
        SaveData();
    }

    /// <summary>「常に除外する」設定のときに、何を差し引いたかを知らせる文言</summary>
    private static string BuildAutoExcludeNotice(List<AwayPeriod> periods)
    {
        var total = TimeSpan.FromTicks(periods.Sum(p => p.Duration.Ticks));
        var text = total.TotalHours >= 1
            ? $"{(int)total.TotalHours}時間{total.Minutes}分"
            : $"{(int)total.TotalMinutes}分";

        return $"離席 {periods.Count} 件・合計 {text} を記録から除きました（Ctrl+Z でこの記録ごと取り消せます）";
    }

    /// <summary>TimeSpan の "hh" は24時間で桁落ちするため総時間数で表記する</summary>
    private static string FormatRecordingDuration(TimeSpan duration)
        => $"記録時間: {(int)duration.TotalHours}:{duration.Minutes:D2}";
}
