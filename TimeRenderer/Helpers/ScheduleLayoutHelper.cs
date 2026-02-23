using System.Collections.ObjectModel;

namespace TimeRenderer.Helpers;

public static class ScheduleLayoutHelper
{
    public static void CalculateClustersAndAssignColumns(List<ScheduleItem> sortedItems)
    {
        if (sortedItems.Count == 0) return;

        var clusters = new List<List<ScheduleItem>>();
        List<ScheduleItem> currentCluster = [sortedItems[0]];
        var clusterEndTime = sortedItems[0].EndTime;

        foreach (var item in sortedItems.Skip(1))
        {
            if (item.StartTime < clusterEndTime)
            {
                currentCluster.Add(item);
                if (item.EndTime > clusterEndTime)
                {
                    clusterEndTime = item.EndTime;
                }
            }
            else
            {
                clusters.Add(currentCluster);
                currentCluster = [item];
                clusterEndTime = item.EndTime;
            }
        }
        clusters.Add(currentCluster);

        foreach (var cluster in clusters)
        {
            AssignColumnsToCluster(cluster);
        }
    }

    private static void AssignColumnsToCluster(List<ScheduleItem> cluster)
    {
        var columnEndTimes = new List<DateTime>();

        foreach (var item in cluster)
        {
            int assignedColumn = columnEndTimes.FindIndex(endTime => endTime <= item.StartTime);

            if (assignedColumn == -1)
            {
                assignedColumn = columnEndTimes.Count;
                columnEndTimes.Add(item.EndTime);
            }
            else
            {
                columnEndTimes[assignedColumn] = item.EndTime;
            }

            item.ColumnIndex = assignedColumn;
        }

        // MaxColumnIndex = 最大列インデックス（0始まり）
        int maxColumnIndex = columnEndTimes.Count - 1;
        foreach (var item in cluster)
        {
            item.MaxColumnIndex = maxColumnIndex;
        }
    }
}
