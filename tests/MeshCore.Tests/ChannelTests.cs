using System;
using System.IO;
using System.Linq;
using System.Net;
using System.Net.Sockets;
using System.Threading;
using System.Threading.Tasks;
using Google.Protobuf;
using Meshtastic.Client;
using Meshtastic.Protobufs;

internal static class ChannelTests
{
    public static async Task RunAsync()
    {
        using (var radio = new Radio())
        using (var client = new MeshtasticClient { MaximumConnectionAttempts = 1, ConfigurationTimeout = TimeSpan.FromMilliseconds(750) })
        using (var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(25)))
        {
            var communicationFrames = new System.Collections.Concurrent.ConcurrentQueue<CommunicationFrameEventArgs>();
            client.CommunicationFrame += (sender, frame) => communicationFrames.Enqueue(frame);
            await client.ConnectTcpAsync("127.0.0.1", radio.Port, timeout.Token);
            await client.RequestFullStateAsync(timeout.Token);
            Check(communicationFrames.Any(f => f.Direction == "TX" && f.Transport == "TCP" && f.Protocol == MeshProtocol.Meshtastic && f.RawData[0] == 0x94) && communicationFrames.Any(f => f.Direction == "RX"), "Meshtastic TCP traffic emits raw bidirectional communication frames");
            Check(client.Device.LoRaConfig != null && client.Device.LoRaConfig.Bandwidth == 125 && client.Device.LoRaConfig.SpreadFactor == 9 && client.Device.LoRaConfig.CodingRate == 5 && client.Device.LoRaConfig.TxPower == 20 && Math.Abs(client.Device.LoRaConfig.OverrideFrequency - 869.525) < 0.001, "Own Meshtastic radio configuration retained");
            Check(client.Device.NodeName == "Unit Radio" && client.Device.NodeId != client.Device.NodeName, "Status uses long node name");
            client.Device.LocalNode.Value.User.LongName = ""; Check(client.Device.NodeName == "UR", "Short name fallback"); client.Device.LocalNode.Value.User.LongName = "Unit Radio";
            await client.SendAdvertAsync(token: timeout.Token);
            while (radio.AdvertName == null) await Task.Delay(10, timeout.Token);
            Check(radio.AdvertName == "Unit Radio", "Meshtastic node info broadcast");
            var importedUser = new User { LongName = "Imported", ShortName = "IM", PublicKey = ByteString.CopyFrom(Enumerable.Repeat((byte)78, 32).ToArray()) };
            await client.ImportSharedContactAsync(MeshProtocol.Meshtastic, ContactSharing.CreateMeshtasticUri(1234, importedUser), timeout.Token);
            Check(client.Nodes.Single(n => n.Number == 1234).LongName == "Imported" && radio.ImportedContact.NodeNum == 1234, "Meshtastic contact import sends device administration and updates local node");
            var changedUser = importedUser.Clone(); changedUser.PublicKey = ByteString.CopyFrom(Enumerable.Repeat((byte)79, 32).ToArray());
            try { await client.ImportSharedContactAsync(MeshProtocol.Meshtastic, ContactSharing.CreateMeshtasticUri(1234, changedUser), timeout.Token); throw new Exception("Different contact key silently overwritten"); } catch (InvalidOperationException) { }
            var adminBeforeEditor = radio.AdminReads;
            var slots = await client.GetChannelSlotsAsync(timeout.Token);
            Check(radio.AdminReads == adminBeforeEditor, "Opening Meshtastic channel editor does not issue admin reads" );
            Check(slots.Count == 8 && slots[0].Role == Channel.Types.Role.Primary && slots[7].Role == Channel.Types.Role.Disabled, "Meshtastic free slots");
            var edited = slots[1].Clone(); edited.Role = Channel.Types.Role.Secondary;
            edited.Settings.Name = "Test"; edited.Settings.Psk = ByteString.CopyFrom(new byte[32]);
            edited.Settings.UplinkEnabled = true; edited.Settings.DownlinkEnabled = true;
            edited.Settings.ModuleSettings = new ModuleSettings { PositionPrecision = 16, IsMuted = true };
            var actual = await client.SaveChannelAsync(edited, timeout.Token);
            Check(actual.Settings.Name == "Test" && actual.Settings.Id == 123 && actual.Settings.ModuleSettings.PositionPrecision == 16 && actual.Settings.ModuleSettings.IsMuted, "Meshtastic write, readback and untouched fields");
            edited.Role = Channel.Types.Role.Disabled;
            Check((await client.SaveChannelAsync(edited, timeout.Token)).Role == Channel.Types.Role.Disabled, "Meshtastic disable");
            var primary = slots[0].Clone(); primary.Role = Channel.Types.Role.Disabled;
            await RejectedAsync(() => client.SaveChannelAsync(primary, timeout.Token), "Primary cannot be disabled");
            edited.Settings.Name = new string('ä', 6);
            await RejectedAsync(() => client.SaveChannelAsync(edited, timeout.Token), "Meshtastic UTF8 name validation");
            edited.Settings.Name = "Reject"; edited.Role = Channel.Types.Role.Secondary;
            try { await client.SaveChannelAsync(edited, timeout.Token); throw new Exception("Rejected write accepted"); } catch (IOException) { }
            Check(client.Channels.Single(c => c.Index == 1).Role == Channel.Types.Role.Disabled, "Rejected write does not fabricate local success");
            edited.Settings.Name = "Mismatch";
            try { await client.SaveChannelAsync(edited, timeout.Token); throw new Exception("Mismatched readback accepted"); } catch (IOException) { }
            await client.DisconnectAsync();
        }
        using (var radio = new Radio(true))
        using (var client = new MeshtasticClient { MaximumConnectionAttempts = 1, ConfigurationTimeout = TimeSpan.FromMilliseconds(750) })
        using (var deadline = new CancellationTokenSource(TimeSpan.FromSeconds(5)))
        {
            await client.ConnectTcpAsync("127.0.0.1", radio.Port, deadline.Token);
            try { await client.RequestFullStateAsync(deadline.Token); throw new Exception("Stalled synchronization did not time out"); }
            catch (TimeoutException error)
            {
                Check(error.Message.Contains("timed out") && error.Message.Contains("0.75 seconds"), "Synchronization timeout explains inactivity and configured interval");
            }
        }
        using (var radio = new Radio(true))
        using (var client = new MeshtasticClient { MaximumConnectionAttempts = 1 })
        using (var deadline = new CancellationTokenSource(TimeSpan.FromSeconds(5)))
        using (var cancel = new CancellationTokenSource())
        {
            await client.ConnectTcpAsync("127.0.0.1", radio.Port, deadline.Token);
            var request = client.RequestFullStateAsync(cancel.Token);
            cancel.Cancel();
            try { await request; throw new Exception("Synchronization cancellation ignored"); }
            catch (OperationCanceledException) { }
        }
        Console.WriteLine("Meshtastic channel tests passed: slots, session key, write/ACK, readback, preservation, validation and device rejection.");
    }
    private static async Task RejectedAsync(Func<Task> action, string name)
    { try { await action(); throw new Exception("FAILED: " + name); } catch (ArgumentException) { } }
    private static void Check(bool valid, string name) { if (!valid) throw new Exception("FAILED: " + name); }

    private sealed class Radio : IDisposable
    {
        private readonly TcpListener listener = new TcpListener(IPAddress.Loopback, 0);
        private readonly Channel[] channels = Enumerable.Range(0, 8).Select(i => new Channel { Index = i, Role = i == 0 ? Channel.Types.Role.Primary : Channel.Types.Role.Disabled, Settings = new ChannelSettings { Name = i == 0 ? "Primary" : "", Psk = ByteString.CopyFrom(new byte[] { 1 }), Id = 123 } }).ToArray();
        private TcpClient connection;
        private bool stopped;
        public int Port { get; private set; }
        public SharedContact ImportedContact;
        public string AdvertName;
        public int AdminReads;
        private readonly bool stall;
        public Radio(bool stall = false) { this.stall = stall; listener.Start(); Port = ((IPEndPoint)listener.LocalEndpoint).Port; _ = RunAsync(); }
        private async Task RunAsync()
        {
            try
            {
                connection = await listener.AcceptTcpClientAsync();
                var stream = connection.GetStream();
                while (!stopped)
                {
                    var header = await ReadAsync(stream, 4);
                    Check(header[0] == 0x94 && header[1] == 0xC3, "Meshtastic framing");
                    var request = ToRadio.Parser.ParseFrom(await ReadAsync(stream, header[2] * 256 + header[3]));
                    if (request.WantConfigId != 0)
                    {
                        await SendAsync(stream, new FromRadio { MyInfo = new MyNodeInfo { MyNodeNum = 42 } });
                        await SendAsync(stream, new FromRadio { Config = new Config { Lora = new Config.Types.LoRaConfig { UsePreset = false, Bandwidth = 125, SpreadFactor = 9, CodingRate = 5, TxPower = 20, OverrideFrequency = 869.525f } } });
                        await SendAsync(stream, new FromRadio { NodeInfo = new NodeInfo { Num = 42, User = new User { Id = "!0000002a", LongName = "Unit Radio", ShortName = "UR" } } });
                        if (stall) continue;
                        // More than 750 ms in total, with configuration progress every 100 ms.
                        for (var progress = 0; progress < 10; progress++) { await Task.Delay(100); await SendAsync(stream, new FromRadio { Channel = channels[0].Clone() }); }
                        await SendAsync(stream, new FromRadio { ConfigCompleteId = request.WantConfigId });
                    }
                    else if (request.Packet != null)
                    {
                        var packet = request.Packet;
                        if (packet.Decoded.Portnum == PortNum.NodeinfoApp) { Check(packet.To == UInt32.MaxValue && packet.Channel == 0, "Advert broadcast destination and primary channel"); AdvertName = User.Parser.ParseFrom(packet.Decoded.Payload).LongName; continue; }
                        Check(packet.To == 42 && packet.Decoded.Portnum == PortNum.AdminApp, "Only local administration");
                        Check(packet.PkiEncrypted && packet.Decoded.WantResponse && packet.Priority == MeshPacket.Types.Priority.Reliable, "Admin requests follow official PKI and response flags" );
                        var admin = AdminMessage.Parser.ParseFrom(packet.Decoded.Payload);
                        // An unrelated response must not complete the pending request.
                        await ReplyAsync(stream, packet.Id + 1, PortNum.RoutingApp, new Routing { ErrorReason = Routing.Types.Error.None });
                        if (admin.GetChannelRequest != 0) Interlocked.Increment(ref AdminReads);
                        if (admin.GetChannelRequest != 0)
                            await ReplyAsync(stream, packet.Id, PortNum.AdminApp, new AdminMessage { GetChannelResponse = channels[admin.GetChannelRequest - 1].Clone(), SessionPasskey = ByteString.CopyFrom(new byte[] { 9, 8, 7, 6, 5, 4, 3, 2 }) });
                        else if (admin.AddContact != null)
                        {
                            Check(admin.SessionPasskey.Length == 8, "Contact import uses a current session passkey"); ImportedContact = admin.AddContact.Clone();
                            await ReplyAsync(stream, packet.Id, PortNum.RoutingApp, new Routing { ErrorReason = Routing.Types.Error.None });
                        }
                        else if (admin.SetChannel != null)
                        {
                            Check(admin.SessionPasskey.Equals(ByteString.CopyFrom(new byte[] { 9, 8, 7, 6, 5, 4, 3, 2 })), "Fresh session passkey forwarded");
                            var reject = admin.SetChannel.Settings.Name == "Reject";
                            if (!reject && admin.SetChannel.Settings.Name != "Mismatch") channels[admin.SetChannel.Index] = admin.SetChannel.Clone();
                            await ReplyAsync(stream, packet.Id, PortNum.RoutingApp, new Routing { ErrorReason = reject ? Routing.Types.Error.NotAuthorized : Routing.Types.Error.None });
                        }
                    }
                }
            }
            catch (EndOfStreamException) { }
            catch (Exception ex) { if (!stopped) Console.Error.WriteLine("Meshtastic fake radio: " + ex); }
        }
        private static Task ReplyAsync(NetworkStream stream, uint id, PortNum port, IMessage message)
        { return SendAsync(stream, new FromRadio { Packet = new MeshPacket { From = 0, To = 42, Decoded = new Data { RequestId = id, Portnum = port, Payload = message.ToByteString() } } }); }
        private static async Task SendAsync(NetworkStream stream, FromRadio message)
        {
            var bytes = message.ToByteArray();
            await stream.WriteAsync(new byte[] { 0x94, 0xC3, (byte)(bytes.Length >> 8), (byte)bytes.Length }, 0, 4);
            await stream.WriteAsync(bytes, 0, bytes.Length);
        }
        private static async Task<byte[]> ReadAsync(NetworkStream stream, int length)
        {
            var bytes = new byte[length]; var count = 0;
            while (count < length) { var read = await stream.ReadAsync(bytes, count, length - count); if (read == 0) throw new EndOfStreamException(); count += read; }
            return bytes;
        }
        public void Dispose() { stopped = true; if (connection != null) connection.Close(); listener.Stop(); }
    }
}
