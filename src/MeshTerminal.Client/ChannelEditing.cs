using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Google.Protobuf;
using Meshtastic.Protobufs;

namespace MeshTerminal.Client
{
    public sealed partial class MeshtasticClient
    {
        private readonly SemaphoreSlim _channelEditing = new SemaphoreSlim(1, 1);

        public async Task<IReadOnlyList<Channel>> GetChannelSlotsAsync(CancellationToken token = default(CancellationToken))
        {
            RequireChannelConnection();
            await _channelEditing.WaitAsync(token).ConfigureAwait(false);
            try
            {
                // Meshtastic configuration already supplies the channel slots.
                // Opening the editor must not depend on eight additional admin RPCs.
                if (Protocol == MeshProtocol.Meshtastic)
                {
                    var snapshot = Channels.ToDictionary(channel => channel.Index, channel => channel.Value.Clone());
                    if (snapshot.Count == 0) throw new InvalidOperationException("Load the device configuration first (Nodes > Refresh from device).");
                    return Enumerable.Range(0, 8).Select(index => snapshot.ContainsKey(index) ? snapshot[index] : new Channel { Index = index, Role = Channel.Types.Role.Disabled, Settings = new ChannelSettings() }).ToList();
                }
                var slots = new List<Channel>();
                for (var index = 0; index < (Protocol == MeshProtocol.MeshCore ? _meshCoreChannelCount : 8); index++)
                    slots.Add(await ReadChannelSlotAsync(index, token).ConfigureAwait(false));
                return slots;
            }
            finally { _channelEditing.Release(); }
        }

        public async Task<Channel> SaveChannelAsync(Channel edited, CancellationToken token = default(CancellationToken))
        {
            RequireChannelConnection();
            if (edited == null) throw new ArgumentNullException("edited");
            var desired = edited.Clone();
            var core = Protocol == MeshProtocol.MeshCore;
            if (desired.Index < 0 || desired.Index >= (core ? _meshCoreChannelCount : 8)) throw new ArgumentOutOfRangeException("edited");
            if (desired.Settings == null) desired.Settings = new ChannelSettings();
            var name = desired.Settings.Name;
            var key = desired.Settings.Psk;
            if (name.IndexOf('\0') >= 0 || Encoding.UTF8.GetByteCount(name) > (core ? 31 : 11))
                throw new ArgumentException("Channel name must fit in " + (core ? 31 : 11) + " UTF-8 bytes and contain no NUL.");
            if (core && desired.Role != Channel.Types.Role.Disabled && (String.IsNullOrWhiteSpace(name) || key.Length != 16))
                throw new ArgumentException("MeshCore channels require a name and a 16-byte key.");
            if (!core && key.Length != 0 && key.Length != 1 && key.Length != 16 && key.Length != 32)
                throw new ArgumentException("Meshtastic keys must contain 0, 1, 16 or 32 bytes.");
            if (desired.Role != Channel.Types.Role.Primary && desired.Role != Channel.Types.Role.Secondary && desired.Role != Channel.Types.Role.Disabled)
                throw new ArgumentException("Invalid channel role.");
            if (!core && desired.Settings.ModuleSettings != null && desired.Settings.ModuleSettings.PositionPrecision > 32)
                throw new ArgumentException("Position precision must be between 0 and 32.");
            await _channelEditing.WaitAsync(token).ConfigureAwait(false);
            try
            {
                if (core)
                {
                    var command = new byte[50]; command[0] = 32; command[1] = (byte)desired.Index;
                    if (desired.Role != Channel.Types.Role.Disabled)
                    {
                        Encoding.UTF8.GetBytes(name).CopyTo(command, 2);
                        key.ToByteArray().CopyTo(command, 34);
                    }
                    await _meshCore.CommandAsync(command, new byte[] { 0 }, token).ConfigureAwait(false);
                }
                else
                {
                    // Read again before writing to preserve fields not exposed by the editor.
                    var response = await RequestLocalAdminAsync(new AdminMessage { GetChannelRequest = (uint)desired.Index + 1 }, false, token).ConfigureAwait(false);
                    var current = response.GetChannelResponse;
                    if (current == null || current.Index != desired.Index) throw new InvalidDataException("Unexpected channel reply.");
                    if ((current.Role == Channel.Types.Role.Primary) != (desired.Role == Channel.Types.Role.Primary))
                        throw new ArgumentException("Keep the existing primary channel. Other slots can be enabled as secondary channels.");
                    if (current.Settings == null) current.Settings = new ChannelSettings();
                    current.Role = desired.Role;
                    current.Settings.Name = name; current.Settings.Psk = key;
                    current.Settings.UplinkEnabled = desired.Settings.UplinkEnabled;
                    current.Settings.DownlinkEnabled = desired.Settings.DownlinkEnabled;
                    if (current.Settings.ModuleSettings == null) current.Settings.ModuleSettings = new ModuleSettings();
                    if (desired.Settings.ModuleSettings != null)
                    {
                        current.Settings.ModuleSettings.PositionPrecision = desired.Settings.ModuleSettings.PositionPrecision;
                        current.Settings.ModuleSettings.IsMuted = desired.Settings.ModuleSettings.IsMuted;
                    }
                    try { await RequestLocalAdminAsync(new AdminMessage { SetChannel = current, SessionPasskey = response.SessionPasskey }, true, token).ConfigureAwait(false); }
                    catch (TimeoutException) when (!token.IsCancellationRequested && IsConnected)
                    {
                        // Local BLE firmware may omit a standalone write ACK.
                        // The subsequent fresh readback remains mandatory.
                        Debug("Channel write ACK absent; verifying actual device settings by readback.");
                    }
                }
                var actual = await ReadChannelSlotAsync(desired.Index, token).ConfigureAwait(false);
                var disabled = desired.Role == Channel.Types.Role.Disabled;
                if ((actual.Role == Channel.Types.Role.Disabled) != disabled || (!disabled &&
                    (actual.Settings.Name != name || !actual.Settings.Psk.Equals(key) || (!core &&
                    (actual.Role != desired.Role || actual.Settings.UplinkEnabled != desired.Settings.UplinkEnabled || actual.Settings.DownlinkEnabled != desired.Settings.DownlinkEnabled || (desired.Settings.ModuleSettings != null && (actual.Settings.ModuleSettings == null || actual.Settings.ModuleSettings.PositionPrecision != desired.Settings.ModuleSettings.PositionPrecision || actual.Settings.ModuleSettings.IsMuted != desired.Settings.ModuleSettings.IsMuted)))))))
                    throw new IOException("The device returned different channel settings. Reload the channels before trying again.");
                return actual;
            }
            finally { _channelEditing.Release(); }
        }

        private void RequireChannelConnection()
        {
            if (!IsConnected || Device.MyNode == null) throw new InvalidOperationException("Connect and synchronize the local device first.");
        }

        private async Task<Channel> ReadChannelSlotAsync(int index, CancellationToken token)
        {
            RequireChannelConnection();
            if (Protocol == MeshProtocol.MeshCore)
            {
                var frame = await _meshCore.CommandAsync(new byte[] { 31, (byte)index }, new byte[] { 18 }, token).ConfigureAwait(false);
                RequireMeshCoreLength(frame, 50);
                if (frame[1] != index) throw new InvalidDataException("Unexpected channel slot.");
                return DecodeMeshCoreChannel(frame);
            }
            var response = await RequestLocalAdminAsync(new AdminMessage { GetChannelRequest = (uint)index + 1 }, false, token).ConfigureAwait(false);
            var channel = response.GetChannelResponse;
            if (channel == null || channel.Index != index) throw new InvalidDataException("Unexpected channel reply.");
            UpdateChannel(channel.Clone());
            return channel.Clone();
        }

        private Channel DecodeMeshCoreChannel(byte[] frame)
        {
            var name = MeshCoreString(frame, 2, 32);
            var key = ByteString.CopyFrom(frame, 34, 16);
            var enabled = name.Length != 0 || key.Any(value => value != 0);
            return new Channel { Index = frame[1], Role = enabled ? (frame[1] == 0 ? Channel.Types.Role.Primary : Channel.Types.Role.Secondary) : Channel.Types.Role.Disabled,
                Settings = new ChannelSettings { Name = name, Psk = key } };
        }

        private async Task<AdminMessage> RequestLocalAdminAsync(AdminMessage message, bool acknowledgment, CancellationToken token)
        {
            RequireChannelConnection();
            var destination = Device.MyNode.MyNodeNum;
            var id = NextPacketId();
            var completion = new TaskCompletionSource<AdminMessage>(TaskCreationOptions.RunContinuationsAsynchronously);
            EventHandler<PacketEventArgs> handler = delegate(object sender, PacketEventArgs e)
            {
                var packet = e.Packet;
                if ((packet.From != destination && packet.From != 0) || packet.Decoded == null || packet.Decoded.RequestId != id) return;
                try
                {
                    if (packet.Decoded.Portnum == PortNum.RoutingApp)
                    {
                        var routing = Routing.Parser.ParseFrom(packet.Decoded.Payload);
                        if (routing.ErrorReason != Routing.Types.Error.None) completion.TrySetException(new IOException("Device rejected channel operation: " + routing.ErrorReason));
                        else if (acknowledgment) completion.TrySetResult(new AdminMessage());
                    }
                    else if (!acknowledgment && packet.Decoded.Portnum == PortNum.AdminApp)
                        completion.TrySetResult(AdminMessage.Parser.ParseFrom(packet.Decoded.Payload));
                }
                catch (Exception ex) { completion.TrySetException(ex); }
            };
            EventHandler<ConnectionStateChangedEventArgs> disconnected = delegate(object sender, ConnectionStateChangedEventArgs e)
            {
                if (e.State != ConnectionState.Connected) completion.TrySetException(new IOException("Connection changed before channel operation was confirmed. Reload channels after reconnecting."));
            };
            PacketReceived += handler; ConnectionStateChanged += disconnected;
            try
            {
                using (var timeout = CancellationTokenSource.CreateLinkedTokenSource(token))
                {
                    timeout.CancelAfter(TimeSpan.FromSeconds(acknowledgment ? 2 : 10));
                    using (timeout.Token.Register(() => completion.TrySetCanceled()))
                    {
                        await SendAsync(new ToRadio { Packet = new MeshPacket { Id = id, To = destination, WantAck = true, PkiEncrypted = true, Priority = MeshPacket.Types.Priority.Reliable,
                            Decoded = new Data { Portnum = PortNum.AdminApp, WantResponse = true, Payload = message.ToByteString() } } }, timeout.Token).ConfigureAwait(false);
                        try { return await completion.Task.ConfigureAwait(false); }
                        catch (OperationCanceledException) when (!token.IsCancellationRequested) { throw new TimeoutException("Channel admin " + message.PayloadVariantCase + " over " + ConnectionInfo.Transport + " timed out. Reload channels to check their current state."); }
                    }
                }
            }
            finally { PacketReceived -= handler; ConnectionStateChanged -= disconnected; }
        }
    }
}
