using System;
using System.Linq;
using MeshTerminal;
using Google.Protobuf;
using MeshTerminal.Client;
using Meshtastic.Protobufs;

internal static class CommunicationLogFilterTests
{
    internal static void Run()
    {
        foreach (byte code in new byte[] { 2, 3, 7, 16, 25, 26, 27, 36, 50, 52, 55, 57, 62, 65 })
            Check(Core("TX", code).IsLoRaRelated(), "MeshCore radio send recognized: " + code);
        foreach (byte code in new byte[] { 1, 4, 5, 6, 8, 9, 10, 11, 12, 13, 14, 15, 20, 22, 29, 30, 31, 32, 37, 38, 40, 41, 42, 56 })
            Check(!Core("TX", code).IsLoRaRelated(), "MeshCore local command excluded: " + code);
        foreach (byte code in new byte[] { 6, 7, 8, 16, 17, 27, 0x80, 0x81, 0x82, 0x84, 0x85, 0x86, 0x87, 0x88, 0x89, 0x8A, 0x8B, 0x8C, 0x8D, 0x8E })
            Check(Core("RX", code).IsLoRaRelated(), "MeshCore radio reception recognized: " + code);
        foreach (byte code in new byte[] { 0, 1, 2, 3, 4, 5, 9, 10, 12, 13, 18, 24, 0x83, 0x8F, 0x90 })
            Check(!Core("RX", code).IsLoRaRelated(), "MeshCore local response excluded: " + code);
        var telemetry = new byte[] { 0x8B, 0 }.Concat(Enumerable.Repeat((byte)0x11, 6)).ToArray();
        Check(!new CommunicationFrameEventArgs(DateTime.UtcNow, "RX", MeshProtocol.MeshCore, "TCP", telemetry, telemetry, 42, new string('1', 64)).IsLoRaRelated(), "Local telemetry response excluded");
        Check(new CommunicationFrameEventArgs(DateTime.UtcNow, "RX", MeshProtocol.MeshCore, "TCP", telemetry, telemetry, 42, new string('0', 64)).IsLoRaRelated(), "Remote telemetry retained");
        var localTelemetry = new byte[] { 39, 0, 0, 0 };
        Check(!new CommunicationFrameEventArgs(DateTime.UtcNow, "TX", MeshProtocol.MeshCore, "TCP", localTelemetry, localTelemetry).IsLoRaRelated(), "Local telemetry request excluded");
        var packet = new MeshPacket { From = 99, To = UInt32.MaxValue, Decoded = new Data { Portnum = PortNum.TextMessageApp, Payload = ByteString.CopyFromUtf8("hello") } };
        Check(Meshtastic("TX", new ToRadio { Packet = packet }.ToByteArray()).IsLoRaRelated(), "Meshtastic LoRa send retained");
        Check(Meshtastic("RX", new FromRadio { Packet = packet }.ToByteArray()).IsLoRaRelated(), "Meshtastic remote reception retained");
        Check(!Meshtastic("TX", new ToRadio { WantConfigId = 123 }.ToByteArray()).IsLoRaRelated(), "Meshtastic config request excluded");
        Check(!Meshtastic("RX", new FromRadio { ConfigCompleteId = 123 }.ToByteArray()).IsLoRaRelated(), "Meshtastic config reply excluded");
        packet.To = 42; packet.From = 42; packet.Decoded.Portnum = PortNum.AdminApp;
        Check(!Meshtastic("TX", new ToRadio { Packet = packet }.ToByteArray()).IsLoRaRelated() && !Meshtastic("RX", new FromRadio { Packet = packet }.ToByteArray()).IsLoRaRelated(), "Own-device administration excluded in both directions");
        packet.To = 99;
        Check(Meshtastic("TX", new ToRadio { Packet = packet }.ToByteArray()).IsLoRaRelated(), "Remote administration retained");
        var buffer = new CommunicationLogBuffer(); buffer.Add(Core("RX", 9)); buffer.Add(Core("RX", 0x88));
        buffer.LoRaOnly = true;
        Check(buffer.Snapshot().Length == 1 && buffer.Snapshot()[0].Frame.Payload[0] == 0x88, "Enabling filter removes buffered local traffic");
        buffer.Add(Core("RX", 12)); buffer.Add(Core("RX", 0x82));
        Check(buffer.Snapshot().Length == 2, "Filtered frames never enter FIFO or clipboard export");
        buffer.LoRaOnly = false; buffer.Add(Core("RX", 9));
        Check(buffer.Snapshot().Length == 3, "Full recording can be restored");
        Console.WriteLine("Communication log filter tests passed: both protocols, local vs remote traffic, telemetry, filtered capture and existing-buffer cleanup.");
    }
    private static CommunicationFrameEventArgs Core(string direction, byte code) { return new CommunicationFrameEventArgs(DateTime.UtcNow, direction, MeshProtocol.MeshCore, "TCP", new[] { code }, new[] { code }); }
    private static CommunicationFrameEventArgs Meshtastic(string direction, byte[] data) { return new CommunicationFrameEventArgs(DateTime.UtcNow, direction, MeshProtocol.Meshtastic, "TCP", data, data, 42); }
    private static void Check(bool condition, string message) { if (!condition) throw new Exception(message); }
}
