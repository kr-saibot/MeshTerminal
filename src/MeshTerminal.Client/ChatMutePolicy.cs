using System;
using System.Linq;

namespace MeshTerminal.Client
{
    public static class ChatMutePolicy
    {
        private static MutedChatSettings[] Chats(MeshtasticApplicationSettings settings, MeshProtocol? activeProtocol)
        {
            var protocol = activeProtocol ?? settings.Connection.Protocol;
            return (settings.Alerts.MutedChats ?? new System.Collections.Generic.List<MutedChatSettings>())
                .Where(c => c != null && c.Protocol == protocol).ToArray();
        }
        public static bool IsMuted(MeshtasticApplicationSettings settings, MeshProtocol? activeProtocol, bool direct, uint id)
        {
            return Chats(settings, activeProtocol).Any(c => c.IsDirect == direct && c.Id == id);
        }
        public static int CountUnread(MeshtasticMessageStore store, MeshtasticApplicationSettings settings, MeshProtocol? activeProtocol)
        {
            var muted = Chats(settings, activeProtocol);
            return store.CountNewMessages(settings.Alerts.SuppressMapMessageAlerts,
                muted.Where(c => !c.IsDirect).Select(c => c.Id), muted.Where(c => c.IsDirect).Select(c => c.Id));
        }
    }
}
