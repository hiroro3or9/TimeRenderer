using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using System.Windows.Media;
using Brushes = System.Windows.Media.Brushes;
using TimeRenderer.Services;

namespace TimeRenderer.Services
{
    public class FilePersistenceService : JsonFileRepositoryBase
    {
        private const string ScheduleFilePath = "schedules.json";
        private const string MemosFilePath = "memos.json";

        public static void SaveData(IEnumerable<ScheduleItem> items) => SaveToFileSync(ScheduleFilePath, items);

        public static ObservableCollection<ScheduleItem> LoadData()
        {
            return LoadFromFileSync<ObservableCollection<ScheduleItem>>(ScheduleFilePath) ?? LoadSampleData();
        }

        public static void SaveMemos(Dictionary<DateTime, string> memos)
        {
            var serializableDict = memos.ToDictionary(k => k.Key.ToString("yyyy-MM-dd"), v => v.Value);
            SaveToFileSync(MemosFilePath, serializableDict);
        }

        public static Dictionary<DateTime, string> LoadMemos()
        {
            var serializableDict = LoadFromFileSync<Dictionary<string, string>>(MemosFilePath);
            return serializableDict?.ToDictionary(k => DateTime.Parse(k.Key), v => v.Value) ?? [];
        }

        private static ObservableCollection<ScheduleItem> LoadSampleData()
        {
            var baseDate = DateTime.Today;
            var baseTime = baseDate.AddHours(9);
            var tomorrowBase = baseDate.AddDays(1).AddHours(14);
            var yesterdayBase = baseDate.AddDays(-1).AddHours(10);

            return
            [
                new ScheduleItem
                {
                    Title = "朝会",
                    StartTime = baseTime,
                    EndTime = baseTime.AddMinutes(30),
                    Content = "定例",
                    BackgroundColor = Brushes.LightBlue
                },
                new ScheduleItem
                {
                    Title = "週次レビュー",
                    StartTime = tomorrowBase,
                    EndTime = tomorrowBase.AddHours(1.5),
                    Content = "進捗確認",
                    BackgroundColor = Brushes.LightGreen
                },
                new ScheduleItem
                {
                    Title = "顧客訪問",
                    StartTime = yesterdayBase,
                    EndTime = yesterdayBase.AddHours(2),
                    Content = "直行",
                    BackgroundColor = Brushes.LightPink
                },
                new ScheduleItem
                {
                    Title = "重複会議A",
                    StartTime = baseTime.AddHours(1),
                    EndTime = baseTime.AddHours(2),
                    Content = "重複テスト",
                    BackgroundColor = Brushes.Orange
                },
                new ScheduleItem
                {
                    Title = "重複会議B",
                    StartTime = baseTime.AddHours(1.5),
                    EndTime = baseTime.AddHours(2.5),
                    Content = "重複テスト",
                    BackgroundColor = Brushes.Purple
                }
            ];
        }
    }
}
