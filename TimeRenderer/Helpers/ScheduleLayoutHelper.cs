using TimeRenderer.Models;

namespace TimeRenderer.Helpers;

public static class ScheduleLayoutHelper
{
    public static void CalculateClustersAndAssignColumns(List<ScheduleSegment> sortedSegments)
    {
        if (sortedSegments.Count == 0) return;

        var clusters = new List<List<ScheduleSegment>>();
        List<ScheduleSegment> currentCluster = [sortedSegments[0]];
        var clusterEndTime = sortedSegments[0].EndTime;

        foreach (var segment in sortedSegments.Skip(1))
        {
            if (segment.StartTime < clusterEndTime)
            {
                currentCluster.Add(segment);
                if (segment.EndTime > clusterEndTime)
                {
                    clusterEndTime = segment.EndTime;
                }
            }
            else
            {
                clusters.Add(currentCluster);
                currentCluster = [segment];
                clusterEndTime = segment.EndTime;
            }
        }
        clusters.Add(currentCluster);

        foreach (var cluster in clusters)
        {
            AssignColumnsToCluster(cluster);
        }
    }

    private static void AssignColumnsToCluster(List<ScheduleSegment> cluster)
    {
        var columnEndTimes = new List<DateTime>();

        foreach (var segment in cluster)
        {
            int assignedColumn = columnEndTimes.FindIndex(endTime => endTime <= segment.StartTime);

            if (assignedColumn == -1)
            {
                assignedColumn = columnEndTimes.Count;
                columnEndTimes.Add(segment.EndTime);
            }
            else
            {
                columnEndTimes[assignedColumn] = segment.EndTime;
            }

            segment.ColumnIndex = assignedColumn;
        }

        // MaxColumnIndex = 最大列インデックス（0始まり）
        int maxColumnIndex = columnEndTimes.Count - 1;
        foreach (var segment in cluster)
        {
            segment.MaxColumnIndex = maxColumnIndex;
        }
    }
}
