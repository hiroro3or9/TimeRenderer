using System;
using System.Collections.Generic;
using System.IO;
using System.Text.Json;

namespace TimeRenderer.Services;

public static class JsonFileRepository
{
    private static readonly JsonSerializerOptions JsonOptions = new() { WriteIndented = true };

    /// <summary>保存先ディレクトリ (%APPDATA%\TimeRenderer)</summary>
    private static readonly string DataDirectory =
        Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "TimeRenderer");

    /// <summary>保存失敗を通知済みのファイル（セッション中1回だけ通知する）</summary>
    private static readonly HashSet<string> NotifiedFailures = [];

    private static string GetFullPath(string fileName) => Path.Combine(DataDirectory, fileName);

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

    public static void SaveToFileSync<T>(string filePath, T data)
    {
        try
        {
            Directory.CreateDirectory(DataDirectory);
            var jsonString = JsonSerializer.Serialize(data, JsonOptions);
            File.WriteAllText(GetFullPath(filePath), jsonString);
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"Save failed for {filePath}: {ex.Message}");
            // 黙殺するとデータ消失に気づけないため、セッション中1回だけ通知する
            if (NotifiedFailures.Add(filePath))
            {
                System.Windows.MessageBox.Show(
                    $"データの保存に失敗しました: {filePath}\n{ex.Message}",
                    "TimeRenderer - 保存エラー",
                    System.Windows.MessageBoxButton.OK,
                    System.Windows.MessageBoxImage.Error);
            }
        }
    }

    public static T? LoadFromFileSync<T>(string filePath)
    {
        MigrateLegacyFileIfNeeded(filePath);

        var fullPath = GetFullPath(filePath);
        if (File.Exists(fullPath))
        {
            try
            {
                var jsonString = File.ReadAllText(fullPath);
                return JsonSerializer.Deserialize<T>(jsonString, JsonOptions);
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"Load failed for {filePath}: {ex.Message}");
            }
        }
        return default;
    }
}
