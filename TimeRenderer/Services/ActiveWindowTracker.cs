using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Windows.Threading;

using TimeRenderer.Models;

namespace TimeRenderer.Services;

/// <summary>
/// 前面（フォアグラウンド）ウィンドウのアプリを一定間隔でサンプリングし、
/// 「どのアプリをいつからいつまで使っていたか」の期間に畳んで集める。
///
/// 方針は離席検知（<see cref="AwayDetector"/>）と同じ:
/// - 監視は軽量なポーリング（5秒間隔）で行う
/// - 集めるのは <see cref="Start"/>〜<see cref="Stop"/> の間（＝記録中）だけ。
///   記録していない時間に何を使っていたかは関心の対象外で、収集もしない
/// - データはこの PC のローカルにしか置かない
///
/// 期間の区切りはプロセス単位。ウィンドウタイトルの変化（ブラウザのタブ切替など）では
/// 区切らず、最後に見えていたタイトルだけを持つ。タイトル単位で区切ると
/// データ量が膨らみ、保存ファイルが肥大化するため。
/// </summary>
public sealed partial class ActiveWindowTracker : IDisposable
{
    [LibraryImport("user32.dll")]
    private static partial IntPtr GetForegroundWindow();

    [LibraryImport("user32.dll", EntryPoint = "GetWindowTextW", StringMarshalling = StringMarshalling.Utf16)]
    private static partial int GetWindowText(IntPtr hWnd, Span<char> lpString, int nMaxCount);

    [LibraryImport("user32.dll")]
    private static partial uint GetWindowThreadProcessId(IntPtr hWnd, out uint lpdwProcessId);

    /// <summary>サンプリング間隔（AwayDetector と同じ 5 秒）</summary>
    private static readonly TimeSpan PollInterval = TimeSpan.FromSeconds(5);

    /// <summary>プロセス名キャッシュの上限。超えたら全部捨てる（PID の再利用対策も兼ねる）</summary>
    private const int ProcessNameCacheLimit = 256;

    private readonly DispatcherTimer _timer;

    /// <summary>PID → (プロセス名, 表示名) のキャッシュ。毎回 Process を開くのは重いため</summary>
    private readonly Dictionary<uint, (string ProcessName, string AppName)> _processNameCache = [];

    /// <summary>収集中か（記録中のみ true）</summary>
    private bool _isCollecting;

    /// <summary>確定した使用期間</summary>
    private readonly List<AppUsageInterval> _completed = [];

    /// <summary>進行中の使用期間（前面アプリが変わったら確定して入れ替える）</summary>
    private AppUsageInterval? _current;

    private bool _disposed;

    public ActiveWindowTracker()
    {
        _timer = new DispatcherTimer { Interval = PollInterval };
        _timer.Tick += (_, _) => Sample();
    }

    /// <summary>収集を開始する（記録開始時に呼ぶ）。前回の残りは破棄する</summary>
    public void Start()
    {
        _completed.Clear();
        _current = null;
        _isCollecting = true;
        _timer.Start();

        // 開始直後の1回をすぐ取る（最初の5秒を取りこぼさないため）
        Sample();
    }

    /// <summary>
    /// 収集を終了し、集めた使用期間を返す（記録停止時に呼ぶ）。
    /// 進行中の期間もここで確定する。
    /// </summary>
    public List<AppUsageInterval> Stop()
    {
        _isCollecting = false;
        _timer.Stop();

        CloseCurrent(DateTime.Now);

        var result = new List<AppUsageInterval>(_completed);
        _completed.Clear();
        return result;
    }

    /// <summary>いま収集中か</summary>
    public bool IsCollecting => _isCollecting;

    private void Sample()
    {
        if (!_isCollecting) return;

        var now = DateTime.Now;

        try
        {
            var hwnd = GetForegroundWindow();
            if (hwnd == IntPtr.Zero)
            {
                // ロック画面など前面ウィンドウが無い状態。進行中の期間を閉じる
                CloseCurrent(now);
                return;
            }

            _ = GetWindowThreadProcessId(hwnd, out uint pid);
            if (pid == 0)
            {
                CloseCurrent(now);
                return;
            }

            var (processName, appName) = ResolveProcessName(pid);
            if (string.IsNullOrEmpty(processName))
            {
                CloseCurrent(now);
                return;
            }

            var title = GetWindowTitle(hwnd);

            if (_current != null && _current.ProcessName == processName)
            {
                // 同じアプリを使い続けている → 期間を伸ばす
                _current.End = now;
                if (!string.IsNullOrEmpty(title))
                {
                    _current.WindowTitle = title;
                }
                return;
            }

            // アプリが切り替わった → 進行中を確定して新しい期間を始める
            CloseCurrent(now);
            _current = new AppUsageInterval
            {
                Start = now,
                End = now,
                ProcessName = processName,
                AppName = appName,
                WindowTitle = title
            };
        }
        catch (Exception ex)
        {
            // サンプリングの失敗で記録機能全体を巻き込まない
            Debug.WriteLine($"ActiveWindowTracker sample failed: {ex.Message}");
        }
    }

    /// <summary>進行中の期間を確定リストへ移す。長さゼロの期間は捨てる</summary>
    private void CloseCurrent(DateTime now)
    {
        if (_current == null) return;

        if (_current.End < now && now - _current.End <= PollInterval)
        {
            // 最後のサンプルから今までの端数も使用時間に含める（1ポーリング分まで）
            _current.End = now;
        }

        if (_current.Duration > TimeSpan.Zero)
        {
            _completed.Add(_current);
        }
        _current = null;
    }

    private static string GetWindowTitle(IntPtr hwnd)
    {
        Span<char> buffer = stackalloc char[512];
        int length = GetWindowText(hwnd, buffer, buffer.Length);
        return length > 0 ? new string(buffer[..length]) : string.Empty;
    }

    /// <summary>
    /// PID からプロセス名と表示名を得る。
    /// MainModule へのアクセスは権限で失敗しうるため、表示名はベストエフォート。
    /// </summary>
    private (string ProcessName, string AppName) ResolveProcessName(uint pid)
    {
        if (_processNameCache.TryGetValue(pid, out var cached)) return cached;

        string processName;
        string appName;
        try
        {
            using var process = Process.GetProcessById((int)pid);
            processName = process.ProcessName;
            appName = processName;
            try
            {
                var description = process.MainModule?.FileVersionInfo.FileDescription;
                if (!string.IsNullOrWhiteSpace(description))
                {
                    appName = description;
                }
            }
            catch (Exception)
            {
                // 管理者権限のプロセスなどは MainModule を読めない。プロセス名で十分
            }
        }
        catch (Exception)
        {
            // プロセスが既に終了している場合など
            return (string.Empty, string.Empty);
        }

        if (_processNameCache.Count >= ProcessNameCacheLimit)
        {
            _processNameCache.Clear();
        }
        _processNameCache[pid] = (processName, appName);
        return (processName, appName);
    }

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;

        _timer.Stop();
        _isCollecting = false;
    }
}
