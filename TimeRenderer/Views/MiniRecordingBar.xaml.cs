using System;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Input;
using System.Windows.Interop;

namespace TimeRenderer.Views
{
    /// <summary>
    /// 記録中だけ現れる、常時最前面の小さなバー。
    ///
    /// アプリ内のツールバーは TimeRenderer を前面に出さないと見えない。
    /// トレイのアイコンは記録中かどうかまでは伝えるが、何をどれだけ記録しているかは伝えない。
    /// 他のアプリを触っている最中に「まだ記録が回っている」「これは別の作業だ」と気づくには、
    /// 画面に出しっぱなしのものが要る。
    ///
    /// 方針:
    /// - <b>載せるのは3つだけ</b>（何を・どれだけ・停止）。小さい窓に機能を足すと、
    ///   結局メインウィンドウの劣化版になる。それ以外の操作はクリックで本体を出して行う
    /// - <b>フォーカスを奪わない</b>（WS_EX_NOACTIVATE）。作業中のエディタから
    ///   入力先が飛ぶと、バーを置いていること自体が邪魔になる
    /// - <b>Alt+Tab に出さない</b>（WS_EX_TOOLWINDOW）。窓の数が増えたようには見せない
    /// - <b>Owner を持たせない</b>。メインウィンドウをトレイへ隠している間こそ必要なので、
    ///   親子にすると本体を隠した時点で道連れに消えてしまう
    /// </summary>
    public partial class MiniRecordingBar : System.Windows.Window
    {
        private const int GwlExStyle = -20;
        private const long WsExToolWindow = 0x00000080;
        private const long WsExNoActivate = 0x08000000;

        /// <summary>位置を画面内へ収めるときに、画面の縁との間に残す余白</summary>
        private const double ScreenMargin = 8;

        /// <summary>既定位置（画面右下）に取る余白</summary>
        private const double DefaultCornerMargin = 24;

        /// <summary>この距離を超えて動いたらドラッグ、超えなければクリックとみなす</summary>
        private const double DragThreshold = 2;

        [LibraryImport("user32.dll", EntryPoint = "GetWindowLongPtrW", SetLastError = true)]
        private static partial IntPtr GetWindowLongPtr(IntPtr hWnd, int nIndex);

        [LibraryImport("user32.dll", EntryPoint = "SetWindowLongPtrW", SetLastError = true)]
        private static partial IntPtr SetWindowLongPtr(IntPtr hWnd, int nIndex, IntPtr dwNewLong);

        private double? _savedLeft;
        private double? _savedTop;

        /// <summary>バーが（ドラッグではなく）クリックされた。メインウィンドウを前面に出す合図</summary>
        public event EventHandler? MainWindowRequested;

        /// <summary>ドラッグで位置が変わった。呼び出し側が設定へ保存する</summary>
        public event EventHandler? PositionChanged;

        /// <summary>右クリックメニューから「このバーを表示しない」が選ばれた</summary>
        public event EventHandler? DisableRequested;

        public MiniRecordingBar()
        {
            InitializeComponent();
        }

        /// <summary>
        /// 前回の位置を伝える。実際に置くのは最初の描画後。
        /// SizeToContent のため、それまで幅と高さが確定しない。
        /// </summary>
        public void SetSavedPosition(double? left, double? top)
        {
            _savedLeft = left;
            _savedTop = top;
        }

        protected override void OnSourceInitialized(EventArgs e)
        {
            base.OnSourceInitialized(e);

            // 拡張スタイルの付与に失敗しても、バーそのものは動く。
            // Alt+Tab に出る・フォーカスを奪うという劣化に留めて、例外は外へ出さない。
            try
            {
                var handle = new WindowInteropHelper(this).Handle;
                if (handle == IntPtr.Zero) return;

                long style = GetWindowLongPtr(handle, GwlExStyle).ToInt64();
                SetWindowLongPtr(handle, GwlExStyle, new IntPtr(style | WsExToolWindow | WsExNoActivate));
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"MiniRecordingBar ex-style failed: {ex.Message}");
            }
        }

        protected override void OnContentRendered(EventArgs e)
        {
            base.OnContentRendered(e);

            // 置く前に一瞬だけ既定位置で見えてしまうのを避けるため、XAML では Opacity=0 で始める。
            // 置けたかどうかに関わらず、ここで必ず見える状態へ戻す。
            if (Opacity < 1)
            {
                PlaceWindow();
                Opacity = 1;
            }
        }

        /// <summary>
        /// 前回の位置があればそこへ、無ければ画面右下へ置く。
        /// どちらの場合も画面内へ収める。モニター構成が変わった後でもバーを見失わないため。
        /// </summary>
        private void PlaceWindow()
        {
            double width = ActualWidth;
            double height = ActualHeight;

            // 何らかの理由で大きさが取れないときは、動かさずに既定の位置のまま出す。
            // 位置合わせを諦めるだけで、バーが使えなくなるわけではない。
            if (width <= 0 || height <= 0) return;

            double left;
            double top;

            if (_savedLeft.HasValue && _savedTop.HasValue)
            {
                left = _savedLeft.Value;
                top = _savedTop.Value;
            }
            else
            {
                var work = SystemParameters.WorkArea;
                left = work.Right - width - DefaultCornerMargin;
                top = work.Bottom - height - DefaultCornerMargin;
            }

            Left = ClampToVirtualScreen(
                left, width, SystemParameters.VirtualScreenLeft, SystemParameters.VirtualScreenWidth);
            Top = ClampToVirtualScreen(
                top, height, SystemParameters.VirtualScreenTop, SystemParameters.VirtualScreenHeight);
        }

        /// <summary>
        /// 1辺ぶんの座標を仮想画面の内側へ収める。
        /// 仮想画面よりバーのほうが大きいという異常な場合は、左上の端に寄せる。
        /// </summary>
        private static double ClampToVirtualScreen(double value, double size, double origin, double extent)
        {
            double min = origin + ScreenMargin;
            double max = origin + extent - size - ScreenMargin;
            if (max < min) return min;
            return Math.Clamp(value, min, max);
        }

        /// <summary>
        /// 押してから離すまでを一度に扱う。<see cref="System.Windows.Window.DragMove"/> は
        /// ボタンが離されるまで戻らないため、戻った時点の位置を見れば
        /// ドラッグだったのかクリックだったのかが分かる。
        /// </summary>
        private void BarRoot_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
        {
            if (e.ButtonState != MouseButtonState.Pressed) return;

            double beforeLeft = Left;
            double beforeTop = Top;

            try
            {
                DragMove();
            }
            catch (InvalidOperationException)
            {
                // 押下を拾ってから DragMove に入るまでの間にボタンが離されていた場合
                return;
            }

            bool moved = Math.Abs(Left - beforeLeft) >= DragThreshold
                         || Math.Abs(Top - beforeTop) >= DragThreshold;

            if (moved)
            {
                PositionChanged?.Invoke(this, EventArgs.Empty);
            }
            else
            {
                MainWindowRequested?.Invoke(this, EventArgs.Empty);
            }
        }

        private void ShowMainWindowMenuItem_Click(object sender, RoutedEventArgs e)
        {
            MainWindowRequested?.Invoke(this, EventArgs.Empty);
        }

        private void DisableMenuItem_Click(object sender, RoutedEventArgs e)
        {
            DisableRequested?.Invoke(this, EventArgs.Empty);
        }
    }
}
