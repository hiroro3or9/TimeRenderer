using System;
using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Interop;
using System.Windows.Threading;

namespace TimeRenderer.Views
{
    /// <summary>
    /// グローバルホットキーによる記録の即開始／停止。
    /// アプリが最小化・トレイ常駐中でも Ctrl+Alt+R（衝突時は Ctrl+Alt+Shift+R → Ctrl+Alt+F9）で
    /// ダイアログなしに記録をトグルできる。結果はトレイのバルーン通知で知らせる。
    /// すべての候補で登録に失敗した場合は起動時にバルーン通知で警告する。
    /// ※ Win+Shift+R は Snipping Tool の画面録画と競合するため使用しない。
    /// </summary>
    public partial class MainWindow
    {
        private const int WmHotkey = 0x0312;
        private const int HotkeyId = 0x5452; // 'T','R'

        private const uint ModAlt = 0x0001;
        private const uint ModControl = 0x0002;
        private const uint ModShift = 0x0004;
        private const uint ModNoRepeat = 0x4000; // 押しっぱなしで連続発火させない

        private const uint VkR = 0x52;
        private const uint VkF9 = 0x78;

        [DllImport("user32.dll", SetLastError = true)]
        private static extern bool RegisterHotKey(IntPtr hWnd, int id, uint fsModifiers, uint vk);

        [DllImport("user32.dll", SetLastError = true)]
        private static extern bool UnregisterHotKey(IntPtr hWnd, int id);

        private HwndSource? _hwndSource;
        private bool _hotkeyRegistered;

        /// <summary>登録に成功したホットキーの表示名（トレイメニュー等での案内用）</summary>
        public string? RegisteredHotkeyText { get; private set; }

        protected override void OnSourceInitialized(EventArgs e)
        {
            base.OnSourceInitialized(e);

            _hwndSource = PresentationSource.FromVisual(this) as HwndSource
                          ?? HwndSource.FromHwnd(new WindowInteropHelper(this).Handle);
            if (_hwndSource == null)
            {
                Debug.WriteLine("Hotkey: HwndSource を取得できませんでした");
                return;
            }
            _hwndSource.AddHook(HotkeyHook);

            // 候補を順に試す（既定 → 衝突回避のフォールバック）
            (uint Mods, uint Vk, string Name)[] candidates =
            [
                (ModControl | ModAlt, VkR, "Ctrl+Alt+R"),
                (ModControl | ModAlt | ModShift, VkR, "Ctrl+Alt+Shift+R"),
                (ModControl | ModAlt, VkF9, "Ctrl+Alt+F9"),
            ];

            foreach (var candidate in candidates)
            {
                // MOD_NOREPEAT 非対応環境（Windows 7 等）も考慮し、付き→無しの順で試す
                if (RegisterHotKey(_hwndSource.Handle, HotkeyId, candidate.Mods | ModNoRepeat, candidate.Vk) ||
                    RegisterHotKey(_hwndSource.Handle, HotkeyId, candidate.Mods, candidate.Vk))
                {
                    _hotkeyRegistered = true;
                    RegisteredHotkeyText = candidate.Name;
                    break;
                }
                Debug.WriteLine($"Hotkey: {candidate.Name} の登録に失敗 (Win32Error={Marshal.GetLastWin32Error()})");
            }

            // トレイメニューの「記録開始」にホットキー表示を反映する
            UpdateContextMenu();

            if (!_hotkeyRegistered)
            {
                // 全候補が失敗：他アプリとの競合か、旧インスタンスが残っている可能性が高い。
                // 起動直後はトレイアイコンの準備が済んでいない場合があるため、アイドル時に通知する。
                Dispatcher.BeginInvoke(
                    () => ShowTrayBalloon(
                        "ホットキーを登録できませんでした",
                        "他のアプリ、またはトレイに残った旧インスタンスと競合している可能性があります"),
                    DispatcherPriority.ApplicationIdle);
            }
        }

        private IntPtr HotkeyHook(IntPtr hwnd, int msg, IntPtr wParam, IntPtr lParam, ref bool handled)
        {
            if (msg == WmHotkey && wParam.ToInt64() == HotkeyId)
            {
                OnGlobalHotkey();
                handled = true;
            }
            return IntPtr.Zero;
        }

        /// <summary>ホットキー押下：記録をトグルし、結果をバルーン通知で知らせる</summary>
        private void OnGlobalHotkey()
        {
            ViewModel.QuickToggleRecording();

            if (ViewModel.IsRecording)
            {
                ShowTrayBalloon("記録を開始しました",
                    $"「{ViewModel.RecordingTitle}」\nタイトルはウィンドウ上部で変更できます");
            }
            else
            {
                ShowTrayBalloon("記録を保存しました", "タイムラインに追加しました");
            }
        }

        /// <summary>ホットキーの登録解除（ウィンドウ破棄時に呼ぶ）</summary>
        private void UnregisterGlobalHotkey()
        {
            if (_hotkeyRegistered && _hwndSource != null)
            {
                UnregisterHotKey(_hwndSource.Handle, HotkeyId);
                _hotkeyRegistered = false;
            }
            _hwndSource?.RemoveHook(HotkeyHook);
            _hwndSource = null;
        }
    }
}
