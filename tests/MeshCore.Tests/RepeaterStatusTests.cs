using System;
using System.IO;
using System.Linq;
using MeshTerminal.Client;
internal static class RepeaterStatusTests
{
    private static void Check(bool value, string message) { if (!value) throw new Exception("Repeater status: " + message); }
    internal static byte[] Frame(byte[] prefix)
    {
        var data = new byte[64]; data[0] = 0x87; Array.Copy(prefix, 0, data, 2, 6);
        Action<int, byte[]> put = (offset, bytes) => Array.Copy(bytes, 0, data, offset, bytes.Length);
        put(8, BitConverter.GetBytes((ushort)4123)); put(10, BitConverter.GetBytes((ushort)2));
        put(12, BitConverter.GetBytes((short)-117)); put(14, BitConverter.GetBytes((short)-92));
        put(16, BitConverter.GetBytes(4000000000u)); put(20, BitConverter.GetBytes(45u));
        put(24, BitConverter.GetBytes(123u)); put(28, BitConverter.GetBytes(90061u));
        put(32, BitConverter.GetBytes(3u)); put(36, BitConverter.GetBytes(4u)); put(40, BitConverter.GetBytes(5u)); put(44, BitConverter.GetBytes(6u));
        put(48, BitConverter.GetBytes((ushort)0x8001)); put(50, BitConverter.GetBytes((short)-13));
        put(52, BitConverter.GetBytes((ushort)12)); put(54, BitConverter.GetBytes((ushort)13));
        put(56, BitConverter.GetBytes(456u)); put(60, BitConverter.GetBytes(7u));
        return data;
    }
    internal static void Run()
    {
        var frame = Frame(new byte[6]); var status = MeshCoreRepeaterStatus.Parse(frame, 99);
        Check(status.NodeNumber == 99 && status.BatteryVoltage == 4.123 && status.TxQueueLength == 2, "Identity, voltage and queue");
        Check(status.NoiseFloorDbm == -117 && status.LastRssiDbm == -92 && status.LastSnrDb == -3.25f, "Signed noise/RSSI and quarter-dB SNR");
        Check(status.ReceivedPackets == 4000000000u && status.UptimeSeconds == 90061 && status.ErrorFlags == 0x8001, "Unsigned counters and error bitmask");
        Check(status.DirectDuplicates == 12 && status.FloodDuplicates == 13 && status.RxAirtimeSeconds == 456 && status.ReceiveErrors == 7, "Optional firmware extensions");
        var old = MeshCoreRepeaterStatus.Parse(frame.Take(52).ToArray(), 99);
        Check(old.LastSnrDb == -3.25f && !old.RxAirtimeSeconds.HasValue && !old.DirectDuplicates.HasValue, "Older status layouts leave absent values unavailable");
        var shortReply = MeshCoreRepeaterStatus.Parse(frame.Take(16).ToArray(), 99);
        Check(shortReply.NoiseFloorDbm == -117 && !shortReply.UptimeSeconds.HasValue, "Short supported status exposes only included fields");
        bool rejected = false; try { MeshCoreRepeaterStatus.Parse(frame.Take(15).ToArray(), 99); } catch (InvalidDataException) { rejected = true; }
        Check(rejected, "Truncated core status is rejected");
        Check(status.ToString().Contains("-117 dBm") && status.ToString().Contains("-3.25 dB") && status.ToString().Contains("4000000000"), "Readable status preserves units and full counter values");
        var file = Path.Combine(Path.GetTempPath(), "repeater-status-" + Guid.NewGuid().ToString("N") + ".db");
        try
        {
            using (var store = MeshtasticMessageStore.CreateSqlite(file))
            {
                store.Initialize();
                store.AddPosition(99, new Meshtastic.Protobufs.Position { LatitudeI = 10000000, LongitudeI = 20000000 }, status.ReceivedUtc.AddSeconds(-1));
                store.AddMeshCoreRepeaterStatus(status);
            }
            using (var store = MeshtasticMessageStore.CreateSqlite(file))
            {
                store.Initialize(); var entries = store.GetTelemetry(99); var saved = entries.Single(t => t.Type == "MeshCore status");
                Check(entries.Count == 2 && saved.NodeNumber == 99 && saved.ReceivedAtUtc == status.ReceivedUtc, "Repeater telemetry preserves node identity and PC reception time across restart");
                Check(saved.NoiseFloorDbm == -117 && saved.LastRssiDbm == -92 && saved.LastSnrDb == -3.25 && saved.Voltage == 4.123 && saved.UptimeSeconds == 90061, "Radio values, voltage and uptime are structured stored telemetry");
                Check(saved.RawTelemetry.Contains("4000000000") && saved.RawTelemetry.Contains("0x8001"), "Complete status counters and flags remain in details");
                Check(entries.Single(t => t.Type != "MeshCore status").NoiseFloorDbm == null, "Unrelated telemetry does not invent radio values");
            }
        }
        finally { Microsoft.Data.Sqlite.SqliteConnection.ClearAllPools(); if (File.Exists(file)) File.Delete(file); }
        Console.WriteLine("Repeater status tests passed: field offsets, signed radio metrics, optional extensions, truncation and readable output.");
    }
}
