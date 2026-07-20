using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;

namespace TimeRenderer.Services;

/// <summary>読み込みの結果種別</summary>
public enum LoadStatus
{
    /// <summary>ファイルが存在しない（真の初回起動）</summary>
    NotFound,
    /// <summary>本体ファイルから正常に読めた</summary>
    Loaded,
    /// <summary>本体が壊れていたため、バックアップから読めた</summary>
    RecoveredFromBackup,
    /// <summary>本体もバックアップも読めなかった</summary>
    Failed
}

/// <summary>読み込み結果。呼び出し側が「壊れていた」ことを判断できるようにする</summary>
public sealed class LoadResult<T>
{
    public required LoadStatus Status { get; init; }
    public T? Value { get; init; }

    /// <summary>実際に読み込んだファイル名（復旧時にどこから復元したかを伝えるため）</summary>
    public string? SourceFile { get; init; }

    /// <summary>失敗・復旧の理由（ユーザーへの通知に使う）</summary>
    public string? Message { get; init; }

    public bool IsUsable => Status is LoadStatus.Loaded or LoadStatus.RecoveredFromBackup;
}

/// <summary>
/// JSON ファイルの読み書き。
///
/// 作業記録は失うと取り返しがつかないため、次の3点を守る:
///
/// 1. <b>書き込みはアトミックに</b>。
///    File.WriteAllText は既存ファイルを切り詰めてから書くため、
///    途中で強制終了・電源断が起きると本体が破損または空になる。
///    一時ファイルへ書いてから File.Replace で入れ替える。
///
/// 2. <b>直前の世代を残す</b>。
///    File.Replace は置換と同時に旧内容を .bak へ退避できる。
///
/// 3. <b>日次のスナップショットを残す</b>。
///    「昨日うっかり全部消した」のような、直前世代では救えないケースに備える。
/// </summary>
public static class JsonFileRepository
{
    private static readonly JsonSerializerOptions JsonOptions = new() { WriteIndented = true };

    /// <summary>保存先ディレクトリ (%APPDATA%\TimeRenderer)</summary>
    public static string DataDirectory { get; } =
        Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "TimeRenderer");

    /// <summary>日次スナップショットの保持世代数</summary>
    private const int DailySnapshotKeepCount = 7;

    private const string DailySnapshotDateFormat = "yyyy-MM-dd";

    /// <summary>保存失敗を通知済みのファイル（セッション中1回だけ通知する）</summary>
    private static readonly HashSet<string> NotifiedFailures = [];

    private static string GetFullPath(string fileName) => Path.Combine(DataDirectory, fileName);
    private static string GetBackupPath(string fileName) => GetFullPath(fileName) + ".bak";
    private static string GetTempPath(string fileName) => GetFullPath(fileName) + ".tmp";
    private static string GetDailySnapshotPath(string fileName, DateTime date) =>
        GetFullPath(fileName) + "." + date.ToString(DailySnapshotDateFormat) + ".bak";

    /// <summary>旧バージョン（exe と同じフォルダ）のデータを AppData へ移行する</summary>
    private static void MigrateLegacyFileIfNeeded(string fileName)
    {
        try
        {
            var newPath = GetFullPath(fileName);
            if (File.Exists(newPath)) return;

            var legacyPath = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, fileName);
            if (File.Exists(legacyPath))
            {
                Directory.CreateDirectory(DataDirectory);
                File.Copy(legacyPath, newPath);
            }
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"Migration failed for {fileName}: {ex.Message}");
        }
    }

    public static void SaveToFileSync<T>(string fileName, T data)
    {
        try
        {
            Directory.CreateDirectory(DataDirectory);

            var jsonString = JsonSerializer.Serialize(data, JsonOptions);

            var target = GetFullPath(fileName);
            var temp = GetTempPath(fileName);

            // まず一時ファイルへ完全に書き出す。
            // ここで失敗しても本体は無傷のまま残る
            File.WriteAllText(temp, jsonString);

            if (File.Exists(target))
            {
                CreateDailySnapshotIfNeeded(fileName, target);
                ReplaceFile(temp, target, GetBackupPath(fileName));
            }
            else
            {
                File.Move(temp, target);
            }
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"Save failed for {fileName}: {ex.Message}");
            NotifyOnce(fileName,
                $"データの保存に失敗しました: {fileName}\n{ex.Message}",
                "TimeRenderer - 保存エラー");
        }
    }

    /// <summary>
    /// 一時ファイルで本体を置き換える。旧内容は backup へ退避する。
    /// File.Replace が使えない環境（別ボリューム等）では、退避してから移動する。
    /// </summary>
    private static void ReplaceFile(string temp, string target, string backup)
    {
        try
        {
            File.Replace(temp, target, backup, ignoreMetadataErrors: true);
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"File.Replace failed, falling back: {ex.Message}");

            // フォールバック。順序が重要で、必ず退避を先に済ませる
            File.Copy(target, backup, overwrite: true);
            File.Delete(target);
            File.Move(temp, target);
        }
    }

    /// <summary>
    /// その日の最初の保存時に、置き換える前の内容をスナップショットとして残す。
    /// 直前世代（.bak）は保存のたびに上書きされるため、
    /// 「昨日の状態に戻したい」はこちらで救う。
    /// </summary>
    private static void CreateDailySnapshotIfNeeded(string fileName, string target)
    {
        try
        {
            var snapshot = GetDailySnapshotPath(fileName, DateTime.Today);
            if (File.Exists(snapshot)) return;

            File.Copy(target, snapshot);
            CleanupOldSnapshots(fileName);
        }
        catch (Exception ex)
        {
            // スナップショットは補助的な仕組みなので、失敗しても保存自体は続行する
            System.Diagnostics.Debug.WriteLine($"Snapshot failed for {fileName}: {ex.Message}");
        }
    }

    private static void CleanupOldSnapshots(string fileName)
    {
        var old = Directory.GetFiles(DataDirectory, fileName + ".*.bak")
            .Where(p => TryParseSnapshotDate(fileName, p, out _))
            .OrderByDescending(p => p)   // 日付形式が yyyy-MM-dd なので文字列順＝日付順
            .Skip(DailySnapshotKeepCount)
            .ToList();

        foreach (var path in old)
        {
            try { File.Delete(path); }
            catch (Exception ex) { System.Diagnostics.Debug.WriteLine($"Cleanup failed: {ex.Message}"); }
        }
    }

    private static bool TryParseSnapshotDate(string fileName, string path, out DateTime date)
    {
        date = default;
        var name = Path.GetFileName(path);

        var prefix = fileName + ".";
        const string suffix = ".bak";
        if (!name.StartsWith(prefix, StringComparison.Ordinal) ||
            !name.EndsWith(suffix, StringComparison.Ordinal))
        {
            return false;
        }

        var middle = name[prefix.Length..^suffix.Length];
        return DateTime.TryParseExact(middle, DailySnapshotDateFormat,
            System.Globalization.CultureInfo.InvariantCulture,
            System.Globalization.DateTimeStyles.None, out date);
    }

    /// <summary>
    /// 本体を読み、壊れていればバックアップ・日次スナップショットの順に試す。
    ///
    /// 「読めなかった」ことを呼び出し側へ必ず伝えるのが要点。
    /// 従来は例外を握りつぶして null を返しており、
    /// 呼び出し側がそれを「初回起動」と誤認してサンプルデータで上書きしていた。
    /// </summary>
    public static LoadResult<T> LoadFromFileSync<T>(string fileName)
    {
        MigrateLegacyFileIfNeeded(fileName);

        var target = GetFullPath(fileName);

        // 本体も控えも一切ない場合だけ「初回起動」とみなす。
        // 日次スナップショットが残っているなら、そこから復元できる可能性がある
        if (!File.Exists(target) && !EnumerateBackups(fileName).Any())
        {
            return new LoadResult<T> { Status = LoadStatus.NotFound };
        }

        // 1. 本体
        if (TryRead<T>(target, out var value, out var error))
        {
            return new LoadResult<T>
            {
                Status = LoadStatus.Loaded,
                Value = value,
                SourceFile = Path.GetFileName(target)
            };
        }

        var firstError = error;

        // 2. 直前世代 → 3. 日次スナップショット（新しい順）
        foreach (var candidate in EnumerateBackups(fileName))
        {
            if (!TryRead<T>(candidate, out var recovered, out _)) continue;

            return new LoadResult<T>
            {
                Status = LoadStatus.RecoveredFromBackup,
                Value = recovered,
                SourceFile = Path.GetFileName(candidate),
                Message = $"{fileName} を読み込めなかったため、バックアップ " +
                          $"{Path.GetFileName(candidate)} から復元しました。\n理由: {firstError}"
            };
        }

        return new LoadResult<T>
        {
            Status = LoadStatus.Failed,
            Message = $"{fileName} とそのバックアップをいずれも読み込めませんでした。\n理由: {firstError}"
        };
    }

    private static IEnumerable<string> EnumerateBackups(string fileName)
    {
        var backup = GetBackupPath(fileName);
        if (File.Exists(backup)) yield return backup;

        string[] snapshots;
        try
        {
            snapshots = [.. Directory.GetFiles(DataDirectory, fileName + ".*.bak")
                .Where(p => TryParseSnapshotDate(fileName, p, out _))
                .OrderByDescending(p => p)];
        }
        catch
        {
            yield break;
        }

        foreach (var snapshot in snapshots) yield return snapshot;
    }

    private static bool TryRead<T>(string path, out T? value, out string? error)
    {
        value = default;
        error = null;

        if (!File.Exists(path))
        {
            error = "ファイルがありません";
            return false;
        }

        try
        {
            var json = File.ReadAllText(path);

            // 空ファイルは「壊れている」扱いにする。
            // 中断された書き込みの結果である可能性が高く、正常な空データと区別できない
            if (string.IsNullOrWhiteSpace(json))
            {
                error = "ファイルが空です";
                return false;
            }

            var parsed = JsonSerializer.Deserialize<T>(json, JsonOptions);
            if (parsed == null)
            {
                error = "内容を解釈できませんでした";
                return false;
            }

            value = parsed;
            return true;
        }
        catch (Exception ex)
        {
            error = ex.Message;
            System.Diagnostics.Debug.WriteLine($"Load failed for {path}: {ex.Message}");
            return false;
        }
    }

    private static void NotifyOnce(string key, string message, string caption)
    {
        if (!NotifiedFailures.Add(key)) return;

        System.Windows.MessageBox.Show(
            message, caption,
            System.Windows.MessageBoxButton.OK,
            System.Windows.MessageBoxImage.Error);
    }
}
