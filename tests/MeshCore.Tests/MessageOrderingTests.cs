using System;
using System.IO;
using System.Linq;
using MeshTerminal.Client;
using Meshtastic.Protobufs;

internal static class MessageOrderingTests
{
    public static void Run()
    {
        var file = Path.Combine(Path.GetTempPath(), "message-order-" + Guid.NewGuid().ToString("N") + ".db");
        try
        {
            using (var store = MeshtasticMessageStore.CreateSqlite(file))
            {
                store.Initialize();
                store.AddIncoming(Incoming("channel first", true, 2000000000));
                store.AddOutgoing(1, 99, UInt32.MaxValue, "sent reply", 0);
                store.AddIncoming(Incoming("channel second", true, 1500000000));
                store.AddIncoming(Incoming("direct first", false, 2000000000));
                store.AddIncoming(Incoming("direct second", false, 1500000000));
                Check(store.GetChannelMessages(0).Select(m => m.Text).SequenceEqual(new[] { "channel first", "sent reply", "channel second" }), "Sender clocks cannot reorder channel messages and own replies");
                Check(store.GetDirectMessages(42).Select(m => m.Text).SequenceEqual(new[] { "direct first", "direct second" }), "Sender clocks cannot reorder direct messages");
                Check(store.GetDirectMessages(42)[0].OccurredUtc > store.GetDirectMessages(42)[1].OccurredUtc, "Original sender timestamps remain available for display");
            }
            // An offline batch can have identical PC write times; the sender time must never break that tie.
            using (var connection = new Microsoft.Data.Sqlite.SqliteConnection("Data Source=" + file))
            {
                connection.Open();
                using (var command = connection.CreateCommand())
                {
                    command.CommandText = "UPDATE MeshtasticMessages SET CreatedUtc='2026-10-14T12:00:00.0000000Z'";
                    command.ExecuteNonQuery();
                }
            }
            using (var store = MeshtasticMessageStore.CreateSqlite(file))
            {
                store.Initialize();
                Check(store.GetChannelMessages().Select(m => m.Text).SequenceEqual(new[] { "channel first", "sent reply", "channel second" }), "Equal database times preserve insertion order after reopening");
                Check(store.GetRecentChannelMessages(0, 2).Select(m => m.Text).SequenceEqual(new[] { "sent reply", "channel second" }), "Telnet history uses the same stable order");
                Check(store.GetDirectMessages(42).Select(m => m.Text).SequenceEqual(new[] { "direct first", "direct second" }), "Offline direct batch retains FIFO order with identical write times");
            }
        }
        finally { Microsoft.Data.Sqlite.SqliteConnection.ClearAllPools(); if (File.Exists(file)) File.Delete(file); }
        Console.WriteLine("Message ordering tests passed: sender clock errors, own replies, offline batches, equal write times, reopen and Telnet history.");
    }
    private static MeshMessage Incoming(string text, bool channel, uint timestamp)
    {
        return new MeshMessage(new MeshPacket { From = 42, To = channel ? UInt32.MaxValue : 99, Channel = 0, RxTime = timestamp }, text) { Protocol = MeshProtocol.MeshCore };
    }
    private static void Check(bool value, string name) { if (!value) throw new Exception("FAILED: " + name); }
}