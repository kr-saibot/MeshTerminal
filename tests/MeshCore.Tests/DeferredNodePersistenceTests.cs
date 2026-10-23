using System;
using System.IO;
using System.Threading;
using ConsoleClient;
using Meshtastic.Client;
using Microsoft.Data.Sqlite;
internal static class DeferredNodePersistenceTests
{
    internal static void Run()
    {
        var alerts = new MeshtasticAlertSettings();
        if (!alerts.EnableSoundCooldown || alerts.SoundCooldownSeconds != 5) throw new Exception("Sound cooldown defaults incorrect");
        using (var queue = new DatabaseWriteQueue())
        using (var done = new ManualResetEventSlim())
        {
            queue.Pause();
            queue.Enqueue(() => done.Set());
            if (done.Wait(100)) throw new Exception("Database write ran during paused synchronization");
            queue.Resume();
            if (!done.Wait(3000)) throw new Exception("Buffered database write did not resume");
        }
        var file = Path.Combine(Path.GetTempPath(), "deferred-identities-" + Guid.NewGuid().ToString("N") + ".db");
        try
        {
            uint first;
            using (var store = MeshtasticMessageStore.CreateSqlite(file))
            {
                store.Initialize(); store.EnableDeferredMeshCoreIdentities();
                first = store.GetOrCreateMeshCoreIdentity("first");
                var second = store.GetOrCreateMeshCoreIdentity("second");
                if (first == second || store.GetOrCreateMeshCoreIdentity("first") != first) throw new Exception("Deferred identities are unstable or collide");
                if (Count(file) != 0) throw new Exception("MeshCore identity was written during reception");
                store.FlushDeferredMeshCoreIdentities();
                if (Count(file) != 2) throw new Exception("Completed synchronization did not persist identities");
                store.FlushDeferredMeshCoreIdentities();
                if (Count(file) != 2) throw new Exception("Identity flush is not idempotent");
            }
            using (var store = MeshtasticMessageStore.CreateSqlite(file))
            {
                store.Initialize(); store.EnableDeferredMeshCoreIdentities();
                if (store.GetOrCreateMeshCoreIdentity("first") != first) throw new Exception("Identity changed after restart");
                if (store.GetOrCreateMeshCoreIdentity("third") <= first) throw new Exception("New identity collides after reload");
            }
            if (Count(file) != 2) throw new Exception("Unflushed identity unexpectedly persisted");
        }
        finally { SqliteConnection.ClearAllPools(); if (File.Exists(file)) File.Delete(file); }
        Console.WriteLine("Deferred node persistence tests passed: paused writes, RAM identities, explicit flush and stable IDs after restart.");
    }
    private static long Count(string file)
    {
        using (var connection = new SqliteConnection("Data Source=" + file))
        {
            connection.Open();
            using (var command = connection.CreateCommand()) { command.CommandText = "SELECT COUNT(*) FROM MeshCoreIdentities"; return Convert.ToInt64(command.ExecuteScalar()); }
        }
    }
}