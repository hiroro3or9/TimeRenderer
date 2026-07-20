using System;
using System.Runtime.InteropServices;
using System.Windows.Threading;

using Microsoft.Win32;

using TimeRenderer.Models;

namespace TimeRenderer.Services;

/// <summary>
/// 席を外していた期間を検知する。
///
/// 3つの経路を見る必要がある:
///
/// 1. <b>無操作</b> — GetLastInputInfo でシステム全体の最終入力時刻を見る。
///    アプリが非アクティブでもキーボード・マウス操作を拾える。
/// 2. <b>スリープ・休止</b> — GetTickCount はスリープ中に進まないため、
///    無操作の監視だけではスリープを検知できない。PowerModeChanged で捕まえ、
///    実時間（DateTime.Now）の差で期間を求める。
/// 3. <b>ロック</b> — SessionSwitch。ロック中は入力が無いので 1 でも拾えるが、
///    「無操作」ではなく「ロック」と理由を出し分けたほうが納得感がある。
///
/// 検知した期間は <see cref="AwayDetected"/> で通知する。
/// どう扱うか（記録から除くかなど）は利用側の判断に委ねる。
/// </summary>
public sealed partial class AwayDetector : IDisposable
{
    [StructLayout(LayoutKind.Sequential)]
    private struct LASTINPUTINFO
    {
        public uint cbSize;
        public uint dwTime;
    }

    // LibraryImport: コンパイル時にマーシャリングコードを生成する（SYSLIB1054 対応）
    [LibraryImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static partial bool GetLastInputInfo(ref LASTINPUTINFO plii);

    [LibraryImport("kernel32.dll")]
    private static partial uint GetTickCount();

    /// <summary>無操作の判定間隔</summary>
    private static readonly TimeSpan PollInterval = TimeSpan.FromSeconds(5);

    private readonly DispatcherTimer _timer;

    /// <summary>無操作がこの時間を超えたら離席とみなす</summary>
    public TimeSpan IdleThreshold { get; set; } = TimeSpan.FromMinutes(10);

    /// <summary>検知を行うか（設定で無効にできる）</summary>
    public bool IsEnabled { get; set; } = true;

    /// <summary>離席期間が確定したときに発火する</summary>
    public event EventHandler<AwayPeriod>? AwayDetected;

    /// <summary>離席状態に入ったと判定したときに発火する（記録中の表示用）</summary>
    public event EventHandler<DateTime>? AwayStarted;

    /// <summary>離席状態から戻ったときに発火する</summary>
    public event EventHandler? AwayEnded;

    /// <summary>現在、無操作が閾値を超えているか</summary>
    public bool IsAway { get; private set; }

    /// <summary>離席が始まったと推定される時刻（IsAway のときのみ意味を持つ）</summary>
    public DateTime? AwaySince { get; private set; }

    // スリープ・ロックの開始時刻（実時間で測る）
    private DateTime? _suspendedAt;
    private DateTime? _lockedAt;

    private bool _disposed;

    public AwayDetector()
    {
        _timer = new DispatcherTimer { Interval = PollInterval };
        _timer.Tick += (_, _) => CheckIdle();
        _timer.Start();

        SystemEvents.PowerModeChanged += OnPowerModeChanged;
        SystemEvents.SessionSwitch += OnSessionSwitch;
    }

    /// <summary>
    /// システム全体の無操作時間。
    /// GetTickCount は約49.7日で一周するため、符号なしの引き算で差を取る
    /// （uint の減算は自然に一周をまたいで正しい差になる）。
    /// </summary>
    public static TimeSpan GetIdleTime()
    {
        var info = new LASTINPUTINFO { cbSize = (uint)Marshal.SizeOf<LASTINPUTINFO>() };
        if (!GetLastInputInfo(ref info)) return TimeSpan.Zero;

        uint elapsed = unchecked(GetTickCount() - info.dwTime);
        return TimeSpan.FromMilliseconds(elapsed);
    }

    private void CheckIdle()
    {
        if (!IsEnabled)
        {
            // 無効化されたら、進行中の離席状態は畳んでおく
            if (IsAway) ResetAwayState();
            return;
        }

        // スリープ・ロック中は専用の経路で扱うため、無操作の判定はしない
        if (_suspendedAt.HasValue || _lockedAt.HasValue) return;

        var idle = GetIdleTime();

        if (idle >= IdleThreshold)
        {
            if (!IsAway)
            {
                IsAway = true;
                // 「今から閾値ぶん遡った時刻」ではなく実際の無操作開始時刻を使う
                AwaySince = DateTime.Now - idle;
                AwayStarted?.Invoke(this, AwaySince.Value);
            }
            return;
        }

        // 入力が戻った → 離席期間を確定する
        if (IsAway)
        {
            var since = AwaySince;
            ResetAwayState();

            if (since.HasValue)
            {
                // 復帰の瞬間は「今 - 現在の無操作時間」。
                // ポーリング間隔ぶんのずれをここで吸収する
                var returnedAt = DateTime.Now - idle;
                Raise(since.Value, returnedAt, AwayReason.Idle);
            }
        }
    }

    /// <summary>
    /// 進行中の離席を、その場で確定させる。
    ///
    /// 監視は5秒間隔のポーリングなので、離席から戻った直後に
    /// ホットキーで記録を停止すると、次のポーリングが来る前に停止処理が走り、
    /// 最後の離席期間を取りこぼす。記録停止の直前にこれを呼んで取りこぼしを防ぐ。
    /// </summary>
    public void FlushPendingAway()
    {
        if (!IsEnabled) return;

        var now = DateTime.Now;

        if (_suspendedAt.HasValue)
        {
            Raise(_suspendedAt.Value, now, AwayReason.Sleep);
            _suspendedAt = null;
        }

        if (_lockedAt.HasValue)
        {
            Raise(_lockedAt.Value, now, AwayReason.Locked);
            _lockedAt = null;
        }

        if (!IsAway || !AwaySince.HasValue) return;

        var since = AwaySince.Value;
        // 直前に操作があったなら、その時点を復帰時刻とみなす
        var returnedAt = now - GetIdleTime();
        if (returnedAt < since) returnedAt = since;

        ResetAwayState();
        Raise(since, returnedAt, AwayReason.Idle);
    }

    /// <summary>
    /// 進行中の離席を、通知せずに破棄する。
    ///
    /// 記録を開始した時点で呼ぶ。直前まで席を外していた場合、
    /// その離席は記録の対象期間より前のものなので、
    /// 確定させて記録に混ぜてしまわないようにする。
    /// </summary>
    public void DiscardPendingAway()
    {
        _suspendedAt = null;
        _lockedAt = null;
        if (IsAway) ResetAwayState();
    }

    private void ResetAwayState()
    {
        IsAway = false;
        AwaySince = null;
        AwayEnded?.Invoke(this, EventArgs.Empty);
    }

    private void OnPowerModeChanged(object sender, PowerModeChangedEventArgs e)
    {
        if (!IsEnabled) return;

        switch (e.Mode)
        {
            case PowerModes.Suspend:
                _suspendedAt = DateTime.Now;
                break;

            case PowerModes.Resume:
                if (_suspendedAt.HasValue)
                {
                    Raise(_suspendedAt.Value, DateTime.Now, AwayReason.Sleep);
                    _suspendedAt = null;
                }
                // スリープ復帰直後は無操作時間が大きく出るため、状態を仕切り直す
                ResetAwayState();
                break;
        }
    }

    private void OnSessionSwitch(object sender, SessionSwitchEventArgs e)
    {
        if (!IsEnabled) return;

        switch (e.Reason)
        {
            case SessionSwitchReason.SessionLock:
            case SessionSwitchReason.ConsoleDisconnect:
            case SessionSwitchReason.RemoteDisconnect:
                _lockedAt ??= DateTime.Now;
                break;

            case SessionSwitchReason.SessionUnlock:
            case SessionSwitchReason.ConsoleConnect:
            case SessionSwitchReason.RemoteConnect:
                if (_lockedAt.HasValue)
                {
                    Raise(_lockedAt.Value, DateTime.Now, AwayReason.Locked);
                    _lockedAt = null;
                }
                ResetAwayState();
                break;
        }
    }

    /// <summary>短すぎる期間は雑音なので通知しない</summary>
    private void Raise(DateTime start, DateTime end, AwayReason reason)
    {
        if (end - start < IdleThreshold) return;
        AwayDetected?.Invoke(this, new AwayPeriod(start, end, reason));
    }

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;

        _timer.Stop();
        SystemEvents.PowerModeChanged -= OnPowerModeChanged;
        SystemEvents.SessionSwitch -= OnSessionSwitch;
    }
}
