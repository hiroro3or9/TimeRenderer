using System;
using System.IO;
using System.Text.Json;
using System.Threading.Tasks;

namespace TimeRenderer.Services;

public static class JsonFileRepository
{
    private static readonly JsonSerializerOptions JsonOptions = new() { WriteIndented = true };

    private static string GetFullPath(string fileName) => Path.Combine(AppDomain.CurrentDomain.BaseDirectory, fileName);

    public static async Task SaveToFileAsync<T>(string filePath, T data)
    {
        try
        {
            var fullPath = GetFullPath(filePath);
            var jsonString = JsonSerializer.Serialize(data, JsonOptions);
            await File.WriteAllTextAsync(fullPath, jsonString);
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"Save failed for {filePath}: {ex.Message}");
        }
    }

    public static async Task<T?> LoadFromFileAsync<T>(string filePath)
    {
        var fullPath = GetFullPath(filePath);
        if (File.Exists(fullPath))
        {
            try
            {
                var jsonString = await File.ReadAllTextAsync(fullPath);
                return JsonSerializer.Deserialize<T>(jsonString, JsonOptions);
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"Load failed for {filePath}: {ex.Message}");
            }
        }
        return default;
    }

    public static void SaveToFileSync<T>(string filePath, T data)
    {
        try
        {
            var fullPath = GetFullPath(filePath);
            var jsonString = JsonSerializer.Serialize(data, JsonOptions);
            File.WriteAllText(fullPath, jsonString);
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"Save failed for {filePath}: {ex.Message}");
        }
    }

    public static T? LoadFromFileSync<T>(string filePath)
    {
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
