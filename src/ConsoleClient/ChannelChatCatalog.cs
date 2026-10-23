using System;
using System.Collections.Generic;
using System.Linq;
using Meshtastic.Client;
using Meshtastic.Protobufs;

namespace ConsoleClient
{
    internal sealed class ChannelChatEntry
    {
        internal int Index;
        internal string Name;
        internal int UnreadCount;
        internal bool OnDevice;
    }

    internal static class ChannelChatCatalog
    {
        internal static IList<ChannelChatEntry> Build(IEnumerable<MeshChannel> configured, IEnumerable<ChannelMessageStatistics> history)
        {
            var statistics = history.Where(s => s.ChannelIndex.HasValue).ToDictionary(s => s.ChannelIndex.Value);
            var channels = configured.Where(c => c.Role != Channel.Types.Role.Disabled).ToDictionary(c => c.Index);
            return channels.Keys.Union(statistics.Keys).OrderBy(i => i).Select(index =>
            {
                MeshChannel channel; ChannelMessageStatistics stat;
                channels.TryGetValue(index, out channel); statistics.TryGetValue(index, out stat);
                var name = channel != null ? channel.Name : stat.ChannelName;
                return new ChannelChatEntry { Index = index, OnDevice = channel != null, UnreadCount = stat == null ? 0 : stat.NewCount,
                    Name = String.IsNullOrEmpty(name) ? "Channel " + index : name };
            }).ToList();
        }
    }
}
