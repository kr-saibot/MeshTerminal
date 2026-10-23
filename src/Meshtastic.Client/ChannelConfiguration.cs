using System;

namespace Meshtastic.Client
{
    public static class ChannelConfiguration
    {
        public static int Count(MeshProtocol protocol, bool capacityKnown, int reportedCount, int savedIndex = -1)
        {
            if (protocol == MeshProtocol.Meshtastic) return 8;
            // Offline configuration must not inherit the eight-slot legacy default.
            var count = capacityKnown && reportedCount > 0 ? reportedCount : Math.Max(40, reportedCount);
            return Math.Min(256, Math.Max(count, savedIndex + 1));
        }
    }
}
