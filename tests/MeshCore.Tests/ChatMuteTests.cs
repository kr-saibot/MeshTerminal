using System;
using System.IO;
using System.Linq;
using System.Xml.Serialization;
using MeshTerminal.Client;
using Meshtastic.Protobufs;

internal static class ChatMuteTests
{
    private static void Check(bool value, string message) { if (!value) throw new Exception("Chat mute: " + message); }
    public static void Run()
    {
        var path = Path.Combine(Path.GetTempPath(), "chat-mute-" + Guid.NewGuid().ToString("N") + ".db");
        try
        {
            using (var store = MeshtasticMessageStore.CreateSqlite(path))
            {
                store.Initialize();
                store.AddIncoming(new MeshMessage(new MeshPacket { From = 42, To = UInt32.MaxValue, Channel = 2 }, "channel"));
                store.AddIncoming(new MeshMessage(new MeshPacket { From = 42, To = 99 }, "direct"));
                store.AddIncoming(new MeshMessage(new MeshPacket { From = 43, To = 99 }, "other direct"));
                store.AddIncoming(new MeshMessage(new MeshPacket { From = 44, To = UInt32.MaxValue, Channel = 3 }, "///MAP:test"));
                Check(store.CountNewMessages() == 4, "Muting must not mark messages read");
                Check(store.CountNewMessages(false, new uint[] { 2 }, null) == 3, "Channel mute does not suppress direct messages from the same sender");
                Check(store.CountNewMessages(false, null, new uint[] { 42 }) == 3, "Direct mute does not suppress channel messages");
                Check(store.CountNewMessages(true, new uint[] { 2 }, new uint[] { 42 }) == 1, "Mute combines with map suppression and preserves other alerts");
                Check(store.CountNewMessages(false, new uint[] { 2, 3 }, new uint[] { 42, 43 }) == 0, "Only muted unread messages cannot drive blinking or repeated alerts");
                Check(store.CountNewMessages() == 4, "Unmuting restores alert count without changing history");
                var startup = new MeshtasticApplicationSettings();
                startup.Connection.Protocol = MeshProtocol.MeshCore;
                startup.Alerts.MutedChats.Add(new MutedChatSettings { Protocol = MeshProtocol.MeshCore, Id = 2 });
                startup.Alerts.MutedChats.Add(new MutedChatSettings { Protocol = MeshProtocol.MeshCore, IsDirect = true, Id = 42 });
                startup.Alerts.MutedChats.Add(null);
                Check(ChatMutePolicy.CountUnread(store, startup, null) == 2, "Startup with saved mutes works before the device client exists");
                Check(ChatMutePolicy.IsMuted(startup, null, true, 42), "Saved protocol selects direct mute before connection");
                Check(ChatMutePolicy.CountUnread(store, startup, MeshProtocol.Meshtastic) == 4, "An active protocol overrides saved connection settings");
                Check(!ChatMutePolicy.IsMuted(startup, MeshProtocol.Meshtastic, true, 42), "Mutes stay isolated between protocols");
            }
            var settings = new MeshtasticAlertSettings();
            settings.MutedChats.Add(new MutedChatSettings { Protocol = MeshProtocol.MeshCore, IsDirect = true, Id = UInt32.MaxValue });
            settings.MutedChats.Add(new MutedChatSettings { Protocol = MeshProtocol.Meshtastic, Id = 0 });
            var serializer = new XmlSerializer(typeof(MeshtasticAlertSettings));
            using (var text = new StringWriter())
            {
                serializer.Serialize(text, settings);
                using (var reader = new StringReader(text.ToString()))
                {
                    var saved = (MeshtasticAlertSettings)serializer.Deserialize(reader);
                    Check(saved.MutedChats.Count == 2 && saved.MutedChats[0].IsDirect && saved.MutedChats[0].Id == UInt32.MaxValue && saved.MutedChats[0].Protocol == MeshProtocol.MeshCore, "Protocol, chat kind and full ID survive restart");
                }
            }
            using (var reader = new StringReader("<MeshtasticAlertSettings />"))
                Check(((MeshtasticAlertSettings)serializer.Deserialize(reader)).MutedChats.Count == 0, "Old settings start unmuted");
            Console.WriteLine("Chat mute tests passed: independent channel/direct filters, other unread messages, map suppression, unmute and settings compatibility.");
        }
        finally { Microsoft.Data.Sqlite.SqliteConnection.ClearAllPools(); foreach (var file in new[] { path, path + "-wal", path + "-shm" }) if (File.Exists(file)) File.Delete(file); }
    }
}
