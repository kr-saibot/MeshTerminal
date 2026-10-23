using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Meshtastic.Client;
internal static class BluetoothTests
{
    internal static async Task RunAsync()
    {
        await LinuxBleDbusTests.RunAsync();
        foreach (var protocol in new[] { MeshProtocol.MeshCore, MeshProtocol.Meshtastic })
        {
            using (var deadline = new CancellationTokenSource(TimeSpan.FromSeconds(5)))
            {
                var fake = new FakeBle();
                using (var transport = new BluetoothTransport("test", protocol, fake))
                {
                    await transport.OpenAsync(deadline.Token);
                    var payload = new byte[] { 7, 8, 9 };
                    var outbound = protocol == MeshProtocol.MeshCore ? new byte[] { 60, 3, 0, 7, 8, 9 } : new byte[] { 148, 195, 0, 3, 7, 8, 9 };
                    await transport.Stream.WriteAsync(outbound, 0, outbound.Length, deadline.Token);
                    Check(fake.LastWrite.SequenceEqual(payload), "BLE writes raw payload, without serial framing");
                    if (protocol == MeshProtocol.MeshCore) fake.Notification(payload);
                    else { lock (fake.Queue) { fake.Queue.Enqueue(payload); fake.Queue.Enqueue(new byte[] { 10 }); } fake.Notification(new byte[] { 1, 0, 0, 0 }); }
                    var expected = protocol == MeshProtocol.MeshCore ? new byte[] { 62, 3, 0, 7, 8, 9 } : outbound;
                    var received = new List<byte>();
                    while (received.Count < expected.Length) { var buffer = new byte[2]; var count = await transport.Stream.ReadAsync(buffer, 0, buffer.Length, deadline.Token); received.AddRange(buffer.Take(count)); }
                    Check(received.SequenceEqual(expected), "BLE packets enter protocol stream across partial reads");
                    if (protocol == MeshProtocol.Meshtastic)
                    {
                        var buffer = new byte[5]; var offset = 0;
                        while (offset < buffer.Length) offset += await transport.Stream.ReadAsync(buffer, offset, buffer.Length - offset, deadline.Token);
                        Check(buffer.SequenceEqual(new byte[] { 148, 195, 0, 1, 10 }), "Meshtastic drains all FromRadio packets after FromNum");
                    }
                    using (var cancellation = new CancellationTokenSource())
                    {
                        var pending = transport.Stream.ReadAsync(new byte[1], 0, 1, cancellation.Token); cancellation.Cancel();
                        try { await pending; throw new Exception("Read cancellation missing"); } catch (OperationCanceledException) { }
                    }
                    var waiting = transport.Stream.ReadAsync(new byte[1], 0, 1, deadline.Token);
                    fake.Disconnected(new IOException("Test disconnect"));
                    try { await waiting; throw new Exception("Disconnect missing"); } catch (IOException) { }
                    Check(fake.Disposed, "Disconnect closes BLE resources");
                }
            }
        }
        var settings = new MeshtasticApplicationSettings();
        settings.Alerts.EnableSoundCooldown = false; settings.Alerts.SoundCooldownSeconds = 12;
        settings.Connection.Transport = MeshtasticTransportType.Bluetooth; settings.Connection.BluetoothDeviceId = "AA:BB:CC:DD:EE:FF";
        var file = Path.Combine(Path.GetTempPath(), "ble-settings-" + Guid.NewGuid().ToString("N") + ".xml");
        try { MeshtasticSettingsStore.Save(file, settings); var loaded = MeshtasticSettingsStore.Load(file); Check(!loaded.Alerts.EnableSoundCooldown && loaded.Alerts.SoundCooldownSeconds == 12, "Sound cooldown settings roundtrip"); Check(loaded.Connection.Transport == MeshtasticTransportType.Bluetooth && loaded.Connection.BluetoothDeviceId == settings.Connection.BluetoothDeviceId, "Bluetooth settings roundtrip"); }
        finally { File.Delete(file); }
        Console.WriteLine("Bluetooth packet transport tests passed (MeshCore and Meshtastic).");
    }
    private static void Check(bool result, string name) { if (!result) throw new Exception(name); }
    private sealed class FakeBle : IBleConnection
    {
        public Action<byte[]> Notification { get; set; }
        public Action<Exception> Disconnected { get; set; }
        internal readonly Queue<byte[]> Queue = new Queue<byte[]>();
        internal byte[] LastWrite; internal bool Disposed;
        public Task ConnectAsync(string id, MeshProtocol protocol, CancellationToken token) { return Task.CompletedTask; }
        public Task WriteAsync(byte[] bytes, CancellationToken token) { LastWrite = bytes; return Task.CompletedTask; }
        public Task<byte[]> ReadAsync(CancellationToken token) { lock (Queue) return Task.FromResult(Queue.Count == 0 ? new byte[0] : Queue.Dequeue()); }
        public void Dispose() { Disposed = true; }
    }
}
