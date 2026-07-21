using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using System.Windows.Media;
using Brushes = System.Windows.Media.Brushes;
using TimeRenderer.Services;
using TimeRenderer.Models;

namespace TimeRenderer.Services;

public static class FilePersistenceService
{
    private const string ScheduleFilePath = "schedules.json";
    private const string MemosFilePath = "memos.json";
    private const string WorkDaysFilePath = "workdays.json";

    public static void SaveData(IEnumerable<ScheduleItem> items) => JsonFileRepository.SaveToFileSync(ScheduleFilePath, items);

    /// <summary>予定データの読み込み結果</summary>
    /// <param name="Items">読み込めたアイテム（失敗時は空）</param>
    /// <param name="Status">読み込みの結果種別</param>
    /// <param name="Message">復旧・失敗の説明（正常時は null）</param>
    public record ScheduleLoadResult(
        ObservableCollection<ScheduleItem> Items,
        LoadStatus Status,
        string? Message);

    /// <summary>
    /// 予定データを読み込む。
    ///
    /// サンプルデータへ差し替えるのは「ファイルが存在しない」＝真の初回起動のときだけ。
    /// 読み込み失敗（破損）でサンプルに差し替えると、
    /// 次の保存で本物の記録がサンプルに上書きされてしまうため、
    /// 失敗は失敗として呼び出し側へ伝える。
    /// </summary>
    public static ScheduleLoadResult LoadData()
    {
        var result = JsonFileRepository.LoadFromFileSync<ObservableCollection<ScheduleItem>>(ScheduleFilePath);

        return result.Status switch
        {
            LoadStatus.NotFound => new ScheduleLoadResult(LoadSampleData(), LoadStatus.NotFound, null),

            LoadStatus.Loaded or LoadStatus.RecoveredFromBackup =>
                new ScheduleLoadResult(result.Value ?? [], result.Status, result.Message),

            _ => new ScheduleLoadResult([], LoadStatus.Failed, result.Message)
        };
    }

    /// <summary>
    /// 勤務記録（出勤・退勤）を保存する。
    /// 予定データとは別ファイルにして、片方が壊れてももう片方が巻き込まれないようにする。
    /// </summary>
    public static void SaveWorkDays(IEnumerable<WorkDayLog> logs) =>
        JsonFileRepository.SaveToFileSync(WorkDaysFilePath, logs);

    /// <summary>勤務記録を読み込む。読めなかった場合は空で始める（記録は日々作り直せるため）</summary>
    public static List<WorkDayLog> LoadWorkDays()
    {
        var result = JsonFileRepository.LoadFromFileSync<List<WorkDayLog>>(WorkDaysFilePath);
        var logs = result.Value ?? [];

        // 壊れた行（出勤時刻が既定値）は捨て、日付順に整えておく
        return [.. logs
            .Where(l => l.StartTime != default)
            .OrderBy(l => l.StartTime)];
    }

    public static void SaveMemos(Dictionary<DateTime, string> memos)
    {
        var serializableDict = memos.ToDictionary(k => k.Key.ToString("yyyy-MM-dd"), v => v.Value);
        JsonFileRepository.SaveToFileSync(MemosFilePath, serializableDict);
    }

    public static Dictionary<DateTime, string> LoadMemos()
    {
        var serializableDict = JsonFileRepository.LoadFromFileSync<Dictionary<string, string>>(MemosFilePath).Value;
        if (serializableDict == null) return [];

        // カルチャ非依存で解析し、壊れたキーは読み飛ばす
        var result = new Dictionary<DateTime, string>();
        foreach (var (key, value) in serializableDict)
        {
            if (DateTime.TryParseExact(key, "yyyy-MM-dd", System.Globalization.CultureInfo.InvariantCulture,
                    System.Globalization.DateTimeStyles.None, out var date))
            {
                result[date] = value;
            }
        }
        return result;
    }

    private static ObservableCollection<ScheduleItem> LoadSampleData()
    {
        var baseDate = DateTime.Today;
        var baseTime = baseDate.AddHours(9);
        var tomorrowBase = baseDate.AddDays(1).AddHours(14);
        var yesterdayBase = baseDate.AddDays(-1).AddHours(10);

        return
        [
            new()
            {
                Title = "朝会",
                StartTime = baseTime,
                EndTime = baseTime.AddMinutes(30),
                Content = "定例",
                BackgroundColor = Brushes.LightBlue
            },
            new()
            {
                Title = "週次レビュー",
                StartTime = tomorrowBase,
                EndTime = tomorrowBase.AddHours(1.5),
                Content = "進捗確認",
                BackgroundColor = Brushes.LightGreen
            },
            new()
            {
                Title = "顧客訪問",
                StartTime = yesterdayBase,
                EndTime = yesterdayBase.AddHours(2),
                Content = "直行",
                BackgroundColor = Brushes.LightPink
            },
            new()
            {
                Title = "重複会議A",
                StartTime = baseTime.AddHours(1),
                EndTime = baseTime.AddHours(2),
                Content = "重複テスト",
                BackgroundColor = Brushes.Orange
            },
            new()
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
