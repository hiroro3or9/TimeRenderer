using System;
using TimeRenderer.Services;

namespace TimeRenderer.Services
{
    public class SettingsService : JsonFileRepositoryBase
    {
        private const string SettingsFilePath = "appsettings.json";

        public void SaveSettings(AppSettings settings)
        {
            SaveToFileSync(SettingsFilePath, settings);
        }

        public AppSettings? LoadSettings()
        {
            return LoadFromFileSync<AppSettings>(SettingsFilePath);
        }
    }
}
