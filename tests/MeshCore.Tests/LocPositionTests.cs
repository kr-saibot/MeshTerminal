using System;
using System.IO;
using System.Linq;
using ConsoleClient;
using Meshtastic.Client;
using Meshtastic.Protobufs;

internal static class LocPositionTests
{
    public static void Run()
    {
        Position position;
        Check(LocMessagePosition.TryParse("[LOC]48.34004,12.12808", out position) && position.LatitudeI == 483400400 && position.LongitudeI == 121280800, "LOC coordinates parsed exactly");
        Check(MessageContentParser.Find("[LOC]48.34004,12.12808").Single().Latitude == 48.34004, "LOC message still selectable for Show on map");
        foreach (var valid in new[] { " [loc] -33.12, +151.25 ", "[LOC]0,0", "[LOC]-90,180" })
            Check(LocMessagePosition.TryParse(valid, out position) && position.HasLatitudeI && position.HasLongitudeI, "Valid signed and boundary coordinates");
        foreach (var invalid in new[] { "48.34004,12.12808", "[LOC]91,12", "[LOC]48,-181", "[LOC]NaN,12", "[LOC]48,12 trailing", "[LOC]48,34,12,12", "[LOC]", "test [LOC]48,12" })
            Check(!LocMessagePosition.TryParse(invalid, out position), "Invalid or untagged position rejected");
        var file = Path.Combine(Path.GetTempPath(), "loc-position-" + Guid.NewGuid().ToString("N") + ".db");
        var received = new DateTime(2026, 10, 17, 12, 30, 0, DateTimeKind.Utc);
        try
        {
            LocMessagePosition.TryParse("[LOC]48.34004,12.12808", out position);
            using (var store = MeshtasticMessageStore.CreateSqlite(file))
            {
                store.Initialize(); store.AddPosition(42, position, received);
                var alias = store.GetOrCreateMeshCoreIdentity("channel-name:DM2TK Mobil 4");
                store.AddPosition(alias, position, received);
                var contact = store.GetOrCreateMeshCoreIdentity(new string('a', 64));
                store.AddOrUpdateManualNode(contact, new string('a', 64), "DM2TK Mobil 4");
                Check(store.GetTelemetry(contact).Count == 1 && store.GetTelemetry(alias).Count == 0, "Existing alias position repaired to uniquely named known contact");
                Check(store.ResolveMeshCorePositionNode(alias) == contact, "New LOC positions resolve to known contact");
                store.AddPosition(alias, position, received.AddSeconds(1));
                Check(store.GetTelemetry(contact).Count == 2, "New alias positions stored under contact");
                var duplicate = store.GetOrCreateMeshCoreIdentity(new string('b', 64));
                store.AddOrUpdateManualNode(duplicate, new string('b', 64), "DM2TK Mobil 4");
                Check(store.ResolveMeshCorePositionNode(alias) == alias, "Ambiguous display name must not select a device");
                store.AddPosition(alias, position, received.AddSeconds(2));
                Check(store.GetTelemetry(alias).Count == 1, "Ambiguous positions remain separate");
            }
            using (var store = MeshtasticMessageStore.CreateSqlite(file))
            {
                store.Initialize(); var saved = store.GetTelemetry(42u).Single();
                Check(saved.Type == "Position" && Math.Abs(saved.Latitude.Value - 48.34004) < 0.00000001 && Math.Abs(saved.Longitude.Value - 12.12808) < 0.00000001, "LOC position retained as telemetry for sender after restart");
                Check(saved.ReceivedAtUtc == received, "Local reception time retained");
            }
        }
        finally { Microsoft.Data.Sqlite.SqliteConnection.ClearAllPools(); if (File.Exists(file)) File.Delete(file); }
        Console.WriteLine("LOC position tests passed: coordinate validation, map action, persisted telemetry and reception time.");
    }
    private static void Check(bool condition, string message) { if (!condition) throw new Exception(message); }
}
