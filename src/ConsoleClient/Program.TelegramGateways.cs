using MessageBox = ConsoleClient.SafeMessageBox;
using Dialog = ConsoleClient.SafeDialog;
using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Threading.Tasks;
using Meshtastic.Client;
using Terminal.Gui;

namespace ConsoleClient
{
    internal static partial class Program
    {
        private static int ConfigurationChannelCount(int savedIndex = -1)
        {
            var configuredMax = _mesh.Channels.Select(c => c.Index).DefaultIfEmpty(-1).Max();
            return ChannelConfiguration.Count(_mesh.Protocol,
                _mesh.IsConnected && _mesh.Device.MeshCoreProtocolVersion.HasValue,
                _mesh.ChannelSlotCount, Math.Max(savedIndex, configuredMax));
        }

        private static void ShowTelegramGateways()
        {
            var protocol = _mesh.Protocol;
            var dialog = new Dialog("Telegram Gateways - " + protocol, 110, 19);
            var list = new ListView { X = 1, Y = 1, Width = Dim.Fill(2), Height = Dim.Fill(5) };
            var feedback = new Label("") { X = 1, Y = Pos.AnchorEnd(4), Width = Dim.Fill(2) };
            var gateways = new List<TelegramGatewaySettings>();
            bool applying = false;
            Action refresh = delegate
            {
                var selected = list.SelectedItem;
                gateways = GetTelegramGatewaysForCurrentProtocol();
                list.SetSource(gateways.Select(FormatTelegramGateway).ToList());
                if (gateways.Count > 0) list.SelectedItem = Math.Max(0, Math.Min(selected, gateways.Count - 1));
            };
            Action apply = delegate
            {
                if (applying) return;
                try { MeshtasticSettingsStore.Save("meshtastic-settings.xml", _settings); }
                catch (Exception ex) { MessageBox.ErrorQuery("Telegram Gateways", ex.Message, "OK"); return; }
                applying = true; list.Enabled = false; feedback.Text = "Applying gateway configuration...";
                Task.Run(async delegate
                {
                    string result;
                    try { await RestartTelegramGatewaysAsync(); result = "Gateway configuration applied."; }
                    catch (Exception ex) { result = "Gateway restart failed: " + ex.Message; }
                    Ui(delegate { applying = false; list.Enabled = true; refresh(); feedback.Text = result; });
                });
            };
            Action edit = delegate
            {
                if (applying || list.SelectedItem < 0 || list.SelectedItem >= gateways.Count) return;
                var selected = gateways[list.SelectedItem]; var edited = EditTelegramGateway(selected);
                if (edited == null) return;
                _settings.TelegramGateways[_settings.TelegramGateways.IndexOf(selected)] = edited; refresh(); apply();
            };
            var add = new Button("Add"); add.Clicked += delegate
            {
                if (applying) return;
                var used = new HashSet<int>(gateways.Select(g => g.ChannelIndex));
                var available = Enumerable.Range(0, ConfigurationChannelCount()).Where(i => !used.Contains(i)).ToList();
                if (available.Count == 0) { MessageBox.Query("Telegram Gateways", "All channels already have a gateway. Edit an existing gateway.", "OK"); return; }
                var channel = available[0];
                var edited = EditTelegramGateway(new TelegramGatewaySettings { Protocol = protocol, ChannelIndex = channel });
                if (edited == null) return;
                _settings.TelegramGateways.Add(edited); refresh(); apply();
            };
            var change = new Button("Edit"); change.Clicked += edit; list.OpenSelectedItem += e => edit();
            var delete = new Button("Delete"); delete.Clicked += delegate
            {
                if (applying || list.SelectedItem < 0 || list.SelectedItem >= gateways.Count) return;
                var selected = gateways[list.SelectedItem];
                if (MessageBox.Query("Delete Telegram Gateway", "Delete " + (String.IsNullOrWhiteSpace(selected.Name) ? TelegramGatewayChannelName(selected.ChannelIndex) : selected.Name) + "?", "Delete", "Cancel") != 0) return;
                _settings.TelegramGateways.Remove(selected); refresh(); apply();
            };
            var restart = new Button("Apply / reconnect"); restart.Clicked += delegate { apply(); };
            var status = new Button("Status"); status.Clicked += ShowTelegramGatewayStatus;
            var close = new Button("Close", true); close.Clicked += delegate { if (!applying) Application.RequestStop(); };
            dialog.Closing += e => { if (applying) e.Cancel = true; };
            dialog.Add(list, feedback); dialog.AddButton(add); dialog.AddButton(change); dialog.AddButton(delete); dialog.AddButton(restart); dialog.AddButton(status); dialog.AddButton(close);
            refresh(); Application.Run(dialog, HandleTerminalGuiException);
        }

        private static TelegramGatewaySettings EditTelegramGateway(TelegramGatewaySettings source)
        {
            var protocol = source.Protocol;
            var dialog = new Dialog("Edit Telegram Gateway - " + protocol, 110, 26);
            var enabled = new CheckBox("Enabled") { X = 1, Y = 1, Checked = source.Enabled };
            var name = new TextField(source.Name ?? "") { X = 15, Y = 3, Width = 43 };
            var meshToTelegram = new CheckBox("Forward " + protocol + " -> Telegram") { X = 1, Y = 5, Checked = source.ForwardMeshtasticToTelegram };
            var telegramToMesh = new CheckBox("Forward Telegram -> " + protocol) { X = 1, Y = 6, Checked = source.ForwardTelegramToMeshtastic };
            var token = new TextField(source.BotToken ?? "") { X = 1, Y = 9, Width = 57, Secret = true };
            var showToken = new CheckBox("Show token") { X = 1, Y = 10 }; showToken.Toggled += delegate { token.Secret = !showToken.Checked; };
            var chatId = new TextField(source.ChatId == 0 ? "" : source.ChatId.ToString(CultureInfo.InvariantCulture)) { X = 20, Y = 12, Width = 38 };
            var channelFrame = new FrameView("Channel") { X = 63, Y = 1, Width = Dim.Fill(1), Height = 14 };
            var channels = new ListView(Enumerable.Range(0, ConfigurationChannelCount(source.ChannelIndex)).Select(TelegramGatewayChannelName).ToList()) { X = 0, Y = 0, Width = Dim.Fill(), Height = Dim.Fill() };
            channels.SelectedItem = source.ChannelIndex; channelFrame.Add(channels);
            var feedbackFrame = new FrameView("Telegram server feedback (saved gateway)") { X = 1, Y = 15, Width = Dim.Fill(1), Height = 7 };
            var statuses = _telegramGateways == null ? new List<TelegramGatewayStatus>() : _telegramGateways.GetStatuses().ToList();
            var feedback = new TextView { X = 0, Y = 0, Width = Dim.Fill(), Height = Dim.Fill(), ReadOnly = true, WordWrap = true, AllowsTab = false,
                Text = BuildTelegramGatewayFeedback(source, statuses.FirstOrDefault(s => s.ChannelIndex == source.ChannelIndex)) };
            feedbackFrame.Add(feedback);
            dialog.Add(enabled, new Label("Name:") { X = 1, Y = 3 }, name, meshToTelegram, telegramToMesh,
                new Label("Bot API token:") { X = 1, Y = 8 }, token, showToken,
                new Label("Telegram chat ID:") { X = 1, Y = 12 }, chatId, channelFrame, feedbackFrame);
            TelegramGatewaySettings result = null;
            var save = new Button("Save", true); save.Clicked += delegate
            {
                long parsedChatId;
                if (!Int64.TryParse(chatId.Text.ToString(), NumberStyles.Integer, CultureInfo.InvariantCulture, out parsedChatId))
                { if (!String.IsNullOrWhiteSpace(chatId.Text.ToString()) || enabled.Checked) { MessageBox.ErrorQuery("Telegram Gateway", "Enter a numeric Telegram chat ID.", "OK"); return; } parsedChatId = 0; }
                if (enabled.Checked && (parsedChatId == 0 || String.IsNullOrWhiteSpace(token.Text.ToString())))
                { MessageBox.ErrorQuery("Telegram Gateway", "An enabled gateway needs a bot token and a numeric Telegram chat ID.", "OK"); return; }
                if (channels.SelectedItem < 0 || _settings.TelegramGateways.Any(g => !Object.ReferenceEquals(g, source) && g.Protocol == protocol && g.ChannelIndex == channels.SelectedItem))
                { MessageBox.ErrorQuery("Telegram Gateway", "Select a channel without another gateway. Edit the existing gateway to change its settings.", "OK"); return; }
                result = new TelegramGatewaySettings { Name = String.IsNullOrWhiteSpace(name.Text.ToString()) ? "Telegram " + channels.SelectedItem : name.Text.ToString().Trim(),
                    Protocol = protocol, ChannelIndex = channels.SelectedItem, Enabled = enabled.Checked, BotToken = token.Text.ToString().Trim(), ChatId = parsedChatId,
                    ForwardMeshtasticToTelegram = meshToTelegram.Checked, ForwardTelegramToMeshtastic = telegramToMesh.Checked };
                Application.RequestStop();
            };
            var cancel = new Button("Cancel"); cancel.Clicked += delegate { Application.RequestStop(); };
            dialog.AddButton(save); dialog.AddButton(cancel); Application.Run(dialog, HandleTerminalGuiException); return result;
        }
    }
}
