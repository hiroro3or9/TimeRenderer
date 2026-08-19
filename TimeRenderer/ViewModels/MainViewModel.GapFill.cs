using System;
using System.Collections.Generic;
using System.Linq;

using TimeRenderer.Helpers;
using TimeRenderer.Models;

namespace TimeRenderer.ViewModels;

/// <summary>
/// 未記録の帯を、その時間の手がかりから埋める。
///
/// アプリは「14:00-14:45 に記録が無い」ことも「その時間 Visual Studio を使っていた」ことも
/// 別々に知っていたが、繋がっていなかった。帯の右クリックから内訳を出し、
/// 1回の操作で記録を作れるようにする。
///
/// 手がかりは2つある。<b>使っていたアプリ</b>と<b>作ったコミット</b>で、
/// 前者は「どこで作業していたか」、後者は「何をしていたか」を語る。
/// どちらか片方しか無くても出す。
///
/// 方針:
/// - <b>証拠を見せてから聞く</b>。離席の確認（AwayReviewDialog）と同じ形にしている。
///   勝手に記録を作らないのは、外していたときに嘘の記録が黙って残るのを避けるため
/// - タイトルとカテゴリは<b>過去の実績から推測して既定値に入れるだけ</b>。
///   「このアプリを使っていた時間帯に、自分が実際どう書いていたか」を材料にする
/// - 作った記録は取り消し履歴へ積む。埋め方を間違えても Ctrl+Z で戻せる
/// </summary>
public partial class MainViewModel
{
    /// <summary>この時間以上そのアプリと重なった記録が無ければ、カテゴリの推測はしない</summary>
    private static readonly TimeSpan CategoryGuessMinOverlap = TimeSpan.FromMinutes(30);

    /// <summary>タイトル候補として出す上限（多すぎると選ぶのが仕事になる）</summary>
    private const int MaxAppTitleSuggestions = 8;

    /// <summary>
    /// アプリ使用記録や ToDo に紐づけず、帯の時間幅を初期値にして実績を手入力する。
    /// 通常の編集ダイアログを使うため、タイトル・内容・カテゴリ・プロジェクトコードを自由に指定できる。
    /// </summary>
    public void FillGapManually(UnrecordedGap gap)
    {
        if (gap.EndTime <= gap.StartTime) return;

        var category = RecordingCategory;
        AddViaDialog(new ScheduleItem
        {
            Kind = ScheduleItemKind.Recorded,
            StartTime = gap.StartTime,
            EndTime = gap.EndTime,
            Title = string.Empty,
            ColorCode = category?.ColorCode ?? System.Windows.Media.Brushes.LightBlue.ToString(),
            CategoryId = category?.Id,
            ProjectCodeId = DefaultProjectCode?.Id,
        });
    }

    /// <summary>
    /// 帯の右クリックから呼ばれる入口。
    /// 材料が無ければその旨だけ伝えて何もしない。
    /// </summary>
    public void FillGapFromAppUsage(UnrecordedGap gap)
    {
        var stats = GetAppUsageStats(gap.StartTime, gap.EndTime);

        // コミットはアプリ使用と別の材料なので、片方だけでも出す価値がある。
        // 収集が止まっていた時間帯でも、コミットが残っていれば埋められる
        var commits = GetCommitsBetween(gap.StartTime, gap.EndTime);

        if (stats.Count == 0 && commits.Count == 0)
        {
            _dialogService.ShowMessage(
                "この時間帯の手がかりがありません。\n" +
                "使用アプリは出勤から退勤までの間だけ収集されます（設定でオン/オフできます）。\n" +
                "コミット履歴を使うには、設定でリポジトリを登録してください。\n\n" +
                "右クリックの「ToDo から埋める」か、空白のドラッグで記録を作れます。",
                "この時間の作業から埋める");
            return;
        }

        var suggestion = BuildGapFillSuggestion(stats, commits);

        var result = _dialogService.ShowGapFillDialog(
            gap.StartTime, gap.EndTime, suggestion, [.. Categories], ActiveProjectCodes);

        if (result == null) return;

        CreateItemForGap(gap, result, commits);
    }

    /// <summary>
    /// アプリ使用内訳とコミットから、タイトル・カテゴリ・プロジェクトコードの下書きを組み立てる。
    ///
    /// タイトル候補は<b>コミットが先</b>。アプリ名から思い出すより、
    /// そのとき自分が書いた一文のほうが具体的で、選ぶ手間も少ない。
    /// ただし既定値まで入れ替えるのは、過去の実績から何も学べなかったときだけにする。
    /// 学習結果（そのアプリを使っていた時間帯に実際に付けていたタイトル）は
    /// 帯全体を言い表しているのに対し、コミット1件は帯の一部でしかない。
    /// </summary>
    private GapFillSuggestion BuildGapFillSuggestion(
        List<AppUsageStat> stats, IReadOnlyList<GitCommit> commits)
    {
        // 一番長く使っていたアプリを基準にする。
        // 複数のアプリを行き来していても、何をしていたかは主役のアプリで決まることが多い
        IReadOnlyList<string> titles = [];
        CategoryInfo? category = null;
        if (stats.Count > 0)
        {
            (titles, category) = LearnFromApp(stats[0].ProcessName);
        }

        // 同じ内容のコミットが並ぶこと（分割コミット）はよくあるので、重複は落とす
        var commitTitles = commits
            .Select(c => c.Subject)
            .Distinct(StringComparer.Ordinal)
            .Take(MaxAppTitleSuggestions)
            .ToList();

        var suggestions = new List<string>(commitTitles);
        foreach (var title in titles)
        {
            if (!suggestions.Contains(title, StringComparer.Ordinal)) suggestions.Add(title);
        }

        // 既定値の優先順: 学習したタイトル → コミット → アプリ名 → 空欄
        var defaultTitle = titles.Count > 0 ? titles[0]
            : commitTitles.Count > 0 ? commitTitles[0]
            : stats.Count > 0 ? stats[0].AppName
            : string.Empty;

        // リポジトリに紐づくプロジェクトコードが決まるなら、そちらを既定値にする。
        // 決められない（複数リポジトリにまたがる）場合は普段の既定値のまま
        var projectCode = GuessProjectCodeFromCommits(commits) ?? DefaultProjectCode;

        return new GapFillSuggestion(stats, commits, defaultTitle, suggestions, category, projectCode);
    }

    /// <summary>
    /// そのアプリを使っていた時間帯に、自分が実際どう記録していたかを集める。
    ///
    /// 重みは「重なっていた時間」。件数で数えると、5分の記録も3時間の記録も
    /// 同じ1票になってしまい、たまたま付けた短い記録が上位に来る。
    /// </summary>
    private (IReadOnlyList<string> Titles, CategoryInfo? Category) LearnFromApp(string processName)
    {
        if (string.IsNullOrEmpty(processName)) return ([], null);

        // 日ごとに束ねてから突き合わせる。全件の総当たりだと
        // 使用記録（60日分）×予定（全期間）で効かないほど重くなる
        var byDate = new Dictionary<DateTime, List<AppUsageInterval>>();
        foreach (var interval in _appUsageHistory)
        {
            if (interval.ProcessName != processName) continue;

            var date = interval.Start.Date;
            if (!byDate.TryGetValue(date, out var list))
            {
                list = [];
                byDate[date] = list;
            }
            list.Add(interval);
        }

        if (byDate.Count == 0) return ([], null);

        var titleWeights = new Dictionary<string, long>();
        var categoryWeights = new Dictionary<string, long>();

        foreach (var item in ScheduleItems)
        {
            if (!item.IsRecorded || item.IsAllDay || item.IsVirtual) continue;

            var title = item.Title?.Trim();
            if (string.IsNullOrEmpty(title)) continue;

            // 日をまたぐ記録もあるので、開始日と翌日の両方を見る
            var ticks = OverlapTicks(byDate, item.StartTime.Date, item)
                      + OverlapTicks(byDate, item.StartTime.Date.AddDays(1), item);
            if (ticks <= 0) continue;

            titleWeights[title] = titleWeights.GetValueOrDefault(title) + ticks;

            var category = ResolveCategory(item);
            if (category != null)
            {
                categoryWeights[category.Id] = categoryWeights.GetValueOrDefault(category.Id) + ticks;
            }
        }

        var titles = titleWeights
            .OrderByDescending(kv => kv.Value)
            .Take(MaxAppTitleSuggestions)
            .Select(kv => kv.Key)
            .ToList();

        return (titles, PickCategory(categoryWeights));
    }

    /// <summary>指定日のアプリ使用期間と、記録1件が重なっていた時間（ticks）</summary>
    private static long OverlapTicks(
        Dictionary<DateTime, List<AppUsageInterval>> byDate, DateTime date, ScheduleItem item)
    {
        if (!byDate.TryGetValue(date, out var intervals)) return 0;

        long ticks = 0;
        foreach (var interval in intervals)
        {
            if (interval.End <= item.StartTime || interval.Start >= item.EndTime) continue;

            var start = interval.Start > item.StartTime ? interval.Start : item.StartTime;
            var end = interval.End < item.EndTime ? interval.End : item.EndTime;
            if (end > start) ticks += (end - start).Ticks;
        }
        return ticks;
    }

    /// <summary>
    /// 重みが一番大きいカテゴリを選ぶ。
    /// 材料が薄いうちは推測しない（外した既定値を直す手間のほうが大きいため）。
    /// </summary>
    private CategoryInfo? PickCategory(Dictionary<string, long> weights)
    {
        if (weights.Count == 0) return null;

        var top = weights.OrderByDescending(kv => kv.Value).First();
        if (top.Value < CategoryGuessMinOverlap.Ticks) return null;

        return Categories.FirstOrDefault(c => c.Id == top.Key);
    }

    /// <summary>
    /// 帯の時間幅そのままで記録を作る。
    /// 埋めたことが後から分かるよう、内容にアプリの内訳を残しておく。
    /// </summary>
    private void CreateItemForGap(
        UnrecordedGap gap, GapFillResult result, IReadOnlyList<GitCommit> commits)
    {
        var category = result.Category;

        var item = new ScheduleItem
        {
            Kind = ScheduleItemKind.Recorded,
            Title = result.Title,
            StartTime = gap.StartTime,
            EndTime = gap.EndTime,
            CategoryId = category?.Id,
            ProjectCodeId = result.ProjectCode?.Id ?? DefaultProjectCode?.Id,
            Content = BuildGapFillContent(gap, commits),
        };

        if (category != null) item.ColorCode = category.ColorCode;

        ScheduleItems.Add(item);
        PushEdits([new AddItemEdit(item)], $"「{item.Title}」で記録漏れを埋める");
    }

    /// <summary>
    /// 埋めた根拠を内容欄に残す（あとで見て、実測なのか記憶なのか分かるように）。
    /// コミットはダイアログへ渡したものをそのまま使う。ここで読み直すと、
    /// 確定を押した後にもう一度 git の起動を待たせることになる。
    /// </summary>
    private string BuildGapFillContent(UnrecordedGap gap, IReadOnlyList<GitCommit> commits)
    {
        var lines = new List<string>();

        var stats = GetAppUsageStats(gap.StartTime, gap.EndTime)
            .Take(3)
            .Select(s => $"{s.AppName} {s.DurationText}")
            .ToList();

        if (stats.Count > 0) lines.Add($"使用アプリから復元: {string.Join(" / ", stats)}");

        // コミットは件数が多くなりうるので、根拠として残すのは先頭の数件に絞る
        if (commits.Count > 0)
        {
            var listed = commits.Take(3).Select(c => $"{c.TimeText} {c.Subject}");
            var suffix = commits.Count > 3 ? $" ほか{commits.Count - 3}件" : string.Empty;
            lines.Add($"コミット: {string.Join(" / ", listed)}{suffix}");
        }

        return string.Join("\n", lines);
    }
}
