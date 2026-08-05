using System;
using System.Collections.Generic;
using System.Linq;

using TimeRenderer.Models;

namespace TimeRenderer.ViewModels;

/// <summary>
/// 見積もりに対する実績の傾向1件分。
/// </summary>
/// <param name="SampleCount">集計に使った ToDo の件数</param>
/// <param name="Ratio">実績 ÷ 見積もり の中央値（1.0 なら見積もり通り）</param>
public sealed record TodoEstimateAccuracy(int SampleCount, double Ratio)
{
    /// <summary>見積もり欄に添える一文</summary>
    public string Display => Ratio switch
    {
        < 0.9 => $"これまでは見積もりの約 {Ratio:0.0} 倍で終わっています（{SampleCount} 件）",
        > 1.1 => $"これまでは見積もりの約 {Ratio:0.0} 倍かかっています（{SampleCount} 件）",
        _ => $"これまではほぼ見積もり通りです（{SampleCount} 件）",
    };

    /// <summary>見積もりを大きく超えがちか（注意を促す色にするかの判定）</summary>
    public bool IsOverrunning => Ratio > 1.1;
}

/// <summary>
/// 完了済みの ToDo から、見積もりに対する実績の傾向を集計したもの。
///
/// 平均ではなく中央値を使う。1件の極端な記録（つけっぱなしで止め忘れた等）で
/// 全体の傾向が引きずられると、添える一文が信用されなくなるため。
/// カテゴリ別に見て、そのカテゴリの標本が足りなければ全体の傾向へ落とす。
/// </summary>
public sealed class TodoEstimateStats
{
    /// <summary>この件数に満たないカテゴリは、傾向として示さない</summary>
    private const int MinSamplesPerCategory = 3;

    private readonly Dictionary<string, TodoEstimateAccuracy> _byCategory;
    private readonly TodoEstimateAccuracy? _overall;

    public static TodoEstimateStats Empty { get; } = new([], null);

    private TodoEstimateStats(Dictionary<string, TodoEstimateAccuracy> byCategory, TodoEstimateAccuracy? overall)
    {
        _byCategory = byCategory;
        _overall = overall;
    }

    /// <summary>
    /// 完了済みの ToDo から集計する。
    /// 見積もりと実績の両方がそろっているものだけが材料になる。
    /// </summary>
    public static TodoEstimateStats Build(IEnumerable<TodoItem> completedTodos)
    {
        var samples = completedTodos
            .Where(t => t.IsCompleted && t.HasEstimate && t.HasRecorded)
            .Select(t => (t.CategoryId, Ratio: t.RecordedDuration.TotalMinutes / t.EstimatedMinutes))
            .ToList();

        if (samples.Count == 0) return Empty;

        var byCategory = new Dictionary<string, TodoEstimateAccuracy>();
        foreach (var group in samples.Where(s => !string.IsNullOrEmpty(s.CategoryId)).GroupBy(s => s.CategoryId!))
        {
            var ratios = group.Select(g => g.Ratio).ToList();
            if (ratios.Count < MinSamplesPerCategory) continue;

            byCategory[group.Key] = new TodoEstimateAccuracy(ratios.Count, Median(ratios));
        }

        var allRatios = samples.Select(s => s.Ratio).ToList();
        var overall = allRatios.Count >= MinSamplesPerCategory
            ? new TodoEstimateAccuracy(allRatios.Count, Median(allRatios))
            : null;

        return new TodoEstimateStats(byCategory, overall);
    }

    /// <summary>
    /// そのカテゴリの傾向を返す。標本が足りなければ全体の傾向、それも無ければ null。
    /// </summary>
    public TodoEstimateAccuracy? For(string? categoryId)
    {
        if (!string.IsNullOrEmpty(categoryId) && _byCategory.TryGetValue(categoryId, out var byCategory))
        {
            return byCategory;
        }
        return _overall;
    }

    private static double Median(List<double> values)
    {
        values.Sort();
        int mid = values.Count / 2;
        return values.Count % 2 == 1
            ? values[mid]
            : (values[mid - 1] + values[mid]) / 2.0;
    }
}
