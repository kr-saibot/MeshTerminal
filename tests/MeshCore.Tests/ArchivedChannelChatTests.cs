using System;
using System.IO;
using System.Linq;
using ConsoleClient;
using Meshtastic.Client;
using Meshtastic.Protobufs;

internal static class ArchivedChannelChatTests
{
    public static void Run()
    {
        var file = Path.Combine(Path.GetTempPath(), "archived-chat-" + Guid.NewGuid().ToString("N") + ".db");
        try
        {
            var configured = new[] { new MeshChannel(new Channel { Index = 0, Role = Channel.Types.Role.Primary, Settings = new ChannelSettings { Name = "Public" } }) };
            using (var store = MeshtasticMessageStore.CreateSqlite(file))
            {
                store.Initialize();
                store.AddIncoming(new MeshMessage(new MeshPacket { From = 42, To = UInt32.MaxValue, Channel = 2 }, "archived channel message"));
                store.AddIncoming(new MeshMessage(new MeshPacket { From = 43, To = UInt32.MaxValue, Channel = 0 }, "current channel message"));
                store.DeleteChannelMessages(0);
                var chats = ChannelChatCatalog.Build(configured, store.GetChannelMessageStatistics());
                Check(chats.Count == 2 && chats.Single(c => c.Index == 0).OnDevice, "Current configured channel remains listed after clearing messages");
                var archived = chats.Single(c => c.Index == 2);
                Check(!archived.OnDevice && archived.UnreadCount == 1 && archived.Name == "Channel 2", "Unread chat from removed device channel stays visible without stored name");
                Check(chats.Sum(c => c.UnreadCount) == store.CountNewMessages(), "No unread channel message is invisible to the chat list");
                store.MarkChannelMessagesRead(2);
                Check(store.CountNewMessages() == 0 && ChannelChatCatalog.Build(configured, store.GetChannelMessageStatistics()).Single(c => c.Index == 2).UnreadCount == 0, "Reading archived chat clears unread alert condition");
            }
            using (var store = MeshtasticMessageStore.CreateSqlite(file))
            {
                store.Initialize();
                Check(ChannelChatCatalog.Build(new MeshChannel[0], store.GetChannelMessageStatistics()).Count == 1, "Stored chats remain accessible offline after restart");
                store.DeleteChannelMessages(2);
                Check(store.CountNewMessages() == 0 && ChannelChatCatalog.Build(configured, store.GetChannelMessageStatistics()).Count == 1, "Deleting archived chat removes it and clears alert condition");
                store.AddIncoming(new MeshMessage(new MeshPacket { From = 44, To = UInt32.MaxValue, Channel = 2 }, "restored channel"));
                var restored = configured.Concat(new[] { new MeshChannel(new Channel { Index = 2, Role = Channel.Types.Role.Secondary, Settings = new ChannelSettings { Name = "Restored" } }) });
                var chats = ChannelChatCatalog.Build(restored, store.GetChannelMessageStatistics());
                Check(chats.Count(c => c.Index == 2) == 1 && chats.Single(c => c.Index == 2).OnDevice && chats.Single(c => c.Index == 2).Name == "Restored", "Reconfigured channel merges with history instead of duplicating chat");
            }
        }
        finally { Microsoft.Data.Sqlite.SqliteConnection.ClearAllPools(); if (File.Exists(file)) File.Delete(file); }
        Console.WriteLine("Archived channel chat tests passed: hidden unread message regression, read/delete, offline restart and restored channels.");
    }
    private static void Check(bool condition, string message) { if (!condition) throw new Exception(message); }
}
