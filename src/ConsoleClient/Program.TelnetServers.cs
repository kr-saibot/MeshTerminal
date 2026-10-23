using MessageBox = ConsoleClient.SafeMessageBox;
using Dialog = ConsoleClient.SafeDialog;
using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Net;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Meshtastic.Client;
using Meshtastic.Protobufs;
using NStack;
using Terminal.Gui;
namespace ConsoleClient
{
    internal static partial class Program
    {
        private static TelnetServerManager _telnetServers;
        private static IReadOnlyDictionary<uint, string> _telnetNodeNames = new Dictionary<uint, string>();
        private static readonly SemaphoreSlim TelnetConfigurationGate = new SemaphoreSlim(1, 1);
        private static string TelnetChannelName(int index)
        {
            var channel = _mesh.Channels.FirstOrDefault(c => c.Index == index);
            return channel != null && !String.IsNullOrWhiteSpace(channel.Name) ? channel.Name : "Channel " + index;
        }
        private static string FormatTelnetMessage(StoredMeshMessage message, TelnetServerSettings settings)
        {
            var name = message.Direction == MessageDirection.Outgoing ? "You" : message.SenderName;
            if (String.IsNullOrWhiteSpace(name))
            {
                var number = message.FromNode.GetValueOrDefault(); name = _mesh.Nodes.FirstOrDefault(n => n.Number == number)?.LongName;
                if (String.IsNullOrWhiteSpace(name) && !_telnetNodeNames.TryGetValue(number, out name)) name = "!" + number.ToString("x8");
            }
            return TelnetMessageFormatter.Format(message.OccurredUtc, name, message.Text, settings);
        }
        private static async Task RestartTelnetServersAsync()
        {
            await TelnetConfigurationGate.WaitAsync().ConfigureAwait(false);
            try
            {
                var previous = _telnetServers; _telnetServers = null;
                if (previous != null) await previous.StopAsync().ConfigureAwait(false);
                if (Volatile.Read(ref _shutdownStarted) != 0) return;
                _telnetNodeNames = !_settings.TelnetServers.Any(s => s != null && s.Enabled && s.Protocol == _mesh.Protocol) ? new Dictionary<uint, string>() : _store.GetNodes().ToDictionary(n => n.NodeNumber, n => !String.IsNullOrWhiteSpace(n.LongName) ? n.LongName : !String.IsNullOrWhiteSpace(n.ShortName) ? n.ShortName : n.NodeId);
                var emojiFormatter = new EmojiNameFormatter(EmojiReplacements);
                var smallEmojiArt = new EmojiNameFormatter(EmojiBlocksHalf.Select(b => new KeyValuePair<string, string>(b.Key, "\n" + b.Value.Text + "\n")));
                var largeEmojiArt = new EmojiNameFormatter(EmojiBlocksFull.Select(b => new KeyValuePair<string, string>(b.Key, "\n" + b.Value.Text + "\n")));
                var manager = new TelnetServerManager(_mesh.Protocol, _settings.TelnetServers.ToArray(),
                    async (channelIndex, text, token) =>
                    {
                        if (!_mesh.IsConnected) throw new InvalidOperationException("Mesh device is disconnected.");
                        if (!_mesh.Channels.Any(c => c.Index == channelIndex && c.Role != Channel.Types.Role.Disabled)) throw new InvalidOperationException("Channel is not configured on the device.");
                        token.ThrowIfCancellationRequested();
                        // Finish an already started device write when a Telnet server is stopped.
                        // Canceling that write could close the shared Bluetooth transport.
                        var packet = await _mesh.SendTextWithIdAsync(UInt32.MaxValue, text, channelIndex, false, CancellationToken.None).ConfigureAwait(false);
                        Save(() => { _store.AddOutgoing(packet, _mesh.Device.MyNode == null ? 0 : _mesh.Device.MyNode.MyNodeNum,
                            UInt32.MaxValue, text, channelIndex, TelnetChannelName(channelIndex)); Ui(RefreshChats); });
                    },
                    TelnetChannelName,
                    (channel, count, serverSettings) => count == 0 ? new List<string>() : _store.GetRecentChannelMessages(channel, count, serverSettings.ShowOwnMessages).Where(m =>
                        m.Direction != MessageDirection.Incoming || _mesh.Device.MyNode == null || m.FromNode != _mesh.Device.MyNode.MyNodeNum).Select(m => FormatTelnetMessage(m, serverSettings)).ToList(), emojiFormatter.ToNames, emojiFormatter.ToEmojis, (text, size) => (size == TelnetEmojiArtSize.Large ? largeEmojiArt : smallEmojiArt).ToNames(text));
                manager.Start(); _telnetServers = manager;
            }
            finally { TelnetConfigurationGate.Release(); }
        }
        private static void ApplyTelnetConfiguration()
        {
            MeshtasticSettingsStore.Save("meshtastic-settings.xml", _settings);
            Task.Run(async () => { try { await RestartTelnetServersAsync(); } catch (Exception error) { Ui(() => MessageBox.ErrorQuery("Telnet Server", error.Message, "OK")); } });
        }
        private static void ShowTelnetServers()
        {
            var dialog = new Dialog("Telnet Servers - " + _mesh.Protocol, 110, 19);
            var list = new ListView { X = 1, Y = 1, Width = Dim.Fill(2), Height = Dim.Fill(4) };
            var servers = new List<TelnetServerSettings>();
            Action refresh = () =>
            {
                servers = _settings.TelnetServers.Where(s => s != null && s.Protocol == _mesh.Protocol).ToList();
                list.SetSource(servers.Select(s => (s.Enabled ? "[on] " : "[off] ") + s.Name + " | " + s.BindAddress + ":" + s.Port + " | " + TelegramGatewayChannelName(s.ChannelIndex)).ToList());
            };
            Action edit = () =>
            {
                if (list.SelectedItem < 0 || list.SelectedItem >= servers.Count) return;
                var selected = servers[list.SelectedItem]; var edited = EditTelnetServer(selected);
                if (edited == null) return;
                _settings.TelnetServers[_settings.TelnetServers.IndexOf(selected)] = edited;
                ApplyTelnetConfiguration(); refresh();
            };
            var add = new Button("Add"); add.Clicked += () =>
            {
                var edited = EditTelnetServer(new TelnetServerSettings { Protocol = _mesh.Protocol });
                if (edited == null) return;
                _settings.TelnetServers.Add(edited); ApplyTelnetConfiguration(); refresh();
            };
            var change = new Button("Edit"); change.Clicked += edit;
            list.OpenSelectedItem += e => edit();
            var delete = new Button("Delete"); delete.Clicked += () =>
            {
                if (list.SelectedItem < 0 || list.SelectedItem >= servers.Count) return;
                if (MessageBox.Query("Delete Telnet Server", "Delete " + servers[list.SelectedItem].Name + "?", "Delete", "Cancel") != 0) return;
                _settings.TelnetServers.Remove(servers[list.SelectedItem]); ApplyTelnetConfiguration(); refresh();
            };
            var debug = new Button("Status"); debug.Clicked += ShowTelnetServerDebug;
            var close = new Button("Close", true); close.Clicked += () => Application.RequestStop();
            dialog.Add(list); dialog.AddButton(add); dialog.AddButton(change); dialog.AddButton(delete); dialog.AddButton(debug); dialog.AddButton(close);
            refresh(); Application.Run(dialog, HandleTerminalGuiException);
        }
        private static TelnetServerSettings EditTelnetServer(TelnetServerSettings source)
        {
            var dialog = new Dialog("Edit Telnet Server - " + _mesh.Protocol, 110, 29);
            var enabled = new CheckBox("Enabled") { X = 1, Y = 1, Checked = source.Enabled };
            var name = new TextField(source.Name ?? "") { X = 15, Y = 3, Width = 44 };
            var address = new TextField(source.BindAddress ?? "127.0.0.1") { X = 15, Y = 5, Width = 28 };
            var port = new TextField(source.Port.ToString()) { X = 49, Y = 5, Width = 10 };
            var welcomeEnabled = new CheckBox("Send welcome message") { X = 1, Y = 8, Checked = source.SendWelcome };
            var welcome = new MessageInputView { X = 1, Y = 10, Width = 59, Height = 3, Text = source.WelcomeMessage ?? "" };
            var history = new TextField(source.HistoryCount.ToString()) { X = 42, Y = 15, Width = 8 };
            var bell = new CheckBox("Send BEL for each new channel message") { X = 1, Y = 17, Checked = source.SendBell };
            var showOwn = new CheckBox("Show own sent messages") { X = 1, Y = 18, Checked = source.ShowOwnMessages };
            var emojiNames = new CheckBox("Convert emojis / names in both directions") { X = 1, Y = 19, Checked = source.ReplaceEmojisWithNames };
            var timestamps = new CheckBox("Show timestamps") { X = 64, Y = 17, Checked = source.ShowTimestamps };
            var sender = new CheckBox("Show sender") { X = 64, Y = 18, Checked = source.ShowSender };
            var echo = new CheckBox("Echo client input") { X = 64, Y = 23, Checked = source.EchoInput };
            var readOnly = new CheckBox("Read-only (disable sending)") { X = 64, Y = 19, Checked = source.ReadOnly };
            var emojiArt = new CheckBox("Send emojis as ASCII art") { X = 1, Y = 20, Checked = source.SendEmojiArt };
            var artSize = new RadioGroup(new Rect(64, 21, 37, 2), new ustring[] { "Small (24 x 12)", "Large (48 x 24)" }) { SelectedItem = source.EmojiArtSize == TelnetEmojiArtSize.Large ? 1 : 0 };
            var passwordEnabled = new CheckBox("Require password") { X = 1, Y = 21, Checked = source.RequirePassword };
            var password = new TextField("") { X = 24, Y = 21, Width = 35, Secret = true };
            var channelFrame = new FrameView("Channel") { X = 63, Y = 1, Width = Dim.Fill(1), Height = 16 };
            var channelCount = ConfigurationChannelCount(source.ChannelIndex);
            var channels = new ListView(Enumerable.Range(0, channelCount).Select(TelegramGatewayChannelName).ToList()) { X = 0, Y = 0, Width = Dim.Fill(), Height = Dim.Fill() };
            channels.SelectedItem = Math.Max(0, source.ChannelIndex); channelFrame.Add(channels);
            dialog.Add(enabled, new Label("Name:") { X = 1, Y = 3 }, name,
                new Label("Bind address:") { X = 1, Y = 5 }, address, new Label("Port:") { X = 44, Y = 5 }, port,
                new Label("127.0.0.1: local | LAN IP: reachable on that interface") { X = 1, Y = 6 },
                welcomeEnabled, new Label("{channel} is replaced with the channel name.") { X = 1, Y = 9 }, welcome,
                new Label("Last messages on connect (0-1000):") { X = 1, Y = 15 }, history, channelFrame, bell, showOwn, echo, emojiNames, timestamps, sender, readOnly, emojiArt, new Label("ASCII emoji size:") { X = 64, Y = 20 }, artSize, passwordEnabled, password, new Label("Leave password blank to keep the existing password.") { X = 1, Y = 22 });
            TelnetServerSettings result = null;
            var save = new Button("Save", true); save.Clicked += () =>
            {
                int parsedPort, parsedHistory; IPAddress parsedAddress;
                if (!Int32.TryParse(port.Text.ToString(), out parsedPort) || parsedPort < 1 || parsedPort > 65535 ||
                    !Int32.TryParse(history.Text.ToString(), out parsedHistory) || parsedHistory < 0 || parsedHistory > 1000 ||
                    !IPAddress.TryParse(address.Text.ToString().Trim(), out parsedAddress) || channels.SelectedItem < 0)
                { MessageBox.ErrorQuery("Telnet Server", "Enter a valid IP address, port 1-65535, channel and history count 0-1000.", "OK"); return; }
                if (enabled.Checked && _settings.TelnetServers.Any(s => s != source && s.Protocol == _mesh.Protocol && s.Enabled && s.Port == parsedPort))
                { MessageBox.ErrorQuery("Telnet Server", "Another active server already uses this port.", "OK"); return; }
                var passwordHash = String.IsNullOrEmpty(password.Text.ToString()) ? source.PasswordHash : TelnetPassword.Hash(password.Text.ToString());
                if (passwordEnabled.Checked && String.IsNullOrEmpty(passwordHash)) { MessageBox.ErrorQuery("Telnet Server", "Enter a password first.", "OK"); return; }
                result = new TelnetServerSettings { Protocol = _mesh.Protocol, Name = name.Text.ToString().Trim(), Enabled = enabled.Checked,
                    BindAddress = parsedAddress.ToString(), Port = parsedPort, ChannelIndex = channels.SelectedItem,
                    SendWelcome = welcomeEnabled.Checked, WelcomeMessage = welcome.Text.ToString(), HistoryCount = parsedHistory, SendBell = bell.Checked, ShowOwnMessages = showOwn.Checked, ReplaceEmojisWithNames = emojiNames.Checked, SendEmojiArt = emojiArt.Checked, EmojiArtSize = artSize.SelectedItem == 1 ? TelnetEmojiArtSize.Large : TelnetEmojiArtSize.Small, ShowTimestamps = timestamps.Checked, ShowSender = sender.Checked, ReadOnly = readOnly.Checked, EchoInput = echo.Checked, RequirePassword = passwordEnabled.Checked, PasswordHash = passwordHash };
                if (String.IsNullOrWhiteSpace(result.Name)) result.Name = "Telnet " + parsedPort;
                Application.RequestStop();
            };
            var cancel = new Button("Cancel"); cancel.Clicked += () => Application.RequestStop();
            dialog.AddButton(save); dialog.AddButton(cancel); Application.Run(dialog, HandleTerminalGuiException);
            return result;
        }
        private static void ShowTelnetServerDebug()
        {
            var dialog = new Dialog("Telnet Server status", 110, 24);
            var text = new TextView { ReadOnly = true, WordWrap = true, X = 1, Y = 1, Width = Dim.Fill(2), Height = Dim.Fill(3) };
            Action refresh = () =>
            {
                var status = new StringBuilder();
                status.AppendLine("Mesh: " + _mesh.Protocol + " / " + _mesh.State);
                var servers = _telnetServers?.GetStatuses();
                if (servers == null || servers.Count == 0) status.AppendLine("No Telnet servers configured for this protocol.");
                else foreach (var server in servers)
                {
                    status.AppendLine(); status.AppendLine(server.Name + " | " + server.State + " | " + (server.Endpoint ?? "-"));
                    status.AppendLine("Channel: " + (server.ChannelName ?? TelnetChannelName(server.ChannelIndex)) + " [" + server.ChannelIndex + "]");
                    status.AppendLine("Clients: " + server.Clients.Length + " | Sent to mesh: " + server.ForwardedToMesh);
                    foreach (var client in server.Clients) status.AppendLine("  " + client);
                    if (!String.IsNullOrWhiteSpace(server.LastError)) status.AppendLine("Last error: " + server.LastError);
                }
                text.Text = status.ToString();
            };
            dialog.Add(text);
            var close = new Button("Close", true); close.Clicked += () => Application.RequestStop(); dialog.AddButton(close);
            refresh(); var timer = Application.MainLoop.AddTimeout(TimeSpan.FromSeconds(1), loop => { refresh(); return true; });
            try { Application.Run(dialog, HandleTerminalGuiException); } finally { Application.MainLoop.RemoveTimeout(timer); }
        }
    }
}