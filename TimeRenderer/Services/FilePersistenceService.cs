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
    private const string WorkDaysFilePath = "workdays.json";
    private const string AppUsageFilePath = "appusage.json";
    private const string TodosFilePath = "todos.json";
    private const string TodoArchiveFilePath = "todos-archive.json";

    /// <summary>アプリ使用記録の保持日数。裏付け用の補助データなので古いものは自動で捨てる</summary>
    private const int AppUsageRetentionDays = 60;

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

    /// <summary>
    /// アプリ使用記録を保存する。
    /// 補助データなので専用ファイルに分け、予定データの破損に巻き込まれないようにする。
    /// </summary>
    public static void SaveAppUsage(IEnumerable<AppUsageInterval> intervals) =>
        JsonFileRepository.SaveToFileSync(AppUsageFilePath, intervals);

    /// <summary>
    /// アプリ使用記録を読み込む。読めなかった場合は空で始める（裏付け用の補助データのため）。
    /// 保持期間を過ぎた古い記録はここで捨てる。
    /// </summary>
    public static List<AppUsageInterval> LoadAppUsage()
    {
        var result = JsonFileRepository.LoadFromFileSync<List<AppUsageInterval>>(AppUsageFilePath);
        var intervals = result.Value ?? [];

        var cutoff = DateTime.Today.AddDays(-AppUsageRetentionDays);
        return [.. intervals
            .Where(i => i.End > i.Start && i.End >= cutoff && !string.IsNullOrEmpty(i.ProcessName))
            .OrderBy(i => i.Start)];
    }

    /// <summary>
    /// ToDo を保存する。
    /// 予定データとは独立した一覧なので専用ファイルに分け、片方の破損で両方を失わないようにする。
    /// </summary>
    public static void SaveTodos(IEnumerable<TodoItem> todos) =>
        JsonFileRepository.SaveToFileSync(TodosFilePath, todos);

    /// <summary>
    /// ToDo を読み込む。読めなかった場合は空で始める。
    /// タイトルの無い行は保存事故の残骸とみなして捨てる（一覧に空行が並ぶのを防ぐ）。
    /// </summary>
    public static List<TodoItem> LoadTodos()
    {
        var result = JsonFileRepository.LoadFromFileSync<List<TodoItem>>(TodosFilePath);
        var todos = result.Value ?? [];

        return [.. todos.Where(t => !string.IsNullOrWhiteSpace(t.Title))];
    }

    /// <summary>
    /// 片付いた ToDo の保管庫を保存する。
    /// 現役の一覧（todos.json）から切り離すことで、完了済みが延々と溜まって
    /// 読み書きが重くなるのを防ぐ。見積もりの実績集計にはこちらも使う。
    /// </summary>
    public static void SaveTodoArchive(IEnumerable<TodoItem> todos) =>
        JsonFileRepository.SaveToFileSync(TodoArchiveFilePath, todos);

    /// <summary>保管庫を読み込む。読めなかった場合は空で始める（集計の材料でしかないため）</summary>
    public static List<TodoItem> LoadTodoArchive()
    {
        var result = JsonFileRepository.LoadFromFileSync<List<TodoItem>>(TodoArchiveFilePath);
        var todos = result.Value ?? [];

        return [.. todos.Where(t => !string.IsNullOrWhiteSpace(t.Title))];
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
                Kind = ScheduleItemKind.Planned,
                Title = "朝会",
                StartTime = baseTime,
                EndTime = baseTime.AddMinutes(30),
                Content = "定例",
                BackgroundColor = Brushes.LightBlue
            },
            new()
            {
                Kind = ScheduleItemKind.Planned,
                Title = "週次レビュー",
                StartTime = tomorrowBase,
                EndTime = tomorrowBase.AddHours(1.5),
                Content = "進捗確認",
                BackgroundColor = Brushes.LightGreen
            },
            new()
            {
                Kind = ScheduleItemKind.Recorded,
                Title = "顧客訪問",
                StartTime = yesterdayBase,
                EndTime = yesterdayBase.AddHours(2),
                Content = "直行",
                BackgroundColor = Brushes.LightPink
            },
            new()
            {
                Kind = ScheduleItemKind.Planned,
                Title = "重複会議A",
                StartTime = baseTime.AddHours(1),
                EndTime = baseTime.AddHours(2),
                Content = "重複テスト",
                BackgroundColor = Brushes.Orange
            },
            new()
            {
                Kind = ScheduleItemKind.Planned,
                Title = "重複会議B",
                StartTime = baseTime.AddHours(1.5),
                EndTime = baseTime.AddHours(2.5),
                Content = "重複テスト",
                BackgroundColor = Brushes.Purple
            }
        ];
    }
}
