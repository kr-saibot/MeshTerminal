using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Threading;
using System.Threading.Tasks;
using Google.Protobuf;
using Meshtastic.Client;
using Meshtastic.Protobufs;
internal static class BluetoothChannelTests
{
    internal static async Task RunAsync()
    {
        var fake = new Radio();
        using (var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(20)))
        using (var client = new MeshtasticClient { MaximumConnectionAttempts = 1 })
        {
            Func<ITransport> factory = () => new BluetoothTransport("channel-test", MeshProtocol.Meshtastic, fake);
            var start = typeof(MeshtasticClient).GetMethod("StartAsync", BindingFlags.NonPublic | BindingFlags.Instance);
            await (Task)start.Invoke(client, new object[] { factory, timeout.Token });
            await client.RequestFullStateAsync(timeout.Token);
            var slots = await client.GetChannelSlotsAsync(timeout.Token);
            var edited = slots[1].Clone(); edited.Settings.Name = "BLE-test";
            var saved = await client.SaveChannelAsync(edited, timeout.Token);
            Check(saved.Settings.Name == "BLE-test" && fake.Writes == 1, "Missing BLE ACK is verified by fresh readback, without resending write");
            fake.Reject = true;
            try { await client.SaveChannelAsync(edited, timeout.Token); throw new Exception("Rejected BLE channel write accepted"); } catch (IOException) { }
            fake.Reject = false; fake.Apply = false; edited.Settings.Name = "Mismatch";
            try { await client.SaveChannelAsync(edited, timeout.Token); throw new Exception("Unapplied BLE channel write accepted"); } catch (IOException) { }
            await client.DisconnectAsync();
        }
        Console.WriteLine("Bluetooth channel tests passed: official admin flags, absent ACK with verified readback, device rejection and unapplied settings.");
    }
    private static void Check(bool value, string text) { if (!value) throw new Exception(text); }
    private sealed class Radio : IBleConnection
    {
        public Action<byte[]> Notification { get; set; }
        public Action<Exception> Disconnected { get; set; }
        private readonly Queue<byte[]> queue = new Queue<byte[]>();
        private Channel channel = new Channel { Index = 1, Role = Channel.Types.Role.Secondary, Settings = new ChannelSettings { Name = "Test", Psk = ByteString.CopyFrom(new byte[32]) } };
        internal bool Reject, Apply = true;
        internal int Writes;
        public Task ConnectAsync(string id, MeshProtocol protocol, CancellationToken token) { return Task.CompletedTask; }
        private void Send(FromRadio frame) { lock (queue) queue.Enqueue(frame.ToByteArray()); Notification?.Invoke(new byte[] { 1, 0, 0, 0 }); }
        private void Reply(uint id, PortNum port, IMessage message) { Send(new FromRadio { Packet = new MeshPacket { From = 42, To = 42, Decoded = new Data { RequestId = id, Portnum = port, Payload = message.ToByteString() } } }); }
        public Task WriteAsync(byte[] bytes, CancellationToken token)
        {
            var request = ToRadio.Parser.ParseFrom(bytes);
            if (request.WantConfigId != 0)
            {
                Send(new FromRadio { MyInfo = new MyNodeInfo { MyNodeNum = 42 } });
                Send(new FromRadio { NodeInfo = new NodeInfo { Num = 42, User = new User { Id = "!0000002a", LongName = "BLE Radio" } } });
                Send(new FromRadio { Channel = new Channel { Index = 0, Role = Channel.Types.Role.Primary, Settings = new ChannelSettings { Name = "Primary" } } });
                Send(new FromRadio { Channel = channel.Clone() });
                Send(new FromRadio { ConfigCompleteId = request.WantConfigId });
            }
            else if (request.Packet != null && request.Packet.Decoded.Portnum == PortNum.AdminApp)
            {
                var packet = request.Packet;
                Check(packet.PkiEncrypted && packet.Decoded.WantResponse && packet.Priority == MeshPacket.Types.Priority.Reliable, "BLE admin metadata matches official client");
                var admin = AdminMessage.Parser.ParseFrom(packet.Decoded.Payload);
                if (admin.GetChannelRequest != 0) Reply(packet.Id, PortNum.AdminApp, new AdminMessage { GetChannelResponse = channel.Clone(), SessionPasskey = ByteString.CopyFrom(new byte[8]) });
                else if (admin.SetChannel != null)
                {
                    Writes++;
                    if (Reject) Reply(packet.Id, PortNum.RoutingApp, new Routing { ErrorReason = Routing.Types.Error.NotAuthorized });
                    else if (Apply) channel = admin.SetChannel.Clone(); // Deliberately no ACK.
                }
            }
            return Task.CompletedTask;
        }
        public Task<byte[]> ReadAsync(CancellationToken token) { lock (queue) return Task.FromResult(queue.Count == 0 ? new byte[0] : queue.Dequeue()); }
        public void Dispose() { }
    }
}
