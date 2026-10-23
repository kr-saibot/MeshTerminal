using System;
namespace MeshTerminal.Client
{
    public static class MapNodeVisibility
    {
        public static bool IsRecentlyHeard(StoredMeshNode node, bool enabled, double hours, DateTime nowUtc)
        {
            if (!enabled) return true;
            if (node.LastReceivedUtc == DateTime.MinValue || Double.IsNaN(hours) || Double.IsInfinity(hours) || hours <= 0) return false;
            return (nowUtc - node.LastReceivedUtc).TotalHours <= hours;
        }
    }
}
