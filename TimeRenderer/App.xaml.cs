using System.Threading.Tasks;
using System.Windows;
using System.Windows.Threading;

using TimeRenderer.Services;

namespace TimeRenderer
{
    /// <summary>
    /// Interaction logic for App.xaml
    /// </summary>
    public partial class App : System.Windows.Application
    {
        public App()
        {
            DispatcherUnhandledException += OnDispatcherUnhandledException;
            AppDomain.CurrentDomain.UnhandledException += OnAppDomainUnhandledException;
            TaskScheduler.UnobservedTaskException += OnUnobservedTaskException;

            CrashLogService.WriteLifecycle("Application started");
        }

        private static void OnDispatcherUnhandledException(
            object sender, DispatcherUnhandledExceptionEventArgs e)
        {
            CrashLogService.WriteException(
                nameof(DispatcherUnhandledException), e.Exception, isTerminating: true);

            try
            {
                System.Windows.MessageBox.Show(
                    "予期しないエラーが発生したため、TimeRenderer を終了します。\n\n" +
                    $"原因調査用のログを次のフォルダーへ保存しました。\n{CrashLogService.LogDirectory}",
                    "TimeRenderer エラー",
                    MessageBoxButton.OK,
                    MessageBoxImage.Error);
            }
            catch (Exception notificationException)
            {
                CrashLogService.WriteException(
                    "Unhandled exception notification", notificationException, isTerminating: false);
            }

            // 状態が壊れたまま継続しない。Handled は false のままにして通常の異常終了へ進める。
        }

        private static void OnAppDomainUnhandledException(object sender, UnhandledExceptionEventArgs e)
        {
            CrashLogService.WriteUnhandledObject(
                nameof(AppDomain.UnhandledException), e.ExceptionObject, e.IsTerminating);
        }

        private static void OnUnobservedTaskException(
            object? sender, UnobservedTaskExceptionEventArgs e)
        {
            CrashLogService.WriteException(
                nameof(TaskScheduler.UnobservedTaskException), e.Exception, isTerminating: false);
            e.SetObserved();
        }

        protected override void OnExit(ExitEventArgs e)
        {
            CrashLogService.WriteLifecycle($"Application exited (ExitCode={e.ApplicationExitCode})");

            DispatcherUnhandledException -= OnDispatcherUnhandledException;
            AppDomain.CurrentDomain.UnhandledException -= OnAppDomainUnhandledException;
            TaskScheduler.UnobservedTaskException -= OnUnobservedTaskException;

            base.OnExit(e);
        }

        /// <summary>
        /// ライト／ダークテーマを切り替える。
        /// Colors.xaml は Styles.xaml の内部に組み込まれているため、
        /// DarkColors.xaml だけを動的に追加／削除して優先度を制御する。
        /// WPF の MergedDictionaries は後に追加されたものが先に検索されるため、
        /// Add（末尾追加）することで最高優先度を確保する。
        /// </summary>
        public static void ApplyTheme(bool isDark)
        {
            var merged = System.Windows.Application.Current.Resources.MergedDictionaries;

            // 動的に追加した DarkColors.xaml をすべて削除する
            for (int i = merged.Count - 1; i >= 0; i--)
            {
                var src = merged[i].Source?.ToString() ?? "";
                if (src.Contains("DarkColors.xaml", System.StringComparison.OrdinalIgnoreCase))
                {
                    merged.RemoveAt(i);
                }
            }

            if (isDark)
            {
                // DarkColors.xaml を末尾に追加（最高優先度）することで
                // Styles.xaml 内部の Colors.xaml より優先して参照される
                var dict = new ResourceDictionary
                {
                    Source = new System.Uri("Themes/DarkColors.xaml", System.UriKind.Relative)
                };
                merged.Add(dict);
            }
            // ライトモード: DarkColors.xaml を除去するだけでよい
            // Colors.xaml は Styles.xaml 内部に常に存在しているため追加不要
        }
    }
}
