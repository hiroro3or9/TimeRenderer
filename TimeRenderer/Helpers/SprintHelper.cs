using System;
using System.Collections.Generic;
using System.Linq;
using TimeRenderer.Models;

namespace TimeRenderer.Helpers;

/// <summary>
/// スプリントの算出および補間を行うヘルパークラスです。
/// </summary>
public static class SprintHelper
{
    /// <summary>手動スプリントが存在しない場合のデフォルト基準日 (2026年最初の月曜日)</summary>
    public static readonly DateTime DefaultReferenceDate = new(2026, 1, 5);

    /// <summary>
    /// 手動定義されたスプリント情報と自動分割ルール（2週間）に基づいて、指定された期間をカバーするスプリント一覧を生成します。
    /// </summary>
    /// <param name="manualSprints">ユーザーが手動で定義したスプリントのリスト</param>
    /// <param name="fromDate">生成を開始する日付</param>
    /// <param name="toDate">生成を終了する日付</param>
    /// <returns>期間内のスプリントリスト（開始日順）</returns>
    public static List<SprintInfo> GetSprintsForRange(IReadOnlyList<SprintInfo> manualSprints, DateTime fromDate, DateTime toDate)
    {
        var result = new List<SprintInfo>();
        fromDate = fromDate.Date;
        toDate = toDate.Date;

        // 手動スプリントを開始日順にソートして抽出
        var orderedManuals = manualSprints
            .Where(x => x.IsManual)
            .OrderBy(x => x.StartDate.Date)
            .ToList();

        // 基準開始日の決定
        DateTime baseStart;
        if (orderedManuals.Count > 0)
        {
            baseStart = orderedManuals[0].StartDate.Date;
        }
        else
        {
            baseStart = DefaultReferenceDate;
        }

        // 1. 基準スプリントより前の期間の自動生成 (過去方向)
        if (fromDate < baseStart)
        {
            var tempStart = baseStart;
            var prevSprints = new List<SprintInfo>();
            int backwardIndex = 1;

            while (tempStart > fromDate)
            {
                var start = tempStart.AddDays(-14);
                var end = tempStart.AddDays(-1);

                prevSprints.Add(new SprintInfo
                {
                    Name = $"Sprint Pre-{backwardIndex} (自動)",
                    StartDate = start,
                    EndDate = end,
                    IsManual = false
                });

                tempStart = start;
                backwardIndex++;
            }

            prevSprints.Reverse();
            result.AddRange(prevSprints);
        }

        // 2. 基準スプリントから未来方向への生成
        var currentPointer = baseStart;
        int manualIdx = 0;
        int autoSprintNumber = 1;

        while (currentPointer <= toDate || manualIdx < orderedManuals.Count)
        {
            // 次に処理すべき手動スプリントがある場合
            if (orderedManuals.Count > 0 && manualIdx < orderedManuals.Count)
            {
                var manualSprint = orderedManuals[manualIdx];

                if (currentPointer >= manualSprint.StartDate.Date)
                {
                    // 手動スプリントの期間をそのまま採用
                    var start = manualSprint.StartDate.Date;
                    var end = manualSprint.EndDate.Date;

                    result.Add(new SprintInfo
                    {
                        Id = manualSprint.Id,
                        Name = string.IsNullOrWhiteSpace(manualSprint.Name) ? $"Sprint {autoSprintNumber++}" : manualSprint.Name,
                        StartDate = start,
                        EndDate = end,
                        IsManual = true
                    });

                    currentPointer = end.AddDays(1);
                    manualIdx++;
                }
                else
                {
                    // 現在のポインタから次の手動スプリントの開始日までの「隙間」を自動生成で埋める
                    var start = currentPointer;
                    var nextManualStart = manualSprint.StartDate.Date;

                    while (start < nextManualStart)
                    {
                        var end = start.AddDays(13); // 2週間(14日間)
                        
                        // 次の手動スプリントの開始日を超えてしまう場合は、その直前でカットする
                        if (end >= nextManualStart)
                        {
                            end = nextManualStart.AddDays(-1);
                        }

                        result.Add(new SprintInfo
                        {
                            Name = $"Sprint {autoSprintNumber++} (自動)",
                            StartDate = start,
                            EndDate = end,
                            IsManual = false
                        });

                        start = end.AddDays(1);
                    }

                    currentPointer = nextManualStart;
                }
            }
            else
            {
                // 手動スプリントがこれ以上ない場合は、14日間隔で自動生成を続ける
                var start = currentPointer;
                var end = start.AddDays(13);

                result.Add(new SprintInfo
                {
                    Name = $"Sprint {autoSprintNumber++} (自動)",
                    StartDate = start,
                    EndDate = end,
                    IsManual = false
                });

                currentPointer = end.AddDays(1);
            }
        }

        // 重複排除や範囲の最終確認を行いソートして返却
        return [.. result.OrderBy(x => x.StartDate)];
    }

    /// <summary>
    /// 指定された基準日が含まれるスプリントを取得します。
    /// </summary>
    public static SprintInfo GetSprintForDate(IReadOnlyList<SprintInfo> manualSprints, DateTime date)
    {
        // 十分に広い期間でスプリント一覧を生成し、日付が含まれるものを探索
        var fromDate = date.AddMonths(-3);
        var toDate = date.AddMonths(3);
        var sprints = GetSprintsForRange(manualSprints, fromDate, toDate);

        var match = sprints.FirstOrDefault(s => date.Date >= s.StartDate && date.Date <= s.EndDate);
        if (match != null)
        {
            return match;
        }

        // 見つからない場合は、基準日そのものをベースに仮スプリントを作成
        return new SprintInfo
        {
            Name = "Sprint (仮)",
            StartDate = date.Date,
            EndDate = date.Date.AddDays(13),
            IsManual = false
        };
    }
}
