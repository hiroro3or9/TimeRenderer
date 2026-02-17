using System.Collections.ObjectModel;

namespace TimeRenderer.Helpers
{
    public static class ScheduleLayoutHelper
    {
        public static void CalculateClustersAndAssignColumns(List<ScheduleItem> sortedItems)
        {
            if (sortedItems.Count == 0) return;

            var clusters = new List<List<ScheduleItem>>();
            var currentCluster = new List<ScheduleItem>();
            DateTime clusterEndTime = DateTime.MinValue;

            foreach (var item in sortedItems)
            {
                if (currentCluster.Count == 0)
                {
                    currentCluster.Add(item);
                    clusterEndTime = item.EndTime;
                }
                else
                {
                    if (item.StartTime < clusterEndTime)
                    {
                        currentCluster.Add(item);
                        if (item.EndTime > clusterEndTime) clusterEndTime = item.EndTime;
                    }
                    else
                    {
                        clusters.Add(currentCluster);
                        currentCluster = [item];
                        clusterEndTime = item.EndTime;
                    }
                }
            }
            if (currentCluster.Count > 0) clusters.Add(currentCluster);

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
                int assignedColumn = -1;
                for (int i = 0; i < columnEndTimes.Count; i++)
                {
                    if (columnEndTimes[i] <= item.StartTime)
                    {
                        assignedColumn = i;
                        columnEndTimes[i] = item.EndTime;
                        break;
                    }
                }

                if (assignedColumn == -1)
                {
                    assignedColumn = columnEndTimes.Count;
                    columnEndTimes.Add(item.EndTime);
                }

                item.ColumnIndex = assignedColumn;
            }

            int maxColumn = columnEndTimes.Count;
            foreach (var item in cluster)
            {
                item.MaxColumnIndex = maxColumn;
            }
        }
    }
}
