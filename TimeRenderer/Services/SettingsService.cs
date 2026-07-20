using TimeRenderer.Models;

namespace TimeRenderer.Services;

public static class SettingsService
{
    private const string SettingsFilePath = "appsettings.json";

    public static void SaveSettings(AppSettings settings) => JsonFileRepository.SaveToFileSync(SettingsFilePath, settings);

    public static AppSettings? LoadSettings() => JsonFileRepository.LoadFromFileSync<AppSettings>(SettingsFilePath).Value;
}
