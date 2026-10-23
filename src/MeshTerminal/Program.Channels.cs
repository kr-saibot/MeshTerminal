using MessageBox = MeshTerminal.SafeMessageBox;
using Dialog = MeshTerminal.SafeDialog;
using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Security.Cryptography;
using System.Threading;
using System.Threading.Tasks;
using Google.Protobuf;
using MeshTerminal.Client;
using Meshtastic.Protobufs;
using Terminal.Gui;

namespace MeshTerminal
{
    internal static partial class Program
    {
        private static int _channelEditorActive;

        private static void ShowChannelEditor()
        {
            if (!_mesh.IsConnected || _mesh.Device.MyNode == null) { MessageBox.Query("Channels", "Connect and synchronize the device first.", "OK"); return; }
            if (Interlocked.CompareExchange(ref _channelEditorActive, 1, 0) != 0) { MessageBox.Query("Channels", "The channel editor is already loading or saving.", "OK"); return; }
            Task.Run(async delegate
            {
                try { var slots = await _mesh.GetChannelSlotsAsync(); Ui(delegate { RunChannelEditor(slots); }); }
                catch (Exception ex) { Interlocked.Exchange(ref _channelEditorActive, 0); Ui(delegate { MessageBox.ErrorQuery("Channels", ex.Message, "OK"); }); }
            });
        }

        private static string ChannelName(Channel channel)
        {
            return String.IsNullOrEmpty(channel.Settings == null ? null : channel.Settings.Name) ? "(default name)" : channel.Settings.Name;
        }

        private static string ChannelDescription(Channel channel, bool device, MeshProtocol protocol)
        {
            if (channel == null) return "No channel selected.";
            var s = channel.Settings ?? new ChannelSettings();
            return (device ? "Slot: " + channel.Index + "\nRole: " + channel.Role + "\n" : "Stored locally on this PC\n") +
                "Name: " + ChannelName(channel) + "\nKey: " + s.Psk.Length + " bytes (hidden)\n" +
                (protocol == MeshProtocol.MeshCore || s.ModuleSettings == null ? "" : "Position precision: " + s.ModuleSettings.PositionPrecision + "\nMuted: " + (s.ModuleSettings.IsMuted ? "yes" : "no") + "\n") +
                (protocol == MeshProtocol.MeshCore ? "" : "MQTT uplink: " + (s.UplinkEnabled ? "yes" : "no") + "\nMQTT downlink: " + (s.DownlinkEnabled ? "yes" : "no"));
        }

        private static void RunChannelEditor(IReadOnlyList<Channel> original)
        {
            try
            {
                var protocol = _mesh.Protocol;
                var deviceId = _mesh.Device.NodeId;
                LocalChannel.MigrateDrafts(_settings);
                MeshtasticSettingsStore.Save("meshtastic-settings.xml", _settings);
                var slots = original.Select(c => c.Clone()).ToList();
                var deviceChannels = new List<Channel>();
                var localEntries = new List<LocalChannel>();
                var localChannels = new List<Channel>();
                bool busy = false;
                var dialog = new Dialog(protocol + " channels", 112, 30);
                var deviceFrame = new FrameView("Channels on device") { X = 1, Y = 0, Width = 33, Height = 12 };
                var deviceList = new ListView(new List<string>()) { X = 0, Y = 0, Width = Dim.Fill(), Height = Dim.Fill() };
                deviceFrame.Add(deviceList);
                var localFrame = new FrameView("Local channels (" + protocol + ")") { X = 53, Y = 0, Width = Dim.Fill(2), Height = 12 };
                var localList = new ListView(new List<string>()) { X = 0, Y = 0, Width = Dim.Fill(), Height = Dim.Fill() };
                localFrame.Add(localList);
                var deviceDetails = new TextView { X = 1, Y = 13, Width = 33, Height = 8, ReadOnly = true, WordWrap = true, AllowsTab = false };
                var localDetails = new TextView { X = 53, Y = 13, Width = Dim.Fill(2), Height = 8, ReadOnly = true, WordWrap = true, AllowsTab = false };
                var status = new Label("Select a device channel or a local channel.") { X = 1, Y = 25, Width = Dim.Fill(2) };
                Func<Channel> currentDevice = () => deviceList.SelectedItem >= 0 && deviceList.SelectedItem < deviceChannels.Count ? deviceChannels[deviceList.SelectedItem] : null;
                Func<Channel> currentLocal = () => localList.SelectedItem >= 0 && localList.SelectedItem < localChannels.Count ? localChannels[localList.SelectedItem] : null;
                var toLocal = new Button("Copy to PC ->") { X = 35, Y = 4 };
                var toDevice = new Button("<- To device") { X = 35, Y = 7 };
                var removeDevice = new Button("Delete from device") { X = 1, Y = 22 };
                var newLocal = new Button("New") { X = 53, Y = 22 };
                var editLocal = new Button("Edit") { X = 63, Y = 22 };
                var removeLocal = new Button("Delete") { X = 74, Y = 22 };
                var deviceQr = new Button("QR") { X = 25, Y = 22 };
                var localQr = new Button("QR") { X = 87, Y = 22 };
                var reload = new Button("Reload device");
                var close = new Button("Close", true);
                Action updateSelection = delegate
                {
                    var d = currentDevice(); var l = currentLocal();
                    deviceDetails.Text = ChannelDescription(d, true, protocol); localDetails.Text = ChannelDescription(l, false, protocol);
                    toLocal.Enabled = !busy && d != null; toDevice.Enabled = !busy && l != null;
                    removeDevice.Enabled = !busy && d != null && (protocol == MeshProtocol.MeshCore || d.Role != Channel.Types.Role.Primary);
                    editLocal.Enabled = removeLocal.Enabled = localQr.Enabled = !busy && l != null;
                    deviceQr.Enabled = !busy && d != null;
                };
                Action refresh = delegate
                {
                    int di = deviceList.SelectedItem, li = localList.SelectedItem;
                    deviceChannels = slots.Where(c => c.Role != Channel.Types.Role.Disabled).OrderBy(c => c.Index).ToList();
                    localEntries = new List<LocalChannel>(); localChannels = new List<Channel>();
                    foreach (var entry in _settings.LocalChannels.Where(e => e.Protocol == protocol))
                    {
                        try { localChannels.Add(entry.Read()); localEntries.Add(entry); }
                        catch (Exception ex) when (ex is FormatException || ex is InvalidProtocolBufferException) { }
                    }
                    var ordered = localEntries.Select((e, i) => new { Entry = e, Channel = localChannels[i] }).OrderBy(e => ChannelName(e.Channel)).ToList();
                    localEntries = ordered.Select(e => e.Entry).ToList(); localChannels = ordered.Select(e => e.Channel).ToList();
                    deviceList.SetSource(deviceChannels.Select(c => c.Index + ": " + ChannelName(c) + (c.Role == Channel.Types.Role.Primary ? " [primary]" : "")).ToList());
                    localList.SetSource(localChannels.Select(ChannelName).ToList());
                    if (deviceChannels.Count > 0) deviceList.SelectedItem = Math.Max(0, Math.Min(di, deviceChannels.Count - 1));
                    if (localChannels.Count > 0) localList.SelectedItem = Math.Max(0, Math.Min(li, localChannels.Count - 1));
                    updateSelection();
                };
                deviceList.SelectedItemChanged += delegate { updateSelection(); };
                localList.SelectedItemChanged += delegate { updateSelection(); };
                Action<bool> setBusy = value =>
                {
                    busy = value; deviceList.Enabled = localList.Enabled = newLocal.Enabled = reload.Enabled = close.Enabled = !value;
                    updateSelection();
                };
                Action<List<LocalChannel>> saveLibrary = entries =>
                {
                    var previous = _settings.LocalChannels; _settings.LocalChannels = entries;
                    try { MeshtasticSettingsStore.Save("meshtastic-settings.xml", _settings); }
                    catch { _settings.LocalChannels = previous; throw; }
                    refresh();
                };
                Action<Channel, string> saveLocal = (channel, editingId) =>
                {
                    try
                    {
                        var conflicts = localEntries.Where((e, i) => e.Id != editingId && LocalChannel.SameName(localChannels[i], channel)).ToList();
                        if (conflicts.Count > 0 && MessageBox.Query("Overwrite local channel", "A local channel named '" + ChannelName(channel) + "' already exists. Replace it?", "Overwrite", "Cancel") != 0) return;
                        var entry = LocalChannel.Create(protocol, channel, editingId ?? (conflicts.Count > 0 ? conflicts[0].Id : null));
                        var entries = _settings.LocalChannels.Where(e => e.Id != editingId && !conflicts.Contains(e)).ToList(); entries.Add(entry);
                        saveLibrary(entries);
                        var index = localEntries.FindIndex(e => e.Id == entry.Id); if (index >= 0) localList.SelectedItem = index;
                        status.Text = "Channel saved locally. The device was not changed.";
                    }
                    catch (Exception ex) { MessageBox.ErrorQuery("Local channel", ex.Message, "OK"); }
                };
                Action<Channel> writeDevice = desired =>
                {
                    var snapshot = slots.Select(c => c.Clone()).ToList();
                    setBusy(true); status.Text = "Checking and updating device...";
                    Task.Run(async delegate
                    {
                        try
                        {
                            if (!_mesh.IsConnected || _mesh.Protocol != protocol || _mesh.Device.NodeId != deviceId) throw new InvalidOperationException("Reconnect and reopen the channel editor.");
                            var fresh = await _mesh.GetChannelSlotsAsync();
                            if (!_mesh.IsConnected || _mesh.Protocol != protocol || _mesh.Device.NodeId != deviceId) throw new InvalidOperationException("Device changed. Reopen the channel editor.");
                            if (!snapshot.SequenceEqual(fresh)) throw new InvalidOperationException("Device channels changed. Reload the device list and try again.");
                            var actual = await _mesh.SaveChannelAsync(desired);
                            if (_mesh.Protocol != protocol || _mesh.Device.NodeId != deviceId) throw new InvalidOperationException("Device changed. Reopen the channel editor.");
                            Ui(delegate { var index = slots.FindIndex(c => c.Index == actual.Index); slots[index] = actual; refresh(); RefreshChats(); status.Text = "Device change saved and verified. Local channels were kept."; });
                        }
                        catch (Exception ex) { Ui(delegate { status.Text = "Device change could not be confirmed. Reload the device list."; MessageBox.ErrorQuery("Channels", ex.Message, "OK"); }); }
                        finally { Ui(delegate { setBusy(false); }); }
                    });
                };
                toLocal.Clicked += delegate { if (!busy && currentDevice() != null) saveLocal(currentDevice().Clone(), null); };
                newLocal.Clicked += delegate
                {
                    if (busy) return;
                    var c = EditLocalChannel(protocol, null); if (c != null) saveLocal(c, null);
                };
                editLocal.Clicked += delegate
                {
                    if (busy || currentLocal() == null) return;
                    var id = localEntries[localList.SelectedItem].Id;
                    var c = EditLocalChannel(protocol, currentLocal()); if (c != null) saveLocal(c, id);
                };
                removeLocal.Clicked += delegate
                {
                    if (busy || currentLocal() == null) return;
                    var entry = localEntries[localList.SelectedItem];
                    if (MessageBox.Query("Delete local channel", "Delete '" + ChannelName(currentLocal()) + "' from this PC? The device will not be changed.", "Delete", "Cancel") != 0) return;
                    try { saveLibrary(_settings.LocalChannels.Where(e => e != entry).ToList()); status.Text = "Local channel deleted."; }
                    catch (Exception ex) { MessageBox.ErrorQuery("Local channel", ex.Message, "OK"); }
                };
                toDevice.Clicked += delegate
                {
                    if (busy || currentLocal() == null) return;
                    var source = currentLocal().Clone();
                    var matches = slots.Where(c => c.Role != Channel.Types.Role.Disabled && LocalChannel.SameName(c, source)).ToList();
                    var target = ChooseDeviceChannelSlot(slots, source, matches.Count > 0 ? matches[0].Index : (int?)null);
                    if (target == null) return;
                    if (matches.Count > 0 && !matches.Any(c => c.Index == target.Index))
                    { MessageBox.Query("Channel already on device", "This name already exists on device in slot(s) " + String.Join(", ", matches.Select(c => c.Index)) + ". Select that channel's slot to overwrite it.", "OK"); return; }
                    if ((target.Role != Channel.Types.Role.Disabled || matches.Count > 0) && MessageBox.Query("Overwrite device channel", "Replace '" + ChannelName(target) + "' in slot " + target.Index + " with '" + ChannelName(source) + "'?", "Overwrite", "Cancel") != 0) return;
                    source.Index = target.Index;
                    source.Role = target.Role == Channel.Types.Role.Primary ? Channel.Types.Role.Primary : Channel.Types.Role.Secondary;
                    writeDevice(source);
                };
                removeDevice.Clicked += delegate
                {
                    if (busy || currentDevice() == null) return;
                    var target = currentDevice();
                    if (protocol == MeshProtocol.Meshtastic && target.Role == Channel.Types.Role.Primary) return;
                    if (MessageBox.Query("Delete device channel", "Delete '" + ChannelName(target) + "' in slot " + target.Index + " from the device? Local copies will be kept.", "Delete", "Cancel") != 0) return;
                    writeDevice(new Channel { Index = target.Index, Role = Channel.Types.Role.Disabled, Settings = new ChannelSettings() });
                };
                deviceQr.Clicked += delegate { try { if (currentDevice() != null) ShowChannelQr(ChannelSharing.CreateUri(protocol, currentDevice()), protocol); } catch (Exception ex) { MessageBox.ErrorQuery("Channel QR", ex.Message, "OK"); } };
                localQr.Clicked += delegate { try { if (currentLocal() != null) ShowChannelQr(ChannelSharing.CreateUri(protocol, currentLocal()), protocol); } catch (Exception ex) { MessageBox.ErrorQuery("Channel QR", ex.Message, "OK"); } };
                reload.Clicked += delegate
                {
                    if (busy) return; setBusy(true); status.Text = "Loading device channels...";
                    Task.Run(async delegate
                    {
                        try
                        {
                            if (!_mesh.IsConnected || _mesh.Protocol != protocol || _mesh.Device.NodeId != deviceId) throw new InvalidOperationException("Reconnect and reopen the channel editor.");
                            var fresh = await _mesh.GetChannelSlotsAsync();
                            if (!_mesh.IsConnected || _mesh.Protocol != protocol || _mesh.Device.NodeId != deviceId) throw new InvalidOperationException("Device changed. Reopen the channel editor.");
                            Ui(delegate { slots = fresh.Select(c => c.Clone()).ToList(); refresh(); status.Text = "Device list reloaded."; });
                        }
                        catch (Exception ex) { Ui(delegate { MessageBox.ErrorQuery("Channels", ex.Message, "OK"); }); }
                        finally { Ui(delegate { setBusy(false); }); }
                    });
                };
                close.Clicked += delegate { if (!busy) Application.RequestStop(); };
                dialog.Closing += e => { if (busy) e.Cancel = true; };
                dialog.Add(deviceFrame, localFrame,
                    new Label("Device settings") { X = 1, Y = 12 }, new Label("Local settings") { X = 53, Y = 12 }, deviceDetails, localDetails,
                    toLocal, toDevice, removeDevice, newLocal, editLocal, removeLocal, deviceQr, localQr, status,
                    new Label("Copy a device channel to PC to edit it, then transfer it back.") { X = 1, Y = 24 });
                dialog.AddButton(reload); dialog.AddButton(close); refresh();
                Application.Run(dialog, HandleTerminalGuiException);
            }
            catch (Exception ex) { MessageBox.ErrorQuery("Channels", ex.Message, "OK"); }
            finally { Interlocked.Exchange(ref _channelEditorActive, 0); }
        }

        private static Channel ChooseDeviceChannelSlot(List<Channel> slots, Channel source, int? preferred)
        {
            var dialog = new Dialog("Target slot for " + ChannelName(source), 74, 20);
            var list = new ListView(slots.Select(c => "Slot " + c.Index + ": " + (c.Role == Channel.Types.Role.Disabled ? "[free]" : ChannelName(c) + (c.Role == Channel.Types.Role.Primary ? " [primary]" : ""))).ToList()) { X = 1, Y = 1, Width = Dim.Fill(2), Height = Dim.Fill(3) };
            int index = preferred.HasValue ? slots.FindIndex(c => c.Index == preferred.Value) : slots.FindIndex(c => c.Role == Channel.Types.Role.Disabled);
            if (index >= 0) list.SelectedItem = index;
            Channel selected = null;
            var use = new Button("Use slot"); use.Clicked += delegate { if (list.SelectedItem >= 0 && list.SelectedItem < slots.Count) { selected = slots[list.SelectedItem].Clone(); Application.RequestStop(); } };
            var cancel = new Button("Cancel", true); cancel.Clicked += delegate { Application.RequestStop(); };
            dialog.Add(list); dialog.AddButton(use); dialog.AddButton(cancel); Application.Run(dialog); return selected;
        }

        private static Channel EditLocalChannel(MeshProtocol protocol, Channel original)
        {
            bool core = protocol == MeshProtocol.MeshCore;
            var channel = original == null ? new Channel { Role = Channel.Types.Role.Secondary, Settings = new ChannelSettings() } : original.Clone();
            if (channel.Settings == null) channel.Settings = new ChannelSettings();
            var s = channel.Settings;
            var dialog = new Dialog(original == null ? "New local channel" : "Edit local channel", 84, core ? 17 : 22);
            var name = new TextField(s.Name) { X = 20, Y = 1, Width = 57 };
            var key = new TextField(BitConverter.ToString(s.Psk.ToByteArray()).Replace("-", "")) { X = 2, Y = 5, Width = 75, Secret = true };
            var show = new CheckBox("Show key") { X = 2, Y = 6 }; show.Toggled += delegate { key.Secret = !show.Checked; };
            Action randomKey = delegate { var bytes = new byte[core ? 16 : 32]; using (var random = RandomNumberGenerator.Create()) random.GetBytes(bytes); key.Text = BitConverter.ToString(bytes).Replace("-", ""); };
            if (original == null) randomKey();
            var randomButton = new Button("New random key") { X = 2, Y = 8 }; randomButton.Clicked += delegate { randomKey(); };
            var defaultButton = new Button("Default key") { X = 27, Y = 8, Visible = !core }; defaultButton.Clicked += delegate { key.Text = "01"; };
            var hashtag = new CheckBox("Derive key from hashtag name") { X = 2, Y = 10, Visible = core,
                Checked = core && MeshCoreHashtag.Matches(s.Name, s.Psk.ToByteArray()) };
            var hashtagInfo = new Label("") { X = 2, Y = 11, Width = 75, Visible = core };
            bool wasHashtag = name.Text.ToString().StartsWith("#", StringComparison.Ordinal);
            Action updateHashtag = delegate
            {
                if (!core) return;
                var text = name.Text.ToString();
                bool hasPrefix = text.StartsWith("#", StringComparison.Ordinal);
                hashtag.Enabled = hasPrefix;
                if (!hasPrefix) hashtag.Checked = false;
                bool derive = hasPrefix && hashtag.Checked;
                key.ReadOnly = derive; randomButton.Enabled = !derive;
                if (derive)
                {
                    try { key.Text = BitConverter.ToString(MeshCoreHashtag.DeriveKey(text)).Replace("-", ""); hashtagInfo.Text = "Key follows the exact name, including # and letter case."; }
                    catch (ArgumentException ex) { key.Text = ""; hashtagInfo.Text = ex.Message; }
                }
                else hashtagInfo.Text = hasPrefix ? "Manual key: existing channel key is kept." : "Start the name with # to create a hashtag channel.";
            };
            hashtag.Toggled += delegate { updateHashtag(); };
            name.TextChanged += delegate
            {
                if (!core) return;
                var hasPrefix = name.Text.ToString().StartsWith("#", StringComparison.Ordinal);
                if (!hasPrefix && wasHashtag && hashtag.Checked) randomKey();
                if (hasPrefix && !wasHashtag) hashtag.Checked = true;
                wasHashtag = hasPrefix; updateHashtag();
            };
            updateHashtag();
            var up = new CheckBox("MQTT uplink") { X = 2, Y = 10, Visible = !core, Checked = s.UplinkEnabled };
            var down = new CheckBox("MQTT downlink") { X = 28, Y = 10, Visible = !core, Checked = s.DownlinkEnabled };
            var precision = new TextField((s.ModuleSettings == null ? 0 : s.ModuleSettings.PositionPrecision).ToString()) { X = 27, Y = 12, Width = 6, Visible = !core };
            var muted = new CheckBox("Mute channel") { X = 2, Y = 14, Visible = !core, Checked = s.ModuleSettings != null && s.ModuleSettings.IsMuted };
            Func<Channel> read = delegate
            {
                var result = channel.Clone(); result.Role = Channel.Types.Role.Secondary; result.Settings.Name = name.Text.ToString();
                var text = key.Text.ToString().Trim();
                byte[] bytes;
                if (core && hashtag.Checked) bytes = MeshCoreHashtag.DeriveKey(result.Settings.Name);
                else if (!core && text.StartsWith("base64:", StringComparison.OrdinalIgnoreCase)) bytes = Convert.FromBase64String(text.Substring(7));
                else
                {
                    text = text.Replace(" ", "").Replace("-", "");
                    if (text.Length % 2 != 0) throw new FormatException("Hex keys need an even number of characters.");
                    bytes = Enumerable.Range(0, text.Length / 2).Select(i => Byte.Parse(text.Substring(i * 2, 2), NumberStyles.HexNumber, CultureInfo.InvariantCulture)).ToArray();
                }
                result.Settings.Psk = ByteString.CopyFrom(bytes);
                if (!core)
                {
                    uint bits; if (!UInt32.TryParse(precision.Text.ToString(), out bits) || bits > 32) throw new FormatException("Position precision must be between 0 and 32.");
                    result.Settings.UplinkEnabled = up.Checked; result.Settings.DownlinkEnabled = down.Checked;
                    if (result.Settings.ModuleSettings == null) result.Settings.ModuleSettings = new ModuleSettings();
                    result.Settings.ModuleSettings.PositionPrecision = bits; result.Settings.ModuleSettings.IsMuted = muted.Checked;
                }
                LocalChannel.Validate(protocol, result); return result;
            };
            Channel edited = null;
            var save = new Button("Save locally"); save.Clicked += delegate { try { edited = read(); Application.RequestStop(); } catch (Exception ex) { MessageBox.ErrorQuery("Local channel", ex.Message, "OK"); } };
            var qr = new Button("Show QR code"); qr.Clicked += delegate { try { ShowChannelQr(ChannelSharing.CreateUri(protocol, read()), protocol); } catch (Exception ex) { MessageBox.ErrorQuery("Channel QR", ex.Message, "OK"); } };
            var cancel = new Button("Cancel", true); cancel.Clicked += delegate { Application.RequestStop(); };
            dialog.Add(new Label("Name:") { X = 2, Y = 1 }, name, new Label(core ? "Key: 32 hex characters (16 bytes)" : "Key: hex, or base64: followed by a Base64 key") { X = 2, Y = 3 }, key, show, randomButton, defaultButton, hashtag, hashtagInfo, up, down,
                new Label("Position precision:") { X = 2, Y = 12, Visible = !core }, precision, muted);
            dialog.AddButton(save); dialog.AddButton(qr); dialog.AddButton(cancel); Application.Run(dialog); return edited;
        }
    }
}
