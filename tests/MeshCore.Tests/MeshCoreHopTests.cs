using System;
using System.IO;
using MeshTerminal.Client;
using Meshtastic.Protobufs;
internal static class MeshCoreHopTests
{
    public static void Run()
    {
        Check(MeshtasticClient.DecodeMeshCoreHops(0) == 0, "Direct reception is zero hops");
        Check(MeshtasticClient.DecodeMeshCoreHops(3) == 3, "One-byte hashes");
        Check(MeshtasticClient.DecodeMeshCoreHops(66) == 2, "Two-byte hashes count repeaters rather than bytes");
        Check(MeshtasticClient.DecodeMeshCoreHops(130) == 2, "Three-byte hashes");
        Check(!MeshtasticClient.DecodeMeshCoreHops(255).HasValue, "Fixed route has no received hop count");
        Check(!MeshtasticClient.DecodeMeshCoreHops(127).HasValue, "Oversize path rejected");
        var rxLog = new byte[109]; rxLog[0] = 0x88; rxLog[1] = 244; rxLog[2] = 161; rxLog[3] = 17; rxLog[4] = 66; rxLog[9] = 42;
        var signal = MeshtasticClient.DecodeMeshCoreAdvertSignal(rxLog);
        Check(signal != null && signal.Rssi == -95 && signal.Snr == -3 && signal.Key.StartsWith("2a"), "Advert RX log signed signal values and multi-byte path offset");
        rxLog[4] = 255;
        Check(MeshtasticClient.DecodeMeshCoreAdvertSignal(rxLog) == null, "Malformed RX path ignored");
        var scopedLog = new byte[109]; scopedLog[0] = 0x88; scopedLog[2] = 156; scopedLog[3] = 16; scopedLog[8] = 0; scopedLog[9] = 43;
        Check(MeshtasticClient.DecodeMeshCoreAdvertSignal(scopedLog).Key.StartsWith("2b"), "Scoped advert transport codes skipped");
        var repeater = new MeshNode(new NodeInfo { Num = 20, User = new User { Id = "aabbcc", LongName = "Relay" } }) { MeshCoreContactType = 2 };
        Check(MeshtasticClient.DescribeMeshCorePath(65, new byte[] { 0xaa, 0xbb }, new[] { repeater }, "A", "B").Contains("A -> Relay [aabb] -> B"), "Path identifiers resolve by full encoded hash size");
        Check(MeshtasticClient.DescribeMeshCorePath(255, new byte[0], new[] { repeater }, "A", "B").StartsWith("Not available"), "Unknown route is not direct");
        Check(MeshtasticClient.DescribeMeshCorePath(0, new byte[0], new[] { repeater }, "A", "B").Contains("(direct)"), "Zero-hop path");
        var start = new MeshCorePathStep { Name = "Self", Latitude = 50, Longitude = 8 };
        var end = new MeshCorePathStep { Name = "Target", Latitude = 50.01, Longitude = 8 };
        var steps = MeshtasticClient.DecodeMeshCorePathSteps(0, new byte[0], new[] { repeater }, start, end);
        var vertical = MeshtasticClient.FormatMeshCorePathVertical(steps).Split('\n');
        Check(vertical.Length == 3 && vertical[0] == "Self" && vertical[1].StartsWith("↓  1.1 km") && vertical[2] == "Target", "Direct path vertical order and geographic distance");
        steps = MeshtasticClient.DecodeMeshCorePathSteps(65, new byte[] { 0xaa, 0xbb }, new[] { repeater }, end, start);
        vertical = MeshtasticClient.FormatMeshCorePathVertical(steps).Split('\n');
        Check(vertical.Length == 5 && vertical[0] == "Target" && vertical[1] == "↓" && vertical[2] == "Relay [aabb]" && vertical[3] == "↓" && vertical[4] == "Self", "Incoming direction and no invented distance for missing coordinates");
        var file = Path.Combine(Path.GetTempPath(), "MeshCoreHops-" + Guid.NewGuid().ToString("N") + ".db");
        try
        {
            using (var store = MeshtasticMessageStore.CreateSqlite(file))
            {
                store.Initialize();
                var message = new MeshMessage(new MeshPacket { From = 1, To = UInt32.MaxValue, Channel = 0 }, "Test") { Protocol = MeshProtocol.MeshCore, MeshCorePathLength = 66, MeshCoreSnr = -3 };
                Check(message.MeshCoreHops == 2, "Message hop count");
                store.AddIncoming(message);
                var saved = store.GetChannelMessages(0)[0];
                Check(saved.ReceivedHops == 2 && saved.ReceivedSnr == -3 && !saved.ReceivedRssi.HasValue, "Message values saved explicitly; unavailable RSSI remains unknown");
                saved.RawMetadata = null;
                Check(saved.MeshCoreHops == 2, "Stored hops independent of raw packet text");
                var node = new MeshNode(new NodeInfo { Num = 7, HopsAway = 3, User = new User { Id = "key", LongName = "Alice" } }) { MeshCoreContactType = 1 };
                node.UpdateRadioMetrics(-95, 0);
                store.AddOrUpdateNode(node);
                store.AddOrUpdateNode(new MeshNode(new NodeInfo { Num = 7, User = node.Value.User.Clone() }) { MeshCoreContactType = 1 });
                var restored = store.GetNodes()[0];
                Check(restored.HopsAway == 3 && restored.LastRssi == -95 && restored.LastSnr == 0, "Contact reload preserves hops and signal; zero SNR is valid");
                node.Value.HopsAway = 0;
                store.AddOrUpdateNode(node);
                Check(store.GetNodes()[0].HopsAway == 0, "New direct advert replaces previous hop count with zero");
                Check(store.GetChannelMessages(0).Count == 1 && store.GetChannelMessages(0)[0].MeshCoreHops == 2, "Hop count survives message storage");
            }
            using (var reopened = MeshtasticMessageStore.CreateSqlite(file)) { reopened.Initialize(); Check(reopened.GetNodes()[0].HopsAway == 0 && reopened.GetNodes()[0].LastRssi == -95 && reopened.GetChannelMessages(0)[0].ReceivedHops == 2 && reopened.GetChannelMessages(0)[0].ReceivedSnr == -3, "Node and message radio metadata survive reopening" ); }
        }
        finally { try { if (File.Exists(file)) File.Delete(file); } catch (IOException) { /* SQLite connection pooling may retain the temporary test file until process exit. */ } }
        Console.WriteLine("MeshCore hop tests passed: direct/flood reception, encoded hash sizes, unavailable fixed routes and persisted message metadata.");
    }
    private static void Check(bool value, string description) { if (!value) throw new Exception(description); }
}
