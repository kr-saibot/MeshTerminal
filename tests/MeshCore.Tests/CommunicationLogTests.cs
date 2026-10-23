using System;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using ConsoleClient;
using Google.Protobuf;
using Meshtastic.Client;
using Meshtastic.Protobufs;

internal static class CommunicationLogTests
{
    public static void Run()
    {
        var buffer = new CommunicationLogBuffer();
        Check(buffer.Capacity == 500 && !buffer.Paused && buffer.Snapshot().Length == 0, "Default 500-frame in-memory recording");
        buffer.SetCapacity(3);
        for (int i = 0; i < 5; i++) buffer.Add(Frame(i));
        Check(buffer.Snapshot().Select(e => e.Frame.TimestampUtc.Ticks).SequenceEqual(new long[] { 2, 3, 4 }), "FIFO evicts oldest frames");
        buffer.Paused = true; buffer.Add(Frame(5));
        Check(buffer.Snapshot().Last().Frame.TimestampUtc.Ticks == 4, "Paused recording ignores incoming frames");
        buffer.Paused = false; buffer.Add(Frame(6)); buffer.SetCapacity(2);
        Check(buffer.Snapshot().Select(e => e.Frame.TimestampUtc.Ticks).SequenceEqual(new long[] { 4, 6 }), "Resume and shrinking retain newest frames");
        Check(buffer.CopyText().Contains("RAW:") && buffer.CopyText().Contains("DECODED: CURRENT_TIME") && buffer.CopyText().Contains("Timestamp: 6"), "Clipboard export includes raw and decoded frame data");
        buffer.Clear(); Check(buffer.Snapshot().Length == 0, "Clear releases all buffered frames");
        buffer.SetCapacity(500); Parallel.For(0, 2000, i => buffer.Add(Frame(i)));
        Check(buffer.Snapshot().Length == 500, "Concurrent producers stay within capacity");
        var raw = new byte[] { 1, 2 }; var payload = new byte[] { 9, 1, 0, 0, 0 };
        var immutable = new CommunicationFrameEventArgs(DateTime.UtcNow, "RX", MeshProtocol.MeshCore, "TCP", raw, payload);
        raw[0] = 99; payload[1] = 99;
        Check(immutable.RawData[0] == 1 && immutable.Decode().Contains("Timestamp: 1"), "Captured bytes cannot change when transport buffers are reused");
        var message = new ToRadio { Packet = new MeshPacket { Decoded = new Data { Portnum = PortNum.TextMessageApp, Payload = ByteString.CopyFromUtf8("hello world") } } };
        var decoded = new CommunicationFrameEventArgs(DateTime.UtcNow, "TX", MeshProtocol.Meshtastic, "Bluetooth LE", message.ToByteArray(), message.ToByteArray()).Decode();
        Check(decoded.Contains("Text: hello world") && decoded.Contains("packet"), "Meshtastic protocol and application payload decoded");
        var channel = new byte[50]; channel[0] = 18; channel[1] = 39; System.Text.Encoding.UTF8.GetBytes("#test").CopyTo(channel, 2);
        decoded = new CommunicationFrameEventArgs(DateTime.UtcNow, "RX", MeshProtocol.MeshCore, "Serial", channel, channel).Decode();
        Check(decoded.Contains("Channel: 39") && decoded.Contains("Name: #test"), "MeshCore extended channel decoded");
        Check(new CommunicationFrameEventArgs(DateTime.UtcNow, "RX", MeshProtocol.Meshtastic, "TCP", new byte[] { 255 }, new byte[] { 255 }).Decode().StartsWith("Decode error:"), "Malformed frames remain inspectable");
        var self = new byte[64]; self[0] = 5; self[56] = 9; self[57] = 5;
        System.Text.Encoding.UTF8.GetBytes("Radio").CopyTo(self, 58);
        Check(new CommunicationFrameEventArgs(DateTime.UtcNow, "RX", MeshProtocol.MeshCore, "Serial", self, self).Decode().Contains("Name: Radio"), "Own MeshCore device information decoded");
        var telemetry = new byte[] { 0x8B, 0, 1, 2, 3, 4, 5, 6, 1, 116, 1, 159 };
        Check(new CommunicationFrameEventArgs(DateTime.UtcNow, "RX", MeshProtocol.MeshCore, "TCP", telemetry, telemetry).Decode().Contains("Voltage 4.15 V"), "MeshCore CayenneLPP telemetry decoded");
        var file = Path.Combine(Path.GetTempPath(), Guid.NewGuid() + ".xml");
        try
        {
            var settings = new MeshtasticApplicationSettings(); Check(settings.CommunicationLogCapacity == 500, "Default configuration capacity");
            settings.CommunicationLogLoRaOnly = true; settings.CommunicationLogCapacity = 1000; MeshtasticSettingsStore.Save(file, settings);
            Check(MeshtasticSettingsStore.Load(file).CommunicationLogCapacity == 1000 && MeshtasticSettingsStore.Load(file).CommunicationLogLoRaOnly, "Only buffer capacity persists in settings");
            Check(new CommunicationLogBuffer().Snapshot().Length == 0, "A new recording starts empty");
        }
        finally { if (File.Exists(file)) File.Delete(file); }
        Console.WriteLine("Communication log tests passed: FIFO, pause/resume, resize, concurrent recording, copy export, decoders and settings.");
    }
    private static CommunicationFrameEventArgs Frame(int id)
    {
        var payload = new byte[] { 9 }.Concat(BitConverter.GetBytes(id)).ToArray();
        return new CommunicationFrameEventArgs(new DateTime(id, DateTimeKind.Utc), "RX", MeshProtocol.MeshCore, "TCP", new byte[] { 62, 5, 0 }.Concat(payload).ToArray(), payload);
    }
    private static void Check(bool condition, string message) { if (!condition) throw new Exception(message); }
}
