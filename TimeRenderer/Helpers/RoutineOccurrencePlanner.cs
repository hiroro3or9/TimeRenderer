using System;
using System.Collections.Generic;
using System.Linq;

using TimeRenderer.Models;

namespace TimeRenderer.Helpers;

/// <summary>
/// 定期予定から表示用の仮想アイテムを生成する、画面状態に依存しない計画処理。
/// </summary>
public static class RoutineOccurrencePlanner
{
    public const int LookBehindDays = 7;
    public const int LookAheadDays = 60;

    /// <summary>
    /// 指定日を中心とする表示範囲について、まだ存在しない仮想アイテムを返す。
    /// 渡された一覧は変更しない。
    /// </summary>
    public static List<ScheduleItem> BuildVirtualItems(
        IReadOnlyList<RoutineScheduleItem> routines,
        IEnumerable<ScheduleItem> existingItems,
        IReadOnlyList<CategoryInfo> categories,
        DateTime aroundDate)
    {
        ArgumentNullException.ThrowIfNull(routines);
        ArgumentNullException.ThrowIfNull(existingItems);
        ArgumentNullException.ThrowIfNull(categories);

        var windowStart = aroundDate.Date.AddDays(-LookBehindDays);
        var rangeEnd = aroundDate.Date.AddDays(LookAheadDays);

        var existingKeys = existingItems
            .Where(i => i.IsPlanned && i.RoutineId != null)
            .Select(i => (i.RoutineId!, i.StartTime.Date))
            .ToHashSet();

        var categoryColors = categories
            .GroupBy(c => c.Id)
            .ToDictionary(g => g.Key, g => g.First().ColorCode);

        var result = new List<ScheduleItem>();
        foreach (var routine in routines)
        {
            if (!routine.IsEnabled || !routine.IsValidRecurrence) continue;

            var excluded = routine.ExcludedDates.Select(d => d.Date).ToHashSet();
            var rangeStart = routine.StartDate.Date > windowStart
                ? routine.StartDate.Date
                : windowStart;

            for (var date = rangeStart; date <= rangeEnd; date = date.AddDays(1))
            {
                var key = (routine.Id, date);
                if (!routine.OccursOn(date)) continue;
                if (excluded.Contains(date)) continue;
                if (!existingKeys.Add(key)) continue;

                var colorCode = routine.CategoryId != null &&
                                categoryColors.TryGetValue(routine.CategoryId, out var categoryColor)
                    ? categoryColor
                    : routine.ColorCode;

                result.Add(new ScheduleItem
                {
                    Id = $"routine:{routine.Id}:{date:yyyyMMdd}",
                    Kind = ScheduleItemKind.Planned,
                    Title = routine.Title,
                    StartTime = date.Add(routine.StartTime),
                    EndTime = date.Add(routine.EndTime),
                    ColorCode = colorCode,
                    CategoryId = routine.CategoryId,
                    ProjectCodeId = routine.ProjectCodeId,
                    RoutineId = routine.Id,
                    IsVirtual = true,
                });
            }
        }

        return result;
    }
}
