using System;
using TimeRenderer.Services;

namespace TimeRenderer.Services;

public class SettingsService : JsonFileRepositoryBase
{
    private const string SettingsFilePath = "appsettings.json";

    public static void SaveSettings(AppSettings settings) => SaveToFileSync(SettingsFilePath, settings);

    public static AppSettings? LoadSettings() => LoadFromFileSync<AppSettings>(SettingsFilePath);
}
