using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net;
using System.Net.Sockets;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Meshtastic.Client;

internal static class Program
{
    private static int Main(string[] args)
    {
        try
        {
            if (args.Length >= 2 && (args[0] == "--ble-admin-read" || args[0] == "--ble-admin-verify")) BleAdminReadProbe.Run(args[1], args[0] == "--ble-admin-verify").GetAwaiter().GetResult();
            else if (args.Contains("--ble-devices")) { using (var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(15))) { var devices = MeshtasticClient.GetBluetoothDevicesAsync(MeshProtocol.MeshCore, timeout.Token).GetAwaiter().GetResult(); Console.WriteLine("Paired Bluetooth devices: " + devices.Count); foreach (var device in devices) Console.WriteLine(device.Name); } }
            else if (args.Length >= 2 && args[0] == "--port") ProbeAsync(args[1], args.Contains("--telemetry"), args.Contains("--channels")).GetAwaiter().GetResult();
            else { RepeaterDialogTests.Run(); CommunicationLogFilterTests.Run(); CommunicationLogTests.Run(); TelnetEchoTests.RunAsync().GetAwaiter().GetResult(); ArchivedChannelChatTests.Run(); GatewayConfigurationTests.Run(); LocPositionTests.Run(); MeshCoreHashtagTests.Run(); ChannelDraftTests.Run(); SharedLinkImportTests.Run(); MessageOrderingTests.Run(); TelnetServerTests.RunAsync().GetAwaiter().GetResult(); MessageContentTests.Run(); DeferredNodePersistenceTests.Run(); PathMapTests.Run(); MeshCoreTelemetryPaddingTests.Run(); MeshCoreHopTests.Run(); TelegramSenderTests.Run(); NodeReceptionTests.Run(); MeshCoreContactTimeoutTests.RunAsync().GetAwaiter().GetResult(); BluetoothTests.RunAsync().GetAwaiter().GetResult(); MapMarkerTests.Run(); SharingTests.Run(); BluetoothChannelTests.RunAsync().GetAwaiter().GetResult(); ChannelTests.RunAsync().GetAwaiter().GetResult(); TestAsync().GetAwaiter().GetResult(); }
            return 0;
        }
        catch (Exception ex) { Console.Error.WriteLine(ex); return 1; }
    }

    private static async Task ProbeAsync(string port, bool telemetry, bool channels)
    {
        using (var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(30)))
        using (var client = new MeshtasticClient { Protocol = MeshProtocol.MeshCore, MaximumConnectionAttempts = 1 })
        {
            await client.ConnectSerialAsync(port, 115200, timeout.Token);
            await client.RequestFullStateAsync(timeout.Token);
            Console.WriteLine("MeshCore hardware probe: " + port + ", firmware " + client.Device.Metadata.FirmwareVersion + ", " + (client.Nodes.Count - 1) + " contacts, " + client.Channels.Count + " configured channels.");
            Check(client.Device.LocalNode != null && client.Device.NodeId.Length == 64, "Device public key");
            if (telemetry) Console.WriteLine((await client.RequestMeshCoreTelemetryAsync(token: timeout.Token)).MeshCoreRawTelemetry);
            if (channels) { var slots = await client.GetChannelSlotsAsync(timeout.Token); Console.WriteLine("Channel editor probe: " + slots.Count + " slots, " + slots.Count(c => c.Role != Meshtastic.Protobufs.Channel.Types.Role.Disabled) + " configured; all keys have 16 bytes: " + slots.All(c => c.Settings.Psk.Length == 16)); }
            // Do not poll: queued messages remain on the radio for the actual UI.
            await client.DisconnectAsync();
        }
    }

    private static async Task TestAsync()
    {
        using (var stalledRadio = new FakeRadio(0, 1))
        using (var stoppedClient = new MeshtasticClient { Protocol = MeshProtocol.MeshCore, MaximumConnectionAttempts = 1, ConfigurationTimeout = TimeSpan.FromMilliseconds(750) })
        using (var deadline = new CancellationTokenSource(TimeSpan.FromSeconds(10)))
        {
            stoppedClient.ConfirmSynchronizationRetryAsync = (info, token) => Task.FromResult(false);
            await stoppedClient.ConnectTcpAsync("127.0.0.1", stalledRadio.Port, deadline.Token);
            await stoppedClient.RequestFullStateAsync(deadline.Token);
            Check(stalledRadio.ContactRequests == 1 && stalledRadio.ChannelRequests == stalledRadio.ChannelCount && !stoppedClient.LastNodeSynchronizationComplete, "Skipping timed-out nodes still loads channels and marks node list incomplete");
            await stoppedClient.DisconnectAsync();
        }
        using (var skipRadio = new FakeRadio(0, 1))
        using (var skipClient = new MeshtasticClient { Protocol = MeshProtocol.MeshCore, MaximumConnectionAttempts = 1 })
        using (var deadline = new CancellationTokenSource(TimeSpan.FromSeconds(10)))
        {
            skipClient.SynchronizationProgress += (sender, e) => { if (e.Text == "Attempt 1 - Loading nodes: 1 / 3") Check(skipClient.SkipMeshCoreNodeSynchronization(), "Manual node skip accepted"); };
            await skipClient.ConnectTcpAsync("127.0.0.1", skipRadio.Port, deadline.Token);
            await skipClient.RequestFullStateAsync(deadline.Token);
            Check(skipRadio.ChannelRequests == skipRadio.ChannelCount && !skipClient.LastNodeSynchronizationComplete, "Manual node skip continues to channels without waiting for timeout");
            await skipClient.DisconnectAsync();
        }
        using (var incompleteRadio = new FakeRadio(3))
        using (var incompleteClient = new MeshtasticClient { Protocol = MeshProtocol.MeshCore, MaximumConnectionAttempts = 1 })
        using (var deadline = new CancellationTokenSource(TimeSpan.FromSeconds(10)))
        {
            await incompleteClient.ConnectTcpAsync("127.0.0.1", incompleteRadio.Port, deadline.Token);
            await incompleteClient.RequestFullStateAsync(deadline.Token);
            Check(incompleteRadio.ContactRequests == 1 && incompleteRadio.ChannelRequests == incompleteRadio.ChannelCount, "End-of-list with missing contacts immediately continues with channels without retrying");
            Check(incompleteClient.LastNodeSynchronizationComplete && incompleteClient.LastMeshCoreContactTransferEnded, "Explicit EOF completes the transfer despite announced count mismatch");
            Check(incompleteClient.Nodes.Any(n => n.LongName == "Alice") && incompleteClient.IsMeshCoreContactSaved(new string('f', 64)) == false, "EOF confirms received DEV contacts and absent PC contacts");
            await incompleteClient.DisconnectAsync();
        }
        var runtime = Path.Combine(Path.GetTempPath(), "MeshCoreTests-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(runtime);
        using (var radio = new FakeRadio(0, 1))
        using (var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(35)))
        using (var store = MeshtasticMessageStore.CreateSqlite(Path.Combine(runtime, "messages.db")))
        using (var client = new MeshtasticClient { Protocol = MeshProtocol.MeshCore, MaximumConnectionAttempts = 1 })
        {
            var communicationFrames = new System.Collections.Concurrent.ConcurrentQueue<CommunicationFrameEventArgs>();
            client.CommunicationFrame += (sender, frame) => communicationFrames.Enqueue(frame);
            var settingsFile = Path.Combine(runtime, "gateway-settings.xml");
            var gatewaySettings = new MeshtasticApplicationSettings();
            gatewaySettings.TelegramGateways = new List<TelegramGatewaySettings>
            {
                new TelegramGatewaySettings { ChannelIndex = 0, Enabled = true, BotToken = "fake-meshtastic", ChatId = 1 },
                new TelegramGatewaySettings { Protocol = MeshProtocol.MeshCore, ChannelIndex = 0, Enabled = true, BotToken = "fake-meshcore", ChatId = 2 },
                new TelegramGatewaySettings { Protocol = MeshProtocol.MeshCore, ChannelIndex = 39, Enabled = true, ChatId = 3 }
            };
            gatewaySettings.ChatBots.Add(new MeshtasticChatBotSettings { Protocol = MeshProtocol.MeshCore, Command = "test", ReactToChannels = new List<int> { 0, 39, 39, 255, 256 } });
            gatewaySettings.ChatBots.Add(new MeshtasticChatBotSettings { Command = "legacy", ReactToChannels = new List<int> { 0, 7, 39 } });
            gatewaySettings.HttpBots.Add(new MeshtasticHttpBotSettings { Protocol = MeshProtocol.MeshCore, Command = "http", ReactToChannels = new List<int> { 39 } });
            MeshtasticSettingsStore.Save(settingsFile, gatewaySettings);
            var loadedBots = MeshtasticSettingsStore.Load(settingsFile);
            Check(loadedBots.ChatBots.Single(bot => bot.Protocol == MeshProtocol.MeshCore).ReactToChannels.SequenceEqual(new[] { 0, 39, 255 }), "MeshCore bot settings preserve high channel indices and remove duplicates");
            Check(loadedBots.ChatBots.Single(bot => bot.Protocol == MeshProtocol.Meshtastic).ReactToChannels.SequenceEqual(new[] { 0, 7 }), "Meshtastic bots retain eight-channel validation");
            Check(loadedBots.HttpBots.Single().Protocol == MeshProtocol.MeshCore && loadedBots.HttpBots.Single().ReactToChannels.Single() == 39, "HTTP bot protocol and high channel survive settings reload");
            var loadedGateways = MeshtasticSettingsStore.Load(settingsFile).TelegramGateways;
            Check(loadedGateways.Count(g => g.ChannelIndex == 0) == 2 && loadedGateways.Any(g => g.Protocol == MeshProtocol.MeshCore && g.ChannelIndex == 39), "Gateway settings preserve high channels and separate protocols");
            var legacyXml = File.ReadAllText(settingsFile).Replace("<Protocol>Meshtastic</Protocol>", "");
            File.WriteAllText(settingsFile, legacyXml);
            Check(MeshtasticSettingsStore.Load(settingsFile).TelegramGateways.Any(g => g.Protocol == MeshProtocol.Meshtastic && g.ChannelIndex == 0 && g.BotToken == "fake-meshtastic"), "Legacy gateways remain Meshtastic gateways");
            store.Initialize(); client.MeshCoreIdentityResolver = store.GetOrCreateMeshCoreIdentity;
            var received = new List<MeshMessage>();
            var positionEvents = new List<MeshCorePositionEventArgs>();
            client.MeshCorePositionReceived += (sender, e) => { lock (positionEvents) positionEvents.Add(e); store.AddPosition(e.NodeNumber, e.Position, e.ReceivedUtc); };
            client.MessageReceived += (sender, e) => { lock (received) received.Add(e.Message); store.AddIncoming(e.Message); };
            client.ConfigurationTimeout = TimeSpan.FromMilliseconds(750);
            var retryQuestions = 0;
            client.ConfirmSynchronizationRetryAsync = (info, token) =>
            {
                Check(info.Attempt == 1 && info.ReceivedNodes == 1 && info.ExpectedNodes == 3 && info.LastNodeName == "Alice", "Timeout prompt names last node and retained progress");
                retryQuestions++;
                return Task.FromResult(true);
            };
            var syncProgress = new List<string>();
            client.SynchronizationProgress += (sender, e) => syncProgress.Add(e.Text);
            await client.ConnectTcpAsync("127.0.0.1", radio.Port, timeout.Token);
            await client.RequestFullStateAsync(timeout.Token);
            Check(radio.ClockSets == 1 && client.Device.DeviceClockUtc.HasValue && Math.Abs((client.Device.DeviceClockUtc.Value - DateTime.UtcNow).TotalSeconds) < 5 && client.Device.DeviceClockSyncStatus == "Synchronized with PC", "Connect synchronizes and verifies the device clock only once per session");
            Check(client.Device.MeshCoreBatteryVoltage.HasValue && Math.Abs(client.Device.MeshCoreBatteryVoltage.Value - 4.05) < 0.0001, "Battery voltage loaded during initial synchronization");
            radio.BatteryMillivolts = 3870;
            await client.RefreshMeshCoreBatteryAsync(timeout.Token);
            Check(Math.Abs(client.Device.MeshCoreBatteryVoltage.Value - 3.87) < 0.0001, "Battery refresh updates the live voltage");
            radio.BatteryMillivolts = 0;
            await client.RefreshMeshCoreBatteryAsync(timeout.Token);
            Check(!client.Device.MeshCoreBatteryVoltage.HasValue, "Unavailable battery measurement is not shown as zero volts");
            radio.BatteryMillivolts = 65535;
            await client.RefreshMeshCoreBatteryAsync(timeout.Token);
            Check(!client.Device.MeshCoreBatteryVoltage.HasValue, "Invalid battery sentinel is not shown as a voltage");
            radio.BatteryMillivolts = 4050;
            await client.RefreshMeshCoreBatteryAsync(timeout.Token);
            Check(client.Device.RoutingHashLengthBytes == 3, "Protocol v10 reports 3-byte routing hashes");
            await client.RefreshRadioStatisticsAsync(timeout.Token);
            Check(client.Device.NoiseFloorDbm == -114, "Radio noise floor decoded as signed int16 dBm");
            Check(syncProgress.Any(line => line == "Attempt 2 - Loading nodes: 1 / 3"), "Retried contacts show current attempt and progress even for retained nodes");
            Check(retryQuestions == 1, "Stalled contact transfer asks before restarting");
            Check(radio.ContactRequests == 2, "A stalled transfer retries after confirmation and completes without an extra full transfer");
            Check(client.Device.LocalNode.LongName == "Test Radio", "Self info name");
            Check(client.Device.Metadata.FirmwareVersion == "1.14-test", "Firmware version");
            Check(communicationFrames.Any(f => f.Direction == "TX" && f.Transport == "TCP" && f.Protocol == MeshProtocol.MeshCore && f.RawData[0] == 60) && communicationFrames.Any(f => f.Direction == "RX" && f.RawData[0] == 62), "MeshCore TCP traffic emits raw bidirectional communication frames");
            Check(client.Nodes.Count == 4, "Contact list including Room Server");
            var discoveryNodesBefore = client.Nodes.Count;
            var discoveryCallbacks = 0;
            var discovered = await client.DiscoverMeshCoreRepeatersAsync(TimeSpan.FromSeconds(1), result => Interlocked.Increment(ref discoveryCallbacks), timeout.Token);
            Check(discovered.Count == 2 && discoveryCallbacks == 3, "Repeater discovery ignores unrelated tags, wrong types, routed responses and truncated keys; duplicates update one result");
            var knownRepeater = discovered.Single(result => result.PublicKey == client.Nodes.Single(node => node.LongName == "Alice").Id);
            Check(knownRepeater.Name == "Alice" && knownRepeater.Rssi == -90 && knownRepeater.ReceivedSnr == 1 && knownRepeater.UplinkSnr == 2, "Repeater search resolves known name and signed bidirectional signal measurements");
            Check(discovered.Any(result => result.Name == "Unknown repeater") && client.Nodes.Count == discoveryNodesBefore, "Unknown repeater retained without manufacturing contacts");
            using (var cancelSearch = new CancellationTokenSource())
            {
                var pendingSearch = client.DiscoverMeshCoreRepeatersAsync(TimeSpan.FromSeconds(2), result => cancelSearch.Cancel(), cancelSearch.Token);
                try { await pendingSearch; throw new Exception("Cancelled search completed"); } catch (OperationCanceledException) { }
                Check(client.IsConnected, "Cancelling repeater results keeps device connection open");
            }
            await client.RefreshOwnDeviceInfoAsync(timeout.Token);
            Check(Math.Abs(client.Device.RadioFrequencyMHz.Value - 869.525) < 0.0001 && client.Device.RadioBandwidthKHz == 62.5 && client.Device.RadioSpreadingFactor == 7 && client.Device.RadioCodingRate == 5 && client.Device.RadioTxPowerDbm == 22, "Own device radio settings decoded with correct units");
            Check(Math.Abs(client.Device.MeshCoreBatteryVoltage.Value - 4.05) < 0.0001, "Own device battery millivolts decoded");
            Check(client.Channels.Count == 1 && client.Channels.First().Name == "Public", "Configured vs empty channels");
            var alicePositionNumber = client.Nodes.Single(node => node.LongName == "Alice").Number;
            Check(!client.Nodes.Single(node => node.LongName == "Alice").LastHeardUtc.HasValue, "Imported advert timestamp is not reception time");
            Check(store.GetPositions(alicePositionNumber).Count == 1, "Advert positions stored for map history");
            await client.RequestFullStateAsync(timeout.Token);
            Check(store.GetPositions(alicePositionNumber).Count == 1, "Repeated contact snapshots do not duplicate history");
            await client.SetFixedPositionAsync(-33.123456, 151.234567, cancellationToken: timeout.Token);
            Check(Math.Abs(client.Device.Latitude.Value + 33.123456) < 0.0000001 && Math.Abs(client.Device.Longitude.Value - 151.234567) < 0.0000001, "Manual MeshCore position verified on device");
            Check(!radio.GpsEnabled, "Manual position disables device GPS");
            await client.ClearFixedPositionAsync(timeout.Token);
            Check(radio.GpsEnabled && radio.GpsInterval == 60, "Use device GPS enables receiver and automatic updates");
            radio.RejectPosition = true;
            try { await client.SetFixedPositionAsync(10, 20, cancellationToken: timeout.Token); throw new Exception("Rejected position accepted"); } catch (IOException) { }
            Check(radio.GpsEnabled, "Rejected manual position restores GPS setting");
            radio.RejectPosition = false; radio.SupportsGps = false;
            try { await client.ClearFixedPositionAsync(timeout.Token); throw new Exception("Missing GPS accepted"); } catch (NotSupportedException) { }
            radio.SupportsGps = true;
            try { await client.SetFixedPositionAsync(10, 20, 100, cancellationToken: timeout.Token); throw new Exception("Unsupported altitude accepted"); } catch (NotSupportedException) { }
            await client.SendAdvertAsync(false, timeout.Token); await client.SendAdvertAsync(true, timeout.Token);
            Check(radio.AdvertModes.SequenceEqual(new byte[] { 0, 1 }), "MeshCore zero-hop and flood adverts");
            try { await client.SendAdvertAsync(true, timeout.Token); throw new Exception("Rejected advert accepted"); } catch (IOException) { }
            await client.SendAdvertAsync(false, false, timeout.Token);
            Check(radio.AdvertPositions.Last() == 0 && radio.AdvertPolicy == 1, "Advert omits position and restores policy");
            radio.AdvertPolicy = 0;
            await client.SendAdvertAsync(true, true, timeout.Token);
            Check(radio.AdvertPositions.Last() == 1 && radio.AdvertPolicy == 0, "Advert includes position");
            radio.AdvertPolicy = 1;
            radio.RejectNextAdvert = true;
            try { await client.SendAdvertAsync(true, false, timeout.Token); throw new Exception("Rejected positional advert accepted"); } catch (IOException) { }
            Check(radio.AdvertPolicy == 1, "Advert policy restored after rejection");
            var slots = await client.GetChannelSlotsAsync(timeout.Token);
            Check(slots.Count == 2 && slots[0].Settings.Psk[0] == 42 && slots[1].Role == Meshtastic.Protobufs.Channel.Types.Role.Disabled, "MeshCore key and free slot readback");
            var edited = slots[1].Clone(); edited.Role = Meshtastic.Protobufs.Channel.Types.Role.Secondary;
            edited.Settings.Name = "Private"; edited.Settings.Psk = Google.Protobuf.ByteString.CopyFrom(Enumerable.Repeat((byte)127, 16).ToArray());
            var saved = await client.SaveChannelAsync(edited, timeout.Token);
            Check(saved.Settings.Name == "Private" && saved.Settings.Psk.Equals(edited.Settings.Psk) && client.Channels.Count == 2, "MeshCore fixed width write and verified cache");
            edited.Settings.Name = new string('ä', 16);
            try { await client.SaveChannelAsync(edited, timeout.Token); throw new Exception("Long UTF8 name accepted"); } catch (ArgumentException) { }
            edited.Settings.Name = "Private"; edited.Settings.Psk = Google.Protobuf.ByteString.CopyFrom(new byte[32]);
            try { await client.SaveChannelAsync(edited, timeout.Token); throw new Exception("AES256 accepted on MeshCore"); } catch (ArgumentException) { }
            edited.Settings.Psk = saved.Settings.Psk; edited.Settings.Name = "reject";
            try { await client.SaveChannelAsync(edited, timeout.Token); throw new Exception("Rejected channel accepted"); } catch (IOException) { }
            edited.Settings.Name = "mismatch";
            try { await client.SaveChannelAsync(edited, timeout.Token); throw new Exception("Mismatched channel accepted"); } catch (IOException) { }
            edited.Role = Meshtastic.Protobufs.Channel.Types.Role.Disabled;
            await client.SaveChannelAsync(edited, timeout.Token);
            Check(client.Channels.Count == 1 && !client.Channels.Any(c => c.Index == 1), "MeshCore clearing removes channel from cache");
            var contact = client.Nodes.Single(node => node.LongName == "Alice");
            var other = client.Nodes.Single(node => node.LongName == "Bob");
            Check(contact.Number != other.Number, "Keys with equal 32-bit prefixes remain distinct");
            Check(contact.Id.Length == 64 && Math.Abs(contact.Latitude.Value - 52.123456) < 0.0000001 && Math.Abs(contact.Longitude.Value - 13.123456) < 0.0000001, "Contact key and location offsets");
            Check(client.GetMaximumTextPayloadBytes(true) == 148 && client.GetMaximumTextPayloadBytes(false) == 160, "UTF-8 group prefix limit");
            var roomNode = client.Nodes.Single(node => node.MeshCoreContactType == 3);
            store.AddOrUpdateNode(contact);
            store.AddOrUpdateNode(roomNode);
            Check(store.GetNodes().Single(n => n.NodeNumber == contact.Number).MeshCoreContactType == 1 &&
                store.GetNodes().Single(n => n.NodeNumber == roomNode.Number).MeshCoreContactType == 3, "Companion and room types persist in node records");
            Check(client.GetMaximumTextPayloadBytes(false, roomNode.Number) == 151, "Room post size limit");
            try { await client.SendTextWithIdAsync(roomNode.Number, "before login", cancellationToken: timeout.Token); throw new Exception("Room post without login accepted"); }
            catch (InvalidOperationException) { }
            try { await client.LoginMeshCoreRoomAsync(roomNode.Number, "deny", timeout.Token); throw new Exception("Failed login accepted"); }
            catch (UnauthorizedAccessException) { }
            var readOnly = await client.LoginMeshCoreRoomAsync(roomNode.Number, "readonly", timeout.Token);
            Check(!readOnly.CanPost, "Read-only room permissions");
            try { await client.SendTextWithIdAsync(roomNode.Number, "read only post", cancellationToken: timeout.Token); throw new Exception("Read-only room post accepted"); }
            catch (InvalidOperationException) { }
            var login = await client.LoginMeshCoreRoomAsync(roomNode.Number, "secret", timeout.Token);
            Check(login.CanPost && radio.RoomPassword == "secret", "Room password login and ACL decoding");
            await client.SendTextWithIdAsync(roomNode.Number, "room post", cancellationToken: timeout.Token);
            var ownTelemetry = await client.RequestMeshCoreTelemetryAsync(null, timeout.Token);
            Check(Math.Abs(ownTelemetry.Telemetry.EnvironmentMetrics.Voltage - 4.15) < 0.001 && Math.Abs(ownTelemetry.Telemetry.EnvironmentMetrics.Temperature + 5.5) < 0.001, "Local telemetry unsigned voltage and negative temperature");
            store.AddTelemetry(ownTelemetry);
            var remoteTelemetry = await client.RequestMeshCoreTelemetryAsync(contact.Number, timeout.Token);
            Check(remoteTelemetry.From == contact.Number && Math.Abs(remoteTelemetry.Telemetry.EnvironmentMetrics.RelativeHumidity - 45) < 0.001, "Remote telemetry matches response tag");
            store.AddTelemetry(remoteTelemetry);
            Check(store.GetTelemetry(contact.Number).Single(item => item.Type == "MeshCore").Type == "MeshCore" && store.GetTelemetry(contact.Number).Single(item => item.Type == "MeshCore").RawTelemetry.Contains("Ch 0: Temperature -5.5"), "Native telemetry raw channels persisted");
            var directId = await client.SendTextWithIdAsync(contact.Number, "Grüße", cancellationToken: timeout.Token);
            await WaitForAsync(() => client.GetMessageDelivery(directId) != null && client.GetMessageDelivery(directId).State == MessageDeliveryState.Delivered, timeout.Token);
            Check(radio.DirectText == "Grüße" && radio.DirectPrefix.SequenceEqual(FakeRadio.AliceKey.Take(6)), "Direct send encoding and immediate ACK correlation");
            var groupId = await client.SendTextToChannelWithIdAsync(0, "Hallo Kanal", cancellationToken: timeout.Token);
            Check(radio.ChannelText == "Hallo Kanal" && client.GetMessageDelivery(groupId).State == MessageDeliveryState.Sent, "Channel OK completes as sent without claiming delivery");
            store.AddOutgoing(groupId, client.Device.MyNode.MyNodeNum, UInt32.MaxValue, "Hallo Kanal", 0);
            store.UpdateDeliveryStatus(groupId, MessageDeliveryState.QueuedAtDevice);
            Check(store.GetChannelMessages(0).Single().DeliveryStatus == "Sent", "Channel queue status normalized in storage");
            store.DeleteChannelMessages();
            try { await client.SendTextToChannelWithIdAsync(0, new string('ä', 75), cancellationToken: timeout.Token); throw new Exception("Oversized UTF-8 text accepted"); }
            catch (ArgumentException) { }
            try { await client.SendTextWithIdAsync(123456, "invalid", cancellationToken: timeout.Token); throw new Exception("Unknown contact accepted"); }
            catch (InvalidOperationException) { }
            try { await client.SendTextToChannelWithIdAsync(0, "reject", cancellationToken: timeout.Token); throw new Exception("Radio rejection ignored"); }
            catch (IOException) { }
            await client.SendTextToChannelWithIdAsync(0, "after rejection", cancellationToken: timeout.Token);
            Check(radio.ChannelText == "after rejection", "Command queue recovers after explicit error response");
            store.AddOutgoing(directId, client.Device.MyNode.MyNodeNum, contact.Number, "Grüße", null);
            store.UpdateDeliveryStatus(directId, MessageDeliveryState.Delivered);
            store.UpdateDeliveryStatus(directId, MessageDeliveryState.QueuedAtDevice);
            Check(store.GetDirectMessages(contact.Number).Single().DeliveryStatus == "Delivered", "Late queued status cannot overwrite delivered");
            await client.ActivatePacketStreamingAsync(timeout.Token);
            await WaitForAsync(() => { lock (received) return received.Count == 6; }, timeout.Token);
            lock (received)
            {
                Check(received[0].IsChannelMessage && received[0].Text == "hello group" && received[0].From != contact.Number, "Group sender name is separate from authenticated contact");
                Check(received[1].IsDirectMessage && received[1].From == contact.Number && received[1].Text == "hello direct", "V3 direct receive");
                Check(received[2].Text == "signed" && received[2].MeshCorePathLength == 255, "Signed message offset and path metadata");
                Check(received[3].IsDirectMessage && received[3].Text == "legacy direct" && received[4].IsChannelMessage && received[4].Text == "legacy group", "Legacy receive frame variants");
                Check(received[5].From == roomNode.Number && received[5].SenderName == "Author 01020304" && received[5].Text == "room history", "Signed room author remains in room chat");
            }
            Check(store.GetDirectMessages(contact.Number).Count == 4 && store.GetChannelMessages(0).Count == 2, "Persisted incoming chats");
            Check(store.GetChannelMessages(0).First().OccurredUtc == new DateTime(2023, 11, 14, 22, 13, 20, DateTimeKind.Utc), "Sender timestamp preserved");
            Check(store.GetDirectMessages(roomNode.Number).Single().SenderName == "Author 01020304", "Room author survives SQLite storage");
            var reopened = MeshtasticMessageStore.CreateSqlite(Path.Combine(runtime, "messages.db"));
            Check(reopened.GetOrCreateMeshCoreIdentity(contact.Id) == contact.Number, "Identity mapping survives reopening");
            var nodesBeforeRefresh = client.Nodes.Count;
            var nodeUpdates = 0;
            client.NodeUpdated += (sender, e) => { if (e.Node.Number == contact.Number) Interlocked.Increment(ref nodeUpdates); };
            radio.ContactPush = 0x80;
            await client.SendAdvertAsync(false, timeout.Token);
            await WaitForAsync(() => Volatile.Read(ref nodeUpdates) >= 1, timeout.Token);
            await WaitForAsync(() => client.Nodes.Single(node => node.Number == contact.Number).HopsAway == 2, timeout.Token);
            Check(client.Nodes.Single(node => node.Number == contact.Number).LastHeardUtc > DateTime.UtcNow.AddSeconds(-5), "Live advert uses local reception time");
            Check(client.Nodes.Single(node => node.Number == contact.Number).LastRssi == -95 && client.Nodes.Single(node => node.Number == contact.Number).LastSnr == -3, "Live advert associates RSSI/SNR with the advertised contact" );
            Check(client.Nodes.Single(node => node.Number == contact.Number).LongName == "Alice updated", "Short advert reloads existing contact name");
            Check(Math.Abs(client.Nodes.Single(node => node.Number == contact.Number).Latitude.Value - 53.123456) < 0.0000001, "Short advert reloads position");
            var updatesBeforePath = Volatile.Read(ref nodeUpdates);
            radio.ContactPush = 0x81;
            await client.SendAdvertAsync(false, timeout.Token);
            await WaitForAsync(() => Volatile.Read(ref nodeUpdates) > updatesBeforePath, timeout.Token);
            Check(client.Nodes.Count == nodesBeforeRefresh, "Path update preserves contact identity without duplication");
            radio.ChannelCount = 40;
            await client.RequestFullStateAsync(timeout.Token);
            Check(client.ChannelSlotCount == 40, "MeshCore reports all 40 channel slots");
            using (var gateways = new MeshtasticTelegramGatewayManager(client, new[]
            {
                new TelegramGatewaySettings { Protocol = MeshProtocol.MeshCore, ChannelIndex = 39, Enabled = true },
                new TelegramGatewaySettings { Protocol = MeshProtocol.Meshtastic, ChannelIndex = 0, Enabled = true },
                new TelegramGatewaySettings { Protocol = MeshProtocol.MeshCore, ChannelIndex = 40, Enabled = true }
            }))
            {
                await gateways.StartAsync();
                Check(gateways.GetStatuses().Count == 1 && gateways.GetStatuses()[0].ChannelIndex == 39, "MeshCore gateways accept channel 39 and filter other protocols and out-of-range channels");
                await gateways.StopAsync();
            }
            radio.ChannelCount = 2;
            var clockSetsBeforeReconnect = radio.ClockSets;
            radio.DeviceClock = checked((uint)(DateTime.UtcNow.AddHours(1) - new DateTime(1970, 1, 1, 0, 0, 0, DateTimeKind.Utc)).TotalSeconds);
            var savedNumber = contact.Number;
            await client.DisconnectAsync();
            await client.ConnectTcpAsync("127.0.0.1", radio.Port, timeout.Token);
            await client.RequestFullStateAsync(timeout.Token);
            Check(client.Nodes.Single(node => node.Id == contact.Id).Number == savedNumber, "Reconnect preserves identity and repeats handshake");
            Check(radio.ClockSets == clockSetsBeforeReconnect && client.Device.DeviceClockSyncStatus.Contains("ahead"), "Reconnect rechecks a device clock that cannot be moved backwards without failing connection");
            Check(client.GetMeshCoreRoomSession(roomNode.Number) == null, "Room login cleared on reconnect");
            Check(client.IsMeshCoreContactSaved(contact.Id) == true, "Complete sync marks device contacts");
            var unknownKey = Enumerable.Repeat((byte)77, 32).ToArray();
            var unknownId = String.Concat(unknownKey.Select(b => b.ToString("x2")));
            Check(client.IsMeshCoreContactSaved(unknownId) == false, "Complete sync distinguishes PC-only identities");
            await radio.PushUnstoredContactAsync(unknownKey);
            await WaitForAsync(() => client.Nodes.Any(n => n.Id == unknownId), timeout.Token);
            Check(client.IsMeshCoreContactSaved(unknownId) == false, "Unstored full advert is not a device contact");
            var unknownNode = client.Nodes.Single(n => n.Id == unknownId);
            try { await client.SendTextWithIdAsync(unknownNode.Number, "not stored", cancellationToken: timeout.Token); throw new Exception("PC-only direct message accepted"); } catch (InvalidOperationException) { }
            try { await client.SaveMeshCoreContactAsync(unknownId, "reject contact", token: timeout.Token); throw new Exception("Full contact table accepted"); } catch (IOException ex) { Check(ex.Message.Contains("contact list is full") && ex.Message.Contains("Remove"), "Full device table provides actionable explanation"); }
            Check(client.IsMeshCoreContactSaved(unknownId) == false, "Rejected save cannot mark a contact as saved");
            await client.ImportSharedContactAsync(MeshProtocol.MeshCore, ContactSharing.CreateMeshCoreUri("Manual contact", unknownKey), timeout.Token);
            Check(client.IsMeshCoreContactSaved(unknownId) == true && client.Nodes.Single(n => n.Id == unknownId).LongName == "Manual contact", "Saving verifies contact and updates node");
            Check(client.Nodes.Single(n => n.Id == unknownId).MeshCoreContactType == 1, "Contact link type is stored on the device");
            await client.SaveMeshCoreContactAsync(unknownId, "Admin repeater", 2, token: timeout.Token);
            try { await client.SendMeshCoreRepeaterCommandAsync(unknownNode.Number, "ver", timeout.Token); throw new Exception("Unauthenticated CLI accepted"); } catch (UnauthorizedAccessException) { }
            try { await client.LoginMeshCoreRepeaterAsync(unknownNode.Number, "deny", timeout.Token); throw new Exception("Denied repeater login accepted"); } catch (UnauthorizedAccessException) { }
            try { await client.LoginMeshCoreRepeaterAsync(unknownNode.Number, "guest", timeout.Token); throw new Exception("Guest administrator accepted"); } catch (UnauthorizedAccessException) { }
            await client.LoginMeshCoreRepeaterAsync(unknownNode.Number, "admin", timeout.Token);
            var cliMessageCount = received.Count;
            Check(await client.SendMeshCoreRepeaterCommandAsync(unknownNode.Number, "ver", timeout.Token) == "Test repeater firmware", "CLI replies match sender, message type and echoed request tag");
            Check(received.Count == cliMessageCount, "CLI replies are not chat messages");
            try { await client.SendMeshCoreRepeaterCommandAsync(unknownNode.Number, "ver\nreboot", timeout.Token); throw new Exception("Multiline CLI accepted"); } catch (ArgumentException) { }
            await client.SendMeshCoreRepeaterCommandAsync(unknownNode.Number, "reboot", timeout.Token);
            try { await client.SendMeshCoreRepeaterCommandAsync(unknownNode.Number, "ver", timeout.Token); throw new Exception("Reboot retained authorization"); } catch (UnauthorizedAccessException) { }
            await client.RemoveMeshCoreContactAsync(unknownId, timeout.Token);
            Check(client.IsMeshCoreContactSaved(unknownId) == false && client.Nodes.Any(n => n.Id == unknownId), "Device deletion retains known PC node identity");
            var removalProgress = new System.Collections.Generic.List<Tuple<int, int>>();
            var removedCount = await client.RemoveAllMeshCoreContactsAsync(timeout.Token, (done, total) => removalProgress.Add(Tuple.Create(done, total)));
            if (removalProgress.Count != removedCount + 1 || removalProgress[0].Item1 != 0 || removalProgress.Last().Item1 != removedCount || removalProgress.Any(p => p.Item2 != removedCount)) throw new Exception("Contact removal progress must count confirmed deletions.");
            Check(removedCount == 3 && client.IsMeshCoreContactSaved(contact.Id) == false, "Delete all removes only confirmed device contacts");
            Check(store.GetDirectMessages(contact.Number).Count == 4, "Deleting device contacts retains stored chat history");
            try { await client.SaveMeshCoreContactAsync("not a key", "Bad", token: timeout.Token); throw new Exception("Invalid key accepted"); } catch (ArgumentException) { }
            radio.RestoreContacts();
            await client.DisconnectAsync();
            using (var failureClient = new MeshtasticClient { Protocol = MeshProtocol.MeshCore, MaximumConnectionAttempts = 1, TransportOperationTimeout = TimeSpan.FromMilliseconds(500) })
            {
                await failureClient.ConnectTcpAsync("127.0.0.1", radio.Port, timeout.Token);
                await failureClient.RequestFullStateAsync(timeout.Token);
                try { await failureClient.SendTextToChannelWithIdAsync(0, "timeout", cancellationToken: timeout.Token); throw new Exception("Missing response did not time out"); }
                catch (TimeoutException) { }
                await failureClient.DisconnectAsync();
                await failureClient.ConnectTcpAsync("127.0.0.1", radio.Port, timeout.Token);
                await failureClient.RequestFullStateAsync(timeout.Token);
                var failureContact = failureClient.Nodes.Single(node => node.LongName == "Alice");
                try { await failureClient.SendTextWithIdAsync(failureContact.Number, "truncated", cancellationToken: timeout.Token); throw new Exception("Truncated response accepted"); }
                catch (InvalidDataException) { }
                await failureClient.DisconnectAsync();
            }
            Console.WriteLine("MeshCore tests passed: framing, handshake, contacts, channels, UTF-8 limits, send/ACK, receive, SQLite identities and reconnect.");
        }
        // The unique temporary test database contains artificial data only.
        var resolved = Path.GetFullPath(runtime);
        if (!resolved.StartsWith(Path.GetFullPath(Path.GetTempPath()), StringComparison.OrdinalIgnoreCase)) throw new Exception("Unexpected test directory");
        try { Directory.Delete(resolved, true); } catch (IOException) { /* SQLite pool may retain test files until exit. */ }
    }

    private static async Task WaitForAsync(Func<bool> condition, CancellationToken token)
    { while (!condition()) await Task.Delay(20, token); }
    private static void Check(bool condition, string name) { if (!condition) throw new Exception("FAILED: " + name); }

    private sealed class FakeRadio : IDisposable
    {
        public static readonly byte[] AliceKey = Enumerable.Range(1, 32).Select(value => (byte)value).ToArray();
        public static readonly byte[] RoomKey = Enumerable.Repeat((byte)201, 32).ToArray();
        private readonly TcpListener _listener = new TcpListener(IPAddress.Loopback, 0);
        private readonly CancellationTokenSource _stop = new CancellationTokenSource();
        private TcpClient _connection;
        private int _messageIndex;
        private readonly byte[][] _channelFrames = Enumerable.Range(0, 40).Select(i => { var frame = new byte[50]; frame[0] = 18; frame[1] = (byte)i; if (i == 0) { Encoding.ASCII.GetBytes("Public").CopyTo(frame, 2); frame[34] = 42; } return frame; }).ToArray();
        public int Port { get; private set; }
        private readonly Dictionary<string, byte[]> _savedContacts = new Dictionary<string, byte[]>();
        private readonly HashSet<string> _deletedContacts = new HashSet<string>();
        public void RestoreContacts() { _savedContacts.Clear(); _deletedContacts.Clear(); }
        public Task PushUnstoredContactAsync(byte[] key) { var frame = Contact(key, "PC-only advert"); frame[0] = 0x8A; return FrameAsync(_connection.GetStream(), frame); }
        public ushort BatteryMillivolts = 4050;
        public uint DeviceClock = 1700000000u;
        public int ClockSets;
        public string DirectText;
        public byte[] DirectPrefix;
        public string ChannelText;
        public string RoomPassword;
        public bool SupportsGps = true, GpsEnabled = true, RejectPosition;
        public int GpsInterval;
        private int _latitude, _longitude;
        public byte ChannelCount = 2;
        public byte ContactPush;
        public byte AdvertPolicy = 1;
        public bool RejectNextAdvert;
        public readonly List<byte> AdvertPositions = new List<byte>();
        public readonly List<byte> AdvertModes = new List<byte>();
        private int _incompleteContactTransfers, _stalledContactTransfers;
        public int ContactRequests, ChannelRequests;
        public FakeRadio(int incompleteContactTransfers = 0, int stalledContactTransfers = 0)
        { _incompleteContactTransfers = incompleteContactTransfers; _stalledContactTransfers = stalledContactTransfers; _listener.Start(); Port = ((IPEndPoint)_listener.LocalEndpoint).Port; _ = RunAsync(); }

        private async Task RunAsync()
        {
            try
            {
                while (!_stop.IsCancellationRequested)
                {
                    _connection = await _listener.AcceptTcpClientAsync();
                    using (_connection)
                    {
                        var stream = _connection.GetStream();
                        try
                        {
                            while (!_stop.IsCancellationRequested)
                            {
                                var header = await ReadAsync(stream, 3);
                                Check(header[0] == '<', "Outbound framing");
                                var command = await ReadAsync(stream, header[1] | header[2] << 8);
                                await ReplyAsync(stream, command);
                            }
                        }
                        catch (EndOfStreamException) { }
                    }
                }
            }
            catch (Exception ex) { if (!_stop.IsCancellationRequested) Console.Error.WriteLine("Fake radio: " + ex); }
        }

        private async Task ReplyAsync(NetworkStream stream, byte[] command)
        {
            switch (command[0])
            {
                case 1:
                    var self = new byte[68]; self[0] = 5; self[1] = 1; self[2] = 22; self[56] = 7; self[57] = 5; Array.Copy(BitConverter.GetBytes(869525u), 0, self, 48, 4); Array.Copy(BitConverter.GetBytes(62500u), 0, self, 52, 4); self[44] = 3; self[45] = AdvertPolicy; self[46] = 0x2A; self[47] = 1;
                    Enumerable.Repeat((byte)200, 32).ToArray().CopyTo(self, 4); Encoding.UTF8.GetBytes("Test Radio").CopyTo(self, 58);
                    BitConverter.GetBytes(_latitude).CopyTo(self, 36); BitConverter.GetBytes(_longitude).CopyTo(self, 40);
                    await FrameAsync(stream, self); break;
                case 56:
                    Check(command.Length == 2 && command[1] == 1, "Radio stats request");
                    var stats = new byte[14]; stats[0] = 24; stats[1] = 1; stats[2] = 142; stats[3] = 255; await FrameAsync(stream, stats); break;
                case 20: await FrameAsync(stream, new byte[] { 12 }.Concat(BitConverter.GetBytes(BatteryMillivolts)).ToArray()); break;
                case 22:
                    Check(command[1] == 3, "Negotiated protocol version");
                    var device = new byte[82]; device[81] = 2; device[0] = 13; device[1] = 10; device[3] = ChannelCount; Encoding.ASCII.GetBytes("1.14-test").CopyTo(device, 60);
                    await FrameAsync(stream, device); break;
                case 4:
                    ContactRequests++;
                    await FrameAsync(stream, new byte[] { 2, 3, 0, 0, 0 });
                    if (_stalledContactTransfers-- > 0)
                    {
                        await FrameAsync(stream, Contact(AliceKey, "Alice"));
                        break; // Keep connection alive but withhold the end-of-list response.
                    }
                    if (_incompleteContactTransfers-- > 0)
                    {
                        await FrameAsync(stream, Contact(AliceKey, "Alice"));
                        await FrameAsync(stream, Contact(AliceKey, "Alice"));
                        await FrameAsync(stream, Contact(AliceKey, "Alice"));
                        await FrameAsync(stream, new byte[] { 4, 0, 0, 0, 0 });
                        break;
                    }
                    await FrameAsync(stream, Contact(AliceKey, "Alice"));
                    var bob = (byte[])AliceKey.Clone(); bob[4] = 99;
                    await FrameAsync(stream, Contact(bob, "Bob"));
                    var room = Contact(RoomKey, "Test Room"); room[33] = 3;
                    await FrameAsync(stream, room);
                    await FrameAsync(stream, new byte[] { 4, 0, 0, 0, 0 }); break;
                case 31:
                    ChannelRequests++;
                    await FrameAsync(stream, _channelFrames[command[1]]); break;
                case 42: Check(command.Length == 34 && command[1] == 0, "Advert path command"); await FrameAsync(stream, new byte[] { 22, 1, 0, 0, 0, 66, 1, 2, 3, 4 }); break;
                case 9:
                    Check(command.Length == 148, "Add contact frame layout and size");
                    var contactName = Encoding.UTF8.GetString(command, 100, 32).TrimEnd('\0');
                    if (contactName == "reject contact") { await FrameAsync(stream, new byte[] { 1, 3 }); break; }
                    var savedContact = (byte[])command.Clone(); savedContact[0] = 3;
                    _savedContacts[BitConverter.ToString(command, 1, 32)] = savedContact; _deletedContacts.Remove(BitConverter.ToString(command, 1, 32));
                    await FrameAsync(stream, new byte[] { 0 }); break;
                case 15:
                    Check(command.Length == 33, "Remove contact uses full public key");
                    _savedContacts.Remove(BitConverter.ToString(command, 1, 32)); _deletedContacts.Add(BitConverter.ToString(command, 1, 32));
                    await FrameAsync(stream, new byte[] { 0 }); break;
                case 30:
                    Check(command.Length == 33, "Contact lookup uses full public key");
                    byte[] saved;
                    if (_savedContacts.TryGetValue(BitConverter.ToString(command, 1, 32), out saved)) { await FrameAsync(stream, saved); break; }
                    if (_deletedContacts.Contains(BitConverter.ToString(command, 1, 32))) { await FrameAsync(stream, new byte[] { 1, 2 }); break; }
                    Check(command.Skip(1).SequenceEqual(AliceKey), "Contact refresh uses full public key");
                    var updatedContact = Contact(AliceKey, "Alice updated");
                    BitConverter.GetBytes(53123456).CopyTo(updatedContact, 136);
                    await FrameAsync(stream, updatedContact);
                    break;
                case 14:
                    Check(command.Length == 9, "MeshCore set-position exact frame size");
                    if (RejectPosition) { await FrameAsync(stream, new byte[] { 1, 3 }); break; }
                    _latitude = BitConverter.ToInt32(command, 1); _longitude = BitConverter.ToInt32(command, 5);
                    await FrameAsync(stream, new byte[] { 0 }); break;
                case 40:
                    await FrameAsync(stream, new byte[] { 21 }.Concat(Encoding.ASCII.GetBytes(SupportsGps ? "gps:" + (GpsEnabled ? "1" : "0") + ",gps_interval:" + GpsInterval : "")).ToArray()); break;
                case 41:
                    var setting = Encoding.ASCII.GetString(command, 1, command.Length - 1);
                    if (setting.StartsWith("gps_interval:")) GpsInterval = Int32.Parse(setting.Substring(13));
                    else if (setting == "gps:0") GpsEnabled = false;
                    else if (setting == "gps:1") GpsEnabled = true;
                    else throw new Exception("Unexpected GPS setting: " + setting);
                    await FrameAsync(stream, new byte[] { 0 }); break;
                case 32:
                    Check(command.Length == 50, "MeshCore set-channel exact frame size");
                    var configuredName = Encoding.UTF8.GetString(command, 2, 32).TrimEnd((char)0);
                    if (configuredName.StartsWith("reject")) { await FrameAsync(stream, new byte[] { 1, 6 }); break; }
                    if (!configuredName.StartsWith("mismatch")) { var configured = (byte[])command.Clone(); configured[0] = 18; _channelFrames[command[1]] = configured; }
                    await FrameAsync(stream, new byte[] { 0 }); break;
                case 2:
                    if (command[1] == 1)
                    {
                        Check(command[2] == 0 && command.Skip(7).Take(6).All(b => b == 77), "CLI packet type and destination");
                        await FrameAsync(stream, new byte[] { 6, 1, 1, 2, 3, 4, 0xE8, 3, 0, 0 });
                        var cliText = Encoding.UTF8.GetString(command, 13, command.Length - 13);
                        if (cliText.EndsWith("reboot")) break;
                        var cliReply = Message(true, false, cliText.Substring(0, 3) + "Test repeater firmware");
                        for (var k = 4; k < 10; k++) cliReply[k] = 77;
                        cliReply[11] = 1;
                        var wrongTag = (byte[])cliReply.Clone(); wrongTag[16] = (byte)'!';
                        await FrameAsync(stream, wrongTag);
                        await FrameAsync(stream, cliReply); break;
                    }
                    DirectPrefix = command.Skip(7).Take(6).ToArray(); DirectText = Encoding.UTF8.GetString(command, 13, command.Length - 13);
                    if (DirectText == "truncated") { await FrameAsync(stream, new byte[] { 6, 1 }); break; }
                    await FrameAsync(stream, new byte[] { 0x83 });
                    await FrameAsync(stream, new byte[] { 6, 1, 1, 2, 3, 4, 0x10, 0x27, 0, 0 });
                    await FrameAsync(stream, new byte[] { 0x82, 1, 2, 3, 4, 1, 0, 0, 0 }); break;
                case 3:
                    ChannelText = Encoding.UTF8.GetString(command, 7, command.Length - 7);
                    if (ChannelText == "timeout") break;
                    if (ChannelText == "reject") { await FrameAsync(stream, new byte[] { 1, 6 }); break; }
                    await FrameAsync(stream, new byte[] { 0 }); break;
                case 10:
                    var index = _messageIndex++;
                    if (index == 0) await FrameAsync(stream, Message(false, false, "Alice: hello group"));
                    else if (index == 1) await FrameAsync(stream, Message(true, false, "hello direct"));
                    else if (index == 2) await FrameAsync(stream, Message(true, true, "signed"));
                    else if (index == 3) await FrameAsync(stream, Message(true, false, "legacy direct", false));
                    else if (index == 4) await FrameAsync(stream, Message(false, false, "Alice: legacy group", false));
                    else if (index == 5) await FrameAsync(stream, Message(true, true, "room history", true, true));
                    else await FrameAsync(stream, new byte[] { 10 });
                    break;
                case 38: Check(command.Length == 5 && command[1] == 1 && command[2] == 0x2A && command[4] == 3, "Advert preserves contact, telemetry and multi-ACK preferences"); AdvertPolicy = command[3]; await FrameAsync(stream, new byte[] { 0 }); break;
                case 7: Check(command.Length == 2, "Advert command length"); AdvertModes.Add(command[1]); AdvertPositions.Add(AdvertPolicy); if (ContactPush != 0) { var push = ContactPush; ContactPush = 0; var rxLog = new byte[105]; rxLog[0] = 0x88; rxLog[1] = 244; rxLog[2] = 161; rxLog[3] = 17; rxLog[4] = 0; Array.Copy(AliceKey, 0, rxLog, 5, 32); await FrameAsync(stream, rxLog); await FrameAsync(stream, new byte[] { push }.Concat(AliceKey).ToArray()); } var rejectAdvert = RejectNextAdvert || AdvertModes.Count == 3; RejectNextAdvert = false; await FrameAsync(stream, rejectAdvert ? new byte[] { 1, 3 } : new byte[] { 0 }); break;
                case 5: await FrameAsync(stream, new byte[] { 9 }.Concat(BitConverter.GetBytes(DeviceClock)).ToArray()); break;
                case 6: Check(command.Length == 5, "Clock command uses UTC Unix seconds"); var requestedTime = BitConverter.ToUInt32(command, 1); if (requestedTime < DeviceClock) { await FrameAsync(stream, new byte[] { 1, 6 }); break; } DeviceClock = requestedTime; ClockSets++; await FrameAsync(stream, new byte[] { 0 }); break;
                case 26:
                    if (command[1] == 77)
                    {
                        Check(command.Skip(1).Take(32).All(b => b == 77), "Repeater login full public key");
                        var adminPassword = Encoding.UTF8.GetString(command, 33, command.Length - 33);
                        await FrameAsync(stream, new byte[] { 6, 1, 1, 2, 3, 4, 0xE8, 3, 0, 0 });
                        var loginReply = new byte[] { (byte)(adminPassword == "deny" ? 0x86 : 0x85), (byte)(adminPassword == "admin" ? 1 : 0) }.Concat(Enumerable.Repeat((byte)77, 6)).Concat(new byte[6]).ToArray();
                        await FrameAsync(stream, loginReply); break;
                    }
                    Check(command.Skip(1).Take(32).SequenceEqual(RoomKey), "Login full public key");
                    RoomPassword = Encoding.UTF8.GetString(command, 33, command.Length - 33);
                    await FrameAsync(stream, new byte[] { 6, 1, 201, 201, 201, 201, 0xE8, 3, 0, 0 });
                    if (RoomPassword == "deny") await FrameAsync(stream, new byte[] { 0x86, 0 }.Concat(RoomKey.Take(6)).ToArray());
                    else await FrameAsync(stream, new byte[] { 0x85, 0 }.Concat(RoomKey.Take(6)).Concat(BitConverter.GetBytes(1700000000u)).Concat(new byte[] { (byte)(RoomPassword == "readonly" ? 1 : 2), 1 }).ToArray());
                    break;
                case 39:
                    Check(command.Length == 4, "Local telemetry request");
                    await FrameAsync(stream, new byte[] { 0x8B, 0 }.Concat(Enumerable.Repeat((byte)200, 6)).Concat(SensorData()).ToArray()); break;
                case 55:
                    Check(command.Length == 11 && command[1] == 0x80 && command[2] == 4 && command.Skip(7).All(value => value == 0), "Zero-hop repeater discovery request with full-key filter, unique tag and since=0");
                    var discoveryReply = new byte[42]; discoveryReply[0] = 0x8E; discoveryReply[1] = 244; discoveryReply[2] = 161; discoveryReply[4] = 0x92; discoveryReply[5] = 8;
                    Array.Copy(command, 3, discoveryReply, 6, 4); Array.Copy(AliceKey, 0, discoveryReply, 10, 32);
                    var unrelatedDiscovery = (byte[])discoveryReply.Clone(); unrelatedDiscovery[6] ^= 1; await FrameAsync(stream, unrelatedDiscovery);
                    var wrongDiscoveryType = (byte[])discoveryReply.Clone(); wrongDiscoveryType[4] = 0x93; await FrameAsync(stream, wrongDiscoveryType);
                    var routedDiscovery = (byte[])discoveryReply.Clone(); routedDiscovery[3] = 1; await FrameAsync(stream, routedDiscovery);
                    await FrameAsync(stream, discoveryReply.Take(18).ToArray());
                    await FrameAsync(stream, discoveryReply);
                    discoveryReply[1] = 4; discoveryReply[2] = 166; await FrameAsync(stream, discoveryReply);
                    Array.Copy(Enumerable.Repeat((byte)44, 32).ToArray(), 0, discoveryReply, 10, 32); await FrameAsync(stream, discoveryReply);
                    await FrameAsync(stream, new byte[] { 0 }); break;
                case 50:
                    Check(command.Skip(1).Take(32).SequenceEqual(AliceKey) && command[33] == 3, "Remote telemetry request type and full public key");
                    await FrameAsync(stream, new byte[] { 6, 1, 9, 8, 7, 6, 0xE8, 3, 0, 0 });
                    await FrameAsync(stream, new byte[] { 0x8C, 0, 1, 1, 1, 1 }.Concat(SensorData()).ToArray()); // unrelated tag must be ignored
                    await FrameAsync(stream, new byte[] { 0x8C, 0, 9, 8, 7, 6 }.Concat(SensorData()).Concat(new byte[13]).ToArray()); break;
                default: throw new Exception("Unexpected command " + command[0]);
            }
        }

        private static byte[] Contact(byte[] key, string name)
        {
            var bytes = new byte[148]; bytes[0] = 3; key.CopyTo(bytes, 1); bytes[33] = 1; bytes[35] = 255;
            Encoding.UTF8.GetBytes(name).CopyTo(bytes, 100); BitConverter.GetBytes(1700000000u).CopyTo(bytes, 132);
            BitConverter.GetBytes(52123456).CopyTo(bytes, 136); BitConverter.GetBytes(13123456).CopyTo(bytes, 140); return bytes;
        }
        private static byte[] Message(bool direct, bool signed, string text, bool v3 = true, bool room = false)
        {
            using (var memory = new MemoryStream())
            using (var writer = new BinaryWriter(memory))
            {
                writer.Write((byte)(v3 ? (direct ? 16 : 17) : (direct ? 7 : 8)));
                if (v3) { writer.Write((sbyte)-12); writer.Write((ushort)0); }
                if (direct) writer.Write(room ? RoomKey : AliceKey, 0, 6); else writer.Write((byte)0);
                writer.Write((byte)255); writer.Write((byte)(signed ? 2 : 0)); writer.Write(1700000000u);
                if (signed) { if (room) writer.Write(AliceKey, 0, 4); else writer.Write(42u); }
                writer.Write(Encoding.UTF8.GetBytes(text)); return memory.ToArray();
            }
        }
        private static byte[] SensorData() { return new byte[] { 0, 116, 1, 159, 0, 103, 255, 201, 1, 104, 90, 1, 115, 39, 148 }; }
        private static async Task FrameAsync(NetworkStream stream, byte[] frame)
        {
            var header = new byte[] { (byte)'>', (byte)frame.Length, (byte)(frame.Length >> 8) };
            await stream.WriteAsync(header, 0, 1); await stream.WriteAsync(header, 1, 2);
            var split = frame.Length / 2;
            await stream.WriteAsync(frame, 0, split); await stream.WriteAsync(frame, split, frame.Length - split);
        }
        private static async Task<byte[]> ReadAsync(NetworkStream stream, int length)
        {
            var bytes = new byte[length]; var offset = 0;
            while (offset < length) { var count = await stream.ReadAsync(bytes, offset, length - offset); if (count == 0) throw new EndOfStreamException(); offset += count; }
            return bytes;
        }
        public void Dispose() { _stop.Cancel(); _listener.Stop(); if (_connection != null) _connection.Close(); }
    }
}

