using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Interop;
using System.Windows.Threading;

namespace TimeRenderer.Views
{
    /// <summary>
    /// グローバルホットキー。アプリが最小化・トレイ常駐中でも手を止めずに押せることが要件なので、
    /// ダイアログを出さずに即実行する。結果はトレイのバルーン通知で知らせる。
    ///
    /// 割り当て（いずれも衝突したらフォールバック候補を順に試す）:
    /// - 記録の開始／停止  : Ctrl+Alt+R
    /// - 出勤（仕事の開始）: Ctrl+Alt+S
    /// - 退勤（仕事の終了）: Ctrl+Alt+E
    ///
    /// 出勤・退勤をトグル1つにまとめないのは、押し間違いの影響が大きいため。
    /// 「出勤したつもりが退勤になっていた」を防ぐ。
    /// ※ Win+Shift+R は Snipping Tool の画面録画と競合するため使用しない。
    /// </summary>
    public partial class MainWindow
    {
        private const int WmHotkey = 0x0312;

        // ホットキーID（このウィンドウ内で一意であればよい）
        private const int HotkeyIdRecord = 0x5452; // 'T','R'
        private const int HotkeyIdClockIn = 0x5453;
        private const int HotkeyIdClockOut = 0x5454;

        private const uint ModAlt = 0x0001;
        private const uint ModControl = 0x0002;
        private const uint ModShift = 0x0004;
        private const uint ModNoRepeat = 0x4000; // 押しっぱなしで連続発火させない

        private const uint VkE = 0x45;
        private const uint VkR = 0x52;
        private const uint VkS = 0x53;
        private const uint VkF9 = 0x78;
        private const uint VkF10 = 0x79;
        private const uint VkF11 = 0x7A;

        // LibraryImport: コンパイル時にマーシャリングコードを生成する（SYSLIB1054 対応）。
        // bool の戻り値は既定でマーシャリングされないため、Win32 BOOL を明示する
        [LibraryImport("user32.dll", SetLastError = true)]
        [return: MarshalAs(UnmanagedType.Bool)]
        private static partial bool RegisterHotKey(IntPtr hWnd, int id, uint fsModifiers, uint vk);

        [LibraryImport("user32.dll", SetLastError = true)]
        [return: MarshalAs(UnmanagedType.Bool)]
        private static partial bool UnregisterHotKey(IntPtr hWnd, int id);

        private HwndSource? _hwndSource;
        private readonly List<int> _registeredHotkeyIds = [];

        /// <summary>登録に成功した記録トグルのホットキー表示名（トレイメニュー等での案内用）</summary>
        public string? RegisteredHotkeyText { get; private set; }

        /// <summary>登録に成功した出勤のホットキー表示名</summary>
        public string? ClockInHotkeyText { get; private set; }

        /// <summary>登録に成功した退勤のホットキー表示名</summary>
        public string? ClockOutHotkeyText { get; private set; }

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

            RegisteredHotkeyText = TryRegister(HotkeyIdRecord,
            [
                (ModControl | ModAlt, VkR, "Ctrl+Alt+R"),
                (ModControl | ModAlt | ModShift, VkR, "Ctrl+Alt+Shift+R"),
                (ModControl | ModAlt, VkF9, "Ctrl+Alt+F9"),
            ]);

            ClockInHotkeyText = TryRegister(HotkeyIdClockIn,
            [
                (ModControl | ModAlt, VkS, "Ctrl+Alt+S"),
                (ModControl | ModAlt | ModShift, VkS, "Ctrl+Alt+Shift+S"),
                (ModControl | ModAlt, VkF10, "Ctrl+Alt+F10"),
            ]);

            ClockOutHotkeyText = TryRegister(HotkeyIdClockOut,
            [
                (ModControl | ModAlt, VkE, "Ctrl+Alt+E"),
                (ModControl | ModAlt | ModShift, VkE, "Ctrl+Alt+Shift+E"),
                (ModControl | ModAlt, VkF11, "Ctrl+Alt+F11"),
            ]);

            // トレイメニューと出勤/退勤ボタンに、実際に登録できたキーを反映する
            UpdateContextMenu();
            ApplyWorkDayHotkeyToolTip();

            if (RegisteredHotkeyText == null && ClockInHotkeyText == null && ClockOutHotkeyText == null)
            {
                // 全滅：他アプリとの競合か、旧インスタンスが残っている可能性が高い。
                // 起動直後はトレイアイコンの準備が済んでいない場合があるため、アイドル時に通知する。
                Dispatcher.BeginInvoke(
                    () => ShowTrayBalloon(
                        "ホットキーを登録できませんでした",
                        "他のアプリ、またはトレイに残った旧インスタンスと競合している可能性があります"),
                    DispatcherPriority.ApplicationIdle);
            }
        }

        /// <summary>候補を順に試して登録する。成功した候補の表示名を返す（全て失敗したら null）</summary>
        private string? TryRegister(int id, (uint Mods, uint Vk, string Name)[] candidates)
        {
            if (_hwndSource == null) return null;

            foreach (var candidate in candidates)
            {
                // MOD_NOREPEAT 非対応環境（Windows 7 等）も考慮し、付き→無しの順で試す
                if (RegisterHotKey(_hwndSource.Handle, id, candidate.Mods | ModNoRepeat, candidate.Vk) ||
                    RegisterHotKey(_hwndSource.Handle, id, candidate.Mods, candidate.Vk))
                {
                    _registeredHotkeyIds.Add(id);
                    return candidate.Name;
                }
                Debug.WriteLine($"Hotkey: {candidate.Name} の登録に失敗 (Win32Error={Marshal.GetLastWin32Error()})");
            }
            return null;
        }

        /// <summary>出勤/退勤ボタンのツールチップに、実際に登録できたホットキーを載せる</summary>
        private void ApplyWorkDayHotkeyToolTip()
        {
            var inText = ClockInHotkeyText ?? "未登録";
            var outText = ClockOutHotkeyText ?? "未登録";
            WorkDayButton.ToolTip =
                $"仕事の開始・終了を登録します（日/週ビューに線で表示されます）\n" +
                $"出勤: {inText}　退勤: {outText}\n" +
                "右クリック、または日/週ビューの線のラベルから時刻を編集できます";
        }

        private IntPtr HotkeyHook(IntPtr hwnd, int msg, IntPtr wParam, IntPtr lParam, ref bool handled)
        {
            if (msg != WmHotkey) return IntPtr.Zero;

            switch ((int)wParam.ToInt64())
            {
                case HotkeyIdRecord:
                    OnGlobalHotkey();
                    handled = true;
                    break;

                case HotkeyIdClockIn:
                    OnClockInHotkey();
                    handled = true;
                    break;

                case HotkeyIdClockOut:
                    OnClockOutHotkey();
                    handled = true;
                    break;
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

        /// <summary>ホットキー押下：出勤を登録する（すでに勤務中なら状況だけ知らせる）</summary>
        private void OnClockInHotkey()
        {
            if (ViewModel.ClockIn())
            {
                ShowTrayBalloon("出勤を登録しました", ViewModel.WorkStatusText);
            }
            else
            {
                ShowTrayBalloon("すでに勤務中です", ViewModel.WorkStatusText);
            }
        }

        /// <summary>ホットキー押下：退勤を登録する</summary>
        private void OnClockOutHotkey()
        {
            if (ViewModel.ClockOut())
            {
                ShowTrayBalloon("退勤を登録しました", ViewModel.WorkStatusText);
            }
            else
            {
                ShowTrayBalloon("出勤が登録されていません", "先に出勤を登録してください（記録の開始とは別の操作です）");
            }
        }

        /// <summary>ホットキーの登録解除（ウィンドウ破棄時に呼ぶ）</summary>
        private void UnregisterGlobalHotkey()
        {
            if (_hwndSource != null)
            {
                foreach (var id in _registeredHotkeyIds)
                {
                    UnregisterHotKey(_hwndSource.Handle, id);
                }
            }
            _registeredHotkeyIds.Clear();
            _hwndSource?.RemoveHook(HotkeyHook);
            _hwndSource = null;
        }
    }
}
