using System;
using System.IO;
using System.Linq;
using Meshtastic.Client;
using Meshtastic.Protobufs;
internal static class NodeReceptionTests
{
    internal static void Run()
    {
        var file = Path.Combine(Path.GetTempPath(), "NodeReception-" + Guid.NewGuid().ToString("N") + ".db");
        try
        {
            using (var store = MeshtasticMessageStore.CreateSqlite(file))
            {
                store.Initialize();
                var old = new MeshNode(new NodeInfo { Num = 7, LastHeard = 1700000000, User = new User { Id = "legacy", LongName = "Alice" } });
                store.AddOrUpdateNode(old);
                var snapshot = new MeshNode(old.Value.Clone()) { MeshCoreContactType = 1 };
                if (snapshot.LastHeardUtc.HasValue) throw new Exception("MeshCore uses sender advert timestamp as reception time");
                store.AddOrUpdateNode(snapshot);
                if (store.GetNodes().Single().LastReceivedUtc != DateTime.MinValue) throw new Exception("Legacy advert timestamp was retained as reception time");
                if (store.GetDirectMessageStatistics().Count != 0) throw new Exception("Contact snapshot created a chat history");
                var now = DateTime.UtcNow;
                snapshot.LocalReceivedUtc = now; store.AddOrUpdateNode(snapshot);
                store.AddOrUpdateNode(new MeshNode(old.Value.Clone()) { MeshCoreContactType = 1 });
                if (store.GetNodes().Single().LastReceivedUtc != now) throw new Exception("Contact reload overwrote local reception time");
                store.AddOrUpdateNode(new MeshNode(new NodeInfo { Num = 8, User = new User { Id = "new" } }) { MeshCoreContactType = 1 });
                if (store.GetNodes().Single(n => n.NodeNumber == 8).LastReceivedUtc != DateTime.MinValue) throw new Exception("Imported contact was marked as heard now");
                store.AddOrUpdateNode(new MeshNode(new NodeInfo { Num = 9, User = new User { Id = "channel-name:Alice", LongName = "Alice" } }));
                if (store.GetNodes().Any(n => n.NodeNumber == 9) || !store.GetNodes().Any(n => n.NodeNumber == 7)) throw new Exception("Channel sender helper appears as a duplicate node");
            }
            Console.WriteLine("Node reception tests passed: sender times ignored, legacy correction, persistent local reception and no phantom chats.");
        }
        finally { try { File.Delete(file); } catch (IOException) { } }
    }
}
