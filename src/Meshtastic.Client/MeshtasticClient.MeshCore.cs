using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Google.Protobuf;
using Meshtastic.Protobufs;

namespace Meshtastic.Client
{
    public sealed partial class MeshtasticClient
    {
        private MeshProtocol _protocol;
        private MeshCoreSession _meshCore;
        private readonly SemaphoreSlim _meshCoreSync = new SemaphoreSlim(1, 1);
        private readonly ConcurrentDictionary<uint, byte[]> _meshCoreKeys = new ConcurrentDictionary<uint, byte[]>();
        private readonly ConcurrentDictionary<string, uint> _meshCoreIdentities = new ConcurrentDictionary<string, uint>();
        private readonly ConcurrentDictionary<uint, MessageDeliveryEventArgs> _meshCoreDeliveries = new ConcurrentDictionary<uint, MessageDeliveryEventArgs>();
                private readonly ConcurrentDictionary<string, DateTime> _meshCoreLastReception = new ConcurrentDictionary<string, DateTime>();
        public event EventHandler<SynchronizationProgressEventArgs> SynchronizationProgress;
        private bool _loadingMeshCoreContacts;
        public bool IsSynchronizingMeshCoreNodes { get { return _loadingMeshCoreContacts; } }
        public bool LastNodeSynchronizationComplete { get; private set; }
        public bool SkipMeshCoreNodeSynchronization() { return _loadingMeshCoreContacts && _meshCore != null && _meshCore.SkipContactTransfer(); }
        private int _loadedMeshCoreContacts, _totalMeshCoreContacts;
        private int _meshCoreSynchronizationAttempt;
        private void ReportMeshCoreSynchronizationProgress(string text) { ReportSynchronizationProgress("Attempt " + _meshCoreSynchronizationAttempt + " - " + text); }
        private HashSet<string> _meshCoreSyncContactKeys;
        private HashSet<string> _meshCoreSyncPassKeys;
        public bool LastMeshCoreContactTransferEnded { get; private set; }
        private string _meshCoreLastSyncNodeName;
        public Func<SynchronizationTimeoutInfo, CancellationToken, Task<bool>> ConfirmSynchronizationRetryAsync { get; set; }
        private void ReportSynchronizationProgress(string text) { Raise(SynchronizationProgress, new SynchronizationProgressEventArgs(text)); }
        private int _meshCoreNextIdentity;
        private int _meshCoreChannelCount = 8;
        private uint _meshCoreLastTimestamp;
        private readonly ConcurrentDictionary<uint, string> _meshCorePositionSamples = new ConcurrentDictionary<uint, string>();
        public event EventHandler<MeshCorePositionEventArgs> MeshCorePositionReceived;

        private void RecordMeshCorePosition(uint number, Position position)
        {
            if (position == null || !position.HasLatitudeI || !position.HasLongitudeI) return;
            var sample = position.ToString();
            string previous;
            if (_meshCorePositionSamples.TryGetValue(number, out previous) && previous == sample) return;
            _meshCorePositionSamples[number] = sample;
            Raise(MeshCorePositionReceived, new MeshCorePositionEventArgs(number, position.Clone(), DateTime.UtcNow));
        }

        public MeshProtocol Protocol
        {
            get { return _protocol; }
            set
            {
                if (State != ConnectionState.Disconnected || _lifetime != null) throw new InvalidOperationException("Disconnect before changing protocol.");
                if (value != MeshProtocol.Meshtastic && value != MeshProtocol.MeshCore) throw new ArgumentOutOfRangeException("value");
                if (_protocol == value) return;
                _protocol = value;
                _meshCoreAdvertSignals.Clear(); _meshCoreAdvertHops.Clear(); _meshCoreLastReception.Clear(); _nodes.Clear(); _channels.Clear(); _meshCoreKeys.Clear(); _meshCoreIdentities.Clear(); _meshCoreDeliveries.Clear();
                MeshMessage ignored; while (_messages.TryDequeue(out ignored)) { }
                Device = new DeviceInfo();
            }
        }

        // The UI supplies its SQLite identity registry. Never truncate a public
        // key to a uint: unrelated contacts could otherwise share a chat history.
        public Func<string, uint> MeshCoreIdentityResolver { get; set; }

        public int ChannelSlotCount { get { return Protocol == MeshProtocol.MeshCore ? _meshCoreChannelCount : 8; } }

        public int GetMaximumTextPayloadBytes(bool channelMessage)
        {
            if (Protocol != MeshProtocol.MeshCore) return MaximumTextPayloadBytes;
            return channelMessage ? Math.Max(0, 160 - Encoding.UTF8.GetByteCount((Device.LocalNode == null ? "" : Device.LocalNode.LongName) ?? "") - 2) : 160;
        }

        public MessageDeliveryEventArgs GetMessageDelivery(uint packetId)
        {
            MessageDeliveryEventArgs value;
            return _meshCoreDeliveries.TryGetValue(packetId, out value) ? value : null;
        }

        private void UpdateMeshCoreDelivery(uint packetId, MessageDeliveryState state)
        {
            var value = new MessageDeliveryEventArgs(packetId, state, Routing.Types.Error.None);
            _meshCoreDeliveries[packetId] = value;
            Raise(MessageDeliveryChanged, value);
        }

        private uint ResolveMeshCoreIdentity(string identity)
        {
            return _meshCoreIdentities.GetOrAdd(identity, delegate(string key)
            {
                var resolver = MeshCoreIdentityResolver;
                return resolver == null ? (uint)Interlocked.Increment(ref _meshCoreNextIdentity) : resolver(key);
            });
        }

        private async Task SynchronizeMeshCoreAsync(CancellationToken token)
        {
            var session = _meshCore;
            if (!IsConnected || session == null) throw new InvalidOperationException("MeshCore is not connected.");
            await _meshCoreSync.WaitAsync(token).ConfigureAwait(false);
            var resumePolling = session.PollMessages;
            session.PollMessages = false;
            try
            {
                await session.CommandAsync(new byte[] { 1, 0, 0, 0, 0, 0, 0, 0 }.Concat(Encoding.UTF8.GetBytes("ConsoleClient")).ToArray(), new byte[] { 5 }, token).ConfigureAwait(false);
                await session.CommandAsync(new byte[] { 22, 3 }, new byte[] { 13 }, token).ConfigureAwait(false);
                ReportSynchronizationProgress("Synchronizing device clock...");
                await SynchronizeMeshCoreClockAsync(session, token).ConfigureAwait(false);
                await session.CommandAsync(new byte[] { 20 }, new byte[] { 12 }, token).ConfigureAwait(false);
                // Only current radio contacts may be used for direct sends. Old
                // histories remain in SQLite, but cannot become stale addresses.
                foreach (var number in _meshCoreKeys.Keys.Where(number => number != Device.MyNode.MyNodeNum).ToArray())
                { byte[] ignored; _meshCoreKeys.TryRemove(number, out ignored); MeshNode node; _nodes.TryRemove(number, out node); }
                ResetMeshCoreContactSnapshot(); LastNodeSynchronizationComplete = false; LastMeshCoreContactTransferEnded = false;
                _loadingMeshCoreContacts = true;
                _meshCoreSynchronizationAttempt = 0;
                _loadedMeshCoreContacts = 0; _meshCoreLastSyncNodeName = null;
                _meshCoreSyncContactKeys = new HashSet<string>(StringComparer.Ordinal);
                while (true)
                {
                    _meshCoreSynchronizationAttempt++;
                    _totalMeshCoreContacts = -1; _meshCoreSyncPassKeys = new HashSet<string>(StringComparer.Ordinal);
                    ReportMeshCoreSynchronizationProgress("Loading nodes: " + _loadedMeshCoreContacts + " received...");
                    var confirm = ConfirmSynchronizationRetryAsync;
                    try
                    {
                        await session.CommandAsync(new byte[] { 4 }, new byte[] { 4 }, token,
                            keepConnectionOnTimeout: confirm != null).ConfigureAwait(false);
                    }
                    catch (NodeSynchronizationSkippedException)
                    {
                        await session.CommandAsync(new byte[] { 1, 0, 0, 0, 0, 0, 0, 0 }.Concat(Encoding.UTF8.GetBytes("ConsoleClient")).ToArray(), new byte[] { 5 }, token).ConfigureAwait(false);
                        break;
                    }
                    catch (TimeoutException)
                    {
                        if (confirm == null) throw;
                        var info = new SynchronizationTimeoutInfo { Attempt = _meshCoreSynchronizationAttempt, ReceivedNodes = _meshCoreSyncPassKeys.Count,
                            ExpectedNodes = _totalMeshCoreContacts, LastNodeName = _meshCoreLastSyncNodeName, Timeout = ConfigurationTimeout };
                        bool retry;
                        try { retry = await confirm(info, token).ConfigureAwait(false); }
                        catch { CloseCurrentTransport(); throw; }
                        if (!retry)
                        {
                            await session.CommandAsync(new byte[] { 1, 0, 0, 0, 0, 0, 0, 0 }.Concat(Encoding.UTF8.GetBytes("ConsoleClient")).ToArray(), new byte[] { 5 }, token).ConfigureAwait(false);
                            break;
                        }
                        token.ThrowIfCancellationRequested();
                        ReportMeshCoreSynchronizationProgress("Restarting node transfer; keeping " + _loadedMeshCoreContacts + " received nodes...");
                        // APP_START explicitly stops the device's previous contact iterator.
                        // Its SELF_INFO reply cannot be confused with trailing contact frames.
                        await session.CommandAsync(new byte[] { 1, 0, 0, 0, 0, 0, 0, 0 }.Concat(Encoding.UTF8.GetBytes("ConsoleClient")).ToArray(),
                            new byte[] { 5 }, token).ConfigureAwait(false);
                        continue;
                    }
                    LastMeshCoreContactTransferEnded = true;
                    var receivedThisPass = _meshCoreSyncPassKeys.Count;
                    foreach (var identity in _meshCoreDeviceContacts.Keys.Where(key => !_meshCoreSyncPassKeys.Contains(key)).ToArray())
                    { byte[] ignored; _meshCoreDeviceContacts.TryRemove(identity, out ignored); }
                    _meshCoreContactsKnown = true;
                    LastNodeSynchronizationComplete = true;
                    Raise(MeshCoreDeviceContactsChanged, EventArgs.Empty);
                    if (_totalMeshCoreContacts != receivedThisPass)
                        Debug("MeshCore end-of-list accepted as complete: received " + receivedThisPass + ", announced " + _totalMeshCoreContacts + ".");
                    ReportMeshCoreSynchronizationProgress("Device finished the contact list (" + receivedThisPass + " / " + _totalMeshCoreContacts + "). Loading channels...");
                    break;
                }
                _loadingMeshCoreContacts = false;
                _channels.Clear();
                for (var index = 0; index < _meshCoreChannelCount; index++)
                {
                    ReportMeshCoreSynchronizationProgress("Loading channels: " + (index + 1) + " / " + _meshCoreChannelCount);
                    await session.CommandAsync(new byte[] { 31, (byte)index }, new byte[] { 18 }, token).ConfigureAwait(false);
                }
                Raise(ChannelsChanged, EventArgs.Empty);
                Raise(ConfigurationReceived, EventArgs.Empty);
            }
            finally { _loadingMeshCoreContacts = false; _meshCoreSyncContactKeys = null; _meshCoreSyncPassKeys = null; session.PollMessages = resumePolling; _meshCoreSync.Release(); }
        }

        private async Task<uint> SendMeshCoreTextAsync(uint destination, string text, int channelIndex, CancellationToken token)
        {
            if (String.IsNullOrWhiteSpace(text)) throw new ArgumentException("Text must not be empty.", "text");
            if (text.IndexOf('\0') >= 0) throw new ArgumentException("MeshCore text must not contain a NUL character.", "text");
            var session = _meshCore;
            if (!IsConnected || session == null || Device.MyNode == null) throw new InvalidOperationException("Connect and synchronize MeshCore first.");
            var direct = destination != UInt32.MaxValue;
            var max = GetMaximumTextPayloadBytes(!direct);
            if (Encoding.UTF8.GetByteCount(text) > max) throw new ArgumentException("MeshCore text is limited to " + max + " UTF-8 bytes for this chat.", "text");
            byte[] publicKey = null;
            if (direct)
            {
                if (!_meshCoreKeys.TryGetValue(destination, out publicKey)) throw new InvalidOperationException("This sender has no known MeshCore contact key. Refresh contacts on the device before starting a direct chat.");
                if (IsMeshCoreContactSaved(Hex(publicKey, 0, 32)) != true) throw new InvalidOperationException("This contact is not stored in the device. Save it to the device first.");
                var prefix = Hex(publicKey, 0, 6);
                if (_meshCoreDeviceContacts.Values.Count(key => Hex(key, 0, 6) == prefix) != 1) throw new InvalidOperationException("Ambiguous MeshCore contact key prefix; message was not sent.");
                MeshNode node;
                if (_nodes.TryGetValue(destination, out node) && node.MeshCoreContactType != 1 && node.MeshCoreContactType != 3) throw new NotSupportedException("Plain chat is available for Companion and Room Server contacts. Repeater commands are not supported.");
                if (node != null && node.MeshCoreContactType == 3)
                {
                    var room = GetMeshCoreRoomSession(destination);
                    if (room == null) throw new InvalidOperationException("Log in to this Room Server before posting.");
                    if (!room.CanPost) throw new InvalidOperationException("This Room Server login permits reading only.");
                    if (Encoding.UTF8.GetByteCount(text) > 151) throw new ArgumentException("Room posts are limited to 151 UTF-8 bytes.", "text");
                }
            }
            else if (channelIndex < 0 || !_channels.ContainsKey(channelIndex)) throw new ArgumentOutOfRangeException("channelIndex", "Select a configured MeshCore channel.");
            var packetId = NextPacketId();
            byte[] command;
            using (var buffer = new MemoryStream())
            using (var writer = new BinaryWriter(buffer, Encoding.UTF8))
            {
                writer.Write((byte)(direct ? 2 : 3)); writer.Write((byte)0); writer.Write((byte)(direct ? 0 : channelIndex));
                // Distinct seconds prevent identical messages from sharing ACK hashes.
                uint timestamp;
                lock (_gate)
                {
                    timestamp = (uint)(DateTime.UtcNow - new DateTime(1970, 1, 1, 0, 0, 0, DateTimeKind.Utc)).TotalSeconds;
                    timestamp = Math.Max(timestamp, _meshCoreLastTimestamp + 1); _meshCoreLastTimestamp = timestamp;
                }
                writer.Write(timestamp);
                if (direct) writer.Write(publicKey, 0, 6);
                writer.Write(Encoding.UTF8.GetBytes(text)); command = buffer.ToArray();
            }
            await session.SendTextAsync(command, direct, packetId, token).ConfigureAwait(false);
            return packetId;
        }

        private void HandleMeshCoreFrame(byte[] frame)
        {
            lock (_gate) { _receivedFrameCount++; _lastReceivedUtc = DateTime.UtcNow; }
            Debug("MeshCore RX type=0x" + frame[0].ToString("X2") + ", length=" + frame.Length);
            switch (frame[0])
            {
                case 5:
                    RequireMeshCoreLength(frame, 58);
                    Device.RadioTxPowerDbm = (sbyte)frame[2];
                    Device.RadioFrequencyMHz = MeshCoreSession.ReadUInt32(frame, 48) / 1000d;
                    Device.RadioBandwidthKHz = MeshCoreSession.ReadUInt32(frame, 52) / 1000d;
                    Device.RadioSpreadingFactor = frame[56]; Device.RadioCodingRate = frame[57];
                    var ownKey = frame.Skip(4).Take(32).ToArray();
                    var ownNumber = ResolveMeshCoreIdentity(Hex(ownKey, 0, 32));
                    Device.MyNode = new MyNodeInfo { MyNodeNum = ownNumber };
                    UpdateMeshCoreNode(ownKey, MeshCoreString(frame, 58, frame.Length - 58), ReadMeshCorePosition(frame, 36), 0, 1);
                    break;
                case 24:
                    if (frame.Length >= 14 && frame[1] == 1) { var noise = unchecked((short)(frame[2] | (frame[3] << 8))); Device.NoiseFloorDbm = noise == 0 ? (int?)null : noise; Raise(DeviceInfoUpdated, EventArgs.Empty); }
                    break;
                case 12:
                    RequireMeshCoreLength(frame, 3);
                    var batteryMillivolts = frame[1] | (frame[2] << 8);
                    Device.MeshCoreBatteryVoltage = batteryMillivolts == 0 || batteryMillivolts == 65535 ? (double?)null : batteryMillivolts / 1000d;
                    Raise(DeviceInfoUpdated, EventArgs.Empty);
                    break;
                case 13:
                    RequireMeshCoreLength(frame, 4);
                    Device.MeshCoreProtocolVersion = frame[1];
                    Device.RoutingHashLengthBytes = frame[1] >= 10 && frame.Length >= 82 && frame[81] <= 2 ? (int?)(frame[81] + 1) : null;
                    _meshCoreChannelCount = frame[3];
                    if (frame.Length >= 60) { Device.HardwareName = MeshCoreString(frame, 20, 40); Device.FirmwareBuildDate = MeshCoreString(frame, 8, 12); }
                    Device.Metadata = new DeviceMetadata { FirmwareVersion = frame.Length >= 80 ? MeshCoreString(frame, 60, 20) : "MeshCore protocol " + frame[1] };
                    Raise(DeviceInfoUpdated, EventArgs.Empty);
                    break;
                case 2:
                    RequireMeshCoreLength(frame, 5);
                    _totalMeshCoreContacts = (int)MeshCoreSession.ReadUInt32(frame, 1);
                    break;
                case 3:
                case 0x8A:
                    RequireMeshCoreLength(frame, 148); RecordMeshCoreContactFrame(frame);
                    if (frame[0] == 0x8A) _meshCoreLastReception[Hex(frame, 1, 32)] = DateTime.UtcNow;
                    UpdateMeshCoreNode(frame.Skip(1).Take(32).ToArray(), MeshCoreString(frame, 100, 32), ReadMeshCorePosition(frame, 136), MeshCoreSession.ReadUInt32(frame, 132), frame[33]);
                    if (frame[0] == 0x8A && _meshCore != null && _lifetime != null) _ = RefreshMeshCoreAdvertPathAsync(_meshCore, frame.Skip(1).Take(32).ToArray(), _lifetime.Token);
                    if (_loadingMeshCoreContacts && frame[0] == 3)
                    {
                        if (_meshCoreSyncContactKeys.Add(Hex(frame, 1, 32))) _loadedMeshCoreContacts++;
                        _meshCoreLastSyncNodeName = MeshCoreString(frame, 100, 32);
                        if (_meshCoreSyncPassKeys.Add(Hex(frame, 1, 32)))
                            ReportMeshCoreSynchronizationProgress("Loading nodes: " + _meshCoreSyncPassKeys.Count + " / " + _totalMeshCoreContacts);
                    }
                    break;
                case 0x80:
                case 0x81:
                    RequireMeshCoreLength(frame, 33);
                    _meshCoreLastReception[Hex(frame, 1, 32)] = DateTime.UtcNow;
                    var refreshSession = _meshCore;
                    var refreshLifetime = _lifetime;
                    if (refreshSession != null && refreshLifetime != null)
                        _ = RefreshMeshCoreContactAsync(refreshSession, frame.Skip(1).Take(32).ToArray(), refreshLifetime.Token);
                    break;
                case 0x8E: HandleMeshCoreDiscoveryResponse(frame); break;
                case 0x88: HandleMeshCoreRxLog(frame); break;
                case 18:
                    RequireMeshCoreLength(frame, 50);
                    var channel = DecodeMeshCoreChannel(frame);
                    if (channel.Role == Channel.Types.Role.Disabled) { MeshChannel removed; _channels.TryRemove(channel.Index, out removed); Raise(ChannelsChanged, EventArgs.Empty); }
                    else UpdateChannel(channel);
                    break;
                case 7:
                case 8:
                case 16:
                case 17:
                    ReceiveMeshCoreMessage(frame);
                    break;
                case 0x8F:
                    RequireMeshCoreLength(frame, 33);
                    var deletedKey = Hex(frame, 1, 32); ForgetMeshCoreDeviceContact(deletedKey);
                    break;
            }
        }

        // Run outside the receive loop: the response must still be read while
        // this request waits for synchronization and the command semaphore.
        private async Task RefreshMeshCoreContactAsync(MeshCoreSession session, byte[] key, CancellationToken token)
        {
            var acquired = false;
            try
            {
                await _meshCoreSync.WaitAsync(token).ConfigureAwait(false);
                acquired = true;
                if (!ReferenceEquals(session, _meshCore) || !IsConnected) return;
                await session.CommandAsync(new byte[] { 30 }.Concat(key).ToArray(), new byte[] { 3 }, token).ConfigureAwait(false);
                await RefreshMeshCoreAdvertPathAsync(session, key, token).ConfigureAwait(false);
            }
            catch (OperationCanceledException) { }
            catch (Exception ex) { Debug("MeshCore contact refresh failed: " + ex.Message); }
            finally { if (acquired) _meshCoreSync.Release(); }
        }

        private void UpdateMeshCoreNode(byte[] key, string name, Position position, uint lastHeard, byte type)
        {
            var identity = Hex(key, 0, 32); var number = ResolveMeshCoreIdentity(identity);
            _meshCoreKeys[number] = key;
            if (position != null && lastHeard != 0) position.Time = lastHeard;
            DateTime receivedUtc; var received = _meshCoreLastReception.TryGetValue(identity, out receivedUtc) ? (DateTime?)receivedUtc : null;
            UpdateNode(new NodeInfo { Num = number, User = new User { Id = identity, LongName = name, ShortName = name.Length > 4 ? name.Substring(0, 4) : name, PublicKey = ByteString.CopyFrom(key) }, Position = position, LastHeard = 0 }, type, received);
            uint advertHops; MeshNode updated;
            if (_meshCoreAdvertHops.TryGetValue(identity, out advertHops) && _nodes.TryGetValue(number, out updated)) { updated.Value.HopsAway = advertHops; Raise(NodeUpdated, new NodeEventArgs(updated)); }
            ApplyMeshCoreAdvertSignal(identity);
            RecordMeshCorePosition(number, position);
        }

        private void ReceiveMeshCoreMessage(byte[] frame)
        {
            var direct = frame[0] == 7 || frame[0] == 16;
            var v3 = frame[0] == 16 || frame[0] == 17;
            var offset = v3 ? 4 : 1;
            RequireMeshCoreLength(frame, offset + (direct ? 12 : 7));
            uint from;
            var channelIndex = 0;
            if (direct)
            {
                var prefix = Hex(frame, offset, 6);
                var matches = _meshCoreKeys.Where(pair => Hex(pair.Value, 0, 6) == prefix).ToArray();
                from = matches.Length == 1 ? matches[0].Key : ResolveMeshCoreIdentity("prefix:" + prefix);
                offset += 6;
            }
            else { channelIndex = frame[offset++]; from = 0; }
            var path = frame[offset++]; var textType = frame[offset++];
            var timestamp = MeshCoreSession.ReadUInt32(frame, offset); offset += 4;
            string authorPrefix = null; string channelSenderName = null;
            if (textType == 2) { RequireMeshCoreLength(frame, offset + 4); authorPrefix = Hex(frame, offset, 4); offset += 4; }
            if (textType != 0 && textType != 2) return; // CLI data is not chat text.
            var text = MeshCoreString(frame, offset, frame.Length - offset);
            if (!direct)
            {
                // Group messages carry a display name, not an authenticated public key.
                // Keep these identities separate from contacts, even for equal names.
                var separator = text.IndexOf(": ", StringComparison.Ordinal);
                var name = separator > 0 ? text.Substring(0, separator) : "Unknown channel sender";
                from = ResolveMeshCoreIdentity("channel-name:" + name);
                channelSenderName = name;
                if (separator > 0) text = text.Substring(separator + 2);
            }
            var packet = new MeshPacket { From = from, To = direct && Device.MyNode != null ? Device.MyNode.MyNodeNum : UInt32.MaxValue, Channel = (uint)channelIndex, RxTime = timestamp, RxSnr = v3 ? (sbyte)frame[1] / 4f : 0, Decoded = new Data { Portnum = PortNum.TextMessageApp, Payload = ByteString.CopyFromUtf8(text) } };
            // Route path length is not Meshtastic's HopsAway; keep it as metadata.
            var message = new MeshMessage(packet, text) { Protocol = MeshProtocol.MeshCore, MeshCorePathLength = path, MeshCoreSnr = v3 ? (float?)((sbyte)frame[1] / 4f) : null, SenderName = channelSenderName };
            if (direct && authorPrefix != null)
            {
                message.MeshCoreSenderPrefix = authorPrefix;
                var authors = _meshCoreKeys.Where(pair => Hex(pair.Value, 0, 4) == authorPrefix).ToArray();
                MeshNode author;
                message.SenderName = authors.Length == 1 && _nodes.TryGetValue(authors[0].Key, out author) ? author.LongName : "Author " + authorPrefix;
            }
            _messages.Enqueue(message);
            MeshMessage excess; while (_messages.Count > MaximumMessageHistory && _messages.TryDequeue(out excess)) { }
            MeshNode signalNode;
                        if (_nodes.TryGetValue(from, out signalNode))
            {
                signalNode.LocalReceivedUtc = DateTime.UtcNow;
                _meshCoreLastReception[signalNode.Id] = signalNode.LocalReceivedUtc.Value;
                if (v3) signalNode.UpdateRadioMetrics(0, packet.RxSnr);
                Raise(NodeUpdated, new NodeEventArgs(signalNode));
            }
            Raise(MessageReceived, new MeshMessageEventArgs(message));
        }

        private static Position ReadMeshCorePosition(byte[] frame, int offset)
        {
            var latitude = (int)MeshCoreSession.ReadUInt32(frame, offset);
            var longitude = (int)MeshCoreSession.ReadUInt32(frame, offset + 4);
            if (latitude == 0 && longitude == 0) return null; // Companion's unset position.
            if (latitude < -90000000 || latitude > 90000000 || longitude < -180000000 || longitude > 180000000) return null;
            return new Position { LatitudeI = latitude * 10, LongitudeI = longitude * 10 };
        }

        private static string MeshCoreString(byte[] frame, int offset, int count)
        {
            RequireMeshCoreLength(frame, offset + count);
            var end = offset; while (end < offset + count && frame[end] != 0) end++;
            return Encoding.UTF8.GetString(frame, offset, end - offset);
        }
        private static string Hex(byte[] frame, int offset, int count) { return BitConverter.ToString(frame, offset, count).Replace("-", "").ToLowerInvariant(); }
        private static void RequireMeshCoreLength(byte[] frame, int length) { if (frame.Length < length) throw new InvalidDataException("Truncated MeshCore frame type 0x" + frame[0].ToString("X2") + "."); }
    }
}
