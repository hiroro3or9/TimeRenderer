using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using System.Windows.Threading;

using TimeRenderer.Models;

namespace TimeRenderer.Services;

/// <summary>
/// 前面（フォアグラウンド）ウィンドウのアプリを監視し、
/// 「どのアプリをいつからいつまで使っていたか」の期間に畳んで集める。
///
/// 方針:
/// - 切り替えは <c>SetWinEventHook(EVENT_SYSTEM_FOREGROUND)</c> で<b>即座に</b>拾う。
///   ポーリングだけだと間隔より短い使用（数秒だけ触ったアプリ）が丸ごと消えるため。
///   5秒のポーリングは、フックが届かない場合の保険と、使用中の期間の
///   終端を伸ばす（＝滞在時間を更新する）ために併用する
/// - 集めるのは <see cref="Start"/>〜<see cref="Stop"/> の間（＝記録中）だけ。
///   記録していない時間に何を使っていたかは関心の対象外で、収集もしない
/// - データはこの PC のローカルにしか置かない
///
/// 期間の区切りはプロセス単位。ウィンドウタイトルの変化（ブラウザのタブ切替など）では
/// 区切らず、最後に見えていたタイトルだけを持つ。タイトル単位で区切ると
/// データ量が膨らみ、保存ファイルが肥大化するため。
///
/// スレッド: フックのコールバックも DispatcherTimer も UI スレッドに届くため、
/// このクラスは UI スレッド専用として扱う（ロックは持たない）。
/// </summary>
public sealed partial class ActiveWindowTracker : IDisposable
{
    // ===== Win32 =====

    [LibraryImport("user32.dll")]
    private static partial IntPtr GetForegroundWindow();

    [LibraryImport("user32.dll", EntryPoint = "GetWindowTextW", StringMarshalling = StringMarshalling.Utf16)]
    private static partial int GetWindowText(IntPtr hWnd, Span<char> lpString, int nMaxCount);

    [LibraryImport("user32.dll", EntryPoint = "GetClassNameW", StringMarshalling = StringMarshalling.Utf16)]
    private static partial int GetClassName(IntPtr hWnd, Span<char> lpClassName, int nMaxCount);

    [LibraryImport("user32.dll")]
    private static partial uint GetWindowThreadProcessId(IntPtr hWnd, out uint lpdwProcessId);

    [LibraryImport("kernel32.dll", SetLastError = true)]
    private static partial IntPtr OpenProcess(uint dwDesiredAccess, [MarshalAs(UnmanagedType.Bool)] bool bInheritHandle, uint dwProcessId);

    [LibraryImport("kernel32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static partial bool CloseHandle(IntPtr hObject);

    [LibraryImport("kernel32.dll", EntryPoint = "QueryFullProcessImageNameW", StringMarshalling = StringMarshalling.Utf16, SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static partial bool QueryFullProcessImageName(IntPtr hProcess, uint dwFlags, Span<char> lpExeName, ref uint lpdwSize);

    [LibraryImport("kernel32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static partial bool GetProcessTimes(IntPtr hProcess, out long lpCreationTime, out long lpExitTime, out long lpKernelTime, out long lpUserTime);

    // コールバックを渡す2つは、マネージドデリゲートではなく関数ポインタで宣言する。
    // デリゲートだと LibraryImport のソースジェネレーターがマーシャリングコードを作れず、
    // DllImport へ逃げることになる。関数ポインタなら生成でき、
    // 「デリゲートを GC から守るためにフィールドで保持する」という定番の事故も無くなる。
    // 受け側は [UnmanagedCallersOnly] を付けた static メソッド（引数は blittable のみ）。

    [LibraryImport("user32.dll")]
    private static unsafe partial IntPtr SetWinEventHook(
        uint eventMin, uint eventMax, IntPtr hmodWinEventProc,
        delegate* unmanaged[Stdcall]<IntPtr, uint, IntPtr, int, int, uint, uint, void> lpfnWinEventProc,
        uint idProcess, uint idThread, uint dwFlags);

    [LibraryImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static partial bool UnhookWinEvent(IntPtr hWinEventHook);

    [LibraryImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static unsafe partial bool EnumChildWindows(
        IntPtr hwndParent,
        delegate* unmanaged[Stdcall]<IntPtr, IntPtr, int> lpEnumFunc,
        IntPtr lParam);

    private const uint EVENT_SYSTEM_FOREGROUND = 0x0003;
    private const uint WINEVENT_OUTOFCONTEXT = 0x0000;
    private const uint PROCESS_QUERY_LIMITED_INFORMATION = 0x1000;

    // ===== 定数 =====

    /// <summary>保険のポーリング間隔。切り替え自体はフックで拾うため、これは滞在時間の更新用</summary>
    private static readonly TimeSpan PollInterval = TimeSpan.FromSeconds(5);

    /// <summary>
    /// 期間を閉じるときに、最後のサンプルから今までを使用時間に足してよい上限。
    /// スリープ・休止で長時間空いた分をまるごと計上しないための歯止め。
    /// タイマーの遅延を吸収するためポーリング間隔より少し大きく取る。
    /// </summary>
    private static readonly TimeSpan MaxTailExtension = PollInterval + TimeSpan.FromSeconds(2);

    /// <summary>
    /// 直前と同じアプリへ戻ってきた場合に、1つの期間として繋げてよい空き時間。
    /// Alt+Tab の途中経過などで期間が細切れになるのを防ぐ。
    /// </summary>
    private static readonly TimeSpan MergeGap = TimeSpan.FromSeconds(2);

    /// <summary>プロセス情報キャッシュの上限。超えたら全部捨てる</summary>
    private const int ProcessInfoCacheLimit = 256;

    /// <summary>UWP／ストアアプリのウィンドウをホストするプロセス（実体は別プロセス）</summary>
    private const string UwpHostProcessName = "ApplicationFrameHost";

    /// <summary>UWP アプリ本体のウィンドウクラス</summary>
    private const string UwpCoreWindowClass = "Windows.UI.Core.CoreWindow";

    // ===== 状態 =====

    private readonly DispatcherTimer _timer;

    /// <summary>(PID, プロセス生成時刻) → 表示情報のキャッシュ。毎回プロセスを開くのは重いため。
    /// 生成時刻をキーに含めるのは、PID が再利用されたときに別アプリの名前を使わないため</summary>
    private readonly Dictionary<(uint Pid, long CreationTime), (string ProcessName, string AppName)> _processInfoCache = [];

    /// <summary>収集中か</summary>
    private bool _isCollecting;

    /// <summary>
    /// ウィンドウタイトルまで控えるか。
    ///
    /// タイトルにはファイル名・URL・チャット相手など<b>中身</b>が出る。
    /// 勤務中はずっと収集するが、タイトルを残すのは記録中だけにして、
    /// 記録していない時間の中身がログに溜まらないようにしている。
    /// </summary>
    private bool _captureWindowTitles;

    /// <summary>
    /// タイトルを控えるかどうかが切り替わった時刻。
    /// この時刻より前に閉じた期間へは繋ぎ直さない（境界をまたいでタイトルが伸びないようにする）。
    /// </summary>
    private DateTime _captureModeChangedAt = DateTime.MinValue;

    /// <summary>確定した使用期間</summary>
    private readonly List<AppUsageInterval> _completed = [];

    /// <summary>進行中の使用期間（前面アプリが変わったら確定して入れ替える）</summary>
    private AppUsageInterval? _current;

    /// <summary>フックのハンドル。未設定なら <see cref="IntPtr.Zero"/></summary>
    private IntPtr _winEventHook = IntPtr.Zero;

    /// <summary>
    /// フックのコールバックは static でなければならない（関数ポインタのため）。
    /// 実際に通知を受け取るインスタンスをここで保持する。
    /// フックを張っている間だけ設定され、外したら null に戻す。
    /// 同時に複数の <see cref="ActiveWindowTracker"/> がフックを張ることは想定していない。
    /// </summary>
    private static ActiveWindowTracker? _hookOwner;

    private bool _disposed;

    public ActiveWindowTracker()
    {
        _timer = new DispatcherTimer(DispatcherPriority.Normal) { Interval = PollInterval };
        _timer.Tick += (_, _) => Sample();
    }

    /// <summary>
    /// ウィンドウタイトルを控えるかどうかを切り替える（記録の開始・終了で呼ぶ）。
    ///
    /// 切り替わった瞬間に進行中の期間を確定する。同じ期間の中に
    /// 「タイトルを残す時間」と「残さない時間」が混ざると、
    /// 記録していない時間の中身がその期間のタイトルとして残ってしまうため。
    /// </summary>
    public void SetCaptureWindowTitles(bool capture)
    {
        if (_captureWindowTitles == capture) return;

        _captureWindowTitles = capture;

        if (!_isCollecting) return;

        var now = DateTime.Now;
        CloseCurrent(now);
        _captureModeChangedAt = now;

        // 切り替え直後の状態をすぐ拾い直す（次のポーリングまで空白にしない）
        Sample();
    }

    /// <summary>収集を開始する（出勤時・アプリ起動時に勤務中なら呼ぶ）</summary>
    public void Start()
    {
        // 既に収集中なら何もしない。ここで _completed を捨てると、
        // 記録中に設定をオフ→オンしたときに集めた分が消える
        if (_isCollecting) return;

        _completed.Clear();
        _current = null;
        _isCollecting = true;

        HookForegroundChanges();
        _timer.Start();

        // 開始直後の1回をすぐ取る（最初のポーリングまでを取りこぼさないため）
        Sample();
    }

    /// <summary>
    /// 収集を終了し、集めた使用期間を返す（退勤時・アプリ終了時に呼ぶ）。
    /// 進行中の期間もここで確定する。
    /// </summary>
    public List<AppUsageInterval> Stop()
    {
        _isCollecting = false;
        _timer.Stop();
        UnhookForegroundChanges();

        CloseCurrent(DateTime.Now);

        var result = new List<AppUsageInterval>(_completed);
        _completed.Clear();
        return result;
    }

    /// <summary>
    /// 収集を続けたまま、確定済みの期間だけを取り出す。
    ///
    /// 勤務中はずっと収集するようになったため、退勤まで書き出さないと
    /// 途中で落ちた日のぶんがまるごと消える。定期的にここで吐き出す。
    /// 進行中の期間は確定しない（次の切り替えかポーリングで自然に閉じる）。
    /// </summary>
    public List<AppUsageInterval> Drain()
    {
        if (_completed.Count == 0) return [];

        var result = new List<AppUsageInterval>(_completed);
        _completed.Clear();
        return result;
    }

    /// <summary>いま収集中か</summary>
    public bool IsCollecting => _isCollecting;

    // ===== フック =====

    private unsafe void HookForegroundChanges()
    {
        if (_winEventHook != IntPtr.Zero) return;

        _hookOwner = this;

        // OUTOFCONTEXT: 自プロセスにフック DLL を注入せず、メッセージとして受け取る。
        // コールバックはこのメソッドを呼んだスレッド（＝UI スレッド）に届く。
        // SKIPOWNTHREAD は付けない。付けると自分自身（このアプリ）へ切り替えた瞬間を拾えなくなる
        _winEventHook = SetWinEventHook(
            EVENT_SYSTEM_FOREGROUND, EVENT_SYSTEM_FOREGROUND,
            IntPtr.Zero, &OnForegroundChangedNative, 0, 0,
            WINEVENT_OUTOFCONTEXT);

        if (_winEventHook == IntPtr.Zero)
        {
            // フックを張れなくてもポーリングだけで動作は続く（精度は落ちる）
            _hookOwner = null;
            Debug.WriteLine("ActiveWindowTracker: SetWinEventHook failed. Falling back to polling only.");
        }
    }

    private void UnhookForegroundChanges()
    {
        if (_hookOwner == this) _hookOwner = null;

        if (_winEventHook == IntPtr.Zero) return;

        UnhookWinEvent(_winEventHook);
        _winEventHook = IntPtr.Zero;
    }

    /// <summary>
    /// 前面ウィンドウが変わったときに Windows から直接呼ばれる。
    /// ネイティブへ例外を投げ返すとプロセスごと落ちるため、ここで必ず食い止める。
    /// </summary>
    [UnmanagedCallersOnly(CallConvs = new[] { typeof(CallConvStdcall) })]
    private static void OnForegroundChangedNative(
        IntPtr hWinEventHook, uint eventType, IntPtr hwnd,
        int idObject, int idChild, uint dwEventThread, uint dwmsEventTime)
    {
        try
        {
            // 切り替わった瞬間に取る。ポーリング間隔より短い使用もこれで残る
            _hookOwner?.Sample();
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"ActiveWindowTracker hook callback failed: {ex.Message}");
        }
    }

    // ===== サンプリング =====

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

            var (processName, appName) = ResolveProcess(pid);

            // UWP／ストアアプリは ApplicationFrameHost が枠だけを持っており、
            // そのままでは全部同じ「ApplicationFrameHost」に化けて実アプリが記録されない。
            // 子ウィンドウから本体のプロセスを探し直す
            if (string.Equals(processName, UwpHostProcessName, StringComparison.OrdinalIgnoreCase))
            {
                var realPid = FindUwpApplicationPid(hwnd, pid);
                if (realPid != 0)
                {
                    var resolved = ResolveProcess(realPid);
                    if (!string.IsNullOrEmpty(resolved.ProcessName))
                    {
                        (processName, appName) = resolved;
                    }
                }
            }

            if (string.IsNullOrEmpty(processName))
            {
                CloseCurrent(now);
                return;
            }

            // 記録していない時間はアプリ名と滞在時間だけを残す
            var title = _captureWindowTitles ? GetWindowTitle(hwnd) : string.Empty;

            if (_current != null && _current.ProcessName == processName)
            {
                // 同じアプリを使い続けている → 期間を伸ばす
                if (now > _current.End) _current.End = now;
                if (!string.IsNullOrEmpty(title))
                {
                    _current.WindowTitle = title;
                }
                return;
            }

            // アプリが切り替わった → 進行中を確定して新しい期間を始める
            CloseCurrent(now);

            // 直前と同じアプリへすぐ戻ってきた場合は、細切れにせず繋げ直す
            if (TryResumeLast(processName, now, title)) return;

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

    /// <summary>
    /// 直前に確定した期間が同じアプリで、空きが十分短ければ、それを進行中に戻して繋げる。
    /// 繋げた場合は true。
    /// </summary>
    private bool TryResumeLast(string processName, DateTime now, string title)
    {
        if (_completed.Count == 0) return false;

        var last = _completed[^1];
        if (last.ProcessName != processName || now - last.End > MergeGap) return false;

        // タイトルを控えるかどうかが切り替わった直後は繋がない。
        // 繋ぐと、記録中に付いたタイトルが記録していない時間まで伸びてしまう
        if (last.End <= _captureModeChangedAt) return false;

        _completed.RemoveAt(_completed.Count - 1);
        last.End = now;
        if (!string.IsNullOrEmpty(title)) last.WindowTitle = title;
        _current = last;
        return true;
    }

    /// <summary>進行中の期間を確定リストへ移す。長さゼロの期間は捨てる</summary>
    private void CloseCurrent(DateTime now)
    {
        if (_current == null) return;

        if (_current.End < now && now - _current.End <= MaxTailExtension)
        {
            // 最後のサンプルから今までの端数も使用時間に含める
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

    private static string GetWindowClassName(IntPtr hwnd)
    {
        Span<char> buffer = stackalloc char[256];
        int length = GetClassName(hwnd, buffer, buffer.Length);
        return length > 0 ? new string(buffer[..length]) : string.Empty;
    }

    /// <summary>
    /// ApplicationFrameHost の枠ウィンドウから、UWP アプリ本体の PID を探す。
    /// 本体は "Windows.UI.Core.CoreWindow" クラスの子ウィンドウで、ホストとは別プロセスに属する。
    /// 見つからない場合は 0。
    /// </summary>
    private static unsafe uint FindUwpApplicationPid(IntPtr hostWindow, uint hostPid)
    {
        // コールバックが static なので、状態は lParam でポインタとして渡す。
        // EnumChildWindows は同期的に返るため、スタック上の変数を指して問題ない
        var context = new UwpSearchContext { HostPid = hostPid, FoundPid = 0 };
        EnumChildWindows(hostWindow, &EnumUwpChildWindow, (IntPtr)(&context));
        return context.FoundPid;
    }

    /// <summary><see cref="EnumUwpChildWindow"/> に lParam で渡す作業用の状態</summary>
    private struct UwpSearchContext
    {
        public uint HostPid;
        public uint FoundPid;
    }

    /// <summary>
    /// 子ウィンドウを1つずつ受け取り、UWP アプリ本体のものなら PID を控えて列挙を止める。
    /// 戻り値は Win32 の BOOL（0 で列挙終了）。ネイティブへ例外を返さないよう囲っておく。
    /// </summary>
    [UnmanagedCallersOnly(CallConvs = new[] { typeof(CallConvStdcall) })]
    private static unsafe int EnumUwpChildWindow(IntPtr hwnd, IntPtr lParam)
    {
        try
        {
            var context = (UwpSearchContext*)lParam;

            GetWindowThreadProcessId(hwnd, out uint childPid);
            if (childPid == 0 || childPid == context->HostPid) return 1;

            if (!string.Equals(GetWindowClassName(hwnd), UwpCoreWindowClass, StringComparison.Ordinal))
            {
                return 1;
            }

            context->FoundPid = childPid;
            return 0; // 見つけたので列挙を止める
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"ActiveWindowTracker child enum failed: {ex.Message}");
            return 0;
        }
    }

    // ===== プロセス名の解決 =====

    /// <summary>
    /// PID からプロセス名と表示名を得る。
    /// 管理者権限で動くプロセスでも名前を取れるよう、
    /// PROCESS_QUERY_LIMITED_INFORMATION で開いて実行ファイルのパスから解決する。
    /// </summary>
    private (string ProcessName, string AppName) ResolveProcess(uint pid)
    {
        IntPtr handle = OpenProcess(PROCESS_QUERY_LIMITED_INFORMATION, false, pid);
        if (handle == IntPtr.Zero)
        {
            // 保護されたプロセスなどは開けない。従来どおりの方法で粘る
            return ResolveProcessFallback(pid);
        }

        try
        {
            _ = GetProcessTimes(handle, out long creationTime, out _, out _, out _);

            var key = (pid, creationTime);
            if (_processInfoCache.TryGetValue(key, out var cached)) return cached;

            var path = GetProcessImagePath(handle);
            if (string.IsNullOrEmpty(path)) return ResolveProcessFallback(pid);

            var processName = Path.GetFileNameWithoutExtension(path);
            var appName = GetFileDescription(path) ?? processName;

            if (_processInfoCache.Count >= ProcessInfoCacheLimit)
            {
                _processInfoCache.Clear();
            }
            _processInfoCache[key] = (processName, appName);
            return (processName, appName);
        }
        finally
        {
            CloseHandle(handle);
        }
    }

    private static string GetProcessImagePath(IntPtr handle)
    {
        Span<char> buffer = stackalloc char[520];
        uint size = (uint)buffer.Length;
        return QueryFullProcessImageName(handle, 0, buffer, ref size) && size > 0
            ? new string(buffer[..(int)size])
            : string.Empty;
    }

    private static string? GetFileDescription(string path)
    {
        try
        {
            var description = FileVersionInfo.GetVersionInfo(path).FileDescription;
            return string.IsNullOrWhiteSpace(description) ? null : description;
        }
        catch (Exception)
        {
            // ファイルを読めない場合はプロセス名で十分
            return null;
        }
    }

    /// <summary>プロセスを開けなかった場合の予備手段。表示名までは望まない</summary>
    private static (string ProcessName, string AppName) ResolveProcessFallback(uint pid)
    {
        try
        {
            using var process = Process.GetProcessById((int)pid);
            var name = process.ProcessName;
            return (name, name);
        }
        catch (Exception)
        {
            // プロセスが既に終了している場合など
            return (string.Empty, string.Empty);
        }
    }

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;

        _timer.Stop();
        UnhookForegroundChanges();
        _isCollecting = false;
    }
}
