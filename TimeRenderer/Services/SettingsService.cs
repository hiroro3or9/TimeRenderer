using System.IO;
using System.Text.Json;

namespace TimeRenderer.Services
{
    public class SettingsService
    {
        private static readonly JsonSerializerOptions _jsonOptions = new() { WriteIndented = true };
        private static string SettingsFilePath => Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "appsettings.json");

        public void SaveSettings(AppSettings settings)
        {
            try
            {
                var jsonString = JsonSerializer.Serialize(settings, _jsonOptions);
                File.WriteAllText(SettingsFilePath, jsonString);
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"Save settings failed: {ex.Message}");
            }
        }

        public AppSettings? LoadSettings()
        {
            if (File.Exists(SettingsFilePath))
            {
                try
                {
                    var jsonString = File.ReadAllText(SettingsFilePath);
                    return JsonSerializer.Deserialize<AppSettings>(jsonString);
                }
                catch (Exception ex)
                {
                    System.Diagnostics.Debug.WriteLine($"Load settings failed: {ex.Message}");
                }
            }
            return null;
        }
    }
}
