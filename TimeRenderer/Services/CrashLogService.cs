using System.Diagnostics;
using System.IO;
using System.Reflection;
using System.Text;

namespace TimeRenderer.Services;

/// <summary>
/// アプリの起動・終了と未処理例外をローカルへ記録する。
/// ログ保存の失敗が元の例外処理を妨げないよう、すべての書き込みはこのクラス内で完結させる。
/// </summary>
public static class CrashLogService
{
    private static readonly Lock SyncRoot = new();

    /// <summary>ログ保存先 (%LOCALAPPDATA%\TimeRenderer\Logs)</summary>
    public static string LogDirectory { get; } = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
        "TimeRenderer",
        "Logs");

    public static void WriteLifecycle(string message) => Write("LIFECYCLE", message, null);

    public static void WriteException(string source, Exception exception, bool isTerminating) =>
        Write(isTerminating ? "FATAL" : "ERROR", source, exception);

    public static void WriteUnhandledObject(string source, object? exceptionObject, bool isTerminating)
    {
        if (exceptionObject is Exception exception)
        {
            WriteException(source, exception, isTerminating);
            return;
        }

        Write(
            isTerminating ? "FATAL" : "ERROR",
            $"{source}: 非 Exception オブジェクト: {exceptionObject ?? "null"}",
            null);
    }

    private static void Write(string level, string message, Exception? exception)
    {
        try
        {
            lock (SyncRoot)
            {
                Directory.CreateDirectory(LogDirectory);

                var now = DateTimeOffset.Now;
                var logPath = Path.Combine(LogDirectory, $"application-{now:yyyy-MM-dd}.log");
                var builder = new StringBuilder()
                    .Append('[').Append(now.ToString("O")).Append("] [")
                    .Append(level).Append("] ")
                    .AppendLine(message)
                    .Append("ProcessId: ").AppendLine(Environment.ProcessId.ToString())
                    .Append("AppVersion: ").AppendLine(
                        Assembly.GetEntryAssembly()?.GetName().Version?.ToString() ?? "unknown")
                    .Append("Runtime: ").AppendLine(Environment.Version.ToString());

                if (exception != null)
                {
                    builder.AppendLine(exception.ToString());
                }

                builder.AppendLine();
                File.AppendAllText(logPath, builder.ToString(), Encoding.UTF8);

                DeleteExpiredLogs(now.Date.AddDays(-30));
            }
        }
        catch (Exception logException)
        {
            // クラッシュ記録の失敗で、アプリ本来の処理や例外通知まで巻き込まない。
            Debug.WriteLine($"Crash log write failed: {logException.Message}");
        }
    }

    private static void DeleteExpiredLogs(DateTime cutoffDate)
    {
        try
        {
            foreach (var path in Directory.EnumerateFiles(LogDirectory, "application-*.log"))
            {
                if (File.GetLastWriteTime(path).Date < cutoffDate)
                {
                    File.Delete(path);
                }
            }
        }
        catch (Exception cleanupException)
        {
            // 保持期間の掃除に失敗しても、今回のログは既に保存できているので継続する。
            Debug.WriteLine($"Crash log cleanup failed: {cleanupException.Message}");
        }
    }
}
